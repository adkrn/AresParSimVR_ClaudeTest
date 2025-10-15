# Unity-Hardware 각도 변환 통신 분석 보고서

## 요약
Unity와 ARES 하드웨어 간의 Yaw 각도 변환 및 통신 과정을 분석한 결과, **상대/절대 각도 혼용 문제**와 **초기 동기화 부재**로 인한 각도 불일치 문제가 발견되었습니다.

## 1. 현재 시스템 아키텍처

### 1.1 데이터 흐름 구조
```
Unity Physics → ParagliderController → AresHardwareService → AresParachuteAPI (DLL) → Hardware
       ↑                                                                                      ↓
       ←──────────────────────── Feedback Processing ←────────────────────────────────────
```

### 1.2 통신 특성
- **업데이트 주기**: 10Hz (100ms 간격)
- **스레드 구조**: Unity 메인 스레드 ↔ 하드웨어 통신 스레드
- **데이터 동기화**: Lock 기반 스레드 안전 통신

## 2. 각도 변환 프로세스 상세 분석

### 2.1 Unity → Hardware 전송 경로

#### 단계 1: Unity에서 목표 Yaw 계산 (ParagliderController.cs)
```csharp
// Line 737-744
float targetYaw = CalculateTargetYawFromRisers();  // Unity 절대 각도
float relativeYaw = Mathf.DeltaAngle(initialYaw, targetYaw);  // 상대 각도 계산

var motionData = new AresMotionData
{
    YawAngle = relativeYaw,  // ⚠️ 상대 각도 전송 (-180° ~ +180°)
    // ...
};
```

#### 단계 2: 서비스에서 API 형식으로 변환 (AresHardwareService.cs)
```csharp
// Line 570-575
// ⚠️ 문제: 상대 각도를 절대 각도로 취급
float normalizedYaw = ((data.YawAngle % 360f) + 360f) % 360f;  // 0-360으로 정규화
uint yawingValue = (uint)(normalizedYaw * 100f);  // 0-36000 스케일로 변환
```

#### 단계 3: 하드웨어 수신 및 해석
- 0-36000 범위 값을 **절대 위치 명령**으로 해석
- 하드웨어가 해당 절대 위치로 물리적 회전

### 2.2 Hardware → Unity 피드백 경로

#### 단계 1: 하드웨어 피드백 변환 (AresHardwareService.cs)
```csharp
// Line 599
YawPosition = data.YawingPosition / 100f;  // 절대 각도 (0-360°)
```

#### 단계 2: Unity에서 피드백 처리 (ParagliderController.cs)
```csharp
// Line 812-854: ProcessHardwareYawSync
float hardwareYaw = hardwareYawPosition;  // 절대 각도
float relativeHardwareYaw = Mathf.DeltaAngle(initialYaw, hardwareYaw);  // 상대 변환
float yawDifference = Mathf.Abs(Mathf.DeltaAngle(relativeGameYaw, relativeHardwareYaw));

if (yawDifference > yawSyncThreshold)
{
    targetHardwareYaw = hardwareYaw;  // Unity를 하드웨어에 동기화
}
```

## 3. 핵심 문제점

### 3.1 상대/절대 각도 혼용 🚨
**문제 상황:**
1. Unity는 `initialYaw` 기준 **상대 각도** 전송 의도
2. AresHardwareService는 이를 **절대 각도로 변환**
3. 하드웨어는 **절대 위치 명령**으로 해석

**예시 시나리오:**
```
Unity initialYaw = 90°
목표 각도 = 95° (5° 우회전)
상대 각도 = DeltaAngle(90°, 95°) = 5°
변환 후 = (5° + 360°) % 360° = 5°
하드웨어 해석 = 절대 5°로 이동 (85° 좌회전!)
```

### 3.2 초기화 시점 동기화 부재 🔄
**현재 코드 (ParagliderController.cs:264-280):**
```csharp
public void JumpStart()
{
    initialYaw = pasimPlayer.eulerAngles.y;  // Unity 각도만 저장
    // ⚠️ 하드웨어 현재 위치 확인 없음!
}
```

**문제점:**
- Unity와 하드웨어가 서로 다른 초기 각도를 가질 수 있음
- 초기 불일치가 이후 모든 회전 명령에 영향

### 3.3 라이저 입력 Yaw 계산 불일치 📐
**Unity 증분 방식:**
```csharp
float yawIncrement = turnInput * maxTurnRate * Time.fixedDeltaTime;
float targetYaw = currentYaw + yawIncrement;  // 누적 증분
```

**하드웨어 기대:**
- 절대 목표 위치
- 증분이 아닌 최종 위치값

## 4. 검증 테스트 케이스

### 테스트 1: 초기화 불일치
```
Given: Unity initialYaw = 0°, Hardware 실제 = 90°
When: JumpStart() 실행
Then: 
  - Unity는 0° 기준으로 상대 각도 계산
  - Hardware는 90°에서 시작
  - 90° 불일치 발생
```

### 테스트 2: 회전 명령 오해석
```
Given: initialYaw = 45°, 현재 = 45°
When: 우측 라이저로 30° 회전 (목표 75°)
Then:
  - Unity 전송: DeltaAngle(45°, 75°) = 30°
  - 변환 후: 30° (절대값)
  - Hardware 이동: 30°로 (15° 좌회전!)
```

### 테스트 3: 피드백 동기화 지연
```
Given: yawSyncThreshold = 5°
When: 작은 회전 누적 (< 5°씩)
Then: 동기화 트리거 안됨, 누적 오차 증가
```

## 5. 개선 방안

### 방안 1: 절대 각도 전송 방식 통일 ✅ (권장)
```csharp
// ParagliderController.cs
private void UpdateAresHardware()
{
    float targetYaw = CalculateTargetYawFromRisers();
    
    var motionData = new AresMotionData
    {
        YawAngle = targetYaw,  // 절대 각도 전송 (0-360°)
        // ...
    };
    
    aresService.SendMotionData(motionData);
}
```

**장점:**
- 명확한 절대 위치 지정
- 누적 오차 없음
- 하드웨어 해석과 일치

### 방안 2: 양방향 초기 동기화 구현 ✅ (필수)
```csharp
public void JumpStart()
{
    if (aresService != null && aresService.IsConnected)
    {
        AresFeedbackData currentFeedback;
        if (aresService.GetLatestFeedback(out currentFeedback))
        {
            // 하드웨어 현재 위치로 Unity 초기화
            float hardwareYaw = currentFeedback.YawPosition;
            pasimPlayer.rotation = Quaternion.Euler(
                pasimPlayer.eulerAngles.x,
                hardwareYaw,
                pasimPlayer.eulerAngles.z
            );
            
            initialYaw = hardwareYaw;
            targetHardwareYaw = hardwareYaw;
            
            Debug.Log($"[Sync] 하드웨어 기준 초기화: {hardwareYaw:F1}°");
        }
    }
}
```

### 방안 3: 실시간 동기화 개선
```csharp
private void ProcessHardwareYawSync(float hardwareYawPosition)
{
    // 절대 각도 직접 비교
    float yawDifference = Mathf.Abs(Mathf.DeltaAngle(
        pasimPlayer.eulerAngles.y, 
        hardwareYawPosition
    ));
    
    if (yawDifference > yawSyncThreshold)
    {
        // 더 적극적인 동기화
        targetHardwareYaw = hardwareYawPosition;
        
        // 임계값이 크면 즉시 동기화
        if (yawDifference > 30f)
        {
            pasimPlayer.rotation = Quaternion.Euler(
                pasimPlayer.eulerAngles.x,
                hardwareYawPosition,
                pasimPlayer.eulerAngles.z
            );
        }
    }
}
```

## 6. 구현 우선순위

1. **[긴급]** 양방향 초기 동기화 구현 - JumpStart 시점 동기화
2. **[높음]** 절대 각도 전송 방식 통일 - 상대/절대 혼용 제거
3. **[중간]** 실시간 동기화 개선 - 더 적극적인 피드백 반영
4. **[낮음]** 디버그 로깅 강화 - 문제 추적 용이성

## 7. 테스트 계획

### 단위 테스트
- [ ] 각도 변환 함수 정확성
- [ ] 0°, 180°, 360° 경계값 처리
- [ ] 음수 각도 처리

### 통합 테스트
- [ ] 초기화 동기화 검증
- [ ] 라이저 입력 → 하드웨어 회전 정확성
- [ ] 피드백 → Unity 동기화 속도

### 시스템 테스트
- [ ] 다양한 초기 각도에서 시작
- [ ] 연속 회전 누적 오차
- [ ] 네트워크 지연 상황

## 8. 예상 효과

- **초기화 문제 해결**: 하드웨어 회전 없이 안정적 시작
- **회전 정확도 향상**: 의도한 방향으로 정확한 회전
- **누적 오차 제거**: 장시간 사용해도 동기화 유지
- **사용자 경험 개선**: 부드럽고 예측 가능한 제어

## 9. 추가 고려사항

### 성능 최적화
- 현재 10Hz 통신 주기 적절성 검토
- 불필요한 데이터 전송 최소화
- 스레드 락 최소화

### 에러 처리
- 하드웨어 미연결 시 폴백
- 통신 실패 시 복구 전략
- 비정상 값 필터링

### 모니터링
- 실시간 각도 차이 모니터링 UI
- 통신 상태 표시
- 디버그 모드 강화

## 10. 결론

현재 Unity-Hardware 각도 변환 시스템은 **상대/절대 각도 혼용**과 **초기 동기화 부재**로 인해 의도하지 않은 하드웨어 회전이 발생하고 있습니다. 

제안된 개선 방안을 단계적으로 적용하면:
1. 초기화 시점 하드웨어 회전 문제 해결
2. 라이저 제어 정확도 향상
3. 장시간 사용 시 누적 오차 제거

이를 통해 안정적이고 예측 가능한 하드웨어 제어가 가능해질 것으로 예상됩니다.