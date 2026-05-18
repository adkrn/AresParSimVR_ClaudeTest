using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 참가자(훈련생) 데이터를 관리하고 모니터링 정보를 전송하는 매니저
/// </summary>
[DefaultExecutionOrder(250)]
public class ParticipantManager : MonoBehaviour
{
    #region Singleton
    public static ParticipantManager Inst { get; private set; }
    #endregion

    #region Private Fields
    // 플레이어 관련
    private Transform player;
    
    // WebSocket 클라이언트
    private WS_DB_Client wsClient;
    
    // 데이터 전송 관련
    private float posDataInterval = 0.01f; // 0.05초마다 데이터 전송
    private float lastSendTime = 0f;
    private bool isTrainingActive = false;
    
    // 모니터링 데이터
    private MonitoringData currentMonitoringData;
    private Vector3 lastPlayerPosition;
    private Quaternion lastPlayerRotation;
    
    // 참가자 정보
    private string participantId;
    private string simNo;
    
    // 다른 참가자 정보 관리
    private Dictionary<string, MonitoringData> otherParticipants = new Dictionary<string, MonitoringData>();
    private float lastOtherParticipantUpdateTime = 0f;
    
    // 교육생 스폰 포인트
    public List<Transform> spawnPoint;
    
    // 플레이어 관절 리스트
    public List<Transform> jointList;

    [SerializeField] private Transform vrCamera;
    
    // 관절 데이터 매퍼
    [Header("Joint Data System")]
    [SerializeField] private float jointDataInterval = 0.1f; // 10Hz
    private jointMapper playerJointMapper;  // Player 송신용
    private Dictionary<string, OtherParticipantAvatar> otherParticipantAvatars = new Dictionary<string, OtherParticipantAvatar>();  // 다른 참가자 수신용
    private float lastJointDataSendTime = 0f;
    
    // 다른 참가자 시각화 관리
    [Header("Participant Visualization")]
    [SerializeField] private GameObject participantPrefab; // 참가자 표시용 프리팹
    [SerializeField] private Color[] participantColors = new Color[] { Color.red, Color.blue, Color.green, Color.yellow, Color.cyan, Color.magenta };
    private Dictionary<string, GameObject> participantVisuals = new Dictionary<string, GameObject>();

    [Header("디버그 모드")]
    [SerializeField] private bool isDebugMode = false;

    // 이벤트
    public event Action<string, MonitoringData> OnParticipantDataUpdated;
    public event Action<string> OnParticipantJoined;
    public event Action<string> OnParticipantLeft;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        Inst = this;
        
        // 데이터 초기화
        currentMonitoringData = new MonitoringData();
    }

    private void Start()
    {
        Initialize();
    }
    
    /// <summary>
    /// ParticipantManager 초기화
    /// </summary>
    private void Initialize()
    {
        // 참가자 정보 초기화
        InitializeParticipantInfo();

        // 플레이어 찾기
        SetThisParticipant();

        StartMonitoring();

        if (vrCamera == null)
        {
            var cameraObj = GameObject.Find("CenterEyeAnchor");
            if (cameraObj == null) return;
            vrCamera = cameraObj.transform;
            Debug.Log("[ParticipantManager] VR 카메라 발견: CenterEyeAnchor");
        }
    }

    private void Update()
    {
        // 훈련이 활성화되어 있을 때만 데이터 전송
        if (isTrainingActive == false) return;

        // 이 시뮬레이터의 교육생 오브젝트를 못찾았을때 찾기
        if (player == null)
        {
            if (Time.time - lastSendTime >= 1.0f)
            {
                Debug.LogError("[ParticipantManager] 이 시뮬레이터의 교육생 오브젝트를 찾지 못했습니다. 다시 설정 시도");
                SetThisParticipant();
                lastSendTime = Time.time;
            }
        }

        // 교육생 위치 데이터 송수신
        // 위치 데이터는 낙하 후에 적용한다.
        if (player == null) return;
        if (Time.time - lastSendTime >= posDataInterval)
        {
            UpdateMonitoringDataInternal();
            UpdateAllParticipantVisuals();
            SendMonitoringData();
            lastSendTime = Time.time;
        }
            
        // 교육생 관절 데이터 수신
        if (playerJointMapper == null) return;
        if (Time.time - lastJointDataSendTime >= jointDataInterval)
        {
            SendJointRotationData();
            lastJointDataSendTime = Time.time;
        }
    }

    private void OnEnable()
    {
        // 씬 변경 이벤트 구독
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        // 씬 변경 이벤트 구독 해제
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopMonitoring();
    }

    private void OnDestroy()
    {
        StopMonitoring();
        
        if (Inst == this)
        {
            Inst = null;
        }
    }
    #endregion

    #region Initialization

    /// <summary>
    /// player 태그를 가진 오브젝트를 찾아서 Transform 캐싱
    /// </summary>
    private void SetThisParticipant()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;

            if (player == null)
            {
                Debug.LogError($"[ParticipantManager] Player 찾기 실패");
                return;
            }

            // JointMapper 초기화 - Transform 재귀 검색
            playerJointMapper = FindJointMapperRecursive(player);
            if (playerJointMapper == null)
            {
                Debug.LogWarning("[ParticipantManager] jointMapper를 찾을 수 없습니다. 관절 데이터 전송 비활성화.");
            }
            else
            {
                Debug.Log($"[ParticipantManager] jointMapper 초기화 성공: {playerJointMapper.gameObject.name}");
            }


            // 시뮬레이터 번호에 맞게 플레이어 위치 설정
            if (spawnPoint is { Count: > 0 })
            {
                // simNo에서 숫자 추출 (예: "SIM_01" → 1)
                int simNumber = 1; // 기본값
                string numberPart = simNo.Replace("SIM_", "").Replace("sim_", "");
                if (int.TryParse(numberPart, out int parsed))
                {
                    simNumber = parsed;
                }

                // 인덱스 계산 (0부터 시작하므로 -1)
                int idx = simNumber - 1;

                // 인덱스 범위 체크
                if (idx >= 0 && idx < spawnPoint.Count)
                {
                    player.position = spawnPoint[idx].position;
                    player.rotation = spawnPoint[idx].rotation;
                    player.transform.parent = spawnPoint[idx];
                    Debug.Log($"[ParticipantManager] {simNo}을 스폰 포인트 {idx}에 배치: {spawnPoint[idx].position}");
                }
                else
                {
                    // 인덱스가 범위를 벗어난 경우 첫 번째 스폰 포인트 사용
                    Debug.LogWarning($"[ParticipantManager] {simNo}에 해당하는 스폰 포인트가 없습니다. 첫 번째 스폰 포인트 사용");
                    player.position = spawnPoint[0].position;
                    player.rotation = spawnPoint[0].rotation;
                    player.transform.parent = spawnPoint[0];
                }
            }
            else
            {
                Debug.LogWarning("[ParticipantManager] 스폰 포인트가 설정되지 않았습니다. 현재 위치 유지");
            }

            lastPlayerPosition = player.position;
            lastPlayerRotation = player.rotation;
        }
        else
        {
            Debug.LogWarning("[ParticipantManager] Player 태그를 가진 오브젝트를 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 참가자 정보 초기화
    /// </summary>
    private void InitializeParticipantInfo()
    {
        simNo = WS_DB_Client.Instance.GetSimulatorNumber();
        Debug.Log($"[ParticipantManager] Simulator Number: {simNo}");

        // WS_DB_Client에서 참가자 ID 가져오기
        participantId = WS_DB_Client.Instance.CurParticipantData.id;
        if (string.IsNullOrEmpty(participantId))
        {
            participantId = $"participant_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        Debug.Log($"[ParticipantManager] Participant ID: {participantId}");
    }

    /// <summary>
    /// 씬이 로드될 때 호출
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 새 씬에서 플레이어 다시 찾기
        if (scene.name.Contains("Main") || scene.name.Contains("Training"))
        {
            // 씬 로드 후 약간의 지연을 두고 플레이어 찾기
            Invoke(nameof(SetThisParticipant), 0.5f);
        }
    }
    #endregion

    #region Monitoring Control
    /// <summary>
    /// 모니터링 시작
    /// </summary>
    public void StartMonitoring()
    {
        if (!isTrainingActive)
        {
            isTrainingActive = true;
            lastSendTime = Time.time;
            Debug.Log("[ParticipantManager] 모니터링 시작");
            
            // 플레이어가 없으면 찾기
            if (player == null)
            {
                SetThisParticipant();
            }
        }
    }

    /// <summary>
    /// 모니터링 중지
    /// </summary>
    public void StopMonitoring()
    {
        if (isTrainingActive)
        {
            isTrainingActive = false;
            Debug.Log("[ParticipantManager] 모니터링 중지");
        }
    }

    /// <summary>
    /// 모니터링 상태 확인
    /// </summary>
    public bool IsMonitoring => isTrainingActive;
    #endregion

    #region Data Transmission
    /// <summary>
    /// 모니터링 데이터 업데이트 (내부 호출용)
    /// </summary>
    private void UpdateMonitoringDataInternal()
    {
        if (player == null) return;

        // 기본 정보 설정
        currentMonitoringData.participantId = participantId;
        currentMonitoringData.simNo = simNo;

        // 점프 여부 확인
        var isJump = StateManager_New.Inst.isJump;

        // 위치 정보 - 점프 여부에 따라 로컬/절대 좌표 선택
        Vector3 currentPosition;
        if (isJump || SceneManager.sceneCount == 0)
        {
            // 점프 후: 절대 좌표 (world position)
            currentPosition = player.position;
        }
        else
        {
            // 점프 전: 로컬 좌표 (spawnPoint 기준)
            currentPosition = player.localPosition;
        }

        // 회전 정보 (VR 카메라의 Y축 회전 사용)
        if (vrCamera == null)
        {
            var cameraObj = GameObject.Find("CenterEyeAnchor");
            vrCamera = cameraObj.transform;
            Debug.Log("[ParticipantManager] VR 카메라 발견: CenterEyeAnchor");
        }
        float yRotation = vrCamera.eulerAngles.y;
        var currentRotation = Quaternion.Euler(0, yRotation, 0);

        currentMonitoringData.pos = currentPosition;
        currentMonitoringData.rotQ = currentRotation;
        currentMonitoringData.eulerDeg = currentRotation.eulerAngles;
        currentMonitoringData.altitude = Mathf.RoundToInt(player.position.y);  // 고도는 항상 world position 사용

        // 위치와 회전 업데이트
        lastPlayerPosition = currentPosition;
        lastPlayerRotation = currentRotation;
    }
    
    /// <summary>
    /// 비행기 위치, 회전값 업데이트
    /// </summary>
    public void SetMonitoringDataPlanePos(Vector3 pos, float rotY)
    {
        currentMonitoringData.planePos = pos;
        currentMonitoringData.planeRotY = rotY;
    }   

    /// <summary>
    /// 교육생 상태 정보 업데이트
    /// </summary>
    /// <param name="pIsPara"></param>
    /// <param name="pIsSubPara"></param>
    /// <param name="pIsLanding"></param>
    public void SetMonitoringDataPlayerFlag(bool pIsPara, bool pIsSubPara, bool pIsLanding)
    {
        currentMonitoringData.isPara = pIsPara;
        currentMonitoringData.isSubPara = pIsSubPara;
        currentMonitoringData.isLanding = pIsLanding;
    }

    /// <summary>
    /// 모니터링 데이터를 WebSocket으로 전송
    /// </summary>
    private void SendMonitoringData()
    {
        if (currentMonitoringData != null)
        {
            try
            {
                WS_DB_Client.Instance.SendMonitoringData(currentMonitoringData);

                if (isDebugMode)
                {
                    Debug.Log($"[SendMonitoringData] 1. 비행기 위치 : {currentMonitoringData.planePos}\n " +
                              $"2. 비행기 회전 : {currentMonitoringData.planeRotY}\n" +
                              $"3. 플레이어 낙하산 여부, 보조 낙하산 여부, 착지 여부 : " +
                              $"{currentMonitoringData.isPara}, {currentMonitoringData.isSubPara}, {currentMonitoringData.isLanding}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ParticipantManager] 데이터 전송 실패: {e.Message}");
            }
        }
    }
    
    #endregion

    #region Other Participants Management
    
    /// <summary>
    /// 다른 참가자의 정보를 받아서 업데이트 (WS_DB_Client에서 호출)
    /// </summary>
    public void UpdateOtherParticipantData(MonitoringData data)
    {
        if (data == null || string.IsNullOrEmpty(data.simNo))
        {
            Debug.LogWarning("[ParticipantManager] 유효하지 않은 참가자 데이터");
            return;
        }
        
        // 디버그 모드가 아니면 자신의 데이터는 무시
        if (data.simNo == simNo && isDebugMode == false)
        {
            return;
        }
        
        // 참가자 데이터 업데이트 또는 추가
        if (!otherParticipants.ContainsKey(data.simNo))
        {
            // 새로운 참가자 추가
            AddNewParticipant(data.simNo, data);
        }
        else
        {
            // 기존 참가자 데이터 업데이트
            UpdateExistingParticipant(data.simNo, data);
        }
    }
    
    /// <summary>
    /// 새로운 참가자 추가
    /// </summary>
    private void AddNewParticipant(string pSimNo, MonitoringData data)
    {
        otherParticipants[pSimNo] = data;
        
        // 새 참가자 시각화 오브젝트 생성
        CreateParticipantVisual(pSimNo, data);
        
        Debug.Log($"[ParticipantManager] 새 참가자 추가: {pSimNo}");
        OnParticipantJoined?.Invoke(pSimNo);
    }

    /// <summary>
    /// 기존 참가자 데이터 업데이트
    /// </summary>
    private void UpdateExistingParticipant(string pSimNo, MonitoringData data)
    {
        otherParticipants[pSimNo] = data;
        
        // 시각화 오브젝트 위치 업데이트
        UpdateParticipantVisual(pSimNo, data);
        
        OnParticipantDataUpdated?.Invoke(pSimNo, data);

#if UNITY_EDITOR
        Debug.Log($"[ParticipantManager] 참가자 데이터 업데이트: {pSimNo}, " + $"위치: {data.pos}, 고도: {data.altitude}m");
#endif
    }
    
    /// <summary>
    /// 모든 참가자 데이터 초기화
    /// </summary>
    public void ClearAllOtherParticipants()
    {
        otherParticipants.Clear();

        // 모든 시각화 오브젝트 제거
        foreach (var kvp in participantVisuals.ToList())
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        participantVisuals.Clear();

        Debug.Log("[ParticipantManager] 모든 다른 참가자 데이터 초기화");
    }
    #endregion
    
    #region Joint Data System

    /// <summary>
    /// 관절 데이터 전송
    /// </summary>
    private void SendJointRotationData()
    {
        if (playerJointMapper != null)
        {
            try
            {
                // 데이터 수집
                var jointData = playerJointMapper.CollectData();
                var fingerData = playerJointMapper.CollectFingerData();

                if (jointData != null)
                {
                    if (isDebugMode)
                    {
                        Debug.Log($"[ParticipantManager] 📤 관절 데이터 전송 - SimNo: {jointData.simNo}\n" +
                                  $"  다리: ThighL({jointData.thighL.x},{jointData.thighL.y},{jointData.thighL.z}), CalfL({jointData.calfL}) | " +
                                  $"ThighR({jointData.thighR.x},{jointData.thighR.y},{jointData.thighR.z}), CalfR({jointData.calfR})\n" +
                                  $"  몸통: Spine({jointData.spine.x},{jointData.spine.y},{jointData.spine.z}), Chest({jointData.chest.x},{jointData.chest.y},{jointData.chest.z})\n" +
                                  $"  팔L: ClavicleL({jointData.clavicleL.y},{jointData.clavicleL.z}), UpperArmL({jointData.upperArmL.x},{jointData.upperArmL.y},{jointData.upperArmL.z}), ForearmL({jointData.forearmL}), HandL({jointData.handL})\n" +
                                  $"  팔R: ClavicleR({jointData.clavicleR.y},{jointData.clavicleR.z}), UpperArmR({jointData.upperArmR.x},{jointData.upperArmR.y},{jointData.upperArmR.z}), ForearmR({jointData.forearmR}), HandR({jointData.handR})\n" +
                                  $"  머리: Neck({jointData.neck.x},{jointData.neck.y},{jointData.neck.z}), Head({jointData.head.x},{jointData.head.y},{jointData.head.z})");
                    }
                    // 전송
                    WS_DB_Client.Instance.SendJointRotationData(jointData);
                }
                else
                {
                    Debug.LogError($"[ParticipantManager] ❌ 수집된 관절 데이터가 null 입니다.");
                }

                if (fingerData != null)
                {
                    if (isDebugMode)
                    {
                        // JSON 직렬화 결과 확인
                        string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(fingerData);
                        Debug.Log($"[ParticipantManager] 📤 손가락 데이터 전송 - SimNo: {fingerData.simNo}\n" +
                                  $"  왼손: fgL0={fingerData.fgL0}, fgL1={fingerData.fgL1}, fgL2={fingerData.fgL2}, fgL3={fingerData.fgL3}, fgL4={fingerData.fgL4}\n" +
                                  $"  오른손: fgR0={fingerData.fgR0}, fgR1={fingerData.fgR1}, fgR2={fingerData.fgR2}, fgR3={fingerData.fgR3}, fgR4={fingerData.fgR4}\n" +
                                  $"  📋 JSON: {jsonString}");
                    }
                    // 전송
                    WS_DB_Client.Instance.SendFingerData(fingerData);
                }
                else
                {
                    Debug.LogError($"[ParticipantManager] ❌ 수집된 손가락 데이터가 null 입니다.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ParticipantManager] ❌ 관절 데이터 전송 실패: {e.Message}");
                Debug.LogError($"[ParticipantManager] Stack Trace: {e.StackTrace}");
            }
        }
        else
        {
            Debug.LogWarning($"[ParticipantManager] playerJointMapper가 null입니다.");
        }
    }

    /// <summary>
    /// 시각화 오브젝트의 아바타 컴포넌트 가져오기 (없으면 생성)
    /// </summary>
    private OtherParticipantAvatar GetOrCreateAvatar(string targetSimNo)
    {
        // 자신의 데이터는 무시
        if (targetSimNo == simNo && !isDebugMode)
            return null;

        // 1. 캐시에서 먼저 찾기
        if (otherParticipantAvatars.TryGetValue(targetSimNo, out OtherParticipantAvatar avatar) && avatar != null)
        {
            return avatar;
        }

        // 2. 시각화 오브젝트에서 찾거나 생성
        if (participantVisuals.TryGetValue(targetSimNo, out GameObject visual) && visual != null)
        {
            avatar = visual.GetComponent<OtherParticipantAvatar>();
            if (avatar == null)
            {
                // 컴포넌트가 없으면 추가
                avatar = visual.AddComponent<OtherParticipantAvatar>();
                avatar.participantSimNo = int.Parse(targetSimNo);
                Debug.Log($"[ParticipantManager] {targetSimNo}에 OtherParticipantAvatar 컴포넌트 추가");
            }
            otherParticipantAvatars[targetSimNo] = avatar;
            return avatar;
        }

#if UNITY_EDITOR
        Debug.LogWarning($"[ParticipantManager] {targetSimNo}의 참가자 모델을 찾을 수 없습니다.");
#endif
        return null;
    }

    /// <summary>
    /// 다른 참가자의 관절 데이터 수신 및 적용
    /// </summary>
    public void ReceiveJointRotationData(JointRotation data)
    {
        if (isDebugMode)
        {
            Debug.Log($"[ParticipantManager] 📥 관절 데이터 수신 - SimNo: {data.simNo}\n" +
                      $"  다리: ThighL({data.thighL.x},{data.thighL.y},{data.thighL.z}), CalfL({data.calfL}) | " +
                      $"ThighR({data.thighR.x},{data.thighR.y},{data.thighR.z}), CalfR({data.calfR})\n" +
                      $"  몸통: Spine({data.spine.x},{data.spine.y},{data.spine.z}), Chest({data.chest.x},{data.chest.y},{data.chest.z})\n" +
                      $"  팔L: ClavicleL({data.clavicleL.y},{data.clavicleL.z}), UpperArmL({data.upperArmL.x},{data.upperArmL.y},{data.upperArmL.z}), ForearmL({data.forearmL}), HandL({data.handL})\n" +
                      $"  팔R: ClavicleR({data.clavicleR.y},{data.clavicleR.z}), UpperArmR({data.upperArmR.x},{data.upperArmR.y},{data.upperArmR.z}), ForearmR({data.forearmR}), HandR({data.handR})\n" +
                      $"  머리: Neck({data.neck.x},{data.neck.y},{data.neck.z}), Head({data.head.x},{data.head.y},{data.head.z})");
        }

        var avatar = GetOrCreateAvatar(data.simNo.ToString());
        if (avatar != null)
        {
            avatar.ApplyData(data);
        }
    }

    /// <summary>
    /// 다른 참가자의 손가락 데이터 수신 및 적용
    /// </summary>
    public void ReceiveFingerRotationData(FingerRotation data)
    {
        if (isDebugMode)
        {
            Debug.Log($"[ParticipantManager] 📥 손가락 데이터 수신 - SimNo: {data.simNo}\n" +
                      $"  왼손: Thumb({data.fgL0}), Index({data.fgL1}), Middle({data.fgL2}), Ring({data.fgL3}), Pinky({data.fgL4})\n" +
                      $"  오른손: Thumb({data.fgR0}), Index({data.fgR1}), Middle({data.fgR2}), Ring({data.fgR3}), Pinky({data.fgR4})");
        }

        var avatar = GetOrCreateAvatar(data.simNo.ToString());
        if (avatar != null)
        {
            avatar.ApplyFingerData(data);
        }
    }

    #endregion
    
    #region Participant Visualization

    /// <summary>
    /// 참가자 시각화 오브젝트 생성
    /// </summary>
    private void CreateParticipantVisual(string pSimNo, MonitoringData data)
    {
        GameObject visual;
        // 프리팹이 있으면 사용, 없으면 기본 박스 생성
        if (participantPrefab != null)
        {
            visual = Instantiate(participantPrefab);
        }
        else
        {
            // 기본 박스 생성
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f); // 사람 크기

            // 충돌 비활성화 (다른 참가자와 충돌하지 않도록)
            Collider col = visual.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
        visual.name = $"Participant_{pSimNo}";

        // OtherParticipantAvatar 컴포넌트 추가 (관절 데이터 수신용)
        OtherParticipantAvatar avatar = visual.GetComponent<OtherParticipantAvatar>();
        if (avatar == null)
        {
            avatar = visual.AddComponent<OtherParticipantAvatar>();
        }

        // 시뮬레이터 번호 설정
        if (int.TryParse(pSimNo, out int no))
        {
            avatar.participantSimNo = no;
            otherParticipantAvatars[pSimNo] = avatar;
            Debug.Log($"[ParticipantManager] 참가자 {pSimNo}에 OtherParticipantAvatar 추가 및 설정 완료");
        }

        // 초기 위치 설정
        int idx = int.Parse(pSimNo) - 1;
        //visual.transform.position = data.pos;
        visual.transform.rotation = data.rotQ;
        visual.transform.parent = spawnPoint[idx];
        visual.transform.localPosition = new Vector3(0, 0, 0);

        // Dictionary에 추가
        participantVisuals[pSimNo] = visual;

        Debug.Log($"[ParticipantManager] 참가자 더미 모델 생성: {pSimNo}");
    }

    /// <summary>
    /// 참가자 시각화 오브젝트 업데이트
    /// </summary>
    private void UpdateParticipantVisual(string pSimNo, MonitoringData data)
    {
        // 시각화 오브젝트가 없으면 생성
        if (!participantVisuals.ContainsKey(pSimNo))
        {
            CreateParticipantVisual(pSimNo, data);
        }

        // 점프 여부 확인
        var isJump = StateManager_New.Inst.isJump;

        GameObject visual = participantVisuals[pSimNo];
        if (visual != null)
        {
            // 회전은 항상 부드럽게 보간
            visual.transform.rotation = Quaternion.Lerp(visual.transform.rotation, data.rotQ, Time.deltaTime * 5f);

            // 위치 동기화 - 직관적인 방식
            if (isJump || SceneManager.sceneCount == 0)
            {
                // 점프 후: 절대 좌표 사용 (world position)
                visual.transform.position = data.pos;
            }
            else
            {
                // 점프 전: 로컬 좌표 사용 (spawnPoint 기준)
                // 부드러운 보간으로 떨림 감소
                visual.transform.localPosition = Vector3.Lerp(
                    visual.transform.localPosition,  // 현재 로컬 위치
                    data.pos,                         // 받은 로컬 위치
                    Time.deltaTime * 10f
                );
            }

            // 낙하산 상태 업데이트
            var avatar = visual.GetComponent<OtherParticipantAvatar>();
            if (avatar != null)
            {
                if(isDebugMode) Debug.Log($"낙하산 상태 업데이트 : 낙하산 {data.isPara}, 서브 낙하산 {data.isSubPara}");
                avatar.UpdateParachuteState(data.isPara, data.isSubPara);
            }
        }
    }
    
    /// <summary>
    /// 모든 참가자 더미 모델 데이터 업데이트
    /// </summary>
    private void UpdateAllParticipantVisuals()
    {
        // ToList()로 스냅샷 생성하여 순회 중 Dictionary 수정 방지 (WebSocket 멀티스레딩 동시성 문제 해결)
        foreach (var kvp in otherParticipants.ToList())
        {
            UpdateParticipantVisual(kvp.Key, kvp.Value);
        }
    }

    #endregion

    #region Door Lineup System

    /// <summary>
    /// 문 앞에 줄 세우기 - 더미 모델 뒤에 배치
    /// </summary>
    /// <param name="doorExitPoint">문(Exit) 위치 Transform</param>
    /// <param name="lineSpacing">줄 간격 (기본 1.0m)</param>
    public void LineupAtDoor(Transform doorExitPoint, float lineSpacing = 1.0f)
    {
        if (player == null)
        {
            Debug.LogWarning("[ParticipantManager] 플레이어를 찾을 수 없어 줄 세우기를 실행할 수 없습니다.");
            return;
        }

        if (doorExitPoint == null)
        {
            Debug.LogWarning("[ParticipantManager] 문(Exit) 위치를 찾을 수 없습니다.");
            return;
        }

        // 1. 문 위치와 방향 계산
        Vector3 doorPos = doorExitPoint.position;
        Vector3 doorBackward = -doorExitPoint.forward; // 문 뒤쪽 방향

        // 2. 더미 모델들 중 문에서 가장 먼 거리 찾기
        float maxDistanceFromDoor = 0f;
        int dummyCount = 0;

        foreach (var kvp in participantVisuals.ToList())
        {
            if (kvp.Value != null)
            {
                // 문 뒤쪽 방향으로의 거리만 계산 (투영)
                Vector3 toDummy = kvp.Value.transform.position - doorPos;
                float distanceAlongLine = Vector3.Dot(toDummy, doorBackward);

                if (distanceAlongLine > maxDistanceFromDoor)
                {
                    maxDistanceFromDoor = distanceAlongLine;
                }
                dummyCount++;
            }
        }

        // 3. 플레이어 위치 계산 및 이동
        Vector3 targetPosition = doorPos + doorBackward * (maxDistanceFromDoor + lineSpacing);
        targetPosition.y = doorPos.y; // 높이는 문과 같은 높이로

        player.position = targetPosition;
        player.rotation = doorExitPoint.rotation; // 문과 같은 방향

        Debug.Log($"[ParticipantManager] 줄 세우기 완료 - 더미 모델 {dummyCount}명, " +
                  $"최대 거리: {maxDistanceFromDoor:F2}m, 배치 위치: {targetPosition}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// player 하위 오브젝트를 재귀적으로 검색하여 jointMapper 찾기
    /// </summary>
    private jointMapper FindJointMapperRecursive(Transform parent)
    {
        // 현재 오브젝트에서 찾기
        jointMapper mapper = parent.GetComponent<jointMapper>();
        if (mapper != null)
        {
            Debug.Log($"[ParticipantManager] jointMapper 발견: {parent.name}");
            return mapper;
        }

        // 자식 오브젝트들 재귀 검색
        foreach (Transform child in parent)
        {
            mapper = FindJointMapperRecursive(child);
            if (mapper != null)
                return mapper;
        }

        return null;
    }

    #endregion
}
