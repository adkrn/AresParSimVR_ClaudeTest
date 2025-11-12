# Animator Controller 추가 시 다리가 바닥 통과하는 문제 해결

**문제:** Animator Controller를 추가하면 캐릭터가 허리 기준으로 배치되고 다리가 바닥을 통과함
**프로젝트:** AresParSimVR - High_Jump_char_006
**작성일:** 2025-10-22

---

## 🔴 문제 상황

### 증상
```
Before (Animator Controller 없음):
캐릭터가 발 기준으로 바닥에 서 있음 ✅

After (Animator Controller 추가):
캐릭터가 허리(Pelvis) 기준으로 배치됨
→ 다리가 바닥을 뚫고 들어감 ❌
```

### 시각적 표현
```
정상 (Before):
     머리
     허리
     다리
   ───────  ← 바닥
     발

문제 (After):
     머리
   ───────  ← 바닥 (허리 위치)
     허리
     다리   ← 바닥 아래로!
     발
```

---

## 🔍 원인 분석

### 원인 1: Apply Root Motion (가장 가능성 높음) ⭐

**문제:**
- Animator의 **"Apply Root Motion"** 옵션이 켜져 있음
- 애니메이션 클립이 허리(Pelvis)를 중심으로 제작됨
- 애니메이션의 Y축 위치가 그대로 적용됨

**왜 허리 기준이 될까?**
```
Humanoid 리그의 Root는 보통 Pelvis(골반)입니다.
애니메이션 제작 시:
- 3D 툴에서 Pelvis를 Y=0 위치에 놓고 제작
- 발은 Y=-1.0 정도 위치 (아래로)
- Unity에서 이 애니메이션 재생 시 Pelvis가 Transform.position에 배치됨
→ 발이 바닥 아래로 가게 됨!
```

### 원인 2: Humanoid Avatar Root Transform Position 설정

**문제:**
- FBX Import 설정에서 Root Transform Position이 잘못 설정됨
- "Original" 또는 "Center of Mass"로 설정되어 있음

### 원인 3: 애니메이션 클립의 Root Transform Bake 설정

**문제:**
- 애니메이션 클립의 Root Transform Position (Y)이 "Original"로 설정됨
- 애니메이션의 원본 Y 위치가 그대로 적용됨

---

## ✅ 해결 방법 (3가지)

### 해결 방법 1: Apply Root Motion 비활성화 ⭐ **가장 쉬움**

**이 방법을 쓰는 경우:**
- 캐릭터를 스크립트로 직접 이동시킴 (PlayCharacter에서 직접 제어)
- 애니메이션의 이동은 필요 없고 자세만 필요함

**절차:**
```
1. Hierarchy에서 High_Jump_char_006 선택
2. Inspector → Animator 컴포넌트 찾기
3. "Apply Root Motion" 체크박스 해제 ✗
4. Play 모드로 테스트
```

**코드로 설정 (PlayCharacter.cs):**
```csharp
private void Start()
{
    if (animator)
    {
        animator.applyRootMotion = false;
        Debug.Log("[PlayCharacter] Apply Root Motion 비활성화");
    }
}
```

**장점:**
- ✅ 즉시 해결됨
- ✅ 추가 설정 불필요
- ✅ 스크립트로 완전히 제어 가능

**단점:**
- ⚠️ 애니메이션의 이동(걷기 등)이 적용되지 않음
- ⚠️ In-place 애니메이션만 사용 가능

**AresParSimVR 프로젝트에 적합한 이유:**
- ✅ PlayCharacter가 이미 물리 기반으로 이동 제어
- ✅ Rigidbody로 낙하 처리
- ✅ 애니메이션은 자세만 필요 (앉기, 서기 등)

---

### 해결 방법 2: Humanoid Avatar 설정 수정

**이 방법을 쓰는 경우:**
- Apply Root Motion은 유지하고 싶음
- FBX 설정을 근본적으로 수정하고 싶음

**절차:**

#### 2-1. FBX Import 설정
```
1. Project 창에서 High_Jump_char_005.fbx 선택
2. Inspector → Rig 탭 클릭
3. Root node: Bip001 (확인)
4. Apply 클릭
```

#### 2-2. Configure Avatar
```
1. Project 창에서 High_Jump_char_005Avatar.asset 선택
   (또는 FBX Inspector → Configure... 버튼)
2. Avatar Configuration 창 열림
3. Inspector → Body 부분 확인
   - Root: Bip001
   - Hips: Bip001 Pelvis
4. Done 클릭
```

#### 2-3. Animation 탭 설정
```
1. High_Jump_char_005.fbx 선택
2. Inspector → Animation 탭
3. 각 애니메이션 클립 선택
4. Root Transform Position (Y):
   - "Bake Into Pose" 선택 ✓
   - Based Upon: "Feet"
   - Offset: 0
5. Apply 클릭
```

**장점:**
- ✅ 근본적인 해결
- ✅ 모든 애니메이션에 적용됨

**단점:**
- ⚠️ 설정이 복잡함
- ⚠️ 다른 애니메이션에도 영향

---

### 해결 방법 3: 캐릭터 위치 보정 (임시방편)

**이 방법을 쓰는 경우:**
- 빠른 임시 해결이 필요함
- 설정 변경이 어려움

**절차:**

```csharp
// PlayCharacter.cs에 추가

[Header("Animator 보정")]
[SerializeField] private Vector3 animatorOffset = new Vector3(0, -1.0f, 0);

private void Start()
{
    if (animator)
    {
        // Animator 때문에 올라간 만큼 아래로 내림
        Transform modelTransform = animator.transform;
        modelTransform.localPosition += animatorOffset;

        Debug.Log($"[PlayCharacter] 모델 위치 보정: {animatorOffset}");
    }
}
```

**장점:**
- ✅ 코드만 수정하면 됨
- ✅ 빠른 해결

**단점:**
- ⚠️ 임시방편
- ⚠️ 다른 문제 발생 가능
- ⚠️ 추천하지 않음

---

## 🎯 AresParSimVR 프로젝트 권장 해결 방법

### ⭐ 방법 1: Apply Root Motion 비활성화 (강력 추천)

**이유:**
1. ✅ PlayCharacter가 이미 Rigidbody로 이동 제어
2. ✅ 물리 시뮬레이션 (낙하, 낙하산) 사용 중
3. ✅ 애니메이션은 자세만 필요 (앉기, 서기, 낙하 자세)
4. ✅ 가장 간단하고 즉시 해결

**적용 방법:**

#### Option A: Inspector에서 설정
```
High_Jump_char_006 → Animator 컴포넌트
→ Apply Root Motion ✗ (체크 해제)
```

#### Option B: 코드로 설정 (권장)
```csharp
// PlayCharacter.cs - Start() 메서드에 추가

private void Start()
{
    // 기존 코드...

    // Animator Root Motion 비활성화
    if (animator)
    {
        animator.applyRootMotion = false;
        Debug.Log("[PlayCharacter] Root Motion 비활성화 - 물리 기반 이동 사용");
    }
}
```

---

## 📊 각 방법 비교

| 방법 | 난이도 | 해결 시간 | 부작용 | AresParSimVR 적합성 |
|------|--------|----------|--------|-------------------|
| Apply Root Motion OFF | ⭐ 쉬움 | 1분 | 없음 | ✅ 최적 |
| Avatar 설정 수정 | ⭐⭐⭐ 어려움 | 30분 | 다른 애니메이션 영향 | ⚠️ 불필요 |
| 위치 보정 | ⭐⭐ 보통 | 5분 | 다른 문제 가능 | ❌ 비추천 |

---

## 🔬 문제 진단 방법

### 현재 상태 확인

1. **Animator 설정 확인:**
```
Hierarchy → High_Jump_char_006 선택
→ Inspector → Animator 컴포넌트
→ Apply Root Motion: [ ] 또는 [✓]?
```

2. **실제 높이 측정:**
```csharp
// 디버그 코드 (PlayCharacter.cs)
private void Update()
{
    if (Input.GetKeyDown(KeyCode.Space))
    {
        Debug.Log($"캐릭터 Y 위치: {transform.position.y}");
        Debug.Log($"발 Y 위치: {leftFoot.position.y}"); // leftFoot Transform 필요
        Debug.Log($"Apply Root Motion: {animator.applyRootMotion}");
    }
}
```

3. **Scene 뷰에서 확인:**
```
Scene 뷰에서 캐릭터를 옆에서 봄
→ 발이 바닥(Y=0)보다 아래에 있는지 확인
```

---

## 🛠️ 단계별 적용 가이드

### Step 1: 현재 상태 백업
```
1. Hierarchy에서 High_Jump_char_006 선택
2. Inspector 전체 스크린샷
3. 또는 Prefab Apply 전에 테스트
```

### Step 2: Apply Root Motion 비활성화
```
1. High_Jump_char_006 → Animator 컴포넌트
2. Apply Root Motion 체크 해제
3. Play 모드로 즉시 테스트
```

### Step 3: 테스트
```
1. Play 모드 진입
2. 캐릭터가 바닥에 제대로 서 있는지 확인
3. Scene 뷰에서 다리가 바닥을 뚫지 않는지 확인
```

### Step 4: 애니메이션 동작 확인
```
1. 앉기/서기 애니메이션 테스트
2. 애니메이션 재생 시 위치 변화 없는지 확인
3. 정상 동작하면 Prefab Apply
```

---

## ❓ FAQ

### Q1: Apply Root Motion을 끄면 걷기 애니메이션이 안 움직이지 않나요?

**A:** 맞습니다. 하지만 AresParSimVR은:
- Rigidbody로 물리 이동
- 애니메이션은 자세만 표현
- 걷기 애니메이션 불필요 (항공 시뮬레이터)

### Q2: 나중에 걷기 애니메이션이 필요하면?

**A:** 두 가지 방법:
1. **스크립트로 이동** (권장):
   ```csharp
   // 애니메이션은 자세만, 이동은 코드로
   transform.position += velocity * Time.deltaTime;
   ```

2. **Root Motion + 물리 혼합**:
   ```csharp
   // Animator에서 이동량 가져오기
   Vector3 deltaPosition = animator.deltaPosition;
   rigidbody.MovePosition(transform.position + deltaPosition);
   ```

### Q3: FBX 설정을 바꾸면 다른 캐릭터에도 영향이 있나요?

**A:**
- 같은 FBX를 사용하는 모든 인스턴스에 영향
- 프리팹마다 다른 설정 불가
- 그래서 Apply Root Motion OFF가 더 안전

### Q4: Animator Controller가 없으면 FootIK를 못 쓰나요?

**A:**
- SimpleFootIK는 Animator Controller 필수
- Animator Controller 없으면 OnAnimatorIK() 호출 안 됨
- 하지만 빈 Animator Controller 만들어서 사용 가능

---

## 🎬 즉시 적용 코드

### PlayCharacter.cs 수정안

```csharp
// PlayCharacter.cs - Start() 메서드

private void Start()
{
    // 자유낙하 항력계수 계산
    dragCoefficient = CalculateDragCoefficient(rho: rho, Cd: Cd, area: A, mass: m);

    // Inspector에서 할당된 값 사용, 없으면 FindAnyObjectByType 사용
    _stateManager = _stateManagerInspector ? _stateManagerInspector : FindAnyObjectByType<StateManager_New>();
    _camera = _cameraInspector ? _cameraInspector : FindAnyObjectByType<CameraController>();

    // ===== 추가: Animator Root Motion 비활성화 =====
    if (animator)
    {
        animator.applyRootMotion = false;
        Debug.Log("[PlayCharacter] Apply Root Motion 비활성화 - 물리 기반 이동 사용");

        // Humanoid 리그 확인
        if (!animator.isHuman)
        {
            Debug.LogWarning("[PlayCharacter] Humanoid 리그가 아닙니다. FootIK가 작동하지 않을 수 있습니다.");
        }
    }
    else
    {
        Debug.LogError("[PlayCharacter] Animator 컴포넌트가 없습니다!");
    }
    // ============================================

    StateManager.OnInit += Init;
}
```

---

## ✅ 최종 체크리스트

### 문제 확인
- [ ] Animator Controller 추가 시 다리가 바닥 통과 확인
- [ ] 캐릭터가 허리 기준으로 배치되는지 확인
- [ ] Play 모드에서 Y 위치 로그 확인

### 해결 적용
- [ ] Apply Root Motion 비활성화 (Inspector 또는 코드)
- [ ] Play 모드로 즉시 테스트
- [ ] 다리가 바닥에 정상적으로 배치되는지 확인

### 추가 확인
- [ ] 애니메이션 재생 시 위치 변화 없는지 확인
- [ ] Rigidbody 물리 이동 정상 작동 확인
- [ ] FootIK 테스트 준비 완료

---

**요약:**
- **원인:** Apply Root Motion이 켜져 있어서 애니메이션의 Y 위치가 적용됨
- **해결:** `animator.applyRootMotion = false;` 설정
- **시간:** 1분
- **부작용:** 없음 (물리 기반 이동 사용 중이므로)

**다음 단계:**
1. Apply Root Motion 비활성화
2. 테스트 확인
3. SimpleFootIK 적용 준비
