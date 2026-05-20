using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ARES 하드웨어 우선 모드 패러글라이더 컨트롤러
/// 하드웨어 피드백만으로 Unity Transform 업데이트
/// </summary>
public class AresHardwareParagliderController : MonoBehaviour
{
    [Header("━━━ Component References ━━━")]
    [SerializeField] private Transform pasimPlayer;
    [SerializeField] private Transform paraRotPivot;
    public Rigidbody rb;
    [SerializeField] private Collider col;
    [SerializeField] private WindZone windZone;
    
    [Header("━━━ Hardware Priority Mode ━━━")]
    [SerializeField] private bool hardwarePriorityMode = true;
    [SerializeField] private bool useSmoothInterpolation = true;
    [SerializeField] private float interpolationSpeed = 10f;
    
    [Header("━━━ Physics Parameters ━━━")]
    [Tooltip("자유낙하 시 하강 속도 (m/s) - 약 60m/s = 200km/h")]
    [SerializeField] private float freeFallSinkSpeed = 60f;

    [Tooltip("낙하산 전개 후 수평 속도 (m/s)")]
    [SerializeField] private float parachuteForwardSpeed = 12f;
    [Tooltip("낙하산 전개 후 하강 속도 (m/s)")]
    [SerializeField] private float parachuteSinkSpeed = 5f;

    [SerializeField] private float yawControlStrength = 5f; // 회전 속도 추종 강도 (P 게인)
    [SerializeField] private float yawDamping = 3f; // 회전 감쇠 (D 게인)
    [SerializeField] private float fwdSpeedGain = 7f;
    [SerializeField] private float sinkRateGain = 7f;
    [SerializeField] private float selfLevelStrengthRoll = 5f;   // P-게인
    [SerializeField] private float selfLevelDampingRoll  = 5f; // D-게인
    [SerializeField] private float selfLevelStrengthPitch = 1f;
    [SerializeField] private float selfLevelDampingPitch = 1f;
    [SerializeField] private float pitchReturnStrength = 100f;
    
    [Header("━━━ Rotation Control ━━━")]
    [SerializeField] private float maxYawSpeed = 4f;  // rad/s
    [SerializeField] private float maxRoll = 20f;     // degrees
    [SerializeField] private float maxPitch = 10f;    // degrees (사용 안함)

    [Header("━━━ Roll Visual Control (VR Mode) ━━━")]
    [Tooltip("Roll 보간 시간 (라이저 입력 시)")]
    [SerializeField] private float rollSmoothTime = 0.15f;
    [Tooltip("Roll 복원 시간 (라이저 놓았을 때)")]
    [SerializeField] private float rollReturnTime = 0.3f;
    [Tooltip("Roll 입력 임계값 (이하 무시)")]
    [SerializeField] private float rollInputThreshold = 0.05f;

    [Header("━━━ Debug ━━━")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool showGUI = true;
    
    // 상태 플래그2번
    public bool isJumpStart = false;
    public bool isPara = false;
    public bool isSubPara = false;
    private bool isUpdate = true;

    [Header("━━━ Procedure (Line Twist) ━━━")]
    [Tooltip("산줄꼬임 회전 속도 (°/s). 멀미 한도 시 30 하향 — D8")]
    [SerializeField] private float _procLineTwistYawSpeed = 60f;

    [Tooltip("산줄꼬임 하드웨어 YawSpeed (ARES 단위, 기본 1500 = 평상시 max abs(turnInput*1500))")]
    [SerializeField] private float _lineTwistHardwareYawSpeed = 5000f;

    [Tooltip("산줄꼬임 sinkSpeed 가산값 (m/s). 자유낙하 속도 도달 목표 — D4. ★ Begin 호출 시 인자로 채워짐. 기본 0 (필드 toggle).")]
    [SerializeField] private float _procDropSpeedBonus = 0f;

    // volatile — background thread (HandleAresFeedback) 에서 main thread 의 세팅 가시성 보장
    private volatile int _procLineTwistYawDir = 0;       // -1=CCW, 0=정지, +1=CW
    private float _procLineTwistYawAcc = 0f;    // 누적 각도(°)
    // volatile — HandleAresFeedback(백그라운드 스레드) 가 메인 스레드 세팅 즉시 인식하도록 (_procLineTwistYawDir 동일 패턴)
    private volatile bool _leftCtrLineDisabled  = false; // 조종 입력 무효 (왼쪽) — D9 / per-side
    private volatile bool _rightCtrLineDisabled = false; // 조종 입력 무효 (오른쪽) — D9 / per-side

    // Sd2d-mini — 산줄꼬임 진입 시점 메인 forward 저장 (자세 회전 후에도 이동 방향은 메인 유지 = 비조종형 reserve 모사)
    private Vector3 _preLineTwistForwardDir = Vector3.forward;
    
    // 라이저 입력 (0~1)
    [SerializeField] private float leftPull = 0f;
    [SerializeField] private float rightPull = 0f;
    [SerializeField] private bool isRiserInput = false;
    
    // VR Hand 입력 (하드웨어 없을 때)
    private float vrLeftPull = 0f;
    private float vrRightPull = 0f;
    
    // 목표 각도 추적
    private float targetYaw = 0f;
    private float targetRoll = 0f;
    
    // 브레이크 효과
    private float brakeMultiplier = 1f;
    private float _pitch;
    private float _autoPitchTorque;
    
    // 성능 최적화: 캐시
    private float lastHardwareUpdateTime;
    private readonly float hardwareUpdateInterval = 0.02f;

    private float unityBaseYaw = 0f;        // 점프 시 Unity 기준점
    private float hardwareBaseYaw = 0f;     // 점프 시 하드웨어 기준점
    private bool needYawRecalibration = false;  // 재연결 시 기준점 리셋 플래그

    private AresEvent currentEvent = AresEvent.None;
    
    [SerializeField] private AresMotionData cachedMotionData;
    private Vector3 cachedForwardDir;
    private Vector3 cachedVelocity;

    [Header("━━━ Reserve Deploy (Sd2) ━━━")]
    [Tooltip("Begin/End 시 SetActive 토글되는 가슴 콜라이더 GameObject (Editor §4.1 ReservePullTarget)")]
    [SerializeField] private GameObject chestTrigger;
    private TriggerListener _chestTriggerListenerCache;
    private System.Action _chestTriggerSub;

    // Sd2-stage — 2단계 분리 (1차 메인 컷어웨이 → 2차 예비 전개)
    private bool _mainCutawayDone = false;
    private float _chestTriggerReactivatedAt = -1f;   // B2 — 활성 직후 0.1s 입력 무시 가드

    [Header("━━━ Harness Hardware (Contingency, Raw API Range 0~20000) ━━━")]
    [Tooltip("mainParaCut 시 어깨축 raw 값 (양쪽 동일). 0=최저, 10000=중립, 20000=최고. 실측 튜닝 필요")]
    [SerializeField, Range(0, 20000)] private int harnessDownRaw = 0;
    [Tooltip("SubParaOn 시 어깨축 raw 값 (양쪽 동일). 일반 낙하산 펴질 때 처럼 하네스 위로 상승")]
    [SerializeField, Range(0, 20000)] private int harnessUpRaw   = 20000;
    [Tooltip("하네스 모터 RPM (Table 7 CENTER=3000)")]
    [SerializeField, Range(0, 5000)]  private int harnessMotorRpm = 3000;
    // PullHarnessDown / RaiseHarnessUp 호출 시 무한 hold (ReleaseHarnessHold() 명시 호출로만 해제)
    // 시간 기반 hold 는 더 이상 지원하지 않음 — 모든 호출 site (mainParaCut/SubParaOn/산줄꼬임) 가 무한 hold 가 적합
    // volatile bool — HandleAresFeedback (백그라운드 스레드) 가 IsHarnessHoldActive 읽을 때 Unity API 호출 회피
    private volatile bool _isHarnessHoldActiveCache = false;
    private bool IsHarnessHoldActive => _isHarnessHoldActiveCache;

    void Awake()
    {
        // 컴포넌트 자동 찾기
        if (!rb) rb = pasimPlayer.GetComponent<Rigidbody>();
        if (!col) col = pasimPlayer.GetComponent<Collider>();
        
        // 캐시 초기화
        cachedMotionData = new AresMotionData();
        cachedForwardDir = Vector3.forward;
        cachedVelocity = Vector3.zero;

        // Sd2 — 씬 간 chestTrigger 자동 lookup (AppBootstrap Player rig → Main 씬 ParaCtrl 참조 불가 해결)
        if (chestTrigger == null) chestTrigger = FindReservePullTargetInScenes();
    }

    /// <summary>
    /// inactive 포함 — Resources.FindObjectsOfTypeAll 로 씬 인스턴스만 필터.
    /// AppBootstrap Player rig 의 ReservePullTarget GameObject 찾아 반환.
    /// </summary>
    private GameObject FindReservePullTargetInScenes()
    {
        var all = Resources.FindObjectsOfTypeAll<TriggerListener>();
        foreach (var t in all)
        {
            var go = t.gameObject;
            if (go.name == "ReservePullTarget"
                && go.scene.IsValid()             // prefab asset 제외
                && go.hideFlags == HideFlags.None)
            {
                Debug.Log($"[AresPara] chestTrigger 자동 lookup: {go.name} (scene={go.scene.name})");
                return go;
            }
        }
        Debug.LogWarning("[AresPara] chestTrigger 자동 lookup 실패 — ReservePullTarget GameObject 미발견");
        return null;
    }

    // [LtDiag] background thread 안전용 — Unity API 는 main thread 전용이라 캐싱 필수 (조사 종료 후 제거)
    private string _diagGoName;
    private int _diagGoInstId;

    void Start()
    {
        // [LtDiag] main thread 캐싱 (background thread 에서 Unity API 호출 회피)
        _diagGoName = gameObject.name;
        _diagGoInstId = GetInstanceID();

        // 하드웨어 이벤트 구독
        AresHardwareService.Inst.OnFeedbackReceived += HandleAresFeedback;
        AresHardwareService.Inst.OnConnectionLost += HandleConnectionLost;

        // [LtDiag-Sub] OnFeedbackReceived 구독 — 인스턴스 식별 (조사 종료 후 제거)
        Debug.Log($"[LtDiag-Sub] OnFeedbackReceived 구독 — GO={_diagGoName}#{_diagGoInstId}, scene={gameObject.scene.name}");

        // 초기 Yaw 설정
        targetYaw = 0;
        Debug.Log($"[AresPara] targetYaw 초기화 완료 {targetYaw}");

        // 물리 설정
        if (rb)
        {
            rb.useGravity = false;

            // ✅ VR/하드웨어 모두 동일: X/Z 차단, Y축(Yaw)만 자유
            // - Yaw: 물리 엔진으로 부드러운 회전
            // - Roll: paraRotPivot로 직접 제어 (안정적)
            // - Pitch: 고정 (안정성 우선)
            rb.constraints = RigidbodyConstraints.FreezeRotationX |
                             RigidbodyConstraints.FreezeRotationZ;
        }
        
        if (paraRotPivot == null)
        {
            GameObject pivotObj = GameObject.FindGameObjectWithTag("PivotRot");
            if (pivotObj != null)
            {
                paraRotPivot = pivotObj.transform;
                Debug.Log($"[CustomConstraint] PivotRot 자동 발견: {pivotObj.name}");
            }
            else
            {
                Debug.LogWarning($"[CustomConstraint] PivotRot 태그를 가진 오브젝트를 찾을 수 없습니다.");
            }
        }
        
        
    }

    void OnDestroy()
    {

        AresHardwareService.Inst.OnFeedbackReceived -= HandleAresFeedback;
        AresHardwareService.Inst.OnConnectionLost -= HandleConnectionLost;
    }

    void Update()
    {
        if (!isJumpStart) return;

        // 입력 소스 결정
        bool hardwareConnected = AresHardwareService.Inst.UseHardware &&
                                 AresHardwareService.Inst.IsConnected;

        if (!hardwareConnected)
        {
            // VR Hand 입력 사용
            leftPull = vrLeftPull;
            rightPull = vrRightPull;

            // isRiserInput 업데이트
            isRiserInput = (leftPull > 0.01f || rightPull > 0.01f);
        }
        else
        {
            currentZ = pasimPlayer.eulerAngles.z;
            UpdateTransform();
        }

        // 하드웨어 모드일 때는 HandleAresFeedback()에서 이미 leftPull/rightPull 설정됨
        RiserDamping();

        // 산줄꼬임 yaw 누적 — 본체(pasimPlayer) 직접 회전 (MC-4 CustomConstraint 이중 회전 회피)
        // 교육생/낙하산 모두 ParaRotPivot 의 자손이므로 본체가 회전하면 1배씩 따라옴.
        // _preLineTwistForwardDir 가 진행방향을 메인 그대로 유지 (RegulateForwardSpeed 분기 확장).
        if (_procLineTwistYawDir != 0 && pasimPlayer != null)
        {
            float yawStep = _procLineTwistYawDir * _procLineTwistYawSpeed * Time.deltaTime;
            _procLineTwistYawAcc += yawStep;

            float newBodyYaw = pasimPlayer.eulerAngles.y + yawStep;
            pasimPlayer.rotation = Quaternion.Euler(0f, newBodyYaw, 0f);
            nowYaw = newBodyYaw;
            unityBaseYaw += yawStep;

            // [LtDiag-B] LineTwist VR 본체 회전 누적 실측 — 1초당 1회 (main thread 안전, 조사 종료 후 제거)
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[LtDiag-B@{_diagGoInstId}] BodyRot: yawDir={_procLineTwistYawDir}, yawSpeed={_procLineTwistYawSpeed:F1}°/s, yawStep={yawStep:F3}, yawAcc={_procLineTwistYawAcc:F1}°, bodyYaw={pasimPlayer.eulerAngles.y:F1}°, unityBase={unityBaseYaw:F1}°");
            }
        }

        // F11 / F12 키보드 트리거 — 우발상황 2단계 테스트 입력
        // F11 = MainParaCut (action 1단계)  /  F12 = SubParaOn (condition 2단계)
        // 순서 강제(Q2=B): MainParaCut 우발상황에서 F11 선행 안 했으면 F12 차단
        bool inContingency = !string.IsNullOrEmpty(StateManager_New.Inst?.ActiveContingencyId ?? "");
        bool linetwistActive = _procLineTwistYawDir != 0 || _mainCutawayDone;

        // F11 — 메인 컷어웨이 (학생 즉시 조치)
        if (!_mainCutawayDone && inContingency && Input.GetKeyDown(KeyCode.F11))
        {
            Debug.Log("[AresPara] F11 → MainParaCut (학생 입력)");
            CutawayMainPara();   // action hook 은 CutawayMainPara 내부에서 발화
        }

        // F12 — 예비낙하산 전개. 우발상황 활성 중이면 action 선행 필요(순서 강제).
        if (!isSubPara && (linetwistActive || inContingency) && Input.GetKeyDown(KeyCode.F12))
        {
            if (inContingency && StateManager_New.Inst != null && !StateManager_New.Inst.IsContingencyActionPerformed)
            {
                Debug.LogWarning("[AresPara] F12 무시 — 우발상황 action(MainParaCut) 선행 필요");
            }
            else
            {
                Debug.Log("[AresPara] F12 → SubPara 전개");
                DeploySubPara();
            }
        }

        // Phase 3 — Harness Hold (단발 송신 + 라이저 path 차단 가드만)
        // SendHarnessRawCommand 가 단발 송신, 모터는 명령 위치 유지. 라이저 path 는 IsHarnessHoldActive 가드로 송신 차단.
        // (매 프레임 반복 송신은 펌웨어가 "이동 명령 재시작" 으로 해석해 모터 도달 못 함)
        _isHarnessHoldActiveCache = cachedMotionData.HasRollOverride;

        // ─── 진단 로그 (임시) — 회전 변화 시점에 입력값/차단상태 캡처. 조사 종료 후 제거. ───
        if (isJumpStart && pasimPlayer != null)
        {
            float curY = pasimPlayer.eulerAngles.y;
            float curR = paraRotPivot != null ? paraRotPivot.localEulerAngles.z : 0f;
            if (curR > 180f) curR -= 360f;

            if (Mathf.Abs(Mathf.DeltaAngle(_diagLastBodyYaw, curY)) > 0.5f
                || Mathf.Abs(curR - _diagLastPivotRoll) > 0.5f)
            {
                bool hwConn = AresHardwareService.Inst != null && AresHardwareService.Inst.UseHardware && AresHardwareService.Inst.IsConnected;
                Debug.Log($"[Diag@{_diagGoInstId}] BodyYaw {_diagLastBodyYaw:F1}→{curY:F1}, PivotRoll {_diagLastPivotRoll:F1}→{curR:F1}, leftPull={leftPull:F2}, rightPull={rightPull:F2}, vrLeft={vrLeftPull:F2}, vrRight={vrRightPull:F2}, hwConn={hwConn}, useHW={AresHardwareService.Inst?.UseHardware}, isConn={AresHardwareService.Inst?.IsConnected}, leftDis={_leftCtrLineDisabled}, rightDis={_rightCtrLineDisabled}, isSubPara={isSubPara}");
                _diagLastBodyYaw = curY;
                _diagLastPivotRoll = curR;
            }
        }
    }

    // ─── 진단 (임시) ───
    private float _diagLastBodyYaw = 0f;
    private float _diagLastPivotRoll = 0f;
    private int _diagFeedbackCount = 0;

    private void FixedUpdate()
    {
        bool hardwareConnected = AresHardwareService.Inst.UseHardware &&
                                 AresHardwareService.Inst.IsConnected;
        RegulateForwardSpeed();
        RegulateSinkRate();
        
        if (!hardwareConnected)
        {
            // VR Hand 입력 사용
            //ApplyTurning();

            ApplyTurningTransform();
        }
        
    }

    private float lastTime;
    private float yawValue = 0;
    private float rollSpeed = 0;
    private float lastRollSpeed = 0;  // 점진적 속도 감소용
    
    float minReturnSpeed = 250f;
    float followRatio = 0.3f;  // 반대쪽이 따라오는 비율

    
    // [LtDiag] 자체 카운터 — Time.frameCount/gameObject API 는 background thread 위반이라 회피 (조사 종료 후 제거)
    private int _diagCalcEntryCount = 0;
    private int _diagCalcSendCount = 0;
    private int _diagFbSyncCount = 0;
    private int _diagLtSendCount = 0;
    private int _diagGuardCheckCount = 0;
    private int _diagLastGuardState = -1;

    private void CalculateAndSendTargetRotation()
    {
        // [LtDiag-Entry] CalcSend 진입 자체 — 가드 통과 여부 확인 (10회 + 30회당 1회, 조사 종료 후 제거)
        _diagCalcEntryCount++;
        if (_diagCalcEntryCount <= 10 || _diagCalcEntryCount % 30 == 0)
        {
            Debug.Log($"[LtDiag-Entry#{_diagCalcEntryCount}@{_diagGoInstId}] CalcSend ENTRY — isPara={isPara}, isSubPara={isSubPara}, yawDir={_procLineTwistYawDir}, threadId={System.Threading.Thread.CurrentThread.ManagedThreadId}");
        }

        // 진단 (임시) — 가드 상태 변화 시점만 (이전 값과 다를 때만 1회)
        _diagGuardCheckCount++;
        int guardState = (_mainCutawayDone ? 1 : 0) | (isPara ? 2 : 0) | (isSubPara ? 4 : 0) | (IsHarnessHoldActive ? 8 : 0);
        if (guardState != _diagLastGuardState)
        {
            Debug.Log($"[GuardDiag#{_diagGuardCheckCount}] 가드 상태 변화 — _mainCutawayDone={_mainCutawayDone}, isPara={isPara}, isSubPara={isSubPara}, IsHarnessHoldActive={IsHarnessHoldActive}, threadId={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            _diagLastGuardState = guardState;
        }

        // 라이저를 당기는 중이거나 하드웨어 연동 상태에서만 실행함
        // Sd2 — isSubPara 가드 추가 (D-pre6=A 입구 가드)
        // Phase 3 — _mainCutawayDone 가드 추가: 메인 컷어웨이 후엔 메인 라이저 자체가 없으므로 motion data 송신 차단
        if (!isPara || isSubPara || _mainCutawayDone)
        {
            return;
        }

        // Phase 2 통합 — Harness Hold 활성 중에는 라이저 입력 무시 (Update 가 매 프레임 송신 중)
        if (IsHarnessHoldActive)
        {
            return;
        }

        // ★ LineTwist 우발상황 — 자동 회전을 motion data 로 송신 (라이저 입력 무시)
        if (_procLineTwistYawDir != 0)
        {
            int ltDir = _procLineTwistYawDir;  // -1=CCW, +1=CW
            cachedMotionData.RollLeftLength  = 0f;
            cachedMotionData.RollRightLength = 0f;
            cachedMotionData.RollLeftSpeed   = minReturnSpeed;
            cachedMotionData.RollRightSpeed  = minReturnSpeed;
            cachedMotionData.YawAngle = AresHardwareService.Inst.LatestFeedback.YawPosition + (5 * ltDir);
            cachedMotionData.YawSpeed = (int)_lineTwistHardwareYawSpeed;

            // [LtDiag-D] LineTwist motion data 송신 실측 (30콜백당 1회, 조사 종료 후 제거)
            _diagLtSendCount++;
            if (_diagLtSendCount <= 5 || _diagLtSendCount % 30 == 0)
            {
                Debug.Log($"[LtDiag-D#{_diagLtSendCount}@{_diagGoInstId}] LT-CalcSend: dir={ltDir}, cmd.YawAng={cachedMotionData.YawAngle:F1}, cmd.YawSpd={cachedMotionData.YawSpeed}, hwYaw={AresHardwareService.Inst.LatestFeedback.YawPosition:F1}");
            }

            AresHardwareService.Inst.SendMotionData(cachedMotionData);
            return;
        }

        // 조종줄 고장 우발상황 — 양쪽 차단 시 motion data 송신 자체 skip
        // (LeftLineFailure/RightLineFailure/BothLineFailure 등 _leftCtrLineDisabled/_rightCtrLineDisabled 가 활성일 때)
        if (_leftCtrLineDisabled && _rightCtrLineDisabled)
        {
            return;
        }

        // 한쪽 차단 시 해당 측 라이저 풀 입력 무효 → 모터 미반응
        float effectiveLeft  = _leftCtrLineDisabled  ? 0f : leftPull;
        float effectiveRight = _rightCtrLineDisabled ? 0f : rightPull;

        // 각 라이저의 실제 당김 값을 그대로 사용 (0~1 범위)
        // API에서 10000(중립) ~ 15000(최대)으로 변환됨
        cachedMotionData.RollLeftLength = effectiveLeft;   // 0 = 중립(10000), 1 = 최대(15000)
        cachedMotionData.RollRightLength = effectiveRight; // 0 = 중립(10000), 1 = 최대(15000)

        // 라이저 입력 차이로 방향과 강도 결정
        float turnInput = effectiveLeft - effectiveRight;  // -1 ~ +1

        // 속도 계산
        float rollSpeed = Mathf.Abs(turnInput * 400f);  // 0 ~ 3000 RPM
        
        lastRollSpeed = Mathf.Max(lastRollSpeed * 0.9f, minReturnSpeed);

        if (turnInput > 0)  // 왼쪽 회전중
        {
            cachedMotionData.RollRightSpeed = minReturnSpeed;
            cachedMotionData.RollLeftSpeed = rollSpeed;
            lastRollSpeed = rollSpeed;
        }
        else if (turnInput < 0)  // 오른쪽 회전중
        {
            cachedMotionData.RollLeftSpeed = minReturnSpeed;
            cachedMotionData.RollRightSpeed = rollSpeed;
            lastRollSpeed = rollSpeed;
        }
        else  // 중립 복귀
        {
            // 점진적으로 복귀 속도 감소 (최소 600 RPM 보장)
            cachedMotionData.RollLeftSpeed = minReturnSpeed;
            cachedMotionData.RollRightSpeed = minReturnSpeed;
        }

        // Yaw 값 설정
        int dir = turnInput > 0 ? 1 : (turnInput < 0 ? -1 : 0);
        cachedMotionData.YawAngle = AresHardwareService.Inst.LatestFeedback.YawPosition + (5 * dir);
        cachedMotionData.YawSpeed = (int)Mathf.Abs(turnInput * 1500f);

        // Sd2 — SendMotionData 직전 이중망 (D-pre6=A+B). 입구 가드 미경유 caller 대비.
        if (isSubPara)
        {
            cachedMotionData.RollLeftSpeed = 0;
            cachedMotionData.RollRightSpeed = 0;
            cachedMotionData.YawSpeed = 0;
        }
        // [LtDiag-A] LineTwist 활성 중 motion data 송신 실측 — 30콜백당 1회 (background-safe, 조사 종료 후 제거)
        if (_procLineTwistYawDir != 0)
        {
            _diagCalcSendCount++;
            if (_diagCalcSendCount <= 5 || _diagCalcSendCount % 30 == 0)
            {
                Debug.Log($"[LtDiag-A#{_diagCalcSendCount}@{_diagGoInstId}] CalcSend: yawDir={_procLineTwistYawDir}, yawAcc={_procLineTwistYawAcc:F1}°, leftPull={leftPull:F2}, rightPull={rightPull:F2}, turnInput={(leftPull-rightPull):F2}, cmd.RollL={cachedMotionData.RollLeftSpeed}, cmd.RollR={cachedMotionData.RollRightSpeed}, cmd.YawAng={cachedMotionData.YawAngle:F1}, cmd.YawSpd={cachedMotionData.YawSpeed}, hwYaw={AresHardwareService.Inst.LatestFeedback.YawPosition:F1}");
            }
        }
        AresHardwareService.Inst.SendMotionData(cachedMotionData);
    }

    [SerializeField] private float nowYaw;
    private float nowRoll;

    private float rollLerpValue;
    [SerializeField] private float newRollValue;

    private float currentZ;
    
    // 적용할 회전값 업데이트
    private void HandleAresFeedback(AresFeedbackData feedbackData)
    {
        // ─── 진단 (임시) — HandleAresFeedback 진입 빈도 + 입력 raw 값 ───
        if (_diagFeedbackCount < 5 || _diagFeedbackCount % 60 == 0)  // 처음 5번 + 1초당 1번
        {
            Debug.Log($"[DiagFB#{_diagFeedbackCount}@{_diagGoInstId}] LeftRiser={feedbackData.LeftRiserLength:F1}, RightRiser={feedbackData.RightRiserLength:F1}, YawPos={feedbackData.YawPosition:F1}, AresUseHW={AresHardwareService.Inst.UseHardware}, AresConn={AresHardwareService.Inst.IsConnected}");

            // [LtDiag-Thread] HandleFB 진입 시 thread + 가시성 진단 (background-safe, 조사 종료 후 제거)
            Debug.Log($"[LtDiag-Thread@{_diagGoInstId}] threadId={System.Threading.Thread.CurrentThread.ManagedThreadId}, yawDir={_procLineTwistYawDir}, isPara={isPara}, isSubPara={isSubPara}, leftDis={_leftCtrLineDisabled}, rightDis={_rightCtrLineDisabled}");
        }
        _diagFeedbackCount++;

        // 재연결 후 기준점 재설정
        if (needYawRecalibration)
        {
            hardwareBaseYaw = feedbackData.YawPosition;
            unityBaseYaw = pasimPlayer.eulerAngles.y;
            needYawRecalibration = false;
            // 하드웨어 모드로 전환
            if (rb)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotationX |
                                 RigidbodyConstraints.FreezeRotationZ;
            }
            Debug.Log($"[Yaw Recalibration] 기준점 재설정: Unity={unityBaseYaw:F1}°, Hardware={hardwareBaseYaw:F1}°");
        }

        // 1. 라이저 입력 업데이트
        UpdateRiserInputs(feedbackData);
        
        // 2. 하드웨어로 목표 roll, yaw 전송
        CalculateAndSendTargetRotation();

        // Yaw 처리
        float hardwareYaw = feedbackData.YawPosition;

        // LineTwist 활성 중 — 하드웨어 base 동기화로 delta=0 → 본체 yaw 가 LineTwist 누적분 유지
        // (양쪽 조종 차단 상태라 사용자 yaw 입력은 어차피 무효; 노이즈로 본체 회전 흔들림 방지)
        if (_procLineTwistYawDir != 0)
        {
            hardwareBaseYaw = hardwareYaw;

            // [LtDiag-C] LineTwist 중 hwBase 동기화 실측 — 30콜백당 1회 (background-safe, 조사 종료 후 제거)
            _diagFbSyncCount++;
            if (_diagFbSyncCount <= 5 || _diagFbSyncCount % 30 == 0)
            {
                Debug.Log($"[LtDiag-C#{_diagFbSyncCount}@{_diagGoInstId}] FB-Sync: hwYaw={hardwareYaw:F1}, hwBase←hwYaw, unityBase={unityBaseYaw:F1}, nowYaw={nowYaw:F1}, deltaForcedZero=true");
            }
        }

        // 하드웨어의 상대 변화량 계산 (360도 경계 자동 처리)
        float hardwareDelta = Mathf.DeltaAngle(hardwareBaseYaw, hardwareYaw);

        // Unity Yaw에 상대 변화량 적용 (반전)
        nowYaw = unityBaseYaw - hardwareDelta;

        //Debug.Log($"Yaw 처리: Hardware={hardwareYaw:F1}° (Base={hardwareBaseYaw:F1}°, Delta={hardwareDelta:F1}°) → Unity={nowYaw:F1}°");

        // roll 처리 — 조종줄 차단 시 effective 적용 (양쪽 차단 시 turnInput=0 → newRollValue=0)
        float effRollLeft  = _leftCtrLineDisabled  ? 0f : leftPull;
        float effRollRight = _rightCtrLineDisabled ? 0f : rightPull;
        var turnInput = effRollLeft - effRollRight;

        // 5% 이하는 무시
        if (Mathf.Abs(turnInput) < 0.05f)
        {
            turnInput = 0f;
        }

        newRollValue = turnInput * maxRoll;

        // // Roll 처리
        // // 롤 차이를 각도로 직접 변환
        // float rollDiff = feedbackData.RollLeft - feedbackData.RollRight;
        //
        //
        // // 목표 롤 각도 계산
        // float targetRollAngle = rollDiff * maxRoll;
        // nowRoll = targetRollAngle;
    }
    
    // 업데이트에서 회전값 적용
    private void UpdateTransform()
    {
        float currentYaw = pasimPlayer.eulerAngles.y;
        float currentRoll = paraRotPivot.localEulerAngles.z;
        
        // ✅ -180~180 범위로 변환
        if (currentRoll > 180f) currentRoll -= 360f;
        
        var newYaw = Mathf.LerpAngle(currentYaw, nowYaw, interpolationSpeed * Time.deltaTime);
        var newRoll = Mathf.LerpAngle(currentRoll, newRollValue, interpolationSpeed * Time.deltaTime);
        
        // Quaternion targetRotation = Quaternion.Euler(
        //     pasimPlayer.eulerAngles.x,
        //     newYaw,
        //     pasimPlayer.eulerAngles.z
        // );
        Quaternion targetRotation = Quaternion.Euler(0f, newYaw, 0f);

        // 낙하산 중심점에 롤값만 적용 — LineTwist yaw 는 본체(pasimPlayer)에 누적되므로 paraRotPivot yaw=0
        paraRotPivot.localRotation = Quaternion.Euler(0f, 0f, newRoll);

        // Rigidbody 사용해서 물리 엔진과 동기화
        if (rb != null)
        {
            rb.MoveRotation(targetRotation);
        }
        else
        {
            //pasimPlayer.rotation = targetRotation;
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


    private void UpdateRiserInputController()
    {
        var lValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        if (Input.GetKey(KeyCode.A) || lValue > 0.1f)
        {
            leftPull += Time.deltaTime / 4f;
            if (leftPull > 1f) leftPull = 1f;

            isRiserInput = true;
        }
        else
        {
            leftPull -= Time.deltaTime / 2f;
            if (leftPull < 0f) leftPull = 0f;

            isRiserInput = false;
        }

        var rValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        if (Input.GetKey(KeyCode.D) || rValue > 0.1f)
        {
            rightPull += Time.deltaTime / 4f;
            if (rightPull > 1f) rightPull = 1f;
            
            isRiserInput = true;
        }
        else
        {
            rightPull -= Time.deltaTime / 2f;
            if (rightPull < 0f) rightPull = 0f;
            
            isRiserInput = false;
        }
    }
    
    
    
    private void UpdateRiserInputs(AresFeedbackData feedback)
    {
        // 임계값 20% 이상만 입력으로 처리
        leftPull = feedback.LeftRiserLength >= 15f ? (feedback.LeftRiserLength - 15) / 100f : 0f;
        rightPull = feedback.RightRiserLength >= 15f ? (feedback.RightRiserLength - 15) / 100f : 0f;
    
        // 라이저 입력중인지 체크
        if (leftPull > 0 || rightPull > 0)
        {
            Debug.Log($"[AresHardwareParaCtrl] LeftRiserLength : {feedback.LeftRiserLength}, RightRiserLength : {feedback.RightRiserLength}");
            isRiserInput = true;
        }
        else
        {
            // StateManager_New.Inst.leftFollower.OnGrabEnd();
            // StateManager_New.Inst.rightFollower.OnGrabEnd();
            isRiserInput = false;
        }

        // if (leftPull > 0)
        // {
        //     StateManager_New.Inst.leftFollower.OnGrabBegin();
        // }
        //
        // if (rightPull > 0)
        // {
        //     StateManager_New.Inst.rightFollower.OnGrabBegin();
        // }
    }
    
    private void UpdateBrakeEffect()
    {
        // 양쪽 라이저 동시 당김 → 브레이크
        float brakeInputDiffer = 1f - Mathf.Abs(leftPull - rightPull);
        float brakeInputMultiplier = brakeInputDiffer * (leftPull * rightPull);
        brakeMultiplier = 1f - brakeInputMultiplier;
        //Debug.Log("[AresHardwarePara] 라이저 댐핑값 : " + brakeMultiplier);
    }
    
    private void RegulateForwardSpeed()
    {
        if (!isJumpStart || !rb) return;

        // 자유낙하 시에는 수평 속도 제어하지 않음 (수직 낙하만)
        if (!isPara) return;

        // 캐시된 벡터 재사용
        // LineTwist 활성 중에도 진입 시점 forward 유지 — 본체(pasimPlayer)가 회전해도 진행방향은 메인 유지.
        // Sd2d-mini: isSubPara/_mainCutawayDone 도 동일 처리 (비조종 reserve 모사).
        bool useCachedForward = isSubPara || _procLineTwistYawDir != 0 || _mainCutawayDone;
        cachedForwardDir = useCachedForward ? _preLineTwistForwardDir : pasimPlayer.forward;
        cachedForwardDir.y = 0f;
        cachedForwardDir.Normalize();

        cachedVelocity = rb.linearVelocity;
        float fwd = Vector3.Dot(cachedVelocity, cachedForwardDir);

        // 목표 속도 추종 (브레이크 효과 적용) - 낙하산 전개 후만
        float cmd = (parachuteForwardSpeed * fwdSpeedGain - fwd) * brakeMultiplier;
        rb.AddForce(cachedForwardDir * cmd, ForceMode.Acceleration);
    }
    
    private void RegulateSinkRate()
    {
        if (!isJumpStart || !rb) return;

        // 자유낙하 vs 낙하산 전개 속도 구분 + 산줄꼬임 가산 — S4b, T8, D4
        // ★ 활성 가드 (_procLineTwistYawDir != 0) — Begin 호출 전 가산 방지 (이중 안전망)
        // Sd2-stage — 1차 컷어웨이로 _procLineTwistYawDir=0 전이된 후에도 dropBonus 유지 (자유낙하 분위기)
        float dropBonus = (_procLineTwistYawDir != 0 || _mainCutawayDone) ? _procDropSpeedBonus : 0f;
        // 우발상황 2단계 — 컷어웨이 완료 & 예비 미전개 구간은 free fall (메인 메쉬 OFF 상태에서 5m/s 잔존 버그 수정)
        bool inFreeFall = !isPara || (_mainCutawayDone && !isSubPara);
        float targetSink = (inFreeFall ? freeFallSinkSpeed : parachuteSinkSpeed) + dropBonus;

        // 하강 속도 제어
        float sinkError = targetSink * sinkRateGain;
        rb.AddForce(Vector3.down * sinkError, ForceMode.Acceleration);
    }

    public void JumpStart()
    {
        isJumpStart = true;

        // 점프 순간의 Unity 방향을 기준점으로 저장
        unityBaseYaw = pasimPlayer.eulerAngles.y;

        // 점프 순간의 하드웨어 Yaw를 기준점으로 저장
        hardwareBaseYaw = AresHardwareService.Inst.LatestFeedback.YawPosition;

        // nowYaw를 현재 방향으로 초기화 (하드웨어 없을 때 대비)
        nowYaw = unityBaseYaw;

        Debug.Log($"[Jump Init] Unity Base: {unityBaseYaw:F1}°, Hardware Base: {hardwareBaseYaw:F1}°");

        if (rb)
        {
            rb.useGravity = true;
        }

        // 자유낙하 이벤트 — 모든 jumpType (STANDARD/HAHO/HALO)
        if (AresHardwareService.Inst.IsConnected)
        {
            AresHardwareService.Inst.SetEvent(AresEvent.FreeFall);
            currentEvent = AresEvent.FreeFall;
        }
    }

    public void ParaDeploy()
    {
        isPara = true;

        if (col) col.enabled = true;

        // 낙하산 전개 후 속도는 RegulateForwardSpeed/RegulateSinkRate에서
        // parachuteForwardSpeed와 parachuteSinkSpeed 파라미터 사용

        // 전개 이벤트
        // ⚠️ 임시 (Step 2 테스트용) — Deploy_Standard 자체 시퀀스 충돌 회피, Deploy_High 사용
        if (AresHardwareService.Inst.IsConnected)
        {
            Debug.Log("낙하산 전개 이벤트 전송 (임시 Deploy_High)");
            AresHardwareService.Inst.SetEvent(AresEvent.Deploy_High);
            currentEvent = AresEvent.Deploy_High;
        }
    }

    // Sd2-stage — 1차 메인 컷어웨이 (visual + 회전 종료 + chestTrigger 1s grace)
    public void CutawayMainPara()
    {
        if (_mainCutawayDone) return;
        if (!isPara) return;   // MainParaOff (자유낙하) — 이미 그 자세이므로 어깨 변화 없음
        _mainCutawayDone = true;

        // 진단 (임시) — CutawayMainPara 진입 시 펌웨어 cached 이벤트 출력 (background polling 마지막 값)
        if (AresHardwareService.Inst != null && AresHardwareService.Inst.IsConnected)
        {
            uint fwEvent = AresHardwareService.Inst.LastFwEvent;
            Debug.Log($"[EvtDiag-F11] CutawayMainPara 진입 시점 펌웨어 cached 이벤트 = {(AresEvent)fwEvent}({fwEvent}) — Deploy_Standard(3)/Deploy_High(4) 가 아니면 Roll 명령 무시됨");
        }

        // Phase 3 — 하드웨어 하네스 다운 (MainParaCut 만 해당, 메인 컷어웨이로 수직 자세 무너짐)
        PullHarnessDown();

        // C1 — 산줄꼬임 회전 효과 종료 (yaw 는 이미 본체에 누적되어 있으므로 state reset 만)
        if (_procLineTwistYawDir != 0)
        {
            _procLineTwistYawDir = 0;
            _procLineTwistYawAcc = 0f;
            Debug.Log("[AresPara] CutawayMainPara — LineTwist 회전 state reset (본체 yaw 유지)");
        }

        // 메인 메쉬 OFF (visual 위임)
        var character = FindAnyObjectByType<PlayCharacter>(FindObjectsInactive.Include);
        character?.CutawayMainPara();

        // chestTrigger 1s grace — 더블탭 방지 + 손 떼는 시간 확보
        if (chestTrigger != null)
        {
            chestTrigger.SetActive(false);
            StartCoroutine(ReactivateChestTriggerAfter(1f));
        }

        Debug.Log("[AresPara] CutawayMainPara — 1차 컷어웨이 완료 (예비 2차 대기)");

        // 우발상황 2단계 완료 — action(MainParaCut) hook (StateManager 가 expectedAction 일치 시 _actionPerformed=true)
        StateManager_New.Inst?.OnContingencyActionPerformed(ContingencyAction.MainParaCut);
    }

    // Sd2-stage — 1s 후 chestTrigger 자동 재활성 (B2: _chestTriggerReactivatedAt 기록)
    private IEnumerator ReactivateChestTriggerAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (isSubPara) yield break;                                       // 그 사이 2차 자동 발화
        if (_procLineTwistYawDir == 0 && !_mainCutawayDone) yield break;  // End 호출됨 (Begin reset 도 안전)
        if (chestTrigger != null)
        {
            chestTrigger.SetActive(true);
            _chestTriggerReactivatedAt = Time.time;
            Debug.Log("[AresPara] chestTrigger 재활성 (1s grace 종료)");
        }
    }

    // 산줄꼬임 절차 — 예비낙하산 state 전개 (S4a, T9)
    public void DeploySubPara()
    {
        if (isSubPara) return;       // 중복 차단

        // ★ 옵션 C (D1) — 우발상황 활성 중이면 isPara=false (TL06 FreeFall) 도 허용 (MainParaOff 그룹)
        bool inContingency = StateManager_New.Inst != null
                          && !string.IsNullOrEmpty(StateManager_New.Inst.ActiveContingencyId);
        if (!isPara && !inContingency) return;
        if (!isPara && inContingency)
        {
            isPara = true;           // reserve 펴는 시점에 비행 모드 강제 진입 — MainParaOff 그룹 (CableBreak/StaticLineBreak/HookDisconnect)
            Debug.Log("[AresPara] DeploySubPara — 우발상황 활성, isPara=true 강제 전이 (옵션 C)");
        }

        // Sd2-stage — 1차 누락 시 자동 진행 (자동 path / F12 / VR 2차 단독 호출 안전망)
        if (!_mainCutawayDone) CutawayMainPara();

        isSubPara = true;
        Debug.Log("[AresPara] DeploySubPara — isSubPara=true 전이");

        // ★ reserve 전개 시 자세 복원 — paraRotPivot roll 잔재 0 (per-side LineFailure 의 한쪽 풀 기울기 정리).
        //   LineTwist yaw 는 본체(pasimPlayer)에 누적되므로 paraRotPivot 은 항상 yaw=0 (영향 없음).
        if (paraRotPivot != null)
        {
            paraRotPivot.localRotation = Quaternion.identity;
        }

        // ★ 옵션 B — 한쪽이라도 조종 가능한 우발상황은 전개 시점 forward 사용 (사용자 회전 반영).
        //   양쪽 차단 우발상황은 BeginContingencyEffects 진입 시점 forward 보존 (옵션 A).
        //   LineTwist 는 BeginLineTwistProcedure 가 별도 캐시 (자동 회전 효과 vs 비행 방향 분리) — 본 분기 제외.
        bool isLineTwistActive = _procLineTwistYawDir != 0 || _procLineTwistYawAcc != 0f;
        bool canControl = !(_leftCtrLineDisabled && _rightCtrLineDisabled);
        if (!isLineTwistActive && canControl)
        {
            _preLineTwistForwardDir = pasimPlayer.forward;
            _preLineTwistForwardDir.y = 0f;
            _preLineTwistForwardDir.Normalize();
            if (_preLineTwistForwardDir.sqrMagnitude < 0.01f) _preLineTwistForwardDir = Vector3.forward;
            Debug.Log($"[AresPara] DeploySubPara — 전개 시점 forward 캐시 (옵션 B): {_preLineTwistForwardDir}");
        }

        // PlayCharacter 콜백 — visual 책임 위임 (S4a 범위에서는 Debug.Log only)
        // Sd2 W-fix1 — FindObjectsInactive.Include: 비활성 GO 까지 검색
        //   (기본 Exclude 시 PlayCharacter 활성 타이밍 어긋나면 null → DeployReserveRecoil 미발화)
        var character = FindAnyObjectByType<PlayCharacter>(FindObjectsInactive.Include);
        if (character == null)
            Debug.LogWarning("[AresPara] PlayCharacter 자동 lookup 실패 — DeploySubPara 시각 콜백 / DeployReserveRecoil 미발화");
        character?.DeploySubPara();

        // Sd2 — 반동 코루틴 (D-pre5=C, D-pre9=A 자동/수동 동일 path)
        if (character != null)
            character.StartCoroutine(character.DeployReserveRecoil());

        // Sd2 — 가슴 콜라이더 비활성 (재진입 방지)
        if (chestTrigger != null) chestTrigger.SetActive(false);

        // Phase 3 — 하드웨어 하네스 업 (SubParaOn — 예비낙 펴짐 시 어깨 상승)
        RaiseHarnessUp();

        // ★ 우발상황 활성 시 완료 hook — S4b LineTwistAction polling 과 등가
        //    OnContingencyConditionMet 내부 _activeContingencyId 가드(:1209)로
        //    우발상황 비활성 시 즉시 return → 절차 흐름과 충돌 없음
        StateManager_New.Inst?.OnContingencyConditionMet(ContingencyCompleteCondition.SubParaOn);
    }

    /// <summary>
    /// 일반 우발상황 효과 진입 — LineTwist 외 11종 (CableBreak/CanopyInversion/Streamer/CanopyBreak/TwoCanopy/
    /// LeftLineFailure/RightLineFailure/BothLineFailure/SuspensionLineBreak/StaticLineBreak/HookDisconnect).
    /// LineTwist 회전 효과 없이 조종 차단(per-side)/dropSpeed/chestTrigger 만 세팅.
    /// </summary>
    /// <param name="leftCtrOn">왼쪽 조종 가능 여부 (CSV leftCtrLine="ON"/"OFF")</param>
    /// <param name="rightCtrOn">오른쪽 조종 가능 여부 (CSV rightCtrLine="ON"/"OFF")</param>
    /// <param name="dropBonus">하강 속도 가산 m/s (CSV dropSpeed)</param>
    /// <param name="needCutaway">true=action=MainParaCut 즉시 메인 컷어웨이 / false=action=MainParaOff chestTrigger 만 (옵션 C 가 reserve 시점 isPara 토글)</param>
    public void BeginContingencyEffects(bool leftCtrOn, bool rightCtrOn, float dropBonus, bool needCutaway)
    {
        _leftCtrLineDisabled  = !leftCtrOn;
        _rightCtrLineDisabled = !rightCtrOn;
        _procDropSpeedBonus   = dropBonus;
        _mainCutawayDone      = false;
        _chestTriggerReactivatedAt = -1f;

        // ★ 진입 시점 forward 캐시 (옵션 A 기본) — 양쪽 차단 우발상황의 reserve 비행 방향.
        //   한쪽이라도 조종 가능하면 DeploySubPara 에서 전개 시점 forward 로 덮어씀 (옵션 B).
        _preLineTwistForwardDir = pasimPlayer.forward;
        _preLineTwistForwardDir.y = 0f;
        _preLineTwistForwardDir.Normalize();
        if (_preLineTwistForwardDir.sqrMagnitude < 0.01f) _preLineTwistForwardDir = Vector3.forward;

        Debug.Log($"[AresPara] BeginContingencyEffects — leftOn={leftCtrOn}, rightOn={rightCtrOn}, dropBonus={dropBonus}, needCutaway={needCutaway}, preForward={_preLineTwistForwardDir}");

        // chestTrigger 활성 + 이벤트 등록 (BeginLineTwistProcedure 와 동일 패턴 — idempotent)
        if (chestTrigger != null)
        {
            chestTrigger.SetActive(true);
            if (_chestTriggerListenerCache == null)
                _chestTriggerListenerCache = chestTrigger.GetComponent<TriggerListener>();
            if (_chestTriggerListenerCache != null && _chestTriggerSub == null)
            {
                _chestTriggerSub = () =>
                {
                    if (_chestTriggerReactivatedAt > 0f && Time.time - _chestTriggerReactivatedAt < 0.1f)
                    {
                        Debug.Log("[AresPara] chestTrigger 발화 무시 (B2 — 활성 직후 0.1s)");
                        return;
                    }
                    if (!_mainCutawayDone)
                    {
                        CutawayMainPara();   // action hook 은 CutawayMainPara 내부에서 발화
                    }
                    else
                    {
                        // 순서 강제(Q2=B): 우발상황 활성 중이면 action 선행 필요. 이중 안전망 — 외부 path 막기.
                        bool inCont = !string.IsNullOrEmpty(StateManager_New.Inst?.ActiveContingencyId ?? "");
                        if (inCont && StateManager_New.Inst != null && !StateManager_New.Inst.IsContingencyActionPerformed)
                        {
                            Debug.LogWarning("[AresPara] chestTrigger 2차 무시 — 우발상황 action 선행 필요");
                            return;
                        }
                        DeploySubPara();
                    }
                };
                _chestTriggerListenerCache.OnPlayerEntered += _chestTriggerSub;
            }
        }

        // needCutaway=true → 즉시 메인 컷어웨이 (action=MainParaCut, TL07 InAir 9종)
        // needCutaway=false → chestTrigger 만 활성, reserve 발화 시 옵션 C 가 isPara 자동 전이 (action=MainParaOff, TL06 FreeFall 3종)
        if (needCutaway) CutawayMainPara();
    }

    /// <summary>
    /// 일반 우발상황 효과 종료. EndLineTwistProcedure 의 yaw 이관 부분 제외 — yaw 효과 없는 우발상황용.
    /// </summary>
    public void EndContingencyEffects()
    {
        _leftCtrLineDisabled  = false;
        _rightCtrLineDisabled = false;
        _procDropSpeedBonus   = 0f;
        _mainCutawayDone      = false;
        _chestTriggerReactivatedAt = -1f;

        // chestTrigger 이벤트 해제 + 비활성 (EndLineTwistProcedure 와 동일 패턴 — idempotent)
        if (chestTrigger != null)
        {
            if (_chestTriggerListenerCache != null && _chestTriggerSub != null)
            {
                _chestTriggerListenerCache.OnPlayerEntered -= _chestTriggerSub;
                _chestTriggerSub = null;
            }
            chestTrigger.SetActive(false);
        }

        Debug.Log("[AresPara] EndContingencyEffects — 상태 원복");
    }

    /// <summary>mainParaCut 시 호출. 하네스 다운 (raw=harnessDownRaw).</summary>
    public void PullHarnessDown() => SendHarnessRawCommand(harnessDownRaw);

    /// <summary>SubParaOn 시 호출. 하네스 업 (raw=harnessUpRaw, 일반 낙하산 펴질 때 처럼).</summary>
    public void RaiseHarnessUp()  => SendHarnessRawCommand(harnessUpRaw);

    /// <summary>
    /// 무한 hold 해제 — 우발상황 종료 hook 에서 호출.
    /// override 즉시 해제 → 다음 라이저 입력 흐름이 정상 (10000~15000) 으로 모터 위치 복귀.
    /// </summary>
    public void ReleaseHarnessHold()
    {
        if (!cachedMotionData.HasRollOverride && !_isHarnessHoldActiveCache) return;
        cachedMotionData.HasRollOverride = false;
        _isHarnessHoldActiveCache        = false;
        Debug.Log("[AresPara] ReleaseHarnessHold — override 해제");
    }

    /// <summary>
    /// 양쪽 동일 raw 값 송신 헬퍼. cachedMotionData 의 override 모드 활성화 + Hold 시작.
    /// Update() 의 Hold 분기가 hold 시간 동안 매 프레임 SendMotionData 반복 송신 (라이저/펌웨어 덮어쓰기 방지).
    /// </summary>
    private void SendHarnessRawCommand(int rawValue)
    {
        if (AresHardwareService.Inst == null || !AresHardwareService.Inst.IsConnected)
        {
            Debug.Log($"[AresPara] Harness 명령 skip — 하드웨어 미연결 (raw={rawValue})");
            return;
        }

        // 무한 hold 활성화 — Update() 가 ReleaseHarnessHold() 호출 전까지 매 프레임 반복 송신
        _isHarnessHoldActiveCache   = true;
        cachedMotionData.HasRollOverride      = true;
        cachedMotionData.RollLeftRawOverride  = rawValue;
        cachedMotionData.RollRightRawOverride = rawValue;
        cachedMotionData.RollLeftSpeed        = harnessMotorRpm;
        cachedMotionData.RollRightSpeed       = harnessMotorRpm;

        // 진단 (임시) — Harness override 송신 — caller 식별
        Debug.Log($"[CallerDiag] SendMotionData 호출 — caller=SendHarnessRawCommand, override={cachedMotionData.HasRollOverride}, rawL={cachedMotionData.RollLeftRawOverride}, rawR={cachedMotionData.RollRightRawOverride}, spd={cachedMotionData.RollLeftSpeed}");
        AresHardwareService.Inst.SendMotionData(cachedMotionData);
        Debug.Log($"[AresPara] HarnessRawCommand — raw={rawValue}, hold=∞");
    }

    // 산줄꼬임 절차 — 효과 활성 (S4b, T4)
    public void BeginLineTwistProcedure(int direction, float dropBonus)
    {
        _procLineTwistYawDir = direction;   // -1 or +1 (D3 호출자 측 random)
        _procDropSpeedBonus = dropBonus;    // 기본 55f (D4)
        // D2=a — LineTwist 는 양쪽 동시 차단 (per-side 분리 후 호환)
        _leftCtrLineDisabled  = true;
        _rightCtrLineDisabled = true;
        _mainCutawayDone = false;           // Sd2-stage — 멱등성 안전망 (End 미호출 누수 회피)
        _chestTriggerReactivatedAt = -1f;   // Sd2-stage — B2 가드 리셋

        // Sd2d-mini — 메인 비행 forward 저장 (자세 회전 후에도 이동 방향은 메인 유지)
        if (pasimPlayer != null)
        {
            _preLineTwistForwardDir = pasimPlayer.forward;
            _preLineTwistForwardDir.y = 0f;
            _preLineTwistForwardDir.Normalize();
            if (_preLineTwistForwardDir.sqrMagnitude < 0.01f) _preLineTwistForwardDir = Vector3.forward;
        }

        Debug.Log($"[AresPara] BeginLineTwistProcedure — dir={direction}, dropBonus={dropBonus}, preForward={_preLineTwistForwardDir}");

        // PlayCharacter 콜백 — broken trigger (S4a OnLineTwistEnter)
        var character = FindAnyObjectByType<PlayCharacter>();
        character?.OnLineTwistEnter();

        // Sd2 — 가슴 콜라이더 활성 + 이벤트 등록 (idempotent — _chestTriggerSub null 검사)
        if (chestTrigger != null)
        {
            chestTrigger.SetActive(true);
            if (_chestTriggerListenerCache == null)
                _chestTriggerListenerCache = chestTrigger.GetComponent<TriggerListener>();
            if (_chestTriggerListenerCache != null && _chestTriggerSub == null)
            {
                // Sd2-stage — 2단계 분기 + B2 가드 (활성 직후 0.1s 입력 무시 — W-physxrearm)
                _chestTriggerSub = () =>
                {
                    if (_chestTriggerReactivatedAt > 0f && Time.time - _chestTriggerReactivatedAt < 0.1f)
                    {
                        Debug.Log("[AresPara] chestTrigger 발화 무시 (B2 — 활성 직후 0.1s)");
                        return;
                    }
                    if (!_mainCutawayDone)
                    {
                        CutawayMainPara();   // action hook 은 CutawayMainPara 내부에서 발화
                    }
                    else
                    {
                        // 순서 강제(Q2=B): 우발상황 활성 중이면 action 선행 필요. 이중 안전망 — 외부 path 막기.
                        bool inCont = !string.IsNullOrEmpty(StateManager_New.Inst?.ActiveContingencyId ?? "");
                        if (inCont && StateManager_New.Inst != null && !StateManager_New.Inst.IsContingencyActionPerformed)
                        {
                            Debug.LogWarning("[AresPara] chestTrigger 2차 무시 — 우발상황 action 선행 필요");
                            return;
                        }
                        DeploySubPara();
                    }
                };
                _chestTriggerListenerCache.OnPlayerEntered += _chestTriggerSub;
            }
        }

        TeleportTowardLandingTarget();
    }

    // 임시 — 산줄꼬임 진입 시 착륙 지점 근처(월드 -295, -75)로 XZ만 이동, Y 보존
    private void TeleportTowardLandingTarget()
    {
        Vector3 cur = pasimPlayer.position;
        Vector3 teleport = new Vector3(-295f, cur.y, -75f);
        pasimPlayer.position = teleport;

        Debug.Log($"[LtTel] 텔레포트 — from=({cur.x:F1},{cur.y:F1},{cur.z:F1}) → to=({teleport.x:F1},{teleport.y:F1},{teleport.z:F1})");
    }

    // 산줄꼬임 절차 — 효과 종료
    public void EndLineTwistProcedure()
    {
        // yaw 는 이미 본체(pasimPlayer)에 누적되어 있으므로 별도 이관 불필요.
        // state reset 만 수행 (paraRotPivot 은 어차피 yaw=0 으로 유지되어 왔음).
        Debug.Log("[AresPara] EndLineTwistProcedure — state reset (본체 yaw 유지)");

        // 4 state 원복
        _procLineTwistYawDir = 0;
        _procLineTwistYawAcc = 0f;
        _procDropSpeedBonus = 0f;
        _leftCtrLineDisabled  = false;
        _rightCtrLineDisabled = false;
        _mainCutawayDone = false;            // Sd2-stage — 다음 회기 안전망
        _chestTriggerReactivatedAt = -1f;    // Sd2-stage — B2 가드 리셋

        // Sd2 — 가슴 콜라이더 비활성 + 이벤트 해제 (idempotent — _chestTriggerSub 동일 인스턴스 -=)
        if (chestTrigger != null)
        {
            if (_chestTriggerListenerCache != null && _chestTriggerSub != null)
            {
                _chestTriggerListenerCache.OnPlayerEntered -= _chestTriggerSub;
                _chestTriggerSub = null;
            }
            chestTrigger.SetActive(false);
        }
    }

    public void OnLanding()
    {
        isJumpStart = false;
        isPara = false;
        
        // 착륙 이벤트
        if (AresHardwareService.Inst.IsConnected)
        {
            AresHardwareService.Inst.SetEvent(AresEvent.Landing);
            currentEvent = AresEvent.Landing;
        }
    }
    
    private void HandleConnectionLost()
    {
        Debug.LogWarning("[ARES] Connection lost → Unity fallback");
        hardwarePriorityMode = false;
        needYawRecalibration = true;  // 재연결 시 Yaw 기준점 재설정 필요
        // ✅ Constraints 변경 불필요: VR/하드웨어 모두 동일한 제약 사용
    }
    
    private AresEvent GetCurrentEvent()
    {
        return currentEvent;
    }
    

    [SerializeField] private float vrTurnInput = 0;

    /// <summary>
    /// 물리 기반 회전 제어 (Torque 사용)
    /// VR 모드에서 자연스러운 물리 기반 회전
    /// </summary>
    private void ApplyTurning()
    {
        if (!isPara || !rb) return;

        vrTurnInput = rightPull - leftPull;
        float targetYawSpeed = vrTurnInput * maxYawSpeed;

        // 각속도 제한 (급격한 회전 방지)
        targetYawSpeed = Mathf.Clamp(targetYawSpeed, -maxYawSpeed, maxYawSpeed);
        float currentYawSpeed = rb.angularVelocity.y;
        float yawError = targetYawSpeed - currentYawSpeed;

        // 감쇠 적용 (급격한 변화 방지)
        float yawCorrection = (yawError * yawControlStrength) - (currentYawSpeed * yawDamping);

        rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);

        // 추가: 각속도가 너무 높아지지 않도록 제한
        // Vector3 angVel = rb.angularVelocity;
        // angVel.y = Mathf.Clamp(angVel.y, -maxYawSpeed * 1.2f, maxYawSpeed * 1.2f);
        // rb.angularVelocity = angVel;
    }

    /// <summary>
    /// 트랜스폼 기반 회전 제어 (직접 회전)
    /// 하드웨어 모드처럼 각도를 직접 제어하는 방식
    /// 물리 엔진을 거치지 않고 즉각적으로 반응
    /// </summary>
    private void ApplyTurningTransform()
    {
        if (!isPara || !rb) return;
        if (isSubPara) return;                                              // Sd2 — isSubPara OR 확장 (W-pre2 회피)
        if (_leftCtrLineDisabled && _rightCtrLineDisabled) return;          // 양쪽 차단 시 전체 무효 (LineTwist/BothLineFailure 등)

        // 회전 입력 계산 — per-side 차단 시 해당 측 입력만 0 처리 (LeftLineFailure/RightLineFailure)
        float effectiveLeft  = _leftCtrLineDisabled  ? 0f : leftPull;
        float effectiveRight = _rightCtrLineDisabled ? 0f : rightPull;
        vrTurnInput = effectiveRight - effectiveLeft;

        // 5% 이하는 무시 (노이즈 제거)
        if (Mathf.Abs(vrTurnInput) < 0.05f)
        {
            vrTurnInput = 0f;
        }

        // 프레임당 회전 각도 계산
        // maxYawSpeed는 rad/s이므로 degree로 변환
        float yawDeltaDegrees = vrTurnInput * maxYawSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime;

        // 현재 Yaw 각도
        float currentYaw = pasimPlayer.eulerAngles.y;

        // 새로운 목표 Yaw 각도
        float targetYaw = currentYaw + yawDeltaDegrees;

        // 부드럽게 보간 (interpolationSpeed 사용)
        float newYaw = Mathf.LerpAngle(currentYaw, targetYaw, interpolationSpeed * Time.fixedDeltaTime);

        // Quaternion으로 회전 생성
        Quaternion targetRotation = Quaternion.Euler(0f, newYaw, 0f);

        // Rigidbody로 회전 적용 (물리 엔진과 동기화)
        rb.MoveRotation(targetRotation);

        // Roll 시각 효과 (선택적)
        float targetRollAngle = -vrTurnInput * maxRoll;
        float currentRoll = paraRotPivot.localEulerAngles.z;
        if (currentRoll > 180f) currentRoll -= 360f;

        float smoothTime = isRiserInput ? rollSmoothTime : rollReturnTime;
        float newRoll = Mathf.Lerp(currentRoll, targetRollAngle, Time.fixedDeltaTime / smoothTime);

        // LineTwist yaw 는 본체(pasimPlayer)에 누적되므로 paraRotPivot yaw=0
        paraRotPivot.localRotation = Quaternion.Euler(0f, 0f, newRoll);

        // 디버그
        if (debugMode && Mathf.Abs(vrTurnInput) > 0.05f)
        {
            Debug.Log($"[ApplyTurningTransform] Input:{vrTurnInput:F2} Yaw:{newYaw:F1}° Roll:{newRoll:F1}°");
        }
    }

    // // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // // OLD CODE - 물리 토크 기반 Roll 제어 (불규칙 회전 발생)
    // // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // /// <summary>
    // /// 라이저 줄 당김으로 인한 자연스러운 회전 조절
    // /// </summary>
    // void ApplyTurning()
    // {
    //     var angleVelo = rb.angularVelocity;
    //     var lineVelo = rb.linearVelocity;
    //
    //     // ░░ 전진 속도가 작을 경우 회전 차단
    //     float horizontalSpeed = new Vector3(lineVelo.x, 0, lineVelo.z).magnitude;
    //     if (horizontalSpeed < 2f) return;   // 거의 정지면 턴 토크 차단
    //
    //     float turnInput = rightPull - leftPull;
    //
    //     // ░░ 목표 회전 속도 계산 (Yaw)
    //     float targetYawSpeed = turnInput * maxYawSpeed;
    //     float currentYawSpeed = angleVelo.y;
    //
    //     // P-D 컨트롤로 토크 계산
    //     float yawError = targetYawSpeed - currentYawSpeed;
    //     float yawCorrection = (yawError * yawControlStrength) - (currentYawSpeed * yawDamping);
    //
    //     rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);
    //
    //     //░░ Roll Torque (좌우 기울이기)
    //     float rollTorque = 0.3f * yawCorrection;
    //     rb.AddTorque(pasimPlayer.forward * -rollTorque, ForceMode.Force);
    //
    //     //░░ Roll 제한 및 복원
    //     float localZRoll = pasimPlayer.localEulerAngles.z;
    //     if (localZRoll > 180f) localZRoll -= 360f;
    //     
    //     if (localZRoll > maxRoll)
    //         rb.AddTorque(pasimPlayer.forward * (-(localZRoll - maxRoll) * 2f), ForceMode.Force);
    //     else if (localZRoll < -maxRoll)
    //         rb.AddTorque(pasimPlayer.forward * (-(localZRoll + maxRoll) * 2f), ForceMode.Force);
    //     
    //     if (!isRiserInput)
    //     {
    //         float autoLevelTorque = (-localZRoll * selfLevelStrengthRoll) - (angleVelo.z * selfLevelDampingRoll);
    //         rb.AddTorque(pasimPlayer.forward * autoLevelTorque, ForceMode.Force);
    //     }
    //     
    //     
    //     //░░ Pitch 제한 및 복원
    //     _pitch = pasimPlayer.localEulerAngles.x;
    //     if (_pitch > 180f) _pitch -= 360f;
    //     
    //     if (_pitch > maxPitch)
    //         rb.AddTorque(pasimPlayer.right * (-(_pitch - maxPitch) * 2f), ForceMode.Force);
    //     else if (_pitch < -maxPitch)
    //         rb.AddTorque(pasimPlayer.right * (-(_pitch + maxPitch) * 2f), ForceMode.Force);
    //     
    //     if (!isRiserInput && _pitch != 0f)
    //     {
    //         _autoPitchTorque = Mathf.Max(rightPull, leftPull) * 2f;
    //         _autoPitchTorque =  _pitch * _pitch /(pitchReturnStrength * _pitch);
    //         _autoPitchTorque = (-_pitch * selfLevelStrengthPitch) - (angleVelo.x * selfLevelDampingPitch);
    //         rb.AddTorque(pasimPlayer.right * _autoPitchTorque, ForceMode.Force);
    //     }
    // }
    // // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // /// <summary>
    // /// VR 모드 낙하산 제어 (하이브리드 방식)
    // /// - Yaw: 물리 엔진 토크로 부드러운 회전
    // /// - Roll: paraRotPivot 직접 제어로 안정적 기울기 표현
    // /// </summary>
    // void ApplyTurning()
    // {
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // 1. 전진 속도 체크
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // var lineVelo = rb.linearVelocity;
    //     // float horizontalSpeed = new Vector3(lineVelo.x, 0, lineVelo.z).magnitude;
    //     //
    //     // if (horizontalSpeed < 2f)
    //     // {
    //     //     // 저속 시 회전 차단하되, Roll은 중립으로 복원
    //     //     UpdateRollVisual(0f);
    //     //     return;
    //     // }
    //
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // 2. 입력 계산
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     float turnInput = rightPull - leftPull;  // -1 ~ +1
    //
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // 3. Yaw 회전 (물리 엔진)
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     ApplyYawRotation(turnInput);
    //
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // 4. Roll 시각 표현 (직접 제어)
    //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //     // float targetRoll = CalculateTargetRoll(turnInput);
    //     // UpdateRollVisual(targetRoll);
    // }

    /// <summary>
    /// Yaw 회전 토크 적용 (물리 엔진)
    /// </summary>
    private void ApplyYawRotation(float turnInput)
    {
        if (isSubPara) return;                                              // Sd2 — isSubPara OR 확장 (W-pre2 회피)
        if (_leftCtrLineDisabled && _rightCtrLineDisabled) return;          // turnInput 은 호출자 측 effectiveLeft-Right 로 이미 처리됨

        var angleVelo = rb.angularVelocity;

        // 목표 각속도 계산
        float targetYawSpeed = turnInput * maxYawSpeed;
        float currentYawSpeed = angleVelo.y;

        // PD 제어
        float yawError = targetYawSpeed - currentYawSpeed;
        float yawCorrection = (yawError * yawControlStrength) - (currentYawSpeed * yawDamping);

        // 토크 적용 (월드 Y축)
        rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);
    }

    /// <summary>
    /// 목표 Roll 각도 계산
    /// </summary>
    private float CalculateTargetRoll(float turnInput)
    {
        if (!isRiserInput)
        {
            // 라이저 놓으면 자동 복원
            return 0f;
        }

        // 입력에 비례한 Roll 각도 (음수: 왼쪽 기울기, 양수: 오른쪽 기울기)
        float targetRoll = -turnInput * maxRoll;  // -20° ~ +20°

        // 임계값 필터링 (5% 이하 무시)
        if (Mathf.Abs(turnInput) < rollInputThreshold)
        {
            targetRoll = 0f;
        }

        return targetRoll;
    }

    /// <summary>
    /// Roll 시각 표현 업데이트 (paraRotPivot)
    /// </summary>
    private void UpdateRollVisual(float targetRoll)
    {
        // 현재 Roll 각도
        float currentRoll = paraRotPivot.localEulerAngles.z;
        if (currentRoll > 180f) currentRoll -= 360f;

        // 부드럽게 보간
        float smoothTime = isRiserInput ? rollSmoothTime : rollReturnTime;
        float newRoll = Mathf.Lerp(currentRoll, targetRoll, Time.fixedDeltaTime / smoothTime);

        // LineTwist yaw 는 본체(pasimPlayer)에 누적되므로 paraRotPivot yaw=0
        paraRotPivot.localRotation = Quaternion.Euler(0f, 0f, newRoll);

        // 디버그
        if (debugMode && Mathf.Abs(newRoll) > 0.1f)
        {
            Debug.Log($"[Roll] Input:{(rightPull - leftPull):F2} Target:{targetRoll:F1}° Current:{newRoll:F1}°");
        }
    }
    
    #region ═══ Debug GUI ═══
    
    void OnGUI()
    {
        if (!showGUI) return;
        
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.white;
        
        float y = 10;
        float spacing = 25;
        
        GUI.Label(new Rect(10, y, 400, 25), 
            $"Mode: {(hardwarePriorityMode ? "Hardware Priority" : "Unity Physics")}", style);
        y += spacing;
        
        if (hardwarePriorityMode)
        {
            GUI.Label(new Rect(10, y, 400, 25), 
                $"Target → Yaw:{targetYaw:F1}° Roll:{targetRoll:F1}°", style);
            y += spacing;
            
            GUI.Label(new Rect(10, y, 400, 25), 
                $"Actual → Yaw:{pasimPlayer.eulerAngles.y:F1}° Roll:{pasimPlayer.eulerAngles.z:F1}°", style);
            y += spacing;
        }
        
        GUI.Label(new Rect(10, y, 400, 25), 
            $"Riser → L:{leftPull*100:F0}% R:{rightPull*100:F0}%", style);
        y += spacing;
        
        GUI.Label(new Rect(10, y, 400, 25), 
            $"Brake: {(1-brakeMultiplier)*100:F0}%", style);
        y += spacing;
        
        if (rb)
        {
            float fwd = Vector3.Dot(rb.linearVelocity, pasimPlayer.forward);
            float sink = -rb.linearVelocity.y;
            GUI.Label(new Rect(10, y, 400, 25), 
                $"Speed → Fwd:{fwd:F1}m/s Sink:{sink:F1}m/s", style);
        }
    }
    
    #endregion

    //--------------------------------------------------
    // VR Input Interface
    //--------------------------------------------------

    /// <summary>
    /// VR Hand 왼쪽 riser 입력 설정
    /// 하드웨어 연결시 무시됨
    /// </summary>
    public void SetLeftPullFromVR(float value)
    {
        // 하드웨어 연결시 VR 입력 무시
        if (AresHardwareService.Inst.UseHardware &&
            AresHardwareService.Inst.IsConnected)
        {
            return;
        }

        // 진단 (임시) — 값 변화 시점 + 호출자 컨텍스트 추적
        if (Mathf.Abs(value - vrLeftPull) > 0.01f)
            Debug.Log($"[DiagSet] LEFT vrLeftPull {vrLeftPull:F3}→{value:F3}, ctgyId='{StateManager_New.Inst?.ActiveContingencyId}', t={Time.time:F2}\nStack: {System.Environment.StackTrace}");

        vrLeftPull = value;
    }

    /// <summary>
    /// VR Hand 오른쪽 riser 입력 설정
    /// 하드웨어 연결시 무시됨
    /// </summary>
    public void SetRightPullFromVR(float value)
    {
        // 하드웨어 연결시 VR 입력 무시
        if (AresHardwareService.Inst.UseHardware &&
            AresHardwareService.Inst.IsConnected)
        {
            return;
        }

        // 진단 (임시) — 값 변화 시점 + 호출자 컨텍스트 추적
        if (Mathf.Abs(value - vrRightPull) > 0.01f)
            Debug.Log($"[DiagSet] RIGHT vrRightPull {vrRightPull:F3}→{value:F3}, ctgyId='{StateManager_New.Inst?.ActiveContingencyId}', t={Time.time:F2}\nStack: {System.Environment.StackTrace}");

        vrRightPull = value;
    }
}