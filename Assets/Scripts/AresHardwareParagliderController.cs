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
    [SerializeField] private Transform paraRotPivot;
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
    public bool isPara = false;
    public bool isSubPara = false;
    private bool isUpdate = true;
    
    // 라이저 입력 (0~1)
    [SerializeField] private float leftPull = 0f;
    [SerializeField] private float rightPull = 0f;
    [SerializeField] private bool isRiserInput = false;
    
    // 목표 각도 추적
    private float targetYaw = 0f;
    private float targetRoll = 0f;
    
    // 브레이크 효과
    private float brakeMultiplier = 1f;
    
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
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
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
        
        currentZ = pasimPlayer.eulerAngles.z;
        UpdateTransform();
        RiserDamping();
        UpdateBrakeEffect();
    }

    private void FixedUpdate()
    {
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
        if (!isPara)
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
        float rollSpeed = Mathf.Abs(turnInput * 1000f);  // 0 ~ 3000 RPM
        
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
        cachedMotionData.YawSpeed = (int)Mathf.Abs(turnInput * 3000f);

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
        // 재연결 후 기준점 재설정
        if (needYawRecalibration)
        {
            hardwareBaseYaw = feedbackData.YawPosition;
            unityBaseYaw = pasimPlayer.eulerAngles.y;
            needYawRecalibration = false;
            Debug.Log($"[Yaw Recalibration] 기준점 재설정: Unity={unityBaseYaw:F1}°, Hardware={hardwareBaseYaw:F1}°");
        }

        // 1. 라이저 입력 업데이트
        UpdateRiserInputs(feedbackData);
        
        // 2. 하드웨어로 목표 roll, yaw 전송
        CalculateAndSendTargetRotation();

        // Yaw 처리
        float hardwareYaw = feedbackData.YawPosition;

        // 하드웨어의 상대 변화량 계산 (360도 경계 자동 처리)
        float hardwareDelta = Mathf.DeltaAngle(hardwareBaseYaw, hardwareYaw);

        // Unity Yaw에 상대 변화량 적용 (반전)
        nowYaw = unityBaseYaw - hardwareDelta;

        Debug.Log($"Yaw 처리: Hardware={hardwareYaw:F1}° (Base={hardwareBaseYaw:F1}°, Delta={hardwareDelta:F1}°) → Unity={nowYaw:F1}°");

        // roll 처리
        var turnInput = leftPull - rightPull;
        
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

        // 낙하산 중심점에 롤값 적용
        paraRotPivot.localRotation = Quaternion.Euler(0, 0, newRoll);
        
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
        if (!rb) return;
        
        // 하강 속도 제어
        float sinkError = targetSinkSpeed * sinkRateGain;
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
        needYawRecalibration = true;  // 재연결 시 Yaw 기준점 재설정 필요
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