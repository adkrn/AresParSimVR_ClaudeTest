using System;
using UnityEngine;

/// <summary>
/// ARES 하드웨어 우선 모드 패러글라이더 컨트롤러
/// 하드웨어 피드백만으로 Unity Transform 업데이트
/// </summary>
public class AresHardwareParagliderController : MonoBehaviour
{
    [Header("━━━ Component References ━━━")]
    [SerializeField] private Transform pasimPlayer;
    public Rigidbody rb;
    [SerializeField] private Collider col;
    [SerializeField] private WindZone windZone;
    
    [Header("━━━ Hardware Priority Mode ━━━")]
    [SerializeField] private bool hardwarePriorityMode = true;
    [SerializeField] private bool useSmoothInterpolation = true;
    [SerializeField] private float interpolationSpeed = 10f;
    
    [Header("━━━ Physics Parameters ━━━")]
    [SerializeField] private float targetForwardSpeed = 12f;
    [SerializeField] private float targetSinkSpeed = 5f;
    [SerializeField] private float fwdSpeedGain = 7f;
    [SerializeField] private float sinkRateGain = 7f;
    
    [Header("━━━ Rotation Control ━━━")]
    [SerializeField] private float maxYawSpeed = 4f;  // rad/s
    [SerializeField] private float maxRoll = 20f;     // degrees
    [SerializeField] private float maxPitch = 10f;    // degrees
    
    [Header("━━━ Debug ━━━")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool showGUI = true;
    
    // 상태 플래그
    public bool isJumpStart = false;
    private bool isPara = false;
    private bool isUpdate = true;
    
    // 라이저 입력 (0~1)
    private float leftPull = 0f;
    private float rightPull = 0f;
    private bool isRiserInput = false;
    
    // 목표 각도 추적
    private float targetYaw = 0f;
    private float targetRoll = 0f;
    
    // 브레이크 효과
    private float brakeMultiplier = 1f;
    
    // 성능 최적화: 캐시
    private float lastHardwareUpdateTime;
    private readonly float hardwareUpdateInterval = 0.02f;

    private float currentUnityYaw = 0;
    private float unityToHardwareOffset = 0f;

    private AresEvent currentEvent = AresEvent.None;
    
    private AresMotionData cachedMotionData;
    private Vector3 cachedForwardDir;
    private Vector3 cachedVelocity;
    
    void Awake()
    {
        // 컴포넌트 자동 찾기
        if (!rb) rb = pasimPlayer.GetComponent<Rigidbody>();
        if (!col) col = pasimPlayer.GetComponent<Collider>();
        
        // 캐시 초기화
        cachedMotionData = new AresMotionData();
        cachedForwardDir = Vector3.forward;
        cachedVelocity = Vector3.zero;
    }

    void Start()
    {
        // 하드웨어 이벤트 구독
        AresHardwareService.Inst.OnFeedbackReceived += HandleAresFeedback;
        AresHardwareService.Inst.OnConnectionLost += HandleConnectionLost;

        // 초기 Yaw 설정
        targetYaw = 0;
        Debug.Log($"[AresPara] targetYaw 초기화 완료 {targetYaw}");

        // 물리 설정
        if (rb)
        {
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.None;
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
        
        UpdateTransform();
        UpdateBrakeEffect();
        RegulateForwardSpeed();
        RegulateSinkRate();
    }

    private float lastTime;
    private float yawValue = 0;
    private float rollSpeed = 0;
    private float lastRollSpeed = 0;  // 점진적 속도 감소용
    
    float minReturnSpeed = 600f;
    float followRatio = 0.3f;  // 반대쪽이 따라오는 비율

    
    private void CalculateAndSendTargetRotation()
    {
        // 라이저를 당기는 중이거나 하드웨어 연동 상태에서만 실행함
        if (!isRiserInput)
        {
            return;
        }

        // 각 라이저의 실제 당김 값을 그대로 사용 (0~1 범위)
        // API에서 10000(중립) ~ 15000(최대)으로 변환됨
        cachedMotionData.RollLeftLength = leftPull;   // 0 = 중립(10000), 1 = 최대(15000)
        cachedMotionData.RollRightLength = rightPull;  // 0 = 중립(10000), 1 = 최대(15000)

        // 라이저 입력 차이로 방향과 강도 결정
        float turnInput = leftPull - rightPull;  // -1 ~ +1

        // 속도 계산
        float rollSpeed = Mathf.Abs(turnInput * 3000f);  // 0 ~ 3000 RPM
        
        lastRollSpeed = Mathf.Max(lastRollSpeed * 0.9f, minReturnSpeed);

        if (turnInput > 0)  // 왼쪽 회전중
        {
            cachedMotionData.RollLeftSpeed = rollSpeed;
            // 반대쪽은 비례적으로 따라오되, 최소 속도 보장
            float followSpeed = rollSpeed * followRatio;
            cachedMotionData.RollRightSpeed = Mathf.Lerp(minReturnSpeed, followSpeed,Mathf.Abs(turnInput));
            lastRollSpeed = rollSpeed;
        }
        else if (turnInput < 0)  // 오른쪽 회전중
        {
            // 반대쪽은 비례적으로 따라오되, 최소 속도 보장
            float followSpeed = rollSpeed * followRatio;
            cachedMotionData.RollLeftSpeed = Mathf.Lerp(minReturnSpeed, followSpeed,Mathf.Abs(turnInput));
            cachedMotionData.RollRightSpeed = rollSpeed;
            lastRollSpeed = rollSpeed;
        }
        else  // 중립 복귀
        {
            // 점진적으로 복귀 속도 감소 (최소 600 RPM 보장)
            cachedMotionData.RollLeftSpeed = lastRollSpeed;
            cachedMotionData.RollRightSpeed = lastRollSpeed;
        }

        // Yaw 값 설정
        int dir = turnInput > 0 ? 1 : (turnInput < 0 ? -1 : 0);
        cachedMotionData.YawAngle = AresHardwareService.Inst.LatestFeedback.YawPosition + (5 * dir);
        cachedMotionData.YawSpeed = (int)Mathf.Abs(turnInput * 3000f);

        AresHardwareService.Inst.SendMotionData(cachedMotionData);
    }

    private float nowYaw;
    private float nowRoll;

    private float rollLerpValue;
    private float newRollValue;
    
    // 적용할 회전값 업데이트
    private void HandleAresFeedback(AresFeedbackData feedbackData)
    {
        UpdateRiserInputs(feedbackData);
        CalculateAndSendTargetRotation();

        // Yaw 처리
        float hardwareYaw = feedbackData.YawPosition + unityToHardwareOffset;
        // 360도 반전 (하드웨어와 Unity의 회전 방향 차이 보정)
        hardwareYaw = 360 - hardwareYaw;
        nowYaw = hardwareYaw;
        
        // roll 처리
        var turnInput = leftPull - rightPull;
        var targetRollValue = turnInput * maxRoll;
        var currentZ = pasimPlayer.eulerAngles.z;
        newRollValue = Mathf.Lerp(currentZ, targetRollValue,  Mathf.Abs(targetRollValue) * 0.1f);
        // rollLerpValue +=  targetRollValue * 0.1f;

        // // Roll 처리
        // // 롤 차이를 각도로 직접 변환
        // float rollDiff = feedbackData.RollLeft - feedbackData.RollRight;
        //
        // // 5% 이하는 무시
        // if (Mathf.Abs(rollDiff) < 0.05f)
        // {
        //     rollDiff = 0f;
        // }
        //
        // // 목표 롤 각도 계산
        // float targetRollAngle = rollDiff * maxRoll;
        // nowRoll = targetRollAngle;
    }
    
    // 업데이트에서 회전값 적용
    private void UpdateTransform()
    {
        float currentYaw = pasimPlayer.eulerAngles.y;
        // float currentRoll = pasimPlayer.eulerAngles.z;
        
        var newYaw = Mathf.LerpAngle(currentYaw, nowYaw, interpolationSpeed * Time.deltaTime);
        // var newRoll = Mathf.LerpAngle(currentRoll, nowRoll, interpolationSpeed * Time.deltaTime);
        
        pasimPlayer.rotation = Quaternion.Euler(
            pasimPlayer.eulerAngles.x,
            newYaw,
            newRollValue
        );
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
        else  isRiserInput = false;
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
        if (!isPara || !rb) return;
        
        // 캐시된 벡터 재사용
        cachedForwardDir = pasimPlayer.forward;
        cachedForwardDir.y = 0f;
        cachedForwardDir.Normalize();
        
        cachedVelocity = rb.linearVelocity;
        float fwd = Vector3.Dot(cachedVelocity, cachedForwardDir);
        
        // 목표 속도 추종 (브레이크 효과 적용)
        float cmd = (targetForwardSpeed * fwdSpeedGain - fwd) * brakeMultiplier;
        rb.AddForce(cachedForwardDir * cmd, ForceMode.Acceleration);
    }
    
    private void RegulateSinkRate()
    {
        if (!isPara || !rb) return;
        
        // 하강 속도 제어
        float sinkError = targetSinkSpeed * sinkRateGain;
        rb.AddForce(Vector3.down * sinkError, ForceMode.Acceleration);
    }

    public void JumpStart()
    {
        isJumpStart = true;
        currentUnityYaw = pasimPlayer.eulerAngles.y;

        // 하드웨어의 실제 초기 위치를 먼저 확인
        float hardwareInitialYaw = 180f; // 기본값: 중앙 위치

        hardwareInitialYaw = AresHardwareService.Inst.LatestFeedback.YawPosition;
        Debug.Log($"[Jump Init] Hardware Initial Position: {hardwareInitialYaw:F1}°");


        // Unity 좌표계와 하드웨어 좌표계 매칭
        // Unity의 현재 방향이 하드웨어의 초기 위치와 일치하도록 오프셋 설정
        unityToHardwareOffset = currentUnityYaw - hardwareInitialYaw;
        //targetYaw = currentUnityYaw;

        Debug.Log($"[Jump Init] Unity: {currentUnityYaw:F1}° " +
                  $"Hardware: {hardwareInitialYaw:F1}° " +
                  $"Offset: {unityToHardwareOffset:F1}°");

        if (rb)
        {
            rb.useGravity = true;
        }

        // 자유낙하 이벤트
        if (AresHardwareService.Inst.IsConnected && DataManager.Inst.scenario.jumpType != JumpType.STANDARD)
        {
            AresHardwareService.Inst.SetEvent(AresEvent.FreeFall);
            currentEvent = AresEvent.FreeFall;
        }
    }

    public void ParaDeploy()
    {
        isPara = true;
        
        if (col) col.enabled = true;
        
        targetForwardSpeed = 12f;
        targetSinkSpeed = 5f;
        
        // 전개 이벤트
        if (AresHardwareService.Inst.IsConnected)
        {
            AresHardwareService.Inst.SetEvent(AresEvent.Deploy_Standard);
            currentEvent = AresEvent.Deploy_Standard;
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
    }
    
    private AresEvent GetCurrentEvent()
    {
        return currentEvent;
    }
    
    private void UnityPhysicsFallback()
    {
        // 기본 Unity 물리 시뮬레이션
        if (!isPara) return;
        
        UpdateBrakeEffect();
        RegulateForwardSpeed();
        RegulateSinkRate();
        ApplyTurning();
    }
    
    private void ApplyTurning()
    {
        if (!rb) return;
        
        float turnInput = leftPull - rightPull;
        float targetYawSpeed = turnInput * maxYawSpeed;
        float currentYawSpeed = rb.angularVelocity.y;
        
        float yawError = targetYawSpeed - currentYawSpeed;
        float yawCorrection = yawError * 5f - currentYawSpeed * 3f;
        
        rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);
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
}