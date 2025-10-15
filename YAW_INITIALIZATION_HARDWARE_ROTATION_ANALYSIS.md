# Yaw 초기화 시 하드웨어 회전 문제 원인 분석

## 문제 설명
낙하 시작 시점(JumpStart)에서 Yaw를 기준값으로 초기화할 때 ARES 하드웨어가 의도하지 않게 회전하는 현상 발생

## 코드 분석 결과

### 1. 현재 구현 흐름

#### 1.1 낙하 시작 시 초기화 (ParagliderController.cs:264-280)
```csharp
public void JumpStart()
{
    isJumpStart = true;
    _firstJumpHeight = pasimPlayer.position.y;
    _lastSendTime = Time.time;
    
    // 낙하 시작 시점의 Yaw를 기준값으로 저장
    initialYaw = pasimPlayer.eulerAngles.y;  // ⚠️ Unity의 현재 Yaw 각도 저장
    Debug.Log($"[ParagliderController] 낙하 시작 기준 Yaw: {initialYaw:F1}°");
    
    // ARES 하드웨어에 자유낙하 상태 전송
    if (aresService != null && aresService.IsConnected && DataManager.Inst.scenario.jumpType != JumpType.STANDARD)
    {
        aresService.SetEvent(AresEvent.FreeFall);
    }
}
```

#### 1.2 하드웨어로 데이터 전송 (ParagliderController.cs:716-751)
```csharp
private void UpdateAresHardware()
{
    // ... 피드백 처리 ...
    
    // 라이저 입력에 따른 목표 YAW 각도 계산
    float targetYaw = CalculateTargetYawFromRisers();
    float relativeYaw = Mathf.DeltaAngle(initialYaw, targetYaw);  // ⚠️ 상대 각도 계산
    
    // 모션 데이터 준비 및 전송
    var motionData = new AresMotionData
    {
        RollAngle = rollAngle,
        YawAngle = relativeYaw,  // ⚠️ 상대 각도를 전송
        LeftRiser = leftPull,
        RightRiser = rightPull
    };
    
    aresService.SendMotionData(motionData);
}
```

#### 1.3 데이터 변환 (AresHardwareService.cs:555-590)
```csharp
private ARES_PARASIM_MOTION_EX_DATA ConvertToApiFormat(AresMotionData data)
{
    // Yawing: Yaw 각도를 0 ~ 36000 범위로 순환
    float normalizedYaw = ((data.YawAngle % 360f) + 360f) % 360f;  // ⚠️ 절대 각도로 정규화
    uint yawingValue = (uint)(normalizedYaw * 100f);
    
    // 범위를 벗어나면 순환
    if (yawingValue >= 36000) yawingValue = 0;
    else if (yawingValue < 0) yawingValue = 35999;
    
    var apiData = new ARES_PARASIM_MOTION_EX_DATA
    {
        Yawing = yawingValue,  // ⚠️ 절대 각도로 변환된 값 전송
        // ...
    };
    
    return apiData;
}
```

## 문제 원인 분석

### 핵심 문제 1: 좌표계 불일치
1. **Unity 시뮬레이션 좌표계**: 낙하 시작 시점의 Unity Yaw를 기준(initialYaw)으로 사용
2. **하드웨어 물리적 좌표계**: 하드웨어의 실제 물리적 위치는 Unity와 독립적
3. **초기화 시점 불일치**: JumpStart 시점에 하드웨어가 이미 다른 각도를 향하고 있을 가능성

### 핵심 문제 2: 상대/절대 각도 혼용
1. **ParagliderController**: 상대 각도를 계산 (Mathf.DeltaAngle 사용)
2. **AresHardwareService**: 이를 절대 각도로 취급하여 변환
3. **하드웨어 해석**: 받은 값을 절대 위치 명령으로 해석

### 핵심 문제 3: 초기 동기화 부재
1. **하드웨어 현재 위치 미확인**: JumpStart 시점에 하드웨어의 실제 Yaw 위치를 확인하지 않음
2. **일방향 초기화**: Unity의 값만 저장하고 하드웨어 상태는 고려하지 않음
3. **동기화 검증 부재**: 초기화 후 실제 동기화 상태를 검증하지 않음

## 문제 시나리오

### 시나리오 1: 초기 위치 불일치
1. Unity 시뮬레이션: Yaw = 0°
2. 하드웨어 실제 위치: Yaw = 90°
3. JumpStart: initialYaw = 0° 저장
4. 상대 각도 0° 전송 → 하드웨어가 절대 0°로 해석 → 90° 회전 발생

### 시나리오 2: 피드백 동기화 충돌
1. 하드웨어 피드백: YawPosition = 실제 물리적 위치
2. Unity 동기화: 피드백 값으로 targetHardwareYaw 설정
3. ApplyHardwareYawSmooth: Unity 회전을 하드웨어 피드백에 맞추려 시도
4. 초기값 불일치로 인한 급격한 회전

## 해결 방안

### 방안 1: 양방향 초기 동기화 (권장)
```csharp
public void JumpStart()
{
    isJumpStart = true;
    
    // 1. 하드웨어 현재 상태 먼저 확인
    if (aresService != null && aresService.IsConnected)
    {
        AresFeedbackData currentFeedback;
        if (aresService.GetLatestFeedback(out currentFeedback))
        {
            // 하드웨어의 현재 Yaw를 기준으로 설정
            float hardwareCurrentYaw = currentFeedback.YawPosition;
            
            // Unity를 하드웨어에 맞춤
            pasimPlayer.rotation = Quaternion.Euler(
                pasimPlayer.eulerAngles.x,
                hardwareCurrentYaw,
                pasimPlayer.eulerAngles.z
            );
            
            initialYaw = hardwareCurrentYaw;
            targetHardwareYaw = hardwareCurrentYaw;
            lastHardwareYaw = hardwareCurrentYaw;
            
            Debug.Log($"[ParagliderController] 하드웨어 기준으로 초기화 - Yaw: {initialYaw:F1}°");
        }
        else
        {
            // 피드백이 없으면 Unity 기준 사용
            initialYaw = pasimPlayer.eulerAngles.y;
            Debug.LogWarning($"[ParagliderController] 하드웨어 피드백 없음, Unity 기준 사용 - Yaw: {initialYaw:F1}°");
        }
        
        // 자유낙하 이벤트 전송
        if (DataManager.Inst.scenario.jumpType != JumpType.STANDARD)
        {
            aresService.SetEvent(AresEvent.FreeFall);
        }
    }
    else
    {
        // 하드웨어 연결 없으면 Unity 기준
        initialYaw = pasimPlayer.eulerAngles.y;
    }
    
    _firstJumpHeight = pasimPlayer.position.y;
    _lastSendTime = Time.time;
}
```

### 방안 2: 절대 각도 전송 방식으로 통일
```csharp
private void UpdateAresHardware()
{
    // ... 피드백 처리 ...
    
    // 절대 각도로 전송 (상대 각도가 아닌)
    float targetYaw = CalculateTargetYawFromRisers();
    
    var motionData = new AresMotionData
    {
        RollAngle = rollAngle,
        YawAngle = targetYaw,  // 절대 각도 전송
        LeftRiser = leftPull,
        RightRiser = rightPull
    };
    
    aresService.SendMotionData(motionData);
}
```

### 방안 3: 하드웨어 리셋 명령 추가
```csharp
public void JumpStart()
{
    isJumpStart = true;
    
    // 하드웨어를 중립 위치로 리셋
    if (aresService != null && aresService.IsConnected)
    {
        // 하드웨어를 0도 위치로 리셋하는 특별 명령 전송
        aresService.ResetToNeutral();  // 새 메서드 필요
        
        // 리셋 완료 대기 (약간의 지연)
        Thread.Sleep(100);
        
        // Unity도 0도로 설정
        initialYaw = 0f;
        pasimPlayer.rotation = Quaternion.Euler(
            pasimPlayer.eulerAngles.x,
            0f,
            pasimPlayer.eulerAngles.z
        );
    }
    
    // ... 나머지 초기화 코드 ...
}
```

## 권장 사항

### 단기 해결책
1. **방안 1 적용**: 양방향 초기 동기화로 하드웨어 상태를 먼저 확인
2. **디버그 로그 강화**: 초기화 시점의 모든 값을 상세히 로깅
3. **동기화 검증**: 초기화 직후 실제 동기화 상태 확인

### 장기 개선 사항
1. **좌표계 통일**: Unity와 하드웨어 간 명확한 좌표계 정의
2. **프로토콜 개선**: 상대/절대 각도 구분을 명확히 하는 프로토콜 설계
3. **캘리브레이션 기능**: 시작 전 하드웨어 캘리브레이션 루틴 추가
4. **상태 모니터링**: 실시간 하드웨어 상태 모니터링 UI 추가

## 테스트 방법

### 1. 초기화 테스트
1. 하드웨어를 다양한 초기 각도로 설정
2. JumpStart 실행
3. 회전 발생 여부 확인

### 2. 동기화 테스트
1. 초기화 후 Unity와 하드웨어 Yaw 값 비교
2. 피드백 데이터와 Unity 값 일치 확인
3. 라이저 입력 시 정상 회전 확인

### 3. 로그 분석
- initialYaw 값
- 하드웨어 피드백 YawPosition
- 전송되는 YawAngle 값
- ConvertToApiFormat의 yawingValue

## 결론

현재 문제는 Unity 시뮬레이션과 ARES 하드웨어 간의 좌표계 초기 동기화 부재와 상대/절대 각도 혼용으로 인해 발생합니다. 

권장 해결책은 JumpStart 시점에 하드웨어의 현재 상태를 먼저 읽어와 Unity를 하드웨어에 맞추는 **양방향 초기 동기화**를 구현하는 것입니다. 이를 통해 두 시스템 간의 초기 상태를 일치시키고, 이후의 모든 회전 명령이 올바르게 해석되도록 보장할 수 있습니다.