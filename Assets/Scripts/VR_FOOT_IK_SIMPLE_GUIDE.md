# VR FootIK 간단 가이드 - Meta Movement SDK

**프로젝트:** AresParSimVR
**작성일:** 2025-10-23
**핵심:** 낙하 중에만 끄면 됨 (ON/OFF만 제어)

---

## 1. Meta Movement SDK의 GroundingConstraint

### 제공되는 컴포넌트

```
✅ GroundingConstraint (Animation Rigging 기반)
  - Unity Animation Rigging 패키지 사용
  - Raycast로 지면 감지 및 발 위치 자동 조정
  - 골반 높이 자동 조정
  - 걷기 애니메이션 (Step 기능)
```

**위치:**
```
Assets/Unity-Movement-74.0.0/Runtime/Scripts/AnimationRigging/GroundingConstraint.cs
```

---

## 2. 설정 방법

### 2.1 Unity Animation Rigging Setup

**Step 1: Rig Builder 추가**

```
1. High_Jump_char_Normal GameObject 선택
2. Add Component → Rig Builder
3. 자동으로 "Rig" GameObject 생성됨
```

**Step 2: GroundingConstraint 추가**

```
1. Rig GameObject 선택
2. 우클릭 → Create Empty → "LeftLegGrounding"
3. LeftLegGrounding에 Add Component → Grounding Constraint
4. 같은 방식으로 "RightLegGrounding" 생성
```

**Step 3: Inspector 설정**

```yaml
LeftLegGrounding (GroundingConstraint):
  # Required Components
  Constraint Skeleton: [OVRCustomSkeleton 또는 비워둠]
  Constraint Animator: [Animator 컴포넌트]
  Pair: [RightLegGrounding] # 반대쪽 다리 연결

  # Ground Detection
  Ground Layers: [Ground, Terrain, Aircraft] # 모든 지면 포함
  Ground Raycast Distance: 1.0
  Ground Offset: 0.05

  # Bone References
  Hips: Bip001 Pelvis
  Leg: Bip001 L Thigh
  Foot: Bip001 L Foot

  # Targets (없으면 자동 생성됨)
  Hips Target: [HipsTarget - 공유]
  Knee Target: [LeftKneeTarget]
  Foot Target: [LeftFootTarget]

  # Step Settings
  Step Distance: 0.3
  Step Height: 0.05
  Step Speed: 5.0
  Step Curve: [기본 커브]
```

**RightLegGrounding:**
- 위와 동일하지만 Right 본들로 설정
- Pair는 LeftLegGrounding으로 연결

**중요:** `Compute Offsets` 버튼 클릭 (Inspector 하단)

---

## 3. 스크립트 제어 (간단 버전)

```csharp
// Assets/Scripts/SimpleGroundingControl.cs
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// GroundingConstraint 간단 제어
/// 낙하 중: OFF, 지상: ON
/// </summary>
public class SimpleGroundingControl : MonoBehaviour
{
    [Header("Rig Reference")]
    [SerializeField] private Rig rig;

    [Header("Settings")]
    [SerializeField] private float fadeSpeed = 3f;

    private bool isGrounded = true;

    private void Start()
    {
        // 기본 활성화
        if (rig != null)
            rig.weight = 1f;
    }

    /// <summary>
    /// Grounding 활성화/비활성화
    /// </summary>
    public void SetGroundingEnabled(bool enabled)
    {
        if (rig == null) return;

        // 부드러운 전환
        StopAllCoroutines();
        StartCoroutine(FadeRigWeight(enabled ? 1f : 0f));

        Debug.Log($"[Grounding] {(enabled ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// 즉시 비활성화 (낙하 시작 시)
    /// </summary>
    public void DisableImmediate()
    {
        if (rig != null)
        {
            StopAllCoroutines();
            rig.weight = 0f;
        }
        Debug.Log("[Grounding] 즉시 비활성화");
    }

    /// <summary>
    /// Rig Weight 페이드
    /// </summary>
    private System.Collections.IEnumerator FadeRigWeight(float targetWeight)
    {
        float startWeight = rig.weight;
        float elapsed = 0f;
        float duration = 1f / fadeSpeed;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            rig.weight = Mathf.Lerp(startWeight, targetWeight, elapsed / duration);
            yield return null;
        }

        rig.weight = targetWeight;
    }
}
```

---

## 4. PlayCharacter 통합

```csharp
// PlayCharacter.cs 수정
public class PlayCharacter : MonoBehaviour
{
    // 기존 FootIK 제거
    // private FootIK ikObj;  // ← 삭제

    [SerializeField] private SimpleGroundingControl groundingControl;

    private void Start()
    {
        // 기존 코드...

        // ikObj = FindAnyObjectByType<FootIK>();  // ← 삭제

        // SimpleGroundingControl 가져오기
        if (groundingControl == null)
            groundingControl = GetComponent<SimpleGroundingControl>();
    }

    public void Jump()
    {
        _stateManager.isJump = true;
        airPlaneGround.layer = 0;

        // 낙하 시작 → Grounding 즉시 비활성화
        groundingControl?.DisableImmediate();

        // 기존 코드...
    }

    private void OnGround()
    {
        Debug.Log("착륙 완료");

        // 착륙 → Grounding 활성화
        groundingControl?.SetGroundingEnabled(true);

        // 기존 코드...
        paraCtrl.rb.useGravity = false;
        // ...
    }
}
```

---

## 5. 실제 적용 순서

### Step 1: 기존 FootIKController 제거

```
1. High_Jump_char_Normal GameObject에서
2. FootIKController 컴포넌트 제거
3. PlayCharacter.cs에서 FootIK 관련 코드 삭제
```

### Step 2: Unity Animation Rigging 설정

```
1. Rig Builder 추가
2. LeftLegGrounding + RightLegGrounding 생성
3. GroundingConstraint 컴포넌트 설정
4. Bone References 연결
5. Pair 서로 연결
6. Compute Offsets 버튼 클릭
```

### Step 3: SimpleGroundingControl 추가

```
1. High_Jump_char_Normal에 SimpleGroundingControl 추가
2. Rig 필드에 "Rig" GameObject 연결
3. Fade Speed: 3
```

### Step 4: PlayCharacter 수정

```
1. groundingControl 필드 추가
2. Jump()에서 DisableImmediate() 호출
3. OnGround()에서 SetGroundingEnabled(true) 호출
4. 기존 FootIK 코드 삭제
```

### Step 5: 테스트

```
1. 로비/비행기: 발이 바닥에 붙는지 확인
2. Jump 실행: Grounding이 꺼지는지 확인
3. 착륙: Grounding이 다시 켜지는지 확인
```

---

## 6. Ground Layers 설정

**중요:** 하나의 Ground Layers로 모든 시나리오 처리

```yaml
Ground Layers:
  ✅ Ground (로비 바닥)
  ✅ Terrain (착륙 지형)
  ✅ Default (비행기 바닥)
  # 또는 커스텀 레이어 사용
```

**주의사항:**
- 비행기에서 Jump 시 `airPlaneGround.layer = 0` 처리 됨 (기존 코드)
- 낙하 중에는 Grounding이 꺼져있으므로 Ground Layer 무관

---

## 7. 문제 해결

### 발이 지면에 안 붙음

```
원인: Compute Offsets 안 했음
해결: Inspector에서 "Compute Offsets" 버튼 클릭
```

### 발이 이상하게 움직임

```
원인: Pair 설정 안 됨
해결: Left와 Right GroundingConstraint 서로 Pair 연결
```

### Rig가 작동 안 함

```
원인: Rig Builder 빌드 안 됨
해결: Play 모드 진입 시 자동 빌드됨, 또는 Rebuild 버튼 클릭
```

### 낙하 후 발이 안 보임

```
원인: OnGround()에서 SetGroundingEnabled(true) 안 불림
해결: PlayCharacter.OnGround()에 코드 추가 확인
```

---

## 8. 성능 최적화

### Quest 타겟 설정

```yaml
Rig Builder:
  Update Mode: Normal (기본값)

GroundingConstraint:
  Ground Raycast Distance: 1.0 (짧게)
  Step Speed: 5.0 (적당히)

SimpleGroundingControl:
  Fade Speed: 3.0 (빠르게 전환)
```

**예상 성능:**
- Rig 활성화 시: ~0.5-1ms per frame
- Rig 비활성화 시: 0ms
- 낙하 중 (비활성화): 성능 영향 없음

---

## 9. 체크리스트

### 설정 완료

- [ ] Rig Builder 추가
- [ ] LeftLegGrounding 생성 및 설정
- [ ] RightLegGrounding 생성 및 설정
- [ ] Pair 서로 연결
- [ ] Bone References 모두 연결
- [ ] Ground Layers 설정
- [ ] Compute Offsets 클릭
- [ ] SimpleGroundingControl 추가
- [ ] PlayCharacter 수정

### 기능 테스트

- [ ] 로비: 발이 바닥에 붙음
- [ ] 비행기: 발이 바닥에 붙음
- [ ] Jump: Grounding 즉시 꺼짐
- [ ] 낙하 중: Grounding 꺼진 상태 유지
- [ ] 착륙: Grounding 다시 켜짐
- [ ] 착륙 후: 발이 Terrain에 맞춰짐

### 정리

- [ ] 기존 FootIKController 삭제
- [ ] PlayCharacter에서 FootIK 코드 삭제
- [ ] 사용하지 않는 문서 파일 정리

---

## 요약

**핵심:**
```csharp
// 낙하 시작
groundingControl.DisableImmediate();  // OFF

// 착륙 완료
groundingControl.SetGroundingEnabled(true);  // ON
```

**설정:**
- Ground Layers: 모든 지면 포함 (Ground, Terrain, Default)
- 하나의 설정으로 모든 시나리오 처리
- 낙하 중에만 비활성화하면 됨

**구현 시간:** 1-2시간

**다음 단계:**
1. Unity에서 Rig Builder + GroundingConstraint 설정
2. SimpleGroundingControl 스크립트 추가
3. PlayCharacter 수정
4. 테스트 및 확인

---

## 10. 실제 프로젝트 적용 방법 (High_Jump_char_006 기준)

### 10.1 현재 프로젝트 구조 분석

**Hierarchy 구조 (스크린샷 기준):**
```
Player
└── High_Jump_char_006
    ├── Bip001
    │   └── Bip001 Pelvis (골반)
    │       ├── Bip001 L Thigh (왼쪽 허벅지)
    │       │   └── Bip001 L Calf (왼쪽 종아리)
    │       │       └── Bip001 L Foot (왼쪽 발)
    │       └── Bip001 R Thigh (오른쪽 허벅지)
    │           └── Bip001 R Calf (오른쪽 종아리)
    │               └── Bip001 R Foot (오른쪽 발)
    ├── HighAltitubePilot_Model
    └── Rig ✅ (이미 존재!)
        ├── RetargetingConstraint
        └── Deformation
            ├── HipsTarget ✅
            ├── LeftFootTarget ✅
            ├── RightFootTarget ✅
            └── (KneeTarget 추가 필요 ❌)
```

**발견:**
- ✅ Rig GameObject 이미 존재
- ✅ HipsTarget, LeftFootTarget, RightFootTarget 존재
- ❌ LeftKneeTarget, RightKneeTarget 없음

### 10.2 KneeTarget 추가 (필수)

**Step 1: LeftKneeTarget 생성**

```
1. Hierarchy에서 Rig/Deformation 선택
2. 우클릭 → Create Empty → "LeftKneeTarget"
3. Transform 설정:
   - Position: Bip001 L Calf 위치 + 앞쪽 0.3m
   - (대략 왼쪽 무릎 앞쪽)
```

**Step 2: RightKneeTarget 생성**

```
1. Rig/Deformation 선택
2. 우클릭 → Create Empty → "RightKneeTarget"
3. Transform 설정:
   - Position: Bip001 R Calf 위치 + 앞쪽 0.3m
```

**팁: 자동 위치 설정**
```csharp
// Unity Console에서 실행 (Play 모드 전)
// LeftKneeTarget 선택 상태에서:
Selection.activeTransform.position =
    GameObject.Find("Bip001 L Calf").transform.position +
    GameObject.Find("Bip001 L Thigh").transform.forward * 0.3f;

// RightKneeTarget 선택 상태에서:
Selection.activeTransform.position =
    GameObject.Find("Bip001 R Calf").transform.position +
    GameObject.Find("Bip001 R Thigh").transform.forward * 0.3f;
```

### 10.3 GroundingConstraint 추가

**Step 1: LeftLegGrounding 생성**

```
1. Rig GameObject 선택
2. 우클릭 → Create Empty → "LeftLegGrounding"
3. Transform 설정 (기본값 유지):
   - Position: (0, 0, 0)
   - Rotation: (0, 0, 0)
   - Scale: (1, 1, 1)
4. Add Component → Grounding Constraint
```

**참고:** LeftLegGrounding과 RightLegGrounding은 GroundingConstraint 컴포넌트를 담는 **컨테이너 오브젝트**입니다. 실제 지면 감지는 발 본(Bone)에서 Raycast로 수행되므로, 이 GameObject들의 Transform 위치는 기능에 영향을 주지 않습니다. 기본값(0,0,0)을 유지하면 됩니다.

**Step 2: Inspector 설정 (실제 경로 기준)**

```yaml
LeftLegGrounding (GroundingConstraint):
  # Components
  Constraint Skeleton: [비워둠 또는 OVRCustomSkeleton]
  Constraint Animator: High_Jump_char_006/Animator
  Pair: Rig/RightLegGrounding

  # Ground Settings
  Ground Layers: Default, Ground, Terrain
  Ground Raycast Distance: 1.0
  Ground Offset: 0.05

  # Bone References (드래그로 연결)
  Hips: Bip001/Bip001 Pelvis
  Leg: Bip001/Bip001 Pelvis/Bip001 L Thigh
  Foot: Bip001/Bip001 Pelvis/Bip001 L Thigh/Bip001 L Calf/Bip001 L Foot

  # Targets (Deformation 폴더에서 드래그)
  Hips Target: Rig/Deformation/HipsTarget
  Knee Target: Rig/Deformation/LeftKneeTarget
  Foot Target: Rig/Deformation/LeftFootTarget

  # Step Settings
  Step Distance: 0.3
  Step Height: 0.05
  Step Speed: 5.0
  Step Curve: [기본 커브 유지]

  Move Lower Threshold: 0.3
  Move Higher Threshold: 0.7
```

**Step 3: RightLegGrounding 생성**

```
1. Rig GameObject 선택
2. 우클릭 → Create Empty → "RightLegGrounding"
3. Transform 설정 (기본값 유지):
   - Position: (0, 0, 0)
   - Rotation: (0, 0, 0)
   - Scale: (1, 1, 1)
4. Add Component → Grounding Constraint
5. 위와 동일하게 설정 (Right 본으로 변경)
```

```yaml
RightLegGrounding (GroundingConstraint):
  Pair: Rig/LeftLegGrounding

  Leg: Bip001/Bip001 Pelvis/Bip001 R Thigh
  Foot: Bip001/Bip001 Pelvis/Bip001 R Thigh/Bip001 R Calf/Bip001 R Foot

  Knee Target: Rig/Deformation/RightKneeTarget
  Foot Target: Rig/Deformation/RightFootTarget

  # 나머지는 LeftLegGrounding과 동일
```

**Step 4: Compute Offsets (중요!)**

```
1. LeftLegGrounding 선택
2. Inspector 하단 "Compute Offsets" 버튼 클릭
3. RightLegGrounding 선택
4. Inspector 하단 "Compute Offsets" 버튼 클릭
```

### 10.4 Rig Builder 확인

**이미 Rig Builder가 있는지 확인:**

```
1. High_Jump_char_006 GameObject 선택
2. Inspector에서 Rig Builder 컴포넌트 확인
   - 있으면: Rig 리스트에 "Rig" GameObject 추가
   - 없으면: Add Component → Rig Builder
```

**Rig Builder 설정:**
```yaml
Rig Builder:
  Rig Layers:
    - Rig: Rig (GameObject 드래그)
      Weight: 1.0
```

### 10.5 SimpleGroundingControl 추가

```
1. High_Jump_char_006 GameObject 선택
2. Add Component → SimpleGroundingControl (스크립트 추가)
3. Inspector 설정:
   - Rig: Rig (GameObject 드래그)
   - Fade Speed: 3
```

### 10.6 PlayCharacter 수정

**위치:** `Assets/Scripts/PlayCharacter.cs`

```csharp
// Line 45 근처 - 기존 FootIK 제거
// private FootIK ikObj;  // ← 주석 처리 또는 삭제

// 새로 추가
[SerializeField] private SimpleGroundingControl groundingControl;

// Line 83 근처 - Start() 수정
private void Start()
{
    // 기존 코드...

    // ikObj = FindAnyObjectByType<FootIK>();  // ← 주석 처리 또는 삭제

    // SimpleGroundingControl 가져오기
    if (groundingControl == null)
        groundingControl = GetComponent<SimpleGroundingControl>();

    StateManager.OnInit += Init;
}

// Line 172 근처 - Jump() 수정
public void Jump()
{
    _stateManager.isJump = true;
    airPlaneGround.layer = 0;

    // ikObj.SetIKEnabled(false);  // ← 주석 처리 또는 삭제

    // Grounding 즉시 비활성화
    groundingControl?.DisableImmediate();

    // 기존 코드...
}

// Line 422 근처 - OnGround() 수정
private void OnGround()
{
    Debug.Log($"땅과 충돌했습니다");

    // ikObj.SetIKEnabled(true);  // ← 주석 처리 또는 삭제

    // Grounding 활성화
    groundingControl?.SetGroundingEnabled(true);

    // 기존 코드...
    paraCtrl.rb.useGravity = false;
}
```

### 10.7 최종 Hierarchy 구조

```
Player
└── High_Jump_char_006
    ├── Bip001 (본 구조 - 변경 없음)
    ├── HighAltitubePilot_Model
    ├── Animator
    ├── Rig Builder ✅ (확인 또는 추가)
    ├── SimpleGroundingControl ✅ (새로 추가)
    └── Rig
        ├── RetargetingConstraint
        ├── LeftLegGrounding ✅ (새로 추가 - Transform: 0,0,0)
        ├── RightLegGrounding ✅ (새로 추가 - Transform: 0,0,0)
        └── Deformation
            ├── HipsTarget
            ├── LeftKneeTarget ✅ (새로 추가 - 무릎 위치 조정 필요)
            ├── RightKneeTarget ✅ (새로 추가 - 무릎 위치 조정 필요)
            ├── LeftFootTarget
            └── RightFootTarget
```

**Transform 정리:**
- **LeftLegGrounding, RightLegGrounding**: (0, 0, 0) 기본값 유지 (컨테이너 오브젝트)
- **LeftKneeTarget, RightKneeTarget**: 해당 무릎 위치 + 앞쪽 0.3m (공간 참조점)

### 10.8 빠른 적용 체크리스트

#### Phase 1: 타겟 추가 (5분)
- [ ] Rig/Deformation/LeftKneeTarget 생성 및 위치 조정
- [ ] Rig/Deformation/RightKneeTarget 생성 및 위치 조정

#### Phase 2: GroundingConstraint 설정 (15분)
- [ ] Rig/LeftLegGrounding 생성 (Transform: 0,0,0 기본값)
- [ ] LeftLegGrounding에 GroundingConstraint 추가
- [ ] Bone References 연결 (Hips, Leg, Foot)
- [ ] Target References 연결 (HipsTarget, KneeTarget, FootTarget)
- [ ] Ground Layers 설정 (Default, Ground, Terrain)
- [ ] Compute Offsets 클릭
- [ ] Rig/RightLegGrounding 생성 (Transform: 0,0,0 기본값) 및 동일 설정
- [ ] Left ↔ Right Pair 연결

#### Phase 3: 스크립트 설정 (10분)
- [ ] SimpleGroundingControl.cs 파일 생성
- [ ] High_Jump_char_006에 SimpleGroundingControl 추가
- [ ] Rig 필드 연결
- [ ] PlayCharacter.cs 수정 (FootIK → GroundingControl)

#### Phase 4: Rig Builder 확인 (5분)
- [ ] High_Jump_char_006에 Rig Builder 있는지 확인
- [ ] Rig Layers에 "Rig" GameObject 추가
- [ ] Weight: 1.0 설정

#### Phase 5: 테스트 (10분)
- [ ] Play 모드 진입
- [ ] 로비/비행기: 발이 바닥에 붙는지 확인
- [ ] Jump: Grounding 꺼지는지 확인
- [ ] 착륙: Grounding 켜지는지 확인
- [ ] Console 로그 확인

**예상 소요 시간:** 45분

### 10.9 문제 해결 (프로젝트 특화)

#### Rig가 작동 안 함
```
원인: Rig Builder가 Rig를 인식 못함
해결:
1. Rig Builder 컴포넌트 제거
2. 다시 추가
3. Rig GameObject를 Rig Layers에 수동 드래그
```

#### Bone을 찾을 수 없음
```
원인: Hierarchy 경로가 다름
해결:
1. Scene에서 Bip001 Pelvis 클릭
2. Inspector에서 경로 확인
3. GroundingConstraint에 직접 드래그
```

#### RetargetingConstraint와 충돌
```
원인: 둘 다 같은 본을 제어
해결:
1. Rig의 Weight를 0.5로 낮춤
2. 또는 RetargetingConstraint 비활성화 후 테스트
```

#### KneeTarget 위치가 이상함
```
원인: 무릎 방향이 잘못됨
해결:
1. Play 모드에서 KneeTarget 위치 조정
2. 무릎이 자연스럽게 구부러지도록 앞쪽에 배치
3. Play 종료 전 Transform 값 복사
```
