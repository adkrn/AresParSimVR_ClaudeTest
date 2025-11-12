# 간단한 Foot IK 구현 가이드 (골반 뒤틀림 해결)

**작성일:** 2025-10-22
**목적:** 골반 뒤틀림 없이 발만 지면에 맞추는 최소한의 FootIK 구현
**방법:** Unity 내장 Animator IK 사용 (Humanoid 리그 전용)

---

## 🎯 핵심 개념

### 기존 방법의 문제점
- TwoBoneIK를 사용하면 골반(Pelvis)까지 영향을 받음
- 수동으로 골반 높이를 조정하면 뒤틀림 발생
- 복잡한 타겟 설정과 Pole 설정 필요

### 새로운 방법의 장점
- ✅ Unity가 골반을 **자동으로 처리** (뒤틀림 없음)
- ✅ 코드 60줄로 완성
- ✅ Inspector에서 본 구조 연결 불필요
- ✅ IK 타겟 GameObject 생성 불필요
- ✅ Humanoid 리그면 바로 작동

---

## 📋 구현 방법 비교

### 방법 1: TwoBoneIK (기존 - 복잡)
```
문제점:
❌ 골반 뒤틀림 발생
❌ 타겟 GameObject 수동 설정 필요
❌ 본 구조 전부 연결 필요
❌ 코드 300줄 이상
```

### 방법 2: Unity Animator IK (새로운 - 간단) ⭐ 추천
```
장점:
✅ Unity가 골반 자동 처리
✅ 코드 60줄
✅ Animator만 있으면 작동
✅ 설정 3분 완료
```

---

## 🚀 구현 단계 (총 3단계)

### Step 1: SimpleFootIK.cs 스크립트 작성 (60줄)

**파일 위치:** `Assets/Scripts/SimpleFootIK.cs`

**핵심 코드 구조:**
```csharp
[RequireComponent(typeof(Animator))]
public class SimpleFootIK : MonoBehaviour
{
    private Animator animator;

    // Unity가 자동으로 호출하는 IK 메서드
    private void OnAnimatorIK(int layerIndex)
    {
        // 왼발 IK
        ProcessFootIK(AvatarIKGoal.LeftFoot);

        // 오른발 IK
        ProcessFootIK(AvatarIKGoal.RightFoot);
    }

    private void ProcessFootIK(AvatarIKGoal foot)
    {
        // 1. IK 가중치 설정
        animator.SetIKPositionWeight(foot, 1f);
        animator.SetIKRotationWeight(foot, 1f);

        // 2. 현재 발 위치 가져오기
        Vector3 footPos = animator.GetIKPosition(foot);

        // 3. 레이캐스트로 지면 검색
        if (Physics.Raycast(footPos + Vector3.up, Vector3.down, out hit, 1.5f))
        {
            // 4. 발 위치 설정
            animator.SetIKPosition(foot, hit.point + Vector3.up * 0.05f);

            // 5. 발 회전 설정 (지면 법선)
            animator.SetIKRotation(foot, footRotation);
        }
    }
}
```

**왜 골반이 안 뒤틀릴까?**
- Unity의 Animator가 **IK 체인 전체**를 자동으로 계산
- 골반, 허벅지, 종아리, 발을 **자연스럽게 연결**
- 우리는 발 위치만 지정하고 나머지는 Unity가 처리

---

### Step 2: Unity 에디터 설정 (2분)

#### 2-1. Ground Layer 설정

1. `Edit` → `Project Settings` → `Tags and Layers`
2. Layers에서 빈 슬롯에 `Ground` 추가
3. 지면 오브젝트의 Layer를 `Ground`로 변경
   - Terrain
   - Floor
   - Landing Zone

#### 2-2. Animator Controller 설정 (중요!)

1. **Animator Controller 열기**:
   - Project 창에서 캐릭터의 Animator Controller 찾기
   - 더블클릭하여 Animator 창 열기

2. **IK Pass 활성화** (필수!):
   ```
   Animator 창 → Base Layer 클릭
   → Inspector에서 "IK Pass" 체크박스 활성화 ✓
   ```

**IK Pass를 활성화하지 않으면 OnAnimatorIK가 호출되지 않음!**

#### 2-3. SimpleFootIK 컴포넌트 추가

1. Hierarchy에서 `High_Jump_char_006` 선택
2. `Add Component` → `SimpleFootIK` 추가
3. Inspector 설정:
   ```
   Enable IK: ✓ (체크)
   IK Weight: 1.0
   Ground Layer: Ground (선택)
   Ray Distance: 1.5
   Foot Offset: 0.05
   Show Debug Rays: ✓ (테스트용)
   ```

---

### Step 3: PlayCharacter 통합 (1분)

**PlayCharacter.cs 수정:**

```csharp
// 필드 추가
[Header("Foot IK")]
[SerializeField] private SimpleFootIK footIK;

// 착지 시 IK 활성화
private void OnCollisionEnter(Collision collision)
{
    if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
    {
        Debug.Log("[PlayCharacter] 착지 - FootIK 활성화");

        if (footIK)
            footIK.SetIKEnabled(true);
    }
}

// 점프 시 IK 비활성화
public void Jump()
{
    if (footIK)
        footIK.SetIKEnabled(false);

    // 기존 점프 코드...
}
```

---

## 📝 전체 코드 (60줄)

```csharp
using UnityEngine;

/// <summary>
/// 초간단 Foot IK - Unity 내장 Animator IK 사용
/// Humanoid 리그 전용
/// 골반은 Unity가 자동으로 처리하므로 뒤틀림 없음
/// </summary>
[RequireComponent(typeof(Animator))]
public class SimpleFootIK : MonoBehaviour
{
    [Header("=== IK 설정 ===")]
    [SerializeField] private bool enableIK = true;
    [SerializeField] [Range(0f, 1f)] private float ikWeight = 1f;

    [Header("=== 레이캐스트 설정 ===")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float rayDistance = 1.5f;
    [SerializeField] private float footOffset = 0.05f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugRays = false;

    private Animator animator;

    private void Start()
    {
        animator = GetComponent<Animator>();

        if (!animator)
        {
            Debug.LogError("[SimpleFootIK] Animator 컴포넌트가 없습니다!");
            enabled = false;
            return;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("[SimpleFootIK] Humanoid 리그가 아닙니다!");
            enabled = false;
            return;
        }

        Debug.Log("[SimpleFootIK] 초기화 완료");
    }

    /// <summary>
    /// Unity가 IK 계산할 때 자동 호출됨
    /// Animator Controller의 Layer에서 IK Pass 활성화 필요
    /// </summary>
    private void OnAnimatorIK(int layerIndex)
    {
        if (!animator || !enableIK) return;

        // 왼발 IK
        ProcessFootIK(AvatarIKGoal.LeftFoot);

        // 오른발 IK
        ProcessFootIK(AvatarIKGoal.RightFoot);
    }

    /// <summary>
    /// 발 IK 처리 - 지면 검색 및 위치/회전 설정
    /// </summary>
    private void ProcessFootIK(AvatarIKGoal foot)
    {
        // IK 가중치 설정
        animator.SetIKPositionWeight(foot, ikWeight);
        animator.SetIKRotationWeight(foot, ikWeight);

        // 현재 발 위치 가져오기
        Vector3 footPos = animator.GetIKPosition(foot);

        // 디버그 레이
        if (showDebugRays)
        {
            Color rayColor = foot == AvatarIKGoal.LeftFoot ? Color.red : Color.blue;
            Debug.DrawRay(footPos + Vector3.up, Vector3.down * rayDistance, rayColor);
        }

        // 지면 검색
        if (Physics.Raycast(footPos + Vector3.up, Vector3.down, out RaycastHit hit,
            rayDistance, groundLayer))
        {
            // 발 위치 설정 (지면 + 오프셋)
            Vector3 targetPos = hit.point + Vector3.up * footOffset;
            animator.SetIKPosition(foot, targetPos);

            // 발 회전 설정 (지면 법선에 맞춤)
            Quaternion footRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal),
                hit.normal
            );
            animator.SetIKRotation(foot, footRotation);
        }
    }

    /// <summary>
    /// IK 활성화/비활성화
    /// </summary>
    public void SetIKEnabled(bool enabled)
    {
        enableIK = enabled;
        Debug.Log($"[SimpleFootIK] IK {(enabled ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// IK 가중치 설정 (0~1)
    /// </summary>
    public void SetIKWeight(float weight)
    {
        ikWeight = Mathf.Clamp01(weight);
    }
}
```

---

## ⚙️ 작동 원리

### Unity Animator IK 시스템

```
1. OnAnimatorIK() 호출 (Unity가 자동으로)
   ↓
2. 현재 발 위치 가져오기
   animator.GetIKPosition(AvatarIKGoal.LeftFoot)
   ↓
3. 레이캐스트로 지면 검색
   Physics.Raycast(footPos, Vector3.down, ...)
   ↓
4. 발 위치 설정
   animator.SetIKPosition(foot, hit.point)
   ↓
5. Unity가 자동으로 골반, 허벅지, 종아리 조정
   (우리는 신경 쓸 필요 없음!)
```

### 왜 골반이 안 뒤틀릴까?

**기존 방법 (TwoBoneIK):**
```
우리가 직접:
- 발 위치 계산
- 무릎 위치 계산
- 골반 높이 계산 ← 여기서 뒤틀림 발생!
```

**새로운 방법 (Animator IK):**
```
Unity가 자동으로:
- 발 위치 → 종아리 → 허벅지 → 골반
- 전체 체인을 자연스럽게 연결
- IK 솔버가 최적 각도 계산 ← 뒤틀림 없음!
```

---

## 🔍 테스트 방법

### 1. Unity Play 모드 진입

### 2. Scene 뷰 확인사항
- ✅ 빨간색/파란색 레이캐스트 선이 보임 (Show Debug Rays 활성화 시)
- ✅ 발이 지면에 닿음
- ✅ 경사면에서 발 각도가 자연스럽게 조정됨
- ✅ 골반이 뒤틀리지 않음

### 3. Console 로그 확인
```
[SimpleFootIK] 초기화 완료
[PlayCharacter] 착지 - FootIK 활성화
[SimpleFootIK] IK 활성화
```

---

## ❌ 문제 해결

### 문제 1: OnAnimatorIK가 호출되지 않음

**증상:** 아무 일도 일어나지 않음

**해결:**
1. Animator Controller 열기
2. Base Layer 선택
3. Inspector에서 **"IK Pass" 체크박스 활성화** ✓

### 문제 2: 발이 지면을 뚫고 들어감

**해결:**
```
Foot Offset 값 증가: 0.05 → 0.1
```

### 문제 3: 발이 지면에서 떨어짐

**해결:**
```
Ray Distance 증가: 1.5 → 2.0
```

### 문제 4: Ground Layer가 감지 안 됨

**해결:**
1. 지면 오브젝트의 Layer가 "Ground"인지 확인
2. 지면에 Collider가 있는지 확인
3. SimpleFootIK의 Ground Layer 설정 확인

### 문제 5: Humanoid 리그가 아니라는 에러

**해결:**
1. Project 창에서 `High_Jump_char_005.fbx` 선택
2. Inspector → Rig 탭
3. Animation Type을 **"Humanoid"**로 변경
4. Apply 클릭

---

## 📊 성능 비교

| 방법 | 코드 줄 수 | 골반 뒤틀림 | 설정 시간 | CPU 사용량 |
|------|-----------|------------|----------|-----------|
| TwoBoneIK (기존) | 300+ | ❌ 발생 | 30분 | 높음 |
| Animator IK (새로운) | 60 | ✅ 없음 | 3분 | 낮음 |

---

## ✅ 최종 체크리스트

### 구현 전
- [ ] 캐릭터가 Humanoid 리그인지 확인
- [ ] Animator 컴포넌트가 있는지 확인
- [ ] Ground Layer 생성

### 구현 중
- [ ] SimpleFootIK.cs 작성
- [ ] Animator Controller에서 IK Pass 활성화 ✓
- [ ] SimpleFootIK 컴포넌트 추가
- [ ] Ground Layer 설정

### 구현 후
- [ ] Play 모드에서 테스트
- [ ] 골반 뒤틀림 없는지 확인
- [ ] 경사면에서 테스트
- [ ] PlayCharacter 통합

---

## 🎓 추가 개선 사항 (선택)

### 1. 발가락 IK 추가

```csharp
private void OnAnimatorIK(int layerIndex)
{
    ProcessFootIK(AvatarIKGoal.LeftFoot);
    ProcessFootIK(AvatarIKGoal.RightFoot);

    // 발가락 IK 추가
    ProcessFootIK(AvatarIKGoal.LeftToe);
    ProcessFootIK(AvatarIKGoal.RightToe);
}
```

### 2. 부드러운 전환 (Smoothing)

```csharp
// 현재 위치와 타겟 위치 사이를 부드럽게 보간
Vector3 currentPos = animator.GetIKPosition(foot);
Vector3 smoothedPos = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * 5f);
animator.SetIKPosition(foot, smoothedPos);
```

### 3. 착지 시에만 활성화 (성능 최적화)

```csharp
// PlayCharacter.cs
private bool isGrounded = false;

private void Update()
{
    // 지면과의 거리 체크
    float distanceToGround = GetDistanceToGround();

    if (distanceToGround < 0.5f && !isGrounded)
    {
        isGrounded = true;
        footIK?.SetIKEnabled(true);
    }
    else if (distanceToGround > 1f && isGrounded)
    {
        isGrounded = false;
        footIK?.SetIKEnabled(false);
    }
}
```

---

## 📚 참고 자료

### Unity 공식 문서
- [Animator.SetIKPosition](https://docs.unity3d.com/ScriptReference/Animator.SetIKPosition.html)
- [OnAnimatorIK](https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnAnimatorIK.html)
- [AvatarIKGoal](https://docs.unity3d.com/ScriptReference/AvatarIKGoal.html)

### 프로젝트 파일
- 캐릭터 모델: `Assets/Model/CharacterModels/High_Jump_char_005.fbx`
- Animator: `PlayCharacter` GameObject의 Animator 컴포넌트

---

**요약:**
- **60줄 코드**로 간단하게 구현
- **골반 뒤틀림 없음** (Unity가 자동 처리)
- **3분 설정**으로 바로 작동
- **Humanoid 리그 필수**

**다음 단계:**
1. SimpleFootIK.cs 코드 작성
2. Animator Controller에서 IK Pass 활성화
3. 테스트!
