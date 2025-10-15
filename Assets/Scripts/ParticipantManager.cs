using System;
using System.Collections;
using System.Collections.Generic;
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
    private float posDataInterval = 0.05f; // 0.05초마다 데이터 전송
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

            // JointMapper 초기화
            playerJointMapper = player.GetComponent<jointMapper>();
            if (playerJointMapper == null)
            {
                Debug.LogWarning("[ParticipantManager] jointMapper를 찾을 수 없습니다. 관절 데이터 전송 비활성화.");
            }
            else
            {
                Debug.Log("[ParticipantManager] jointMapper 초기화 성공");
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
        
        // 위치 및 회전 정보
        Vector3 currentPosition = player.position;
        Quaternion currentRotation = player.rotation;
        
        currentMonitoringData.pos = currentPosition;
        currentMonitoringData.rotQ = currentRotation;
        currentMonitoringData.eulerDeg = currentRotation.eulerAngles;
        currentMonitoringData.altitude = Mathf.RoundToInt(currentPosition.y);
        
        // 위치와 회전 업데이트
        lastPlayerPosition = currentPosition;
        lastPlayerRotation = currentRotation;
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
                
                // 디버그 로그 (성능을 위해 조건부 컴파일)
                // #if UNITY_EDITOR
                // Debug.Log($"[ParticipantManager] 데이터 전송 - 위치: {currentMonitoringData.pos}m, " + 
                //           $"쿼너티언: {currentMonitoringData.rotQ}" +
                //          $"오일러: {currentMonitoringData.eulerDeg}");
                // #endif
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
        foreach (var kvp in participantVisuals)
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
                
                if (jointData != null)
                {
                    // 전송
                    WS_DB_Client.Instance.SendJointRotationData(jointData);
                }
                else
                {
                    Debug.LogError($"[ParticipantManager] ❌ CollectData()가 null을 반환했습니다.");
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
    /// 다른 참가자의 관절 데이터 수신 및 적용
    /// </summary>
    public void ReceiveJointRotationData(JointRotation data)
    {
        var targetNo = data.simNo.ToString();
        
        // 자신의 데이터는 무시
        if (targetNo == simNo && isDebugMode == false)
            return;
        
        // OtherParticipantAvatar 컴포넌트를 찾아서 데이터 적용
        if (otherParticipantAvatars.ContainsKey(targetNo))
        {
            // 이미 등록된 아바타가 있으면 직접 적용
            OtherParticipantAvatar avatar = otherParticipantAvatars[targetNo];
            if (avatar != null)
            {
                avatar.ApplyData(data);
            }
        }
        else if (participantVisuals.ContainsKey(targetNo))
        {
            // 시각화 오브젝트는 있지만 아바타 컴포넌트가 없는 경우
            GameObject visual = participantVisuals[targetNo];
            if (visual != null)
            {
                OtherParticipantAvatar avatar = visual.GetComponent<OtherParticipantAvatar>();
                if (avatar == null)
                {
                    // 컴포넌트가 없으면 추가
                    avatar = visual.AddComponent<OtherParticipantAvatar>();
                    avatar.participantSimNo = int.Parse(targetNo);
                    otherParticipantAvatars[targetNo] = avatar;
                    Debug.Log($"[ParticipantManager] {targetNo}에 OtherParticipantAvatar 컴포넌트 추가");
                }
                avatar.ApplyData(data);
            }
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"[ParticipantManager] {targetNo}의 참가자 모델을 찾을 수 없습니다.");
            #endif
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
        visual.transform.position = data.pos;
        visual.transform.rotation = data.rotQ;
        visual.transform.parent = spawnPoint[idx];

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
        
        GameObject visual = participantVisuals[pSimNo];
        if (visual != null)
        {
            // 부드러운 위치 이동
            visual.transform.position = Vector3.Lerp(visual.transform.position, data.pos, Time.deltaTime * 10f);
            visual.transform.rotation = Quaternion.Lerp(visual.transform.rotation, data.rotQ, Time.deltaTime * 10f);
            
            // 즉시 위치 업데이트
            // visual.transform.position = data.pos;
            // visual.transform.rotation = data.rotQ;
        }
    }
    
    /// <summary>
    /// 모든 참가자 더미 모델 데이터 업데이트
    /// </summary>
    private void UpdateAllParticipantVisuals()
    {
        foreach (var kvp in otherParticipants)
        {
            UpdateParticipantVisual(kvp.Key, kvp.Value);
        }
    }
    
    #endregion
}
