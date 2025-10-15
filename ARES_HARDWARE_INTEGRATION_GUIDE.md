# ARES 하드웨어-시뮬레이터 연동 구현 가이드

## 📌 개요
이 문서는 ARES 낙하산 시뮬레이터 하드웨어와 Unity VR 시뮬레이션 소프트웨어 간의 연동 구현을 위한 상세 가이드입니다.

## 🏗️ 시스템 아키텍처

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐     ┌──────────────┐
│  VR 헤드셋/     │────▶│  Unity 시뮬레이터 │────▶│ ARES Hardware   │────▶│ ARES Motion  │
│  컨트롤러       │◀────│                  │◀────│    Service      │◀────│   Platform   │
└─────────────────┘     └──────────────────┘     └─────────────────┘     └──────────────┘
                               │                          │
                               ▼                          ▼
                        ┌──────────────┐           ┌─────────────┐
                        │   Network    │           │   DLL API   │
                        │    Sync      │           │  (COM Port) │
                        └──────────────┘           └─────────────┘
```

## 📋 목차
1. [사전 준비사항](#1-사전-준비사항)
2. [초기 설정 및 연결](#2-초기-설정-및-연결)
3. [실시간 데이터 동기화](#3-실시간-데이터-동기화)
4. [게임 로직 통합](#4-게임-로직-통합)
5. [양방향 동기화](#5-양방향-동기화)
6. [이벤트 처리](#6-이벤트-처리)
7. [에러 처리 및 복구](#7-에러-처리-및-복구)
8. [최적화 및 성능](#8-최적화-및-성능)
9. [테스트 및 검증](#9-테스트-및-검증)

---

## 1. 사전 준비사항

### 1.1 하드웨어 요구사항
- ARES 낙하산 시뮬레이터 모션 플랫폼
- RS-232/USB 시리얼 통신 포트
- Windows 10/11 (64-bit)
- Meta Quest 2/3/Pro VR 헤드셋

### 1.2 소프트웨어 요구사항
- Unity 6.1 (2023.x)
- ARES SDK Ver 0205 이상
- ARESParaSimDllMotionExternC.dll
- Visual C++ 2015-2022 재배포 가능 패키지

### 1.3 프로젝트 설정
```csharp
// Unity 프로젝트 설정
Platform: Android (Meta Quest)
XR Plugin: Meta XR SDK v74.0.3
Rendering: Universal Render Pipeline (URP)
```

---

## 2. 초기 설정 및 연결

### 2.1 하드웨어 초기화 코드

```csharp
public class AresHardwareService : MonoBehaviour
{
    [Header("Hardware Settings")]
    [SerializeField] private uint comPort = 0;     // COM1
    [SerializeField] private uint timeout = 1000;  // 1초
    
    private bool isConnected = false;
    private Thread communicationThread;
    
    public bool Initialize()
    {
        // 1. DLL 로드 및 환경 확인
        Debug.Log($"[ARES] 초기화 시작 - COM{comPort + 1}");
        Debug.Log($"[ARES] 플랫폼: {Application.platform}");
        Debug.Log($"[ARES] Unity 버전: {Application.unityVersion}");
        
        try
        {
            // 2. 하드웨어 연결
            isConnected = AresParachuteAPI.ARESParaSIM__Initial(comPort, timeout);
            
            if (isConnected)
            {
                Debug.Log($"[ARES] COM{comPort + 1} 연결 성공");
                
                // 3. 하드웨어 중립 위치로 리셋
                ResetHardware();
                
                // 4. 통신 스레드 시작 (10Hz)
                StartCommunicationThread();
                
                return true;
            }
            else
            {
                Debug.LogError($"[ARES] COM{comPort + 1} 연결 실패");
                return false;
            }
        }
        catch (DllNotFoundException e)
        {
            Debug.LogError($"[ARES] DLL을 찾을 수 없습니다: {e.Message}");
            return false;
        }
    }
    
    private void ResetHardware()
    {
        var neutralData = new ARES_PARASIM_MOTION_EX_DATA();
        neutralData.Init(); // Roll=10000, Yaw=18000 (중앙값)
        
        ARES_PARASIM_FEEDBACK_EX_DATA feedback;
        AresParachuteAPI.ARESParaSIM__MotionControlEx(ref neutralData, out feedback);
        
        Debug.Log("[ARES] 하드웨어 중립 위치 설정 완료");
    }
}
```

### 2.2 통신 스레드 구현

```csharp
private void StartCommunicationThread()
{
    threadRunning = true;
    communicationThread = new Thread(CommunicationLoop)
    {
        IsBackground = true,
        Name = "ARES Communication Thread"
    };
    communicationThread.Start();
    Debug.Log("[ARES] 통신 스레드 시작 (10Hz)");
}

private void CommunicationLoop()
{
    ARES_PARASIM_MOTION_EX_DATA localMotionData = new();
    localMotionData.Init();
    
    while (threadRunning)
    {
        try
        {
            // 1. Unity 데이터 수집
            lock (dataLock)
            {
                if (hasNewMotionData)
                {
                    localMotionData = ConvertToApiFormat(outgoingData);
                    hasNewMotionData = false;
                }
            }
            
            // 2. 하드웨어와 통신
            ARES_PARASIM_FEEDBACK_EX_DATA feedbackData;
            bool success = AresParachuteAPI.ARESParaSIM__MotionControlEx(
                ref localMotionData, 
                out feedbackData
            );
            
            if (success)
            {
                // 3. 피드백 처리
                ProcessFeedback(feedbackData);
            }
            
            Thread.Sleep(20); // 50Hz → 실제 10Hz
        }
        catch (Exception e)
        {
            Debug.LogError($"[ARES] 통신 오류: {e.Message}");
        }
    }
}
```

---

## 3. 실시간 데이터 동기화

### 3.1 데이터 구조체 정의

```csharp
// SDK 구조체 (Ver 0205)
[StructLayout(LayoutKind.Sequential)]
public struct ARES_PARASIM_MOTION_EX_DATA
{
    public uint Roll;                // 0-20000 (중심 10000)
    public uint RollSpeed;            // 0-6000 RPM
    public uint Yawing;               // 0-36000 (중심 18000)
    public uint YawingSpeed;          // 0-6000 RPM
    public uint YawingMode;           // 0-5 (회전 모드)
    public uint LTRiserLineStrength;  // 0-100%
    public uint RTRiserLineStrength;  // 0-100%
    public uint Event;                // 0-5 (이벤트)
}

[StructLayout(LayoutKind.Sequential)]
public struct ARES_PARASIM_FEEDBACK_EX_DATA
{
    public uint RollPosition;              // 0-20000 (Ver 0205 추가)
    public uint YawingPosition;            // 0-36000
    public uint LTRiserLineCurrentLength;  // 0-100%
    public uint LTRiserLineDetect;         // 0 or 1
    public uint RTRiserLineCurrentLength;  // 0-100%
    public uint RTRiserLineDetect;         // 0 or 1
}
```

### 3.2 각도 변환 공식 상세 설명

#### Roll 각도 변환 수학적 유도

**Unity → Hardware (Roll: -180°~+180° → 0~20000)**

```
목표: Unity의 Roll 각도를 하드웨어 값으로 변환
Unity Range: -180° ~ +180° (총 360°)
Hardware Range: 0 ~ 20000 (중심값 10000)

수학적 관계식:
1. 중심값 매핑: Unity 0° = Hardware 10000
2. 범위 매핑: Unity ±180° = Hardware 범위 ±10000

변환 공식 유도:
Unity -180° → Hardware 0     (10000 - 10000)
Unity 0°     → Hardware 10000 (10000 + 0)
Unity +180° → Hardware 20000  (10000 + 10000)

일반식: Hardware_Value = 10000 + (Unity_Angle × 10000 / 180)

검증:
Unity -180° → 10000 + (-180 × 10000/180) = 10000 - 10000 = 0 ✓
Unity 0°     → 10000 + (0 × 10000/180) = 10000 ✓
Unity +180° → 10000 + (180 × 10000/180) = 10000 + 10000 = 20000 ✓
```

**Hardware → Unity (Roll: 0~20000 → -180°~+180°)**

```
역변환 공식 유도:
Hardware 0     → Unity -180°
Hardware 10000 → Unity 0°
Hardware 20000 → Unity +180°

Step 1: Hardware 값을 0~1 범위로 정규화
normalized = Hardware_Value / 20000

Step 2: 0~1을 0°~360° 범위로 변환
angle_360 = normalized × 360°

Step 3: 0°~360°를 -180°~+180° 범위로 조정
final_angle = angle_360 - 180°

일반식: Unity_Angle = (Hardware_Value / 20000 × 360) - 180

검증:
Hardware 0     → (0/20000 × 360) - 180 = 0 - 180 = -180° ✓
Hardware 10000 → (10000/20000 × 360) - 180 = 180 - 180 = 0° ✓
Hardware 20000 → (20000/20000 × 360) - 180 = 360 - 180 = +180° ✓
```

#### Yaw 각도 변환 수학적 유도

**Unity → Hardware (Yaw: 0°~360° → 0~36000)**

```
목표: Unity의 Yaw 각도를 하드웨어 값으로 변환
Unity Range: 0° ~ 360°
Hardware Range: 0 ~ 36000

수학적 관계식:
비율 = 36000 / 360 = 100
즉, 1° = 100 units

변환 공식:
Hardware_Value = Unity_Angle × 100

검증:
Unity 0°   → 0 × 100 = 0 ✓
Unity 90°  → 90 × 100 = 9000 ✓
Unity 180° → 180 × 100 = 18000 ✓
Unity 270° → 270 × 100 = 27000 ✓
Unity 360° → 360 × 100 = 36000 (→ 0으로 처리) ✓
```

**Hardware → Unity (Yaw: 0~36000 → 0°~360°)**

```
역변환 공식:
Unity_Angle = Hardware_Value / 100

검증:
Hardware 0     → 0 / 100 = 0° ✓
Hardware 9000  → 9000 / 100 = 90° ✓
Hardware 18000 → 18000 / 100 = 180° ✓
Hardware 27000 → 27000 / 100 = 270° ✓
Hardware 36000 → 36000 / 100 = 360° (→ 0°로 처리) ✓
```

### 3.3 Unity → Hardware 데이터 변환 구현

```csharp
private ARES_PARASIM_MOTION_EX_DATA ConvertToApiFormat(AresMotionData data)
{
    // ========== Roll 변환: -180°~+180° → 0~20000 ==========
    // Step 1: Unity 각도를 -180~+180 범위로 정규화
    float normalizedRoll = data.RollAngle;
    while (normalizedRoll > 180f) normalizedRoll -= 360f;
    while (normalizedRoll < -180f) normalizedRoll += 360f;
    
    // Step 2: 변환 공식 적용
    // Hardware_Value = 10000 + (Unity_Angle × 10000 / 180)
    float rollValue = 10000f + (normalizedRoll * 10000f / 180f);
    
    // Step 3: 안전 범위 클램핑
    rollValue = Mathf.Clamp(rollValue, 0f, 20000f);
    
    // ========== Yaw 변환: 0°~360° → 0~36000 ==========
    // Step 1: Unity 각도를 0~360 범위로 정규화
    float normalizedYaw = ((data.YawAngle % 360f) + 360f) % 360f;
    
    // Step 2: 변환 공식 적용
    // Hardware_Value = Unity_Angle × 100
    uint yawingValue = (uint)(normalizedYaw * 100f);
    
    // Step 3: 360도 경계 처리
    if (yawingValue >= 36000) yawingValue = 0;
    
    return new ARES_PARASIM_MOTION_EX_DATA
    {
        Roll = (uint)rollValue,
        Yawing = yawingValue,
        LTRiserLineStrength = (uint)(Mathf.Clamp01(data.LeftRiser) * 100f),
        RTRiserLineStrength = (uint)(Mathf.Clamp01(data.RightRiser) * 100f),
        RollSpeed = 3000,      // 기본 속도
        YawingSpeed = 3000,    // 기본 속도
        YawingMode = 0,        // Near 모드
        Event = data.Event
    };
}
```

### 3.4 Hardware → Unity 데이터 변환 구현

```csharp
private AresFeedbackData ConvertFromApiFormat(ARES_PARASIM_FEEDBACK_EX_DATA data)
{
    return new AresFeedbackData
    {
        // ========== Roll 변환: 0~20000 → -180°~+180° ==========
        // 공식: Unity_Angle = (Hardware_Value / 20000 × 360) - 180
        RollPosition = (data.RollPosition / 20000f * 360f) - 180f,
        
        // ========== Yaw 변환: 0~36000 → 0°~360° ==========
        // 공식: Unity_Angle = Hardware_Value / 100
        YawPosition = data.YawingPosition / 100f,
        
        // 라이저 길이: 0~100% (변환 불필요)
        LeftRiserLength = data.LTRiserLineCurrentLength,
        RightRiserLength = data.RTRiserLineCurrentLength,
        
        // 라이저 감지 여부: 0/1 → bool
        LeftRiserDetected = data.LTRiserLineDetect > 0,
        RightRiserDetected = data.RTRiserLineDetect > 0
    };
}
```

### 3.5 변환 예시 및 검증

#### Roll 변환 예시

| Unity Roll | 계산 과정 | Hardware Value |
|------------|-----------|----------------|
| -180° | 10000 + (-180 × 10000/180) = 10000 - 10000 | 0 |
| -90° | 10000 + (-90 × 10000/180) = 10000 - 5000 | 5000 |
| 0° | 10000 + (0 × 10000/180) = 10000 + 0 | 10000 |
| +90° | 10000 + (90 × 10000/180) = 10000 + 5000 | 15000 |
| +180° | 10000 + (180 × 10000/180) = 10000 + 10000 | 20000 |

#### Yaw 변환 예시

| Unity Yaw | 계산 과정 | Hardware Value |
|-----------|-----------|----------------|
| 0° | 0 × 100 | 0 |
| 45° | 45 × 100 | 4500 |
| 90° | 90 × 100 | 9000 |
| 180° | 180 × 100 | 18000 |
| 270° | 270 × 100 | 27000 |
| 360° | 360 × 100 | 36000 → 0 |

#### 변환 검증 테스트 코드

```csharp
[ContextMenu("Test Angle Conversions")]
private void TestAngleConversions()
{
    Debug.Log("===== 각도 변환 테스트 =====");
    
    // Roll 테스트 케이스
    float[] testRolls = { -180f, -90f, 0f, 90f, 180f };
    foreach (float roll in testRolls)
    {
        // Unity → Hardware
        uint hardwareRoll = ConvertRollToHardware(roll);
        // Hardware → Unity
        float unityRoll = ConvertRollToUnity(hardwareRoll);
        
        Debug.Log($"Roll: {roll}° → HW:{hardwareRoll} → Unity:{unityRoll}°");
        Debug.Assert(Mathf.Abs(roll - unityRoll) < 0.01f, 
            $"Roll 변환 오류: {roll} != {unityRoll}");
    }
    
    // Yaw 테스트 케이스
    float[] testYaws = { 0f, 90f, 180f, 270f, 360f };
    foreach (float yaw in testYaws)
    {
        // Unity → Hardware
        uint hardwareYaw = ConvertYawToHardware(yaw);
        // Hardware → Unity
        float unityYaw = ConvertYawToUnity(hardwareYaw);
        
        Debug.Log($"Yaw: {yaw}° → HW:{hardwareYaw} → Unity:{unityYaw}°");
        Debug.Assert(Mathf.Abs(yaw % 360f - unityYaw) < 0.01f, 
            $"Yaw 변환 오류: {yaw} != {unityYaw}");
    }
    
    Debug.Log("===== 변환 테스트 완료 =====");
}

private uint ConvertRollToHardware(float unityAngle)
{
    // -180~+180 범위로 정규화
    float normalized = ((unityAngle % 360f) + 360f) % 360f;
    if (normalized > 180f) normalized -= 360f;
    
    // 변환 공식 적용
    return (uint)Mathf.Clamp(10000 + (normalized * 10000f / 180f), 0, 20000);
}

private float ConvertRollToUnity(uint hardwareValue)
{
    // 역변환 공식 적용
    return (hardwareValue / 20000f * 360f) - 180f;
}

private uint ConvertYawToHardware(float unityAngle)
{
    // 0~360 범위로 정규화
    float normalized = ((unityAngle % 360f) + 360f) % 360f;
    
    // 변환 공식 적용
    uint value = (uint)(normalized * 100f);
    return value >= 36000 ? 0 : value;
}

private float ConvertYawToUnity(uint hardwareValue)
{
    // 역변환 공식 적용
    return hardwareValue / 100f;
}
```

---

## 🔄 양방향 피드백 루프 (4단계 연동 프로세스)

### 연동 과정 흐름도

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          4단계 양방향 피드백 루프                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1단계: 하드웨어 → Unity (라이저 입력)                                        │
│  ┌──────────────┐        ┌──────────────────┐        ┌──────────────┐     │
│  │ 물리적 라이저  │───────▶│   FEEDBACK_DATA   │───────▶│ Unity 수신   │     │
│  │    당김       │        │ LT/RTRiserLength │        │ leftPull     │     │
│  └──────────────┘        └──────────────────┘        └──────────────┘     │
│                                                              ▼              │
│                                                                             │
│  2단계: Unity → 하드웨어 (회전 명령)                                          │
│  ┌──────────────┐        ┌──────────────────┐        ┌──────────────┐     │
│  │ 물리 시뮬레이션│───────▶│   MOTION_DATA    │───────▶│ 하드웨어 명령 │     │
│  │ ApplyTurning │        │  Roll, Yawing    │        │   회전 시작   │     │
│  └──────────────┘        └──────────────────┘        └──────────────┘     │
│                                                              ▼              │
│                                                                             │
│  3단계: 하드웨어 → Unity (실제 위치 피드백)                                    │
│  ┌──────────────┐        ┌──────────────────┐        ┌──────────────┐     │
│  │ 물리적 회전   │───────▶│   FEEDBACK_DATA   │───────▶│ Unity 수신   │     │
│  │   수행 완료   │        │ Roll/YawPosition │        │ 위치 데이터   │     │
│  └──────────────┘        └──────────────────┘        └──────────────┘     │
│                                                              ▼              │
│                                                                             │
│  4단계: Unity 화면 동기화                                                    │
│  ┌──────────────┐        ┌──────────────────┐        ┌──────────────┐     │
│  │ 위치 보정     │───────▶│ Transform 업데이트│───────▶│  화면 갱신    │     │
│  │ProcessPosSync│        │ Rotation 조정     │        │  동기화 완료  │     │
│  └──────────────┘        └──────────────────┘        └──────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 각 단계별 상세 구현

#### 1단계: 하드웨어 라이저 입력 처리
```csharp
// AresHardwareService에서 피드백 수신
private void ProcessFeedback(ARES_PARASIM_FEEDBACK_EX_DATA feedbackData)
{
    var feedback = ConvertFromApiFormat(feedbackData);
    
    // 라이저 입력 데이터 (0~100%)
    // LTRiserLineCurrentLength: 왼쪽 라이저 당김 정도
    // RTRiserLineCurrentLength: 오른쪽 라이저 당김 정도
    
    lock (dataLock)
    {
        incomingFeedback = feedback;
        hasNewFeedback = true;
    }
    
    // Unity 메인 스레드로 이벤트 전달
    OnFeedbackReceived?.Invoke(feedback);
}
```

#### 2단계: Unity에서 하드웨어로 회전 명령 전송 (개선된 버전)
```csharp
// ParagliderController에서 목표 회전값 계산 및 전송
void CalculateAndSendTargetRotation()
{
    // 라이저 입력 기반 목표 회전 계산
    float turnInput = rightPull - leftPull;  // -1 ~ +1
    
    // 목표 Yaw 각도 계산 (누적)
    targetYaw += turnInput * maxYawSpeed * Time.fixedDeltaTime;
    targetYaw = Mathf.Repeat(targetYaw, 360f);  // 0~360 범위 유지
    
    // 목표 Roll 각도 계산
    float targetRoll = turnInput * maxRoll;  // -20° ~ +20°
    
    // 하드웨어로 목표 위치 전송
    var motionData = new AresMotionData
    {
        YawAngle = targetYaw,        // 목표 Yaw 각도
        RollAngle = targetRoll,      // 목표 Roll 각도
        LeftRiser = leftPull,        // 현재 라이저 입력
        RightRiser = rightPull,      // 현재 라이저 입력
        Event = currentEvent
    };
    
    // 하드웨어로 전송 (Unity 자체는 회전하지 않음)
    aresService?.SendMotionData(motionData);
    
    // Unity Transform은 업데이트하지 않음!
    // 실제 회전은 4단계에서 하드웨어 피드백을 받아서만 수행
}
```

#### 3단계: 하드웨어 실제 위치 피드백
```csharp
// 하드웨어가 물리적 회전 수행 후 실제 위치 반환
// ARES_PARASIM_FEEDBACK_EX_DATA 구조체:
// - RollPosition: 실제 Roll 위치 (0~20000)
// - YawingPosition: 실제 Yaw 위치 (0~36000)

private AresFeedbackData ConvertFromApiFormat(ARES_PARASIM_FEEDBACK_EX_DATA data)
{
    return new AresFeedbackData
    {
        // 실제 하드웨어 위치
        RollPosition = (data.RollPosition / 20000f * 360f) - 180f,
        YawPosition = data.YawingPosition / 100f,
        
        // 라이저 상태
        LeftRiserLength = data.LTRiserLineCurrentLength,
        RightRiserLength = data.RTRiserLineCurrentLength,
        LeftRiserDetected = data.LTRiserLineDetect > 0,
        RightRiserDetected = data.RTRiserLineDetect > 0
    };
}
```

#### 4단계: Unity 화면을 하드웨어 피드백으로만 업데이트 (핵심 변경!)
```csharp
private void HandleAresFeedback(AresFeedbackData feedback)
{
    // 1. 라이저 입력 처리 (1단계 데이터)
    if (feedback.LeftRiserLength >= 20f)
    {
        leftPull = feedback.LeftRiserLength / 100f;
        isInputRiserL = true;
    }
    else
    {
        leftPull = 0f;
        isInputRiserL = false;
    }
    
    if (feedback.RightRiserLength >= 20f)
    {
        rightPull = feedback.RightRiserLength / 100f;
        isInputRiserR = true;
    }
    else
    {
        rightPull = 0f;
        isInputRiserR = false;
    }
    
    // 2. 하드웨어 피드백으로 Unity Transform 직접 업데이트 (하드웨어 우선 모드)
    if (hardwarePriorityMode && _isPara)
    {
        UpdateTransformFromHardware(feedback);
    }
}

private void UpdateTransformFromHardware(AresFeedbackData feedback)
{
    // 하드웨어의 실제 위치를 Unity에 직접 적용
    float hardwareYaw = feedback.YawPosition;    // 0~360°
    float hardwareRoll = feedback.RollPosition;  // -180~+180°
    
    // 부드러운 보간 적용 (선택적)
    if (useSmoothInterpolation)
    {
        float currentYaw = transform.eulerAngles.y;
        float currentRoll = transform.eulerAngles.z;
        
        // Lerp로 부드럽게 전환
        hardwareYaw = Mathf.LerpAngle(currentYaw, hardwareYaw, 
                                      interpolationSpeed * Time.deltaTime);
        hardwareRoll = Mathf.LerpAngle(currentRoll, hardwareRoll, 
                                       interpolationSpeed * Time.deltaTime);
    }
    
    // Unity Transform 업데이트 (하드웨어 위치 그대로 반영)
    transform.rotation = Quaternion.Euler(
        transform.eulerAngles.x,  // Pitch는 유지
        hardwareYaw,               // 하드웨어 Yaw
        hardwareRoll               // 하드웨어 Roll
    );
    
    // 디버그 로그
    if (debugMode)
    {
        Debug.Log($"[HW Priority] Yaw: {hardwareYaw:F1}°, Roll: {hardwareRoll:F1}°");
    }
}
```

### 하드웨어 우선 모드 설정
```csharp
[Header("Hardware Priority Mode Settings")]
[SerializeField] private bool hardwarePriorityMode = true;    // 하드웨어 우선 모드
[SerializeField] private bool useSmoothInterpolation = true;  // 부드러운 전환
[SerializeField] private float interpolationSpeed = 10f;      // 보간 속도
[SerializeField] private bool debugMode = false;              // 디버그 로그

// 목표 각도 추적 변수
private float targetYaw = 0f;
private float targetRoll = 0f;
```

### Unity 물리 시뮬레이션 비활성화 (하드웨어 우선 모드)
```csharp
void FixedUpdate()
{
    if (!isJumpStart) return;
    
    if (hardwarePriorityMode && aresService?.IsConnected == true)
    {
        // 하드웨어 우선 모드: Unity 물리 시뮬레이션 비활성화
        // ApplyTurning() 호출하지 않음!
        
        // 목표 위치만 계산하여 하드웨어로 전송
        CalculateAndSendTargetRotation();
        
        // 속도 제어는 유지 (전진/하강)
        if (_isPara)
        {
            RegulateForwardSpeed();
            RegulateSinkRate();
        }
    }
    else
    {
        // 하드웨어 없을 때: 기존 Unity 물리 시뮬레이션 사용
        UpdateControlInputs();
        
        if (_isPara)
        {
            RiserDamping();
            RegulateForwardSpeed();
            RegulateSinkRate();
            ApplyTurning();  // Unity 자체 회전
            ApplyWindZoneForce();
        }
    }
}
```

### 개선된 타이밍 다이어그램 (하드웨어 우선 모드)
```
시간 →
T0: 사용자가 왼쪽 라이저 50% 당김
T1: [10ms] 하드웨어가 라이저 입력 감지
T2: [20ms] Unity가 FEEDBACK_DATA 수신 (leftPull = 0.5)
T3: [30ms] Unity가 목표 Yaw 계산 (targetYaw += 회전속도)
T4: [40ms] 하드웨어로 MOTION_DATA 전송 (목표 위치)
T5: [50ms] 하드웨어 물리적 회전 시작
T6: [70ms] 하드웨어 회전 진행 중, 중간 위치 피드백
T7: [80ms] Unity가 하드웨어 위치로 Transform 업데이트
T8: [100ms] 하드웨어 목표 위치 도달
T9: [110ms] Unity 화면 = 하드웨어 실제 위치 (완전 동기화)
```

### 동작 모드 비교

| 항목 | 기존 모드 (Unity 우선) | 개선 모드 (하드웨어 우선) |
|------|----------------------|------------------------|
| Unity 물리 | rb.AddTorque() 사용 | 비활성화 |
| 회전 주체 | Unity가 먼저 회전 | 하드웨어만 회전 |
| Transform 업데이트 | 즉시 (물리 엔진) | 하드웨어 피드백 대기 |
| 지연 시간 | 낮음 (즉시 반응) | 중간 (피드백 대기) |
| 동기화 정확도 | 오차 발생 가능 | 완벽한 동기화 |
| 시각적 부드러움 | 매우 부드러움 | 보간 필요 |

---

## 📊 라이저 입력 → 회전값 계산 물리 시뮬레이션 상세 분석

### 개요
패러글라이더의 라이저(조종줄) 당김이 어떻게 회전 움직임으로 변환되는지 물리 엔진 기반으로 분석합니다.

### 1. 라이저 입력 처리 단계

#### 1.1 입력값 범위 및 의미
```csharp
[Range(0f, 1f)] private float leftPull = 0f;   // 왼쪽 라이저 (0=안당김, 1=최대)
[Range(0f, 1f)] private float rightPull = 0f;  // 오른쪽 라이저 (0=안당김, 1=최대)

// 입력 차이 계산
float turnInput = rightPull - leftPull;  // -1 ~ +1 범위
// 음수: 왼쪽 회전, 양수: 오른쪽 회전, 0: 직진
```

#### 1.2 브레이크 댐핑 효과 (양쪽 동시 당김)
```csharp
void RiserDamping()
{
    // 좌우 라이저 당김 차이 계산 (0~1)
    float brakeInputDiffer = 1f - Mathf.Abs(leftPull - rightPull);
    
    // 양쪽을 동시에 당길수록 증가하는 브레이크 효과
    float brakeInputMultiplier = brakeInputDiffer * (leftPull * rightPull);
    
    // 최종 브레이크 배수 (1=브레이크 없음, 0=최대 브레이크)
    brakeMultiplier = 1f - brakeInputMultiplier;
}
```

**브레이크 효과 분석:**
| 왼쪽 | 오른쪽 | 차이 | 브레이크 효과 | 결과 |
|------|--------|------|--------------|------|
| 0% | 0% | 0 | 0% | 정상 속도 |
| 50% | 50% | 0 | 25% | 속도 75%로 감소 |
| 100% | 100% | 0 | 100% | 완전 정지 |
| 100% | 0% | 1 | 0% | 정상 속도 (좌회전) |
| 70% | 30% | 0.4 | 12.6% | 약간 감속 (좌회전) |

### 2. Yaw 회전 계산 (좌우 회전)

#### 2.1 목표 회전 속도 계산
```csharp
// 물리 파라미터
[SerializeField] private float maxYawSpeed = 4f;         // 최대 회전 속도 (rad/s)
[SerializeField] private float yawControlStrength = 5f;  // P 게인 (추종 강도)
[SerializeField] private float yawDamping = 3f;          // D 게인 (감쇠)

void ApplyTurning()
{
    // Step 1: 입력으로부터 목표 회전 속도 계산
    float turnInput = rightPull - leftPull;  // -1 ~ +1
    float targetYawSpeed = turnInput * maxYawSpeed;  // -4 ~ +4 rad/s
    
    // Step 2: 현재 회전 속도 측정
    float currentYawSpeed = rb.angularVelocity.y;
    
    // Step 3: PD 제어기로 토크 계산
    float yawError = targetYawSpeed - currentYawSpeed;
    float yawCorrection = (yawError * yawControlStrength)     // P항: 오차에 비례
                        - (currentYawSpeed * yawDamping);      // D항: 속도에 반비례 (감쇠)
    
    // Step 4: 토크 적용
    rb.AddTorque(Vector3.up * yawCorrection, ForceMode.Force);
}
```

#### 2.2 Yaw 토크 계산 공식
```
토크 = P항 - D항
     = (목표속도 - 현재속도) × P게인 - 현재속도 × D게인
     
예시: 오른쪽 50% 당김
- turnInput = 0.5 - 0 = 0.5
- targetYawSpeed = 0.5 × 4 = 2 rad/s (약 114°/s)
- 현재속도 = 0 rad/s 가정
- yawError = 2 - 0 = 2
- yawCorrection = 2 × 5 - 0 × 3 = 10 N·m
```

### 3. Roll 회전 계산 (좌우 기울기)

#### 3.1 Roll과 Yaw 연동
```csharp
// Roll은 Yaw 토크에 비례하여 자동 생성 (실제 패러글라이더 물리)
float rollTorque = 0.3f * yawCorrection;  // Yaw 토크의 30%
rb.AddTorque(pasimPlayer.forward * -rollTorque, ForceMode.Force);
```

#### 3.2 Roll 제한 및 복원
```csharp
[SerializeField] private float maxRoll = 20f;  // 최대 기울기 각도

// 현재 Roll 각도 계산 (-180 ~ +180)
float localZRoll = pasimPlayer.localEulerAngles.z;
if (localZRoll > 180f) localZRoll -= 360f;

// Roll 제한 (maxRoll 초과시 복원력 적용)
if (localZRoll > maxRoll)
    rb.AddTorque(pasimPlayer.forward * -(localZRoll - maxRoll) * 2f, ForceMode.Force);
else if (localZRoll < -maxRoll)
    rb.AddTorque(pasimPlayer.forward * -(localZRoll + maxRoll) * 2f, ForceMode.Force);

// 라이저 입력이 없을 때 자동 수평 복원
if (!isInputRiser)
{
    float autoLevelTorque = (-localZRoll * selfLevelStrengthRoll)      // P항
                           - (angleVelo.z * selfLevelDampingRoll);      // D항
    rb.AddTorque(pasimPlayer.forward * autoLevelTorque, ForceMode.Force);
}
```

### 4. Pitch 제어 (앞뒤 기울기)

#### 4.1 Pitch 제한 및 복원
```csharp
[SerializeField] private float maxPitch = 10f;  // 최대 피치 각도

// 현재 Pitch 각도 계산
float pitch = pasimPlayer.localEulerAngles.x;
if (pitch > 180f) pitch -= 360f;

// Pitch 제한
if (pitch > maxPitch)
    rb.AddTorque(pasimPlayer.right * -(pitch - maxPitch) * 2f, ForceMode.Force);
else if (pitch < -maxPitch)
    rb.AddTorque(pasimPlayer.right * -(pitch + maxPitch) * 2f, ForceMode.Force);

// 라이저 입력이 없을 때 자동 복원
if (!isInputRiser && pitch != 0f)
{
    float autoPitchTorque = (-pitch * selfLevelStrengthPitch)
                          - (angleVelo.x * selfLevelDampingPitch);
    rb.AddTorque(pasimPlayer.right * autoPitchTorque, ForceMode.Force);
}
```

### 5. 속도 제약 조건

#### 5.1 전진 속도 제어
```csharp
void RegulateForwardSpeed()
{
    // 전방 벡터 추출
    Vector3 fwdDir = pasimPlayer.forward;
    fwdDir.y = 0f;
    fwdDir.Normalize();
    
    // 실제 전진 속도 계산
    float fwd = Vector3.Dot(rb.linearVelocity, fwdDir);
    
    // 목표 속도 추종 (브레이크 효과 적용)
    float cmd = (targetForwardSpeed * fwdSpeedGain - fwd) * brakeMultiplier;
    rb.AddForce(fwdDir * cmd, ForceMode.Acceleration);
}
```

#### 5.2 회전 가능 조건
```csharp
// 전진 속도가 2m/s 미만이면 회전 불가
float horizontalSpeed = new Vector3(lineVelo.x, 0, lineVelo.z).magnitude;
if (horizontalSpeed < 2f) return;  // 회전 토크 차단
```

### 6. 실제 적용 예시

#### 예시 1: 오른쪽 70% 라이저 당김
```
입력:
- leftPull = 0, rightPull = 0.7
- turnInput = 0.7

Yaw 계산 (하드웨어 우선 모드):
- 목표 회전속도 = 0.7 × 4 rad/s = 2.8 rad/s (약 160°/s)
- 프레임당 회전 = 160°/s × 0.02s = 3.2° (50Hz 기준)
- targetYaw += 3.2° (누적)

Roll 계산:
- targetRoll = 0.7 × 20° = 14° (즉시 적용)

속도 영향:
- 브레이크 효과 = 0% (한쪽만 당김)
- 전진 속도 유지 = 12 m/s
```

#### 예시 2: 양쪽 50% 동시 당김 (브레이크)
```
입력:
- leftPull = 0.5, rightPull = 0.5
- turnInput = 0

Yaw 계산:
- 목표 회전속도 = 0 rad/s (직진)
- Yaw 토크 = 0 N·m

브레이크 효과:
- brakeInputDiffer = 1 - 0 = 1
- brakeInputMultiplier = 1 × 0.5 × 0.5 = 0.25
- brakeMultiplier = 1 - 0.25 = 0.75
- 전진 속도 = 12 × 0.75 = 9 m/s (25% 감속)
```

### 7. 하드웨어 전송 데이터 구성 (하드웨어 우선 모드)

```csharp
// 하드웨어 우선 모드: 목표 위치를 계산하여 전송
private void CalculateAndSendTargetRotation()
{
    if (aresService == null || !aresService.IsConnected) return;
    
    // 라이저 입력으로 목표 각도 계산
    float turnInput = rightPull - leftPull;
    
    // Yaw: 누적 회전 (연속적)
    float yawDelta = turnInput * maxYawSpeed * Time.fixedDeltaTime * Mathf.Rad2Deg;  // rad/s → deg/s
    targetYaw += yawDelta;
    targetYaw = Mathf.Repeat(targetYaw, 360f);  // 0~360 범위 유지
    
    // Roll: 즉각 반응 (비누적)
    targetRoll = turnInput * maxRoll;  // -20° ~ +20°
    targetRoll = Mathf.Clamp(targetRoll, -maxRoll, maxRoll);
    
    var motionData = new AresMotionData
    {
        // 목표 위치 전송 (현재 위치가 아님!)
        YawAngle = targetYaw,
        RollAngle = targetRoll,
        
        // 라이저 입력값
        LeftRiser = leftPull,
        RightRiser = rightPull,
        
        // 이벤트 상태
        Event = currentEvent
    };
    
    aresService.SendMotionData(motionData);
    
    if (debugMode)
    {
        Debug.Log($"[Target] Yaw: {targetYaw:F1}°, Roll: {targetRoll:F1}°");
    }
}
```

### 7.1 하드웨어 우선 모드 전체 구현 예시

```csharp
public class ParagliderController : MonoBehaviour
{
    [Header("Hardware Priority Mode")]
    [SerializeField] private bool hardwarePriorityMode = true;
    [SerializeField] private bool useSmoothInterpolation = true;
    [SerializeField] private float interpolationSpeed = 10f;
    
    private float targetYaw = 0f;
    private float targetRoll = 0f;
    private float lastHardwareUpdateTime;
    
    void Start()
    {
        // 하드웨어 서비스 초기화
        if (aresService != null)
        {
            aresService.OnFeedbackReceived += HandleAresFeedback;
        }
        
        // 초기 Yaw 설정
        targetYaw = transform.eulerAngles.y;
    }
    
    void FixedUpdate()
    {
        if (!isJumpStart) return;
        
        if (hardwarePriorityMode && aresService?.IsConnected == true)
        {
            // 하드웨어 우선 모드
            HardwarePriorityUpdate();
        }
        else
        {
            // 폴백: Unity 물리 시뮬레이션
            UnityPhysicsUpdate();
        }
    }
    
    private void HardwarePriorityUpdate()
    {
        // 1. 목표 위치 계산 및 전송
        CalculateAndSendTargetRotation();
        
        // 2. 속도 제어 (전진/하강)는 Unity에서 유지
        if (_isPara)
        {
            RiserDamping();
            RegulateForwardSpeed();
            RegulateSinkRate();
        }
        
        // 3. 회전은 하드웨어 피드백을 기다림
        // Transform 업데이트는 HandleAresFeedback에서 처리
    }
    
    private void HandleAresFeedback(AresFeedbackData feedback)
    {
        // 라이저 입력 업데이트
        UpdateRiserInputs(feedback);
        
        // 하드웨어 우선 모드: Transform 업데이트
        if (hardwarePriorityMode && _isPara)
        {
            UpdateTransformFromHardware(feedback);
        }
        
        lastHardwareUpdateTime = Time.time;
    }
    
    private void UpdateTransformFromHardware(AresFeedbackData feedback)
    {
        float newYaw = feedback.YawPosition;
        float newRoll = feedback.RollPosition;
        
        if (useSmoothInterpolation)
        {
            // 부드러운 보간
            float currentYaw = transform.eulerAngles.y;
            float currentRoll = transform.eulerAngles.z;
            
            newYaw = Mathf.LerpAngle(currentYaw, newYaw, 
                                        interpolationSpeed * Time.deltaTime);
            newRoll = Mathf.LerpAngle(currentRoll, newRoll, 
                                         interpolationSpeed * Time.deltaTime);
        }
        
        // Transform 업데이트
        transform.rotation = Quaternion.Euler(
            transform.eulerAngles.x,
            newYaw,
            newRoll
        );
    }
}
```

### 8. 물리 파라미터 튜닝 가이드

| 파라미터 | 기본값 | 범위 | 효과 |
|---------|--------|------|------|
| maxYawSpeed | 4 rad/s | 2-6 | 최대 회전 속도 |
| yawControlStrength | 5 | 3-10 | 회전 반응성 (높을수록 빠른 반응) |
| yawDamping | 3 | 1-5 | 회전 감쇠 (높을수록 부드러움) |
| maxRoll | 20° | 10-30 | 최대 기울기 각도 |
| maxPitch | 10° | 5-15 | 최대 피치 각도 |
| selfLevelStrength | 5 | 3-10 | 자동 수평 복원 강도 |
| selfLevelDamping | 5 | 3-10 | 복원 감쇠 (진동 방지) |

### 9. 디버깅 및 모니터링

```csharp
void OnGUI()
{
    // 실시간 상태 표시
    if (hardwarePriorityMode)
    {
        GUI.Label(new Rect(10, 10, 300, 25), 
            $"Mode: Hardware Priority");
        GUI.Label(new Rect(10, 35, 300, 25), 
            $"Target Yaw: {targetYaw:F1}°");
        GUI.Label(new Rect(10, 60, 300, 25), 
            $"Target Roll: {targetRoll:F1}°");
        GUI.Label(new Rect(10, 85, 300, 25), 
            $"Actual Yaw: {transform.eulerAngles.y:F1}°");
        GUI.Label(new Rect(10, 110, 300, 25), 
            $"Actual Roll: {transform.eulerAngles.z:F1}°");
    }
    else
    {
        GUI.Label(new Rect(10, 10, 300, 25), 
            $"Mode: Unity Physics");
        GUI.Label(new Rect(10, 35, 300, 25), 
            $"Yaw Speed: {rb.angularVelocity.y * Mathf.Rad2Deg:F1}°/s");
        GUI.Label(new Rect(10, 60, 300, 25), 
            $"Roll Angle: {transform.eulerAngles.z:F1}°");
    }
    
    GUI.Label(new Rect(10, 135, 300, 25), 
        $"Forward Speed: {Vector3.Dot(rb.linearVelocity, transform.forward):F1} m/s");
    GUI.Label(new Rect(10, 160, 300, 25), 
        $"Brake Effect: {(1-brakeMultiplier)*100:F0}%");
}
```

---

## 4. 게임 로직 통합

### 4.1 ParagliderController 통합

```csharp
public class ParagliderController : MonoBehaviour
{
    [Header("ARES Hardware Integration")]
    [SerializeField] private AresHardwareService aresService;
    
    private bool isJumpStart = false;
    private bool _isPara = false;
    private float initialYaw;
    
    void Start()
    {
        // ARES 서비스 초기화
        if (aresService == null)
        {
            aresService = GetComponent<AresHardwareService>();
        }
        
        // 이벤트 구독
        if (aresService != null)
        {
            aresService.OnFeedbackReceived += HandleAresFeedback;
            aresService.OnConnectionLost += HandleConnectionLost;
        }
    }
    
    void FixedUpdate()
    {
        if (!isJumpStart) return;
        
        if (hardwarePriorityMode && aresService?.IsConnected == true)
        {
            // 하드웨어 우선 모드
            CalculateAndSendTargetRotation();
            
            if (_isPara)
            {
                RegulateForwardSpeed();
                RegulateSinkRate();
            }
        }
        else
        {
            // Unity 물리 시뮬레이션 모드 (폴백)
            if (_isPara)
            {
                ApplyTurning();
                RegulateForwardSpeed();
                RegulateSinkRate();
            }
        }
    }
}
```

### 4.2 낙하 시퀀스 구현

```csharp
// 낙하 시작
public void JumpStart()
{
    isJumpStart = true;
    initialYaw = transform.eulerAngles.y;
    
    // 자유낙하 이벤트 전송
    if (jumpType != JumpType.STANDARD)
    {
        aresService?.SetEvent(AresEvent.FreeFall);
        Debug.Log("[낙하] FreeFall 이벤트 전송");
    }
    
    // 물리 시작
    rb.useGravity = true;
}

// 낙하산 전개
public void ParaDeploy()
{
    _isPara = true;
    
    // 속도 파라미터 변경
    targetForwardSpeed = 12f;  // 12 m/s
    targetSinkSpeed = 5f;       // 5 m/s
    
    // 전개 이벤트 전송
    aresService?.SetEvent(AresEvent.Deploy);
    Debug.Log("[낙하산] Deploy 이벤트 전송");
    
    col.enabled = true;
}

// 착륙
public void OnLanding()
{
    aresService?.SetEvent(AresEvent.Landing);
    Debug.Log("[착륙] Landing 이벤트 전송");
}
```

---

## 5. 양방향 동기화

### 5.1 하드웨어 피드백 처리

```csharp
private void HandleAresFeedback(AresFeedbackData feedback)
{
    // 1. 라이저 입력 처리
    if (feedback.LeftRiserLength >= 20f)
    {
        leftPull = feedback.LeftRiserLength / 100f;
        isInputRiserL = true;
    }
    else
    {
        leftPull = 0f;
        isInputRiserL = false;
    }
    
    if (feedback.RightRiserLength >= 20f)
    {
        rightPull = feedback.RightRiserLength / 100f;
        isInputRiserR = true;
    }
    else
    {
        rightPull = 0f;
        isInputRiserR = false;
    }
    
    // 2. 위치 동기화
    if (_isPara && (isInputRiserL || isInputRiserR))
    {
        ProcessPositionSync(feedback);
    }
}
```

### 5.2 위치 동기화 로직

```csharp
private void ProcessPositionSync(AresFeedbackData feedback)
{
    // Yaw 동기화
    float currentYaw = transform.eulerAngles.y;
    float targetYaw = feedback.YawPosition;
    float yawDiff = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));
    
    if (yawDiff > yawSyncThreshold)
    {
        Debug.Log($"[동기화] Yaw 차이: {yawDiff:F1}° - 보정 시작");
        
        // 부드러운 보간
        float newYaw = Mathf.LerpAngle(
            currentYaw, 
            targetYaw, 
            yawSyncSpeed * Time.fixedDeltaTime
        );
        
        transform.rotation = Quaternion.Euler(
            transform.eulerAngles.x,
            newYaw,
            transform.eulerAngles.z
        );
    }
    
    // Roll 동기화 (Ver 0205 이후)
    float currentRoll = transform.eulerAngles.z;
    float targetRoll = feedback.RollPosition;
    float rollDiff = Mathf.Abs(Mathf.DeltaAngle(currentRoll, targetRoll));
    
    if (rollDiff > rollSyncThreshold)
    {
        Debug.Log($"[동기화] Roll 차이: {rollDiff:F1}° - 보정 시작");
        
        float newRoll = Mathf.LerpAngle(
            currentRoll,
            targetRoll,
            rollSyncSpeed * Time.fixedDeltaTime
        );
        
        transform.rotation = Quaternion.Euler(
            transform.eulerAngles.x,
            transform.eulerAngles.y,
            newRoll
        );
    }
}
```

---

## 6. 이벤트 처리

### 6.1 이벤트 정의

```csharp
public enum AresEvent
{
    None = 0,          // 기본 상태
    FreeFall = 1,      // 자유낙하 (Pitching + Heave UP)
    Deploy = 2,        // 낙하산 전개 (Pitching DOWN)
    Malfunction = 3,   // 고장 (Heave DOWN)
    Landing = 4,       // 착륙 직전 (Roll 유지 + Heave 천천히 DOWN)
    Landed = 5         // 착륙 완료 (Roll 해제 + Heave 천천히 DOWN)
}
```

### 6.2 이벤트별 하드웨어 반응

| 이벤트 | Pitching | Heave | Roll | 설명 |
|--------|----------|-------|------|------|
| FreeFall | 빠르게 UP | 빠르게 UP | - | 낙하 시작 충격 |
| Deploy | 빠르게 DOWN | - | - | 전개 충격 |
| Malfunction | - | 빠르게 DOWN | - | 급낙하 |
| Landing | - | 천천히 DOWN | 유지 | 착륙 준비 |
| Landed | - | 천천히 DOWN | 해제 | 착륙 완료 |

---

## 7. 에러 처리 및 복구

### 7.1 연결 실패 처리

```csharp
private void HandleConnectionLost()
{
    Debug.LogWarning("[ARES] 하드웨어 연결 끊김!");
    
    // 1. 자동 재연결 시도
    if (autoReconnect && reconnectAttempts < maxReconnectAttempts)
    {
        reconnectAttempts++;
        Debug.Log($"[ARES] 재연결 시도 {reconnectAttempts}/{maxReconnectAttempts}");
        
        if (Initialize())
        {
            reconnectAttempts = 0;
            OnConnectionRestored?.Invoke();
        }
    }
    
    // 2. VR 컨트롤러로 폴백
    else
    {
        Debug.Log("[ARES] VR 컨트롤 모드로 전환");
        useVRControllerFallback = true;
    }
}
```

### 7.2 데이터 검증

```csharp
private bool ValidateFeedback(AresFeedbackData feedback)
{
    // 범위 검증
    if (feedback.YawPosition < 0 || feedback.YawPosition > 360)
    {
        Debug.LogWarning($"[ARES] 비정상 Yaw 값: {feedback.YawPosition}°");
        return false;
    }
    
    if (feedback.RollPosition < -180 || feedback.RollPosition > 180)
    {
        Debug.LogWarning($"[ARES] 비정상 Roll 값: {feedback.RollPosition}°");
        return false;
    }
    
    if (feedback.LeftRiserLength < 0 || feedback.LeftRiserLength > 100)
    {
        Debug.LogWarning($"[ARES] 비정상 왼쪽 라이저 값: {feedback.LeftRiserLength}%");
        return false;
    }
    
    if (feedback.RightRiserLength < 0 || feedback.RightRiserLength > 100)
    {
        Debug.LogWarning($"[ARES] 비정상 오른쪽 라이저 값: {feedback.RightRiserLength}%");
        return false;
    }
    
    // 급격한 변화 감지
    float deltaYaw = Mathf.Abs(feedback.YawPosition - lastYawPosition);
    if (deltaYaw > 180f)
    {
        Debug.LogWarning($"[ARES] 급격한 Yaw 변화: {deltaYaw}°");
        return false;
    }
    
    return true;
}
```

---

## 8. 최적화 및 성능

### 8.1 스레드 분리

| 스레드 | 주기 | 담당 작업 |
|--------|------|-----------|
| Main Thread | 60 FPS | Unity 렌더링, 물리 |
| Communication Thread | 10 Hz | 하드웨어 통신 |
| Network Thread | 4 Hz | 다중 사용자 동기화 |

### 8.2 메모리 최적화

```csharp
public class AresHardwareService : MonoBehaviour
{
    // 객체 재사용으로 GC 방지
    private readonly ARES_PARASIM_MOTION_EX_DATA cachedMotionData = new();
    private readonly AresFeedbackData cachedFeedback = new();
    private readonly object dataLock = new();
    
    // StringBuilder 재사용
    private readonly StringBuilder debugLog = new(256);
}
```

### 8.3 조건부 업데이트

```csharp
private bool HasSignificantChange(AresMotionData current, AresMotionData last)
{
    const float ANGLE_THRESHOLD = 1f;    // 1도
    const float RISER_THRESHOLD = 0.05f; // 5%
    
    if (Mathf.Abs(current.RollAngle - last.RollAngle) > ANGLE_THRESHOLD)
        return true;
        
    if (Mathf.Abs(current.YawAngle - last.YawAngle) > ANGLE_THRESHOLD)
        return true;
        
    if (Mathf.Abs(current.LeftRiser - last.LeftRiser) > RISER_THRESHOLD)
        return true;
        
    if (Mathf.Abs(current.RightRiser - last.RightRiser) > RISER_THRESHOLD)
        return true;
        
    if (current.Event != last.Event)
        return true;
        
    return false;
}
```

---

## 9. 테스트 및 검증

### 9.1 연결 테스트

```csharp
[ContextMenu("Test Hardware Connection")]
private void TestConnection()
{
    Debug.Log("=== ARES 하드웨어 연결 테스트 ===");
    
    // 1. 초기화 테스트
    if (Initialize())
    {
        Debug.Log("✅ 초기화 성공");
        
        // 2. 상태 확인
        bool isConnected = AresParachuteAPI.ARESParaSIM__StateCheck();
        Debug.Log($"✅ 연결 상태: {(isConnected ? "연결됨" : "끊김")}");
        
        // 3. 데이터 송수신 테스트
        var testData = new ARES_PARASIM_MOTION_EX_DATA();
        testData.Init();
        
        ARES_PARASIM_FEEDBACK_EX_DATA feedback;
        bool success = AresParachuteAPI.ARESParaSIM__MotionControlEx(
            ref testData, 
            out feedback
        );
        
        if (success)
        {
            Debug.Log("✅ 데이터 송수신 성공");
            Debug.Log($"  - Roll Position: {feedback.RollPosition}");
            Debug.Log($"  - Yaw Position: {feedback.YawingPosition}");
            Debug.Log($"  - Left Riser: {feedback.LTRiserLineCurrentLength}%");
            Debug.Log($"  - Right Riser: {feedback.RTRiserLineCurrentLength}%");
        }
        else
        {
            Debug.LogError("❌ 데이터 송수신 실패");
        }
    }
    else
    {
        Debug.LogError("❌ 초기화 실패");
    }
}
```

### 9.2 이벤트 테스트

```csharp
[ContextMenu("Test All Events")]
private IEnumerator TestAllEvents()
{
    Debug.Log("=== 이벤트 테스트 시작 ===");
    
    // FreeFall
    SetEvent(AresEvent.FreeFall);
    Debug.Log("1. FreeFall 이벤트 전송");
    yield return new WaitForSeconds(2f);
    
    // Deploy
    SetEvent(AresEvent.Deploy);
    Debug.Log("2. Deploy 이벤트 전송");
    yield return new WaitForSeconds(2f);
    
    // Landing
    SetEvent(AresEvent.Landing);
    Debug.Log("3. Landing 이벤트 전송");
    yield return new WaitForSeconds(2f);
    
    // Landed
    SetEvent(AresEvent.Landed);
    Debug.Log("4. Landed 이벤트 전송");
    yield return new WaitForSeconds(2f);
    
    // Reset
    SetEvent(AresEvent.None);
    Debug.Log("5. 이벤트 리셋");
    
    Debug.Log("=== 이벤트 테스트 완료 ===");
}
```

### 9.3 성능 모니터링

```csharp
public class PerformanceMonitor : MonoBehaviour
{
    public int TotalPacketsSent { get; private set; }
    public int TotalPacketsReceived { get; private set; }
    public float AverageLatency { get; private set; }
    public DateTime LastSuccessfulUpdate { get; private set; }
    
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 25), 
            $"패킷 송신: {TotalPacketsSent}");
        GUI.Label(new Rect(10, 35, 300, 25), 
            $"패킷 수신: {TotalPacketsReceived}");
        GUI.Label(new Rect(10, 60, 300, 25), 
            $"평균 지연: {AverageLatency:F1}ms");
        GUI.Label(new Rect(10, 85, 300, 25), 
            $"마지막 업데이트: {LastSuccessfulUpdate:HH:mm:ss}");
    }
}
```

---

## 📝 체크리스트

### 초기 설정
- [ ] DLL 파일 Assets/Plugins 폴더에 복사
- [ ] Visual C++ 재배포 패키지 설치
- [ ] COM 포트 번호 확인 및 설정
- [ ] Unity 프로젝트 설정 (Android, Meta XR)

### 구현 확인
- [ ] AresHardwareService 컴포넌트 추가
- [ ] ParagliderController 연동
- [ ] 데이터 변환 로직 구현
- [ ] 이벤트 처리 구현
- [ ] 에러 처리 구현

### 테스트
- [ ] 하드웨어 연결 테스트
- [ ] 데이터 송수신 테스트
- [ ] 이벤트 반응 테스트
- [ ] 양방향 동기화 테스트
- [ ] 성능 모니터링

### 최종 검증
- [ ] 10Hz 통신 주기 확인
- [ ] 메모리 누수 체크
- [ ] 장시간 안정성 테스트
- [ ] VR 컨트롤러 폴백 동작 확인

---

## 🔧 트러블슈팅

### 문제: DLL을 찾을 수 없음
**해결방법:**
1. Visual C++ 2015-2022 재배포 가능 패키지 설치
2. DLL 파일을 Assets/Plugins 폴더에 복사
3. Unity Editor 재시작

### 문제: COM 포트 연결 실패
**해결방법:**
1. 장치 관리자에서 COM 포트 번호 확인
2. 다른 프로그램이 포트 사용 중인지 확인
3. USB 케이블 연결 상태 확인

### 문제: 데이터 동기화 지연
**해결방법:**
1. 통신 주기 확인 (10Hz)
2. Thread.Sleep(20) 값 조정
3. 네트워크 부하 확인

### 문제: 급격한 움직임
**해결방법:**
1. 보간 속도 (SyncSpeed) 값 조정
2. 데이터 검증 임계값 확인
3. 하드웨어 캘리브레이션

---

## 📚 참고 자료

- ARES_Parachute_Simulator_SDK_API_Ver0205.pdf
- Unity 6.1 Documentation
- Meta XR SDK Documentation
- [프로젝트 GitHub 저장소]

---

*최종 업데이트: 2025년 9월*
*작성자: ARES ParaSim VR 개발팀*