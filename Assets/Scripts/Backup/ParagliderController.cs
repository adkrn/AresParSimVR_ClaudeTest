using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;
using OVR;

public class ParagliderController : MonoBehaviour
{
    [Header("ARES Hardware Integration")]
    [SerializeField] private AresHardwareService aresService;  // ARES 하드웨어 서비스 컴포넌트
    [Header("Preloaded Components")]
    [SerializeField] private Transform pasimPlayer;
    public Rigidbody rb;
    public Collider col;
    [SerializeField] private WindZone windZone;
    [SerializeField] private Transform goalPoint;
    
    [SerializeField] private WS_DB_Client wsInspector;

    [Header("Control Inputs (0~1)")]
    [Range(0f, 1f)] [SerializeField] private float leftPull = 0f;
    [Range(0f, 1f)] [SerializeField] private float rightPull = 0f;

    [Header("Velocity Steering Settings")] 
    [SerializeField] private bool steerVelocity = true;
    
    [SerializeField] bool isInputRiser = false;
    [SerializeField] bool isInputRiserL = false;
    [SerializeField] bool isInputRiserR = false;
    
    // 원하는 복원 강도(튜닝용)
    [SerializeField] private float maxYawSpeed =4f; // 최대 회전 속도 (rad/s)
    [SerializeField] private float yawControlStrength = 5f; // 회전 속도 추종 강도 (P 게인)
    [SerializeField] private float yawDamping = 3f; // 회전 감쇠 (D 게인)
    [SerializeField] private float maxRoll = 20f;
    [SerializeField] private float maxPitch = 10f;
    [SerializeField] private float selfLevelStrengthRoll = 5f;   // P-게인
    [SerializeField] private float selfLevelDampingRoll  = 5f; // D-게인
    [SerializeField] private float selfLevelStrengthPitch = 1f;
    [SerializeField] private float selfLevelDampingPitch = 1f;

    [SerializeField] private float pitchReturnStrength = 100f;
    
    [SerializeField] private float pitchReturnSpeed = 2f; // Pitch 복원 속도
    [SerializeField] private float rollReturnSpeed = 2f; // Roll 복원 속도
    
    [Header("Speed Targets (m/s)")]
    [Header("---전진 속도")]
    [SerializeField] private float targetForwardSpeed = 0f;   // 일반 낙하 : 0, 낙하산 폈을때 : 12
    [Header("---하강 속도")]
    [SerializeField] private float targetSinkSpeed     =  9.80665f;   // 일반 낙하 : 9.8, 낙하산 폈을때 : 5
   
    [Header("전진낙하 최소최대 범위 조절")]
    [Header("---전진 범위")]
    [SerializeField] private float minForwardSpeed    =  8f;
    [SerializeField] private float maxForwardSpeed    = 15f;
    [Header("---하강 범위")]
    [SerializeField] private float minSinkSpeed        =  4.5f;
    [SerializeField] private float maxSinkSpeed        =  5.5f;

    [Header("Controller Gains / 강하자 150kg일때 ::: fwdSpeedGain: 6.55 - sinkRateGain: 3.45")]
    [SerializeField] private float fwdSpeedGain   = 7f;  // 전진 속도 P-게인
    [SerializeField] private float sinkRateGain   = 7f;  // 침강 속도 P-게인

    [Header("낙하상태값 전송주기")] 
    [SerializeField] private float sendInterval;
    
    [Tooltip("하드웨어와 게임 Yaw 차이 허용 범위 (도)")]
    [Range(1f, 30f)]
    [SerializeField] private float yawSyncThreshold = 5f;
    
    [Tooltip("부드러운 동기화 속도 (높을수록 빠름)")]
    [Range(1f, 20f)]
    [SerializeField] private float yawSyncSmoothSpeed = 10f;
    
    [Tooltip("동기화 디버그 로그 출력")]
    [SerializeField] private bool debugYawSync = false;
    
    // 낙하 시작 시 기준 Yaw 값 저장
    private float initialYaw = 0f;
    // 하드웨어 동기화를 위한 목표 Yaw
    private float targetHardwareYaw = 0f;
    private float lastHardwareYaw = 0f;
    // Unity와 Hardware 간의 각도 offset (Unity - Hardware)
    private float unityToHardwareOffset = 0f;
    
    private const float FreeGravity = 9.80665f; // 일반 낙하 속도
    private const float ParaGravity = 5f;       // 낙하산 폈을때 낙하 속도
    private const float FreeForwardSpeed = 0;       // 일반 속도
    private const float ParaForwardSpeed = 12;  // 낙하산 폈을때 속도


    private WS_DB_Client ws;
    [HideInInspector] public float brakeMultiplier;
    private float _pitch;
    private float _autoPitchTorque;

    // 낙하시작여부 체크
    [HideInInspector] public bool isJumpStart = false;
    // 낙하산 켜졌을때 true
    private bool _isPara = false;
    
    private Vector3 _dnDir;
    private Vector3 _upAccelCmd;
    
    // 한 번만 만들고 재사용할 스타일
    private GUIStyle labelStyle;
    private bool isGUIStyleInitialized = false;
    
    void Start()
    {
        // ARES 서비스 자동 찾기 (Inspector에서 할당하지 않은 경우)
        if (aresService == null)
        {
            aresService = GetComponent<AresHardwareService>();
        }
    }
    
    #if UNITY_EDITOR
    void OnGUI()
    {
        if (!isGUIStyleInitialized)
        {
            InitializeGUIStyle();
        }
        
        Vector3 v = rb.linearVelocity;
        float fwd  = Vector3.Dot(v, pasimPlayer.forward);
        float sink = -v.y;
        GUI.Label(new Rect(10,10,200,25), $"Forward : {fwd:0.0} m/s",labelStyle);
        GUI.Label(new Rect(10,35,200,25), $"Sink    : {sink:0.0} m/s",labelStyle);
    }
    
    private void InitializeGUIStyle()
    {
        labelStyle = new GUIStyle();
        labelStyle.fontSize = 18;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = Color.red;
        labelStyle.alignment = TextAnchor.MiddleLeft;
        isGUIStyleInitialized = true;
    }
    #endif

    void Awake()
    {
        if (pasimPlayer)
        {
            rb = pasimPlayer.GetComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.None;
            
            col = pasimPlayer.GetComponent<Collider>();
            col.enabled = false;
        }
        // else Debug.Log("_pasimPlayer가 존재하지 않습니다.");

        // FindAnyObjectByType 최적화: Inspector 할당 우선, 없으면 Find 사용
        if (!windZone)
        {
            windZone = FindAnyObjectByType<WindZone>();
            // if (!windZone) Debug.Log("windZone이 존재하지 않습니다.");
        }

        ws = wsInspector ? wsInspector : FindAnyObjectByType<WS_DB_Client>();
        
        // ARES 서비스 자동 찾기 (Inspector에서 할당하지 않은 경우)
        if (aresService == null)
        {
            aresService = GetComponent<AresHardwareService>();
        }
        
        // ARES 이벤트 구독
        if (aresService != null)
        {
            //aresService.OnFeedbackReceived += HandleAresFeedback;
            aresService.OnConnectionLost += HandleAresConnectionLost;
            aresService.OnConnectionRestored += HandleAresConnectionRestored;
            aresService.OnConnectionFailed += HandleAresConnectionFailed;
        }
    }
    
    void OnDestroy()
    {
        // ARES 이벤트 구독 해제
        if (aresService != null)
        {
            //aresService.OnFeedbackReceived -= HandleAresFeedback;
            aresService.OnConnectionLost -= HandleAresConnectionLost;
            aresService.OnConnectionRestored -= HandleAresConnectionRestored;
            aresService.OnConnectionFailed -= HandleAresConnectionFailed;
        }
    }

    private void Init()
    {
        // Debug.Log("[ParagliderController] 초기화");
        _isPara = false;
        isJumpStart = false;
        col.enabled = false;

        targetForwardSpeed = FreeForwardSpeed;
        targetSinkSpeed = FreeGravity;
    }
    
    public void ParaDeploy()
    {
        // Debug.Log("[ParagliderController] 낙하산 펼치기");
        _isPara = true;
        rb.useGravity = true;
        col.enabled = true;
        targetForwardSpeed = ParaForwardSpeed;
        targetSinkSpeed = ParaGravity;
        
        // ARES 하드웨어에 낙하산 전개 상태 전송
        if (aresService != null && aresService.IsConnected)
        {
            aresService.SetEvent(AresEvent.Deploy_Standard);
        }
    }

    /// <summary>
    /// 물리현상으로 인한 업데이트 되어야 될 것들 처리
    /// </summary>
    void FixedUpdate()
    {
        // 낙하 시작 전에는 아무것도 실행하지 않음
        if (!isJumpStart) return;
        
        UpdateCustom();
        RiserDamping();
        
        RegulateForwardSpeed();   // ⬅︎ 새 메서드
        RegulateSinkRate();       // ⬅︎ 새 메서드
        
        // 하드웨어 통신
        UpdateAresHardware();
        if (aresService != null && aresService.UseHardware && aresService.IsConnected)
        {
            ApplyHardwareYawSmooth();
        }
        // 하드웨어 통신상태가 아니면 자체 처리
        else
        {
            ApplyTurning();
        }
        
        ApplyWindZoneForce();
        SendFallInfo();

        if (steerVelocity) SteerVelocityTowardsForward();
        if (!isInputRiser)
        {
            RecoverPitchSmoothly();
            RecoverRollSmoothly();
        }
    }

    private float _firstJumpHeight;
    private float _lastSendTime;
    
    /// <summary>
    /// 낙하가 실행되었음을 알림
    /// 양방향 초기 동기화 구현 - 하드웨어 상태를 먼저 확인하고 Unity를 동기화
    /// </summary>
    public void JumpStart()
    {
        isJumpStart = true;
        _firstJumpHeight = pasimPlayer.position.y;
        _lastSendTime  = Time.time;
        initialYaw = pasimPlayer.eulerAngles.y;
        
        if (DataManager.Inst.scenario.jumpType != JumpType.STANDARD)
        {
            aresService.SetEvent(AresEvent.FreeFall);
            Debug.Log($"[ParagliderController] 📡 FreeFall 이벤트 전송됨");
        }
    }

    /// <summary>
    /// 외부 입력 체크용 임시 업데이트 코드
    /// 현재 라이저줄 입력 체크
    /// </summary>
    private void UpdateCustom()
    {
        // 최적화: 낙하산이 펼쳐지지 않았으면 입력 체크 안함
        if (!_isPara)
        {
            isInputRiser = false;
            isInputRiserL = false;
            isInputRiserR = false;
            return;
        }
        
        // ARES 하드웨어 사용 중이면 VR/키보드 입력 무시
        // 하드웨어 피드백이 ProcessAresFeedback에서 처리됨
        if (aresService != null && aresService.IsConnected)
        {
            // ARES 하드웨어가 입력을 제어하므로 여기서는 처리하지 않음
            return;
        }
        
        // ARES 하드웨어가 없을 때만 VR/키보드 입력 처리
        isInputRiser = false;
        isInputRiserL = false;
        isInputRiserR = false;

        var lValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        if (Input.GetKey(KeyCode.A) || lValue > 0.1f)
        {
            leftPull += Time.deltaTime / 4f;
            if (leftPull > 1f) leftPull = 1f;
            isInputRiser = true;
            isInputRiserL = true;
        }
        else
        {
            leftPull -= Time.deltaTime / 2f;
            if (leftPull < 0f) leftPull = 0f;
        }

        var rValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        if (Input.GetKey(KeyCode.D) || rValue > 0.1f)
        {
            rightPull += Time.deltaTime / 4f;
            if (rightPull > 1f) rightPull = 1f;
            isInputRiser = true;
            isInputRiserR = true;
        }
        else
        {
            rightPull -= Time.deltaTime / 2f;
            if (rightPull < 0f) rightPull = 0f;
        }
    }

    /// <summary>
    /// 라이저 줄을 동시에 당겼을 때 발생하는 댐핑 값을 계산
    /// </summary>
    void RiserDamping()
    {
        // ── 브레이크(라이저 당김) 계산은 그대로 유지 ──
        float brakeInputDiffer = 1f - Mathf.Abs(leftPull - rightPull);
        float brakeInputMultiplier = brakeInputDiffer * (leftPull * rightPull);
        brakeMultiplier = 1f - brakeInputMultiplier;
        // Debug.Log("[ParagliderCtrl] 라이저 댐핑값 : " + brakeMultiplier);
    }

    /// <summary>
    /// 낙하산의 하강 속도를 조정
    /// </summary>
    void RegulateSinkRate()
    {
        if (!_isPara)
        {
            pasimPlayer.position += new Vector3(0, GetFreeFallDistance(), 0);
            return;
        }
        
        /* ① 수직 하강 벡터만 추출 */
        _dnDir = pasimPlayer.up;
        _dnDir.z = 0f;
        _dnDir.x = 0f;

        float sinkSpeed = Mathf.Clamp(targetSinkSpeed, minSinkSpeed, maxSinkSpeed);
        float sinkError = sinkSpeed * sinkRateGain;

        /* 중력 + 리프트 합력이 조정되도록 위(+Y)방향 가속 또는 압력 */
        _upAccelCmd = Vector3.down * sinkError -_dnDir;
        rb.AddForce(_upAccelCmd, ForceMode.Acceleration);
    }
    
    /// <summary>
    /// 낙하산의 전진속도를 조절
    /// </summary>
    void RegulateForwardSpeed()
    {
        // 낙하산 펼쳐지지 않았으면 전진속도 조절 안함
        if (!_isPara) return;
        
        /* ① 수평 전방 벡터만 추출 */
        Vector3 fwdDir = pasimPlayer.forward;
        fwdDir.y = 0f;
        fwdDir.Normalize();

        /* ② 실제 전진속도 계산 */
        float fwd = Vector3.Dot(rb.linearVelocity, fwdDir);

        /* ③ 목표-속도 → 가속도 명령 * 라이저 좌우 당김으로 인한 댐핑(속도저하) 구현 */
        float cmd = (targetForwardSpeed * fwdSpeedGain - fwd) * brakeMultiplier;
        rb.AddForce(fwdDir * cmd, ForceMode.Acceleration);
    }
    
    
    /// <summary>
    /// 라이저 줄 당김으로 인한 자연스러운 회전 조절
    /// </summary>
    void ApplyTurning()
    {
        var angleVelo = rb.angularVelocity;
        var lineVelo = rb.linearVelocity;
        
        // ░░ 전진 속도가 작을 경우 회전 차단
        float horizontalSpeed = new Vector3(lineVelo.x, 0, lineVelo.z).magnitude;
        if (horizontalSpeed < 2f) return;   // 거의 정지면 턴 토크 차단
        
        float turnInput = rightPull - leftPull;
        
        // ░░ 목표 회전 속도 계산 (Yaw)
        float targetYawSpeed = turnInput * maxYawSpeed;
        float currentYawSpeed = angleVelo.y;
        
        // P-D 컨트롤로 토크 계산
        float yawError = targetYawSpeed - currentYawSpeed;
        float yawCorrection = (yawError * yawControlStrength) - (currentYawSpeed * yawDamping);

        rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);
        
        // ░░ Roll Torque (좌우 기울이기)
        float rollTorque = 0.3f * yawCorrection;
        rb.AddTorque(pasimPlayer.forward * -rollTorque, ForceMode.Force);
        
        // ░░ Roll 제한 및 복원
        float localZRoll = pasimPlayer.localEulerAngles.z;
        if (localZRoll > 180f) localZRoll -= 360f;

        if (localZRoll > maxRoll)
            rb.AddTorque(pasimPlayer.forward * -(localZRoll - maxRoll) * 2f, ForceMode.Force);
        else if (localZRoll < -maxRoll)
            rb.AddTorque(pasimPlayer.forward * -(localZRoll + maxRoll) * 2f, ForceMode.Force);

        if (!isInputRiser)
        {
            float autoLevelTorque = (-localZRoll * selfLevelStrengthRoll) - (angleVelo.z * selfLevelDampingRoll);
            rb.AddTorque(pasimPlayer.forward * autoLevelTorque, ForceMode.Force);
        }

        // ░░ Pitch 제한 및 복원
        _pitch = pasimPlayer.localEulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        if (_pitch > maxPitch)
            rb.AddTorque(pasimPlayer.right * -(_pitch - maxPitch) * 2f, ForceMode.Force);
        else if (_pitch < -maxPitch)
            rb.AddTorque(pasimPlayer.right * -(_pitch + maxPitch) * 2f, ForceMode.Force);

        if (!isInputRiser && _pitch != 0f)
        {
            _autoPitchTorque = Mathf.Max(rightPull, leftPull) * 2f;
            _autoPitchTorque =  _pitch * _pitch /(pitchReturnStrength * _pitch);
            _autoPitchTorque = (-_pitch * selfLevelStrengthPitch) - (angleVelo.x * selfLevelDampingPitch);
            rb.AddTorque(pasimPlayer.right * _autoPitchTorque, ForceMode.Force);
        }
    }
    
    /// <summary>
    /// 바람의 영향을 구현
    /// </summary>
    void ApplyWindZoneForce()
    {
        // windZone이 없거나 낙하산 펼쳐지지 않았으면 실행 않함
        if (!windZone || !_isPara) return;

        Vector3 windDir = windZone.transform.forward;
        float mainStrength = windZone.windMain;
        float turbulence = windZone.windTurbulence;

        Vector3 windForce = windDir * mainStrength;
        windForce += Random.insideUnitSphere * turbulence;

        rb.AddForce(windForce, ForceMode.Force);
    }
    
    /// <summary>
    /// 전방 방향으로 날아가려는 힘에 대한 속도 조정
    /// </summary>
    void SteerVelocityTowardsForward()
    {
        // 속도 덮어쓰기 금지 → 대신 목표 방향으로 살짝 힘을 준다
        Vector3 vHoriz = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        if (vHoriz.sqrMagnitude < 0.1f) return;

        Vector3 desired = pasimPlayer.forward;
        desired.y = 0;
        desired.Normalize();

        float angle = Vector3.SignedAngle(vHoriz.normalized, desired, Vector3.up);
        Vector3 sideAccel = Quaternion.AngleAxis(90, Vector3.up) * desired * angle * 0.05f;                                 // 위력 조정
        rb.AddForce(sideAccel, ForceMode.Acceleration);
    }
    
    /// <summary>
    /// 라이저 줄을 더이상 당기지 않을 때 자연스럽게 피칭을 조절
    /// 기수가 앞으로 꺽이는 각도를 0도로 복귀
    /// </summary>
    private void RecoverPitchSmoothly()
    {
        // 현재 로컬 회전 (Euler)
        Vector3 localEuler = pasimPlayer.localEulerAngles;

        // pitch 값 정규화 (-180 ~ 180)
        float pitch = localEuler.x;
        if (pitch > 180f) pitch -= 360f;

        // 목표 pitch = 0
        float targetPitch = 0f;

        // 보간 적용
        float newPitch = Mathf.Lerp(pitch, targetPitch, Time.fixedDeltaTime * pitchReturnSpeed);

        // 새 로컬 회전 적용
        localEuler.x = newPitch;
        pasimPlayer.localRotation = Quaternion.Euler(localEuler);
    }
    
    /// <summary>
    /// 라이저 줄을 더이상 당기지 않을 때 자연스럽게 롤링을 조절
    /// 기수가 좌우로 꺽이는 각도를 0도로 복귀
    /// </summary>
    private void RecoverRollSmoothly()
    {
        Vector3 localEuler = pasimPlayer.localEulerAngles;

        // roll 값 정규화 (-180 ~ 180)
        float roll = localEuler.z;
        if (roll > 180f) roll -= 360f;

        float targetRoll = 0f;
        float newRoll = Mathf.Lerp(roll, targetRoll, Time.fixedDeltaTime * rollReturnSpeed);
        localEuler.z = newRoll;
        pasimPlayer.localRotation = Quaternion.Euler(localEuler);
    }
    
    
    [Header("현재 낙하속도")]
    [SerializeField] private float currentSpeed = 0f; 
    [Header("자유낙하항력계수: 기본 0.005")]
    [SerializeField] private float dragCoefficient = 0.005f;
    [ShowConst("중력 및 항력 설정")]
    private const float Gravity = 9.80665f; // 중력 가속도
    [ShowConst("속도 제한: 자유낙학 기본 60m/s(200), 85m/s(300), 낙하산 전개 5m/s", label: "자유낙하 시 사용할 항력 계수 (0.5 * rho * Cd * A / m)")]
    private const float fallingMaxSpeed = 85f; // 자유낙하 초당 최대 속도(200km/h -> 약60, 300km/h -> 약85)
    
    private float GetFreeFallDistance()
    {
        // Debug.Log("<color=yellow>[PlayCharacter]</color>GetFreeFallDistance 실행");
        // 항력으로 인한 감속 가속도: a_drag = dragCoefficient * v^2
        float dragAcc = dragCoefficient * currentSpeed * currentSpeed;
        // 순가속도 = 중력 - 항력
        float acceleration = Gravity - dragAcc;
        // 속도 업데이트
        currentSpeed += acceleration * Time.deltaTime;
        // 최대 속도 제한
        currentSpeed = Mathf.Clamp(currentSpeed, 0f, fallingMaxSpeed);
        // 델타타임 동안 이동한 거리 반환
        return (currentSpeed * Time.deltaTime) * -1;
    }


    private float _tempSpeed;
    public float impactSpeed;
    
    // /// <summary>
    // /// 점프 후 1초에 n번씩 
    // /// 현재 플레이어 낙하 정보를 교관에 보내준다.
    // /// </summary>
    // private void SendFallInfo()
    // {
    //     if(isJumpStart == false) return;
    //     
    //     if (Time.time - _lastSendTime >= sendInterval)
    //     {
    //         var posY = pasimPlayer.transform.position.y;
    //         var dist = Mathf.RoundToInt(Vector3.Distance(pasimPlayer.position, goalPoint.position));
    //         var alt = Mathf.RoundToInt(pasimPlayer.transform.position.y);
    //         if (alt < 0)
    //         {
    //             alt = 0;
    //         }
    //         var speed = ((_tempSpeed - posY) / sendInterval);
    //         impactSpeed = speed;
    //         
    //         MonitoringData data = new MonitoringData()
    //         {
    //             participantId =  NetworkManager.Inst.preparedId,
    //             altitude = alt,
    //             distance = dist,
    //             forwardSpeed = Mathf.RoundToInt(targetForwardSpeed),
    //             fallingSpeed = Mathf.RoundToInt(speed),
    //         };
    //         
    //         // Debug.Log($"SendFallingInfo ::: tempSpeed: {_tempSpeed}, speed : {speed}, posY : {posY}");
    //         _tempSpeed = posY;
    //         ws.SendMonitoringData(data);
    //         _lastSendTime = Time.time;
    //     }
    // }
    //
    
    // ── 추가(또는 기존 교체)할 필드 ─────────────────────────────
    private const float SEND_INTERVAL = 0.25f;          // 4 Hz 전송
    private readonly MonitoringData _monData = new();   // 객체 캐시
    private readonly StringBuilder  _jsonSB  = new(128);// 문자열 버퍼
    private float _nextSendTime;                        // 다음 전송 시각
    // ──────────────────────────────────────────────────────────
    /// <summary>
    /// 플레이어 낙하 정보를 일정 주기로 교관(서버)에 전송한다.
    /// Non-Alloc 버전: GC 0 B / frame
    /// </summary>
    private void SendFallInfo()
    {
        // 전송 주기 제어
        if (Time.time < _nextSendTime) return;
        _nextSendTime = Time.time + SEND_INTERVAL;

        // ── ① 데이터 갱신 ─────────────────────────────
        float posY  = pasimPlayer.position.y;
        int   dist  = Mathf.RoundToInt(Vector3.Distance(pasimPlayer.position, goalPoint.position));
        int   alt   = Mathf.Max(0, Mathf.RoundToInt(posY));
        float speed = (_tempSpeed - posY) / SEND_INTERVAL;
        impactSpeed = speed;
        _tempSpeed  = posY;

        // WS_DB_Client에서 받은 participantId 사용
        var participantData = ws.GetParticipantData();
        if (participantData != null && !string.IsNullOrEmpty(participantData.participantId))
        {
            _monData.participantId = participantData.participantId;
            _monData.simNo = participantData.simNo;
        }
        else
        {
            // fallback: 참가자 데이터가 없으면 CurParticipantData 사용
            _monData.participantId = ws.CurParticipantData?.id ?? "unknown";
        }
        
        _monData.altitude      = alt;
        _monData.distance      = dist;
        _monData.forwardSpeed  = Mathf.RoundToInt(targetForwardSpeed);
        _monData.fallingSpeed  = Mathf.RoundToInt(speed);

        // ── ② Non-Alloc JSON 직렬화 & 전송 ─────────────
        //ParticipantManager.Inst.UpdateMonitoringData(_monData);
    }
    
    #region ARES Hardware Integration
    
    #region Event Handlers
    
    /// <summary>
    /// ARES 피드백 수신 이벤트 핸들러
    /// </summary>
    private void HandleAresFeedback(AresFeedbackData feedback)
    {
        // 이벤트 방식으로 피드백 처리
        // UpdateAresHardware의 GetLatestFeedback 대신 사용 가능
        if (feedback.LeftRiserLength > 0 || feedback.RightRiserLength > 0)
        {
            // Debug.Log($"[ARES Event] 피드백 수신 - LeftRiserLength : {feedback.LeftRiserLength}%, RightRiserLength : {feedback.RightRiserLength}%");
            // Debug.Log($"[ARES Event] 피드백 수신 - RollPosition : {feedback.RollPosition}, YawPosition : {feedback.YawPosition}");
            ProcessAresFeedback(feedback);
        }
    }
    
    /// <summary>
    /// ARES 연결 끊김 이벤트 핸들러
    /// </summary>
    private void HandleAresConnectionLost()
    {
        Debug.LogWarning("[ARES Event] 하드웨어 연결 끊김!");
        
        // UI 경고 표시 (필요시)
        if (UIManager.Inst != null)
        {
            //UIManager.Inst.ShowWarning("ARES 하드웨어 연결이 끊어졌습니다");
        }
        
        // VR 컨트롤로 자동 전환은 UpdateCustom에서 처리됨
    }
    
    /// <summary>
    /// ARES 연결 복구 이벤트 핸들러
    /// </summary>
    private void HandleAresConnectionRestored()
    {
        Debug.Log("[ARES Event] 하드웨어 연결 복구됨!");
        
        // UI 알림
        if (UIManager.Inst != null)
        {
            //UIManager.Inst.ShowSuccess("ARES 하드웨어 연결 복구");
        }
    }
    
    /// <summary>
    /// ARES 연결 실패 이벤트 핸들러
    /// </summary>
    private void HandleAresConnectionFailed()
    {
        Debug.LogError("[ARES Event] 하드웨어 연결 실패 - 재연결 불가");
        
        // UI 경고
        if (UIManager.Inst != null)
        {
            //UIManager.Inst.ShowError("ARES 하드웨어 연결 실패. VR 컨트롤을 사용하세요.");
        }
    }
    
    #endregion
    
    /// <summary>
    /// ARES 하드웨어 업데이트 - AresHardwareService 사용
    /// </summary>
    private void UpdateAresHardware()
    {
        if (aresService == null || !aresService.IsConnected) return;
        
        // 1. 하드웨어 피드백 받아서 처리
        AresFeedbackData feedback;
        if (aresService.GetLatestFeedback(out feedback))
        {
            ProcessAresFeedback(feedback);
        }
        // 라이저 입력 없으면 업데이트 스킵
        if (isInputRiser == false) return;
        
        // 2. Roll 각도 계산 (라이저 입력 기반)
        float rollAngle = 0f;
        if (isInputRiserL && !isInputRiserR)
            rollAngle = -maxRoll * leftPull;
        else if (isInputRiserR && !isInputRiserL)
            rollAngle = maxRoll * rightPull;

        // 3. 라이저 입력에 따른 목표 YAW 각도 계산
        float targetYaw = CalculateTargetYawFromRisers();
        
        // Unity 각도를 Hardware 각도로 변환 (offset 적용)
        float hardwareYaw = targetYaw - unityToHardwareOffset;
        
        // 정규화 (0-360 범위로)
        if (hardwareYaw < 0) hardwareYaw += 360f;
        if (hardwareYaw >= 360f) hardwareYaw -= 360f;
        
        var motionData = new AresMotionData
        {
            //RollAngle = rollAngle,
            YawAngle = hardwareYaw,  // Hardware 기준 절대 각도 전송
        };
        
        // 서비스로 전송 (스레드 처리는 서비스 내부에서)
        aresService.SendMotionData(motionData);
    }

    /// <summary>
    /// ARES 하드웨어 피드백 처리
    /// 실제 하드웨어 센서값으로 게임 입력 보정 및 Yaw 동기화
    /// </summary>
    private void ProcessAresFeedback(AresFeedbackData feedback)
    {
        // 라이저 입력 처리
        ProcessRiserInputs(feedback);

        // 전체 라이저 입력 상태 업데이트
        isInputRiser = isInputRiserL || isInputRiserR;

        // 하드웨어 YawPosition 항상 업데이트 (정확한 회전 계산을 위해)
        lastHardwareYaw = feedback.YawPosition;

        // Unity 동기화는 낙하산 펼친 후 라이저 입력 시에만
        if (_isPara && isInputRiser)
        {
            ProcessHardwareYawSync(feedback.YawPosition);
        }
    }

    /// <summary>
    /// 라이저 입력 처리 (분리된 메서드)
    /// </summary>
    private void ProcessRiserInputs(AresFeedbackData feedback)
    {
        // 왼쪽 라이저 입력 처리
        if (feedback.LeftRiserLength >= 20f)
        {
            float hardwareLeftInput = feedback.LeftRiserLength / 100f;
            leftPull = Mathf.Clamp01(hardwareLeftInput);
            isInputRiserL = true;
            
            Debug.Log($"[ARES Feedback] 왼쪽 라이저 하드웨어 입력: {feedback.LeftRiserLength}% -> {leftPull:F2}");
        }
        else
        {
            isInputRiserL = false;
            leftPull = 0f;  // 라이저 놓으면 값 초기화
        }
        
        // 오른쪽 라이저 입력 처리
        if (feedback.RightRiserLength >= 20f)
        {
            float hardwareRightInput = feedback.RightRiserLength / 100f;
            rightPull = Mathf.Clamp01(hardwareRightInput);
            isInputRiserR = true;
            
            Debug.Log($"[ARES Feedback] 오른쪽 라이저 하드웨어 입력: {feedback.RightRiserLength}% -> {rightPull:F2}");
        }
        else
        {
            isInputRiserR = false;
            rightPull = 0f;  // 라이저 놓으면 값 초기화
        }
    }
    
    /// <summary>
    /// 하드웨어 Yaw 동기화 처리
    /// </summary>
    private void ProcessHardwareYawSync(float hardwareYawPosition)
    {
        // 하드웨어의 절대 Yaw 위치 (0-360도)
        float hardwareYaw = hardwareYawPosition;
        
        // 현재 게임의 Yaw 위치
        float currentGameYaw = pasimPlayer.eulerAngles.y;
        
        // 초기값 기준 상대 각도로 변환
        float relativeHardwareYaw = Mathf.DeltaAngle(initialYaw, hardwareYaw);
        float relativeGameYaw = Mathf.DeltaAngle(initialYaw, currentGameYaw);
        
        // 하드웨어와 게임의 차이 계산
        float yawDifference = Mathf.Abs(Mathf.DeltaAngle(relativeGameYaw, relativeHardwareYaw));
        
        // 디버그 로그 (상세)
        if (debugYawSync && Time.frameCount % 50 == 0)  // 1초마다 출력
        {
            Debug.Log($"[ARES Yaw Sync] HW절대: {hardwareYaw:F1}° HW상대: {relativeHardwareYaw:F1}° | " +
                     $"Game절대: {currentGameYaw:F1}° Game상대: {relativeGameYaw:F1}° | 차이: {yawDifference:F1}°");
        }
        
        // 차이가 임계값을 넘으면 동기화 시작
        if (yawDifference > yawSyncThreshold)
        {
            if (debugYawSync)
            {
                Debug.Log($"[ARES Yaw Sync] 동기화 필요 - 차이: {yawDifference:F1}° (임계값: {yawSyncThreshold}°)");
            }
            
            targetHardwareYaw = hardwareYaw;
        }
        else if (yawDifference < 1f)
        {
            if (debugYawSync)
            {
                Debug.Log($"[ARES Yaw Sync] 동기화 완료");
            }
        }
        
        // 마지막 하드웨어 Yaw 저장 (변화 추적용)
        lastHardwareYaw = hardwareYaw;
    }
    
    /// <summary>
    /// 하드웨어 Yaw 부드럽게 적용 (FixedUpdate에서 호출)
    /// </summary>
    private void ApplyHardwareYawSmooth()
    {
        Vector3 currentEuler = pasimPlayer.eulerAngles;
        float currentYaw = currentEuler.y;
        
        // 각도 차이를 최소화하는 방향으로 보간
        float deltaAngle = Mathf.DeltaAngle(currentYaw, targetHardwareYaw);
        float newYaw = currentYaw + deltaAngle * Mathf.Min(1f, yawSyncSmoothSpeed * Time.fixedDeltaTime);
        
        // 새로운 회전 적용
        pasimPlayer.rotation = Quaternion.Euler(
            currentEuler.x,
            newYaw,
            currentEuler.z
        );
        
        // 목표에 도달했는지 확인
        if (Mathf.Abs(deltaAngle) < 0.5f)
        {
            if (debugYawSync && deltaAngle > 0)
            {
                Debug.Log($"[ARES Yaw Sync] 부드러운 동기화 완료 -> {newYaw:F1}°");
            }
        }
    }
    
    /// <summary>
    /// 착륙 상태 전송
    /// </summary>
    public void OnLanding()
    {
        if (aresService != null && aresService.IsConnected)
        {
            aresService.SetEvent(AresEvent.Landing);
        }
    }
    
    /// <summary>
    /// 착륙 완료 상태 전송
    /// </summary>
    public void OnLanded()
    {
        if (aresService != null && aresService.IsConnected)
        {
            aresService.SetEvent(AresEvent.Landed);
        }
    }
    
    /// <summary>
    /// 낙하산 고장 시뮬레이션
    /// </summary>
    public void TriggerMalfunction()
    {
        if (aresService != null && aresService.IsConnected)
        {
            //aresService.SetEvent(AresEvent.Malfunction);
        }
    }
    
    /// <summary>
    /// 바람 효과 설정
    /// </summary>
    public void SetWindControl(int strength, int direction, int variation)
    {
        if (aresService != null && aresService.IsConnected)
        {
            aresService.SetWindControl(strength, direction, variation);
        }
    }
    
    /// <summary>
    /// 라이저 입력에 따른 목표 Yaw 각도 계산
    /// 하드웨어가 제어할 목표 각도를 생성
    /// </summary>
    private float CalculateTargetYawFromRisers()
    {
        // 하드웨어가 연결된 경우 하드웨어 피드백 기준으로 계산
        float currentYaw;
        
        if (aresService != null && aresService.IsConnected && lastHardwareYaw >= 0)
        {
            // 하드웨어의 실제 위치를 Unity 좌표계로 변환
            currentYaw = lastHardwareYaw + unityToHardwareOffset;
            if (currentYaw >= 360f) currentYaw -= 360f;
            if (currentYaw < 0f) currentYaw += 360f;
        }
        else
        {
            // 하드웨어 미연결 시 Unity 기준
            currentYaw = pasimPlayer.eulerAngles.y;
        }
        
        // 라이저 입력에 따른 회전 증분 계산
        float turnInput = rightPull - leftPull;  // -1(좌회전) ~ +1(우회전)
        
        // 라이저 입력이 없으면 현재 각도 유지
        if (Mathf.Abs(turnInput) < 0.01f)
        {
            return currentYaw;
        }
        
        // 회전 속도 조절 (초당 최대 회전 각도)
        float maxTurnRate = 45f;  // 초당 45도 최대 회전
        float yawIncrement = turnInput * maxTurnRate * Time.fixedDeltaTime;
        
        // 목표 Yaw 계산 (현재 위치에 증분 추가)
        float targetYaw = currentYaw + yawIncrement;
        
        // 0-360도 범위로 정규화
        targetYaw = ((targetYaw % 360f) + 360f) % 360f;
        
        if (debugYawSync && Time.frameCount % 50 == 0)
        {
            Debug.Log($"[Yaw Calc] 라이저: L={leftPull:F2} R={rightPull:F2} | " +
                     $"입력값: {turnInput:F2} | 증분: {yawIncrement:F2}° | " +
                     $"현재: {currentYaw:F1}° → 목표: {targetYaw:F1}°");
        }
        
        return targetYaw;
    }
    
    #endregion
}
