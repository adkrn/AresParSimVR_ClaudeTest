# Animator 추가 시 다리 바닥 통과 문제 - 심화 진단

**문제:** Apply Root Motion을 꺼도 여전히 다리가 바닥을 통과함
**프로젝트:** AresParSimVR
**작성일:** 2025-10-22

---

## 🔍 추가 원인 분석

Apply Root Motion을 꺼도 문제가 지속된다면 다음 원인들을 확인해야 합니다.

---

## 원인 1: Animator Controller에 애니메이션이 설정되어 있음

### 문제
```
Animator Controller가 비어있지 않고 애니메이션이 자동 재생됨
→ 애니메이션 클립의 Root Transform이 캐릭터 위치에 영향
→ Apply Root Motion과 무관하게 발생
```

### 확인 방법
```
1. Project → Assets/Anims/Character_HighJump_Controller.controller 더블클릭
2. Animator 창에서 Base Layer 확인
3. Entry → 기본 State 연결 확인
```

### 해결 방법 1: Animator Controller 비우기
```
1. Animator 창에서 모든 State 삭제
2. 빈 Controller로 유지
3. 또는 Idle 애니메이션만 추가 (제자리 애니메이션)
```

### 해결 방법 2: Animator Controller를 아예 제거
```
PlayCharacter GameObject 선택
→ Animator 컴포넌트
→ Controller: None (비우기)
```

**이 방법의 문제:**
- SimpleFootIK는 OnAnimatorIK를 사용하므로 Animator Controller 필요
- 빈 Controller라도 있어야 함

---

## 원인 2: 애니메이션 클립의 Root Transform Position 설정

### 문제
```
애니메이션 클립 자체가 Y축 위치를 변경하도록 설정됨
→ Root Transform Position (Y)가 "Original"로 설정
→ 애니메이션의 원본 Y 위치가 적용됨
```

### 확인 방법
```
1. Project → Assets/Model/CharacterModels/High_Jump_char_Idle_Stand.fbx
2. Inspector → Animation 탭
3. Clips 목록에서 애니메이션 선택
4. Root Transform Position (Y) 확인
```

### 해결 방법: Bake Into Pose 설정
```
각 애니메이션 클립마다:

1. High_Jump_char_Idle_Stand.fbx 선택
2. Inspector → Animation 탭
3. Clips → 애니메이션 클립 선택
4. Root Transform Position (Y):
   ☑ Bake Into Pose
   Based Upon: Feet
   Offset: 0
5. Apply 클릭
6. 다른 애니메이션도 반복
```

**설정할 파일들:**
- High_Jump_char_Idle_Stand.fbx
- High_Jump_char_Idle_Seat.fbx
- High_Jump_char_005.fbx

---

## 원인 3: FBX 모델의 기본 위치 문제

### 문제
```
FBX 파일 자체가 Pelvis를 Y=0 기준으로 제작됨
→ Unity에서 Import 시 Pelvis가 Transform 위치에 배치
→ 발은 자동으로 아래로 내려감
```

### 확인 방법
```
1. Hierarchy에서 High_Jump_char_006 선택
2. 하위 구조 확인:
   High_Jump_char_006
   └─ Bip001 (Root)
      └─ Bip001 Pelvis
         └─ 다리들...

3. Bip001 Pelvis의 Local Position 확인
```

### 해결 방법 1: FBX Import 시 Offset 설정
```
1. High_Jump_char_005.fbx 선택
2. Inspector → Model 탭
3. Scene → Import Transform:
   Position Y: -1.0 (발까지의 거리만큼 아래로)
4. Apply
```

### 해결 방법 2: GameObject 계층 구조 조정
```
새 구조:
CharacterRoot (빈 GameObject)
└─ High_Jump_char_006 (Y = -1.0)
   └─ Bip001
      └─ ...

코드:
Transform characterModel = High_Jump_char_006;
characterModel.localPosition = new Vector3(0, -1.0f, 0);
```

---

## 원인 4: Rigidbody와 Animator 충돌

### 문제
```
Rigidbody가 있는 상태에서 Animator 추가
→ Rigidbody의 Center of Mass가 변경됨
→ 물리 시뮬레이션에 영향
```

### 확인 방법
```
1. PlayCharacter GameObject 선택
2. Rigidbody 컴포넌트 확인
3. Center of Mass 값 확인
```

### 해결 방법
```csharp
// PlayCharacter.cs

private void Start()
{
    var rb = GetComponent<Rigidbody>();
    if (rb)
    {
        // Center of Mass를 발 위치로 설정
        rb.centerOfMass = new Vector3(0, -1.0f, 0);
        rb.automaticCenterOfMass = false;
        Debug.Log($"[PlayCharacter] Center of Mass 설정: {rb.centerOfMass}");
    }
}
```

---

## 원인 5: RetargetingConstraint / Deformation 컴포넌트 충돌

### 문제
```
스크린샷에서 확인됨:
High_Jump_char_006
└─ Rig
   ├─ RetargetingConstraint
   └─ Deformation

이 컴포넌트들이 Animator와 충돌할 수 있음
```

### 해결 방법 1: 컴포넌트 실행 순서 변경
```
1. Rig GameObject 선택
2. Inspector 우측 상단 ⋮ (점 3개)
3. Move Component → Move to Bottom
4. Animator가 먼저 실행되도록 순서 조정
```

### 해결 방법 2: 컴포넌트 비활성화 (테스트용)
```
1. Rig GameObject 선택
2. RetargetingConstraint 컴포넌트 비활성화
3. Play 모드로 테스트
4. 문제 해결되면 → 컴포넌트 충돌 확인됨
```

---

## ✅ 단계별 진단 가이드

### Step 1: Animator Controller 확인

```
1. PlayCharacter → Animator 컴포넌트
2. Controller 필드 확인
   - 비어있음? → 문제 없음, 다음 단계
   - 설정됨? → 다음 확인
```

**Controller가 설정된 경우:**
```
Project → Controller 더블클릭 → Animator 창
→ Entry에서 어떤 State로 연결되는지 확인
→ 기본 State가 있으면 → 해당 애니메이션 확인
```

### Step 2: 애니메이션 클립 Root Transform 확인

```
각 애니메이션 FBX:
1. FBX 선택
2. Animation 탭
3. Root Transform Position (Y) 확인
   - "Bake Into Pose"? ✅ 정상
   - "Original"? ❌ 문제 원인
```

**문제가 있으면:**
```
Root Transform Position (Y):
☑ Bake Into Pose
Based Upon: Feet
Offset: 0
→ Apply 클릭
```

### Step 3: 임시 해결 - 모델 위치 오프셋

**가장 빠른 해결책:**

```csharp
// PlayCharacter.cs - Start() 메서드

private void Start()
{
    // 기존 코드...

    // Animator 추가로 인한 위치 보정
    if (animator)
    {
        // 캐릭터 모델을 발 높이만큼 아래로 이동
        Transform modelRoot = animator.transform;

        // Bip001 찾기
        Transform bip001 = modelRoot.Find("Bip001");
        if (bip001)
        {
            Transform pelvis = bip001.Find("Bip001 Pelvis");
            Transform leftFoot = FindDeepChild(transform, "Bip001 L Foot");

            if (pelvis && leftFoot)
            {
                // Pelvis와 발 사이의 거리 계산
                float offset = pelvis.position.y - leftFoot.position.y;

                // 모델을 그만큼 아래로
                modelRoot.localPosition = new Vector3(0, -offset, 0);

                Debug.Log($"[PlayCharacter] 모델 오프셋 적용: {-offset}");
            }
        }
    }
}

// 깊은 자식 찾기 헬퍼 함수
private Transform FindDeepChild(Transform parent, string name)
{
    foreach (Transform child in parent.GetComponentsInChildren<Transform>())
    {
        if (child.name == name)
            return child;
    }
    return null;
}
```

### Step 4: 근본적 해결 - FBX Import 설정

```
1. High_Jump_char_005.fbx 선택
2. Inspector → Animation 탭
3. 모든 애니메이션 클립:
   Root Transform Position (Y): Bake Into Pose
   Based Upon: Feet
4. Apply
5. Unity가 자동으로 재임포트
```

---

## 🎯 즉시 테스트 가능한 해결책

### 해결책 1: Animator Controller 제거 (가장 쉬움)

```
PlayCharacter → Animator 컴포넌트
→ Controller: None

단점: FootIK 못 씀
```

### 해결책 2: 빈 Animator Controller 생성

```
1. Project 우클릭 → Create → Animator Controller
2. 이름: "EmptyController"
3. 아무 State도 추가하지 않음
4. PlayCharacter → Animator → Controller: EmptyController
```

### 해결책 3: 코드로 모델 위치 보정 (권장)

```csharp
// PlayCharacter.cs

[Header("Animator 보정")]
[SerializeField] private float modelYOffset = -1.0f; // 발까지 거리

private void Start()
{
    if (animator)
    {
        // 모델을 아래로 이동
        Transform model = animator.transform;
        model.localPosition = new Vector3(0, modelYOffset, 0);
        Debug.Log($"[PlayCharacter] 모델 Y 오프셋: {modelYOffset}");
    }
}
```

**사용 방법:**
1. 코드 추가
2. Play 모드
3. Model Y Offset 값을 조정하면서 테스트
4. 발이 바닥에 닿을 때까지 값 조정 (보통 -0.8 ~ -1.2)

---

## 🔬 디버그 정보 출력

### 현재 상태 확인용 코드

```csharp
// PlayCharacter.cs - Update() 또는 Start()

private void Start()
{
    if (animator)
    {
        Debug.Log("=== Animator 상태 ===");
        Debug.Log($"Apply Root Motion: {animator.applyRootMotion}");
        Debug.Log($"Is Human: {animator.isHuman}");
        Debug.Log($"Controller: {animator.runtimeAnimatorController?.name}");

        // Root 위치
        Transform bip001 = FindDeepChild(transform, "Bip001");
        if (bip001)
        {
            Debug.Log($"Bip001 Position: {bip001.position}");
            Debug.Log($"Bip001 Local Position: {bip001.localPosition}");
        }

        // Pelvis 위치
        Transform pelvis = FindDeepChild(transform, "Bip001 Pelvis");
        if (pelvis)
        {
            Debug.Log($"Pelvis Position: {pelvis.position}");
            Debug.Log($"Pelvis Local Position: {pelvis.localPosition}");
        }

        // 발 위치
        Transform leftFoot = FindDeepChild(transform, "Bip001 L Foot");
        if (leftFoot)
        {
            Debug.Log($"Left Foot Position: {leftFoot.position}");
            Debug.Log($"Left Foot Local Position: {leftFoot.localPosition}");
        }

        Debug.Log($"Character Root Position: {transform.position}");
    }
}
```

---

## 📊 문제 해결 우선순위

| 순위 | 방법 | 시간 | 난이도 | 효과 |
|------|------|------|--------|------|
| 1 | 코드로 Y 오프셋 | 2분 | ⭐ 쉬움 | ✅ 즉시 해결 |
| 2 | 빈 Controller | 3분 | ⭐ 쉬움 | ✅ 정상 작동 |
| 3 | 애니메이션 Bake Into Pose | 10분 | ⭐⭐ 보통 | ✅ 근본 해결 |
| 4 | FBX Import 설정 | 20분 | ⭐⭐⭐ 어려움 | ✅ 완벽 해결 |

---

## ✅ 권장 해결 순서

### 1단계: 임시 해결 (즉시)
```csharp
// PlayCharacter.cs
animator.transform.localPosition = new Vector3(0, -1.0f, 0);
```

### 2단계: 애니메이션 설정 (10분)
```
각 FBX → Animation 탭
→ Root Transform Position (Y): Bake Into Pose
→ Apply
```

### 3단계: 테스트 및 미세 조정
```
Play 모드에서 확인
→ 필요시 오프셋 값 조정
→ Prefab Apply
```

---

**다음 단계:**
1. 위 디버그 코드를 추가해서 현재 상태 확인
2. Console에 출력된 위치 정보 확인
3. 가장 적합한 해결책 선택
4. 적용 및 테스트

어떤 방법을 시도해보시겠습니까?
