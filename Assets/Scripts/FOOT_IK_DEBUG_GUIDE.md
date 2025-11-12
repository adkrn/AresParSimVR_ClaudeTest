# Foot IK 버그 진단 - 오른쪽 다리가 왼쪽 위치로 고정

**문제:** 오른쪽 다리가 왼쪽 다리 위치로 고정됨
**증상:** 양쪽 발이 같은 위치에 있음
**작성일:** 2025-10-22

---

## 🔍 원인 분석

### 가능한 원인 5가지

#### 원인 1: animator.GetIKPosition() 타이밍 문제 ⭐ (가장 가능성 높음)

**문제:**
```csharp
private void Update()
{
    SolveFeetPositions();  // ← 여기서 호출
}

private void SolveFootPosition(AvatarIKGoal foot, out Vector3 position, ...)
{
    position = animator.GetIKPosition(foot);  // ← 문제!
    // Update()에서 호출하면 제대로 작동 안 함
}
```

**왜 문제인가?**
- `animator.GetIKPosition()`은 **OnAnimatorIK() 안에서만** 정확한 값 반환
- Update()에서 호출하면 초기화되지 않은 값 또는 이전 프레임 값 반환
- 왼발을 먼저 계산한 후, 오른발 계산 시 왼발 값이 그대로 사용될 수 있음

**해결:**
```csharp
// animator.GetIKPosition() 사용 ❌
position = animator.GetIKPosition(foot);

// animator.GetBoneTransform() 사용 ✅
Transform footBone = animator.GetBoneTransform(
    foot == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot
);
position = footBone.position;
```

---

#### 원인 2: 변수 재사용 또는 참조 문제

**문제:**
```csharp
private Vector3 footPosition;  // 멤버 변수로 선언

private void SolveFootPosition(...)
{
    footPosition = ...;  // ← 같은 변수 사용
}
```

**왜 문제인가?**
- 왼발 계산 시 footPosition에 값 저장
- 오른발 계산 시 같은 footPosition 변수 재사용
- 왼발 값이 오버라이드 안 되고 그대로 유지

**해결:**
```csharp
// 각각 별도 변수 사용
private Vector3 leftFootPosition;
private Vector3 rightFootPosition;
```

---

#### 원인 3: Out 파라미터 문제

**문제:**
```csharp
SolveFootPosition(AvatarIKGoal.LeftFoot, out leftFootPosition, ...);
SolveFootPosition(AvatarIKGoal.RightFoot, out leftFootPosition, ...);  // ← 복사 실수!
```

**왜 문제인가?**
- 코드 복사 시 Left/Right 구분 안 함
- 오른발도 leftFootPosition에 저장

**해결:**
- 코드 재확인
- 오타 수정

---

#### 원인 4: 레이캐스트 시작 위치가 동일

**문제:**
```csharp
Vector3 rayStart = transform.position + Vector3.up;  // ← 항상 같은 위치!

if (Physics.Raycast(rayStart, Vector3.down, ...))
{
    // 왼발이든 오른발이든 같은 지점에서 레이캐스트
}
```

**왜 문제인가?**
- 캐릭터 중심에서 레이캐스트
- 왼발/오른발 구분 없이 같은 지면 지점 감지
- 결과적으로 같은 위치

**해결:**
```csharp
// 각 발의 현재 위치에서 레이캐스트
Vector3 rayStart = footBone.position + Vector3.up;  // ← 발 위치 기준
```

---

#### 원인 5: Animator IK Goal 잘못 지정

**문제:**
```csharp
// 양쪽 다 LeftFoot로 설정
animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootPosition);
animator.SetIKPosition(AvatarIKGoal.LeftFoot, rightFootPosition);  // ← 잘못됨!
```

**해결:**
```csharp
animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootPosition);
animator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootPosition);  // ← 올바름
```

---

## 🎯 가장 가능성 높은 원인

### **원인 1: animator.GetIKPosition() 타이밍 문제** ⭐

제가 제공한 코드에서:

```csharp
private void Update()
{
    if (!enableIK) return;
    SolveFeetPositions();  // ← Update()에서 호출
}

private void SolveFootPosition(AvatarIKGoal foot, out Vector3 position, ...)
{
    // ❌ 문제: Update()에서 GetIKPosition 호출
    position = animator.GetIKPosition(foot);
    rotation = animator.GetIKRotation(foot);

    // 레이캐스트...
}
```

**문제점:**
1. `GetIKPosition()`은 OnAnimatorIK() 컨텍스트 안에서만 정확함
2. Update()에서 호출하면 초기화 안 된 값 또는 (0,0,0) 반환 가능
3. 왼발/오른발 모두 같은 값 반환

---

## ✅ 해결 방법

### 해결책 1: GetBoneTransform 사용 (권장) ⭐

```csharp
private void SolveFootPosition(
    AvatarIKGoal foot,
    out Vector3 position,
    out Quaternion rotation,
    out float ikWeight)
{
    // ✅ animator.GetIKPosition() 대신 GetBoneTransform 사용
    HumanBodyBones bone = foot == AvatarIKGoal.LeftFoot
        ? HumanBodyBones.LeftFoot
        : HumanBodyBones.RightFoot;

    Transform footBone = animator.GetBoneTransform(bone);

    // 현재 발의 실제 위치 가져오기
    position = footBone.position;
    rotation = footBone.rotation;
    ikWeight = 0f;

    // 레이캐스트 시작 위치 (각 발의 위치 기준)
    Vector3 rayStart = footBone.position + Vector3.up * rayStartHeight;

    if (showDebugRays)
    {
        Color rayColor = foot == AvatarIKGoal.LeftFoot ? Color.red : Color.blue;
        Debug.DrawRay(rayStart, Vector3.down * rayDistance, rayColor);
    }

    // 지면 검색
    if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
        rayDistance, groundLayer))
    {
        // 목표 위치 계산
        position = hit.point + Vector3.up * footOffset;

        // 목표 회전 계산
        rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, hit.normal),
            hit.normal
        );

        ikWeight = 1f;
    }
}
```

---

### 해결책 2: 전체 프로세스 OnAnimatorIK로 이동

```csharp
// Update()에서는 계산 안 함
private void Update()
{
    // 아무것도 안 함 (또는 다른 로직)
}

// OnAnimatorIK에서 전부 처리
private void OnAnimatorIK(int layerIndex)
{
    if (!animator || !enableIK) return;

    // Step 1: 왼발 계산 및 즉시 적용
    SolveAndApplyFoot(AvatarIKGoal.LeftFoot);

    // Step 2: 오른발 계산 및 즉시 적용
    SolveAndApplyFoot(AvatarIKGoal.RightFoot);

    // Step 3: 골반 조정
    if (adjustPelvis)
    {
        MovePelvisHeight();
        ApplyPelvisHeight();
    }
}

private void SolveAndApplyFoot(AvatarIKGoal foot)
{
    // GetIKPosition은 OnAnimatorIK 안에서 호출 ✅
    Vector3 currentPos = animator.GetIKPosition(foot);
    Quaternion currentRot = animator.GetIKRotation(foot);

    // 레이캐스트
    Vector3 rayStart = currentPos + Vector3.up * rayStartHeight;

    if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
        rayDistance, groundLayer))
    {
        Vector3 targetPos = hit.point + Vector3.up * footOffset;
        Quaternion targetRot = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, hit.normal),
            hit.normal
        );

        // 즉시 적용
        animator.SetIKPositionWeight(foot, ikWeight);
        animator.SetIKRotationWeight(foot, ikWeight);
        animator.SetIKPosition(foot, targetPos);
        animator.SetIKRotation(foot, targetRot);
    }
}
```

---

## 🔬 디버깅 방법

### 디버그 로그 추가

```csharp
private void SolveFootPosition(
    AvatarIKGoal foot,
    out Vector3 position,
    out Quaternion rotation,
    out float ikWeight)
{
    HumanBodyBones bone = foot == AvatarIKGoal.LeftFoot
        ? HumanBodyBones.LeftFoot
        : HumanBodyBones.RightFoot;

    Transform footBone = animator.GetBoneTransform(bone);

    position = footBone.position;
    rotation = footBone.rotation;
    ikWeight = 0f;

    // ===== 디버그 로그 =====
    Debug.Log($"[FootIK] {foot} 본 위치: {footBone.position}");
    Debug.Log($"[FootIK] {foot} 계산된 위치: {position}");
    // ======================

    Vector3 rayStart = footBone.position + Vector3.up * rayStartHeight;

    if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
        rayDistance, groundLayer))
    {
        position = hit.point + Vector3.up * footOffset;

        // ===== 디버그 로그 =====
        Debug.Log($"[FootIK] {foot} 지면 감지: {hit.point}");
        Debug.Log($"[FootIK] {foot} 최종 위치: {position}");
        // ======================

        rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(transform.forward, hit.normal),
            hit.normal
        );

        ikWeight = 1f;
    }
    else
    {
        Debug.LogWarning($"[FootIK] {foot} 지면 감지 실패!");
    }
}
```

**예상 출력:**
```
[FootIK] LeftFoot 본 위치: (-0.2, 0.1, 0.5)
[FootIK] LeftFoot 계산된 위치: (-0.2, 0.1, 0.5)
[FootIK] LeftFoot 지면 감지: (-0.2, 0.0, 0.5)
[FootIK] LeftFoot 최종 위치: (-0.2, 0.05, 0.5)

[FootIK] RightFoot 본 위치: (0.2, 0.1, 0.5)  ← 이게 달라야 정상
[FootIK] RightFoot 계산된 위치: (0.2, 0.1, 0.5)
[FootIK] RightFoot 지면 감지: (0.2, 0.0, 0.5)
[FootIK] RightFoot 최종 위치: (0.2, 0.05, 0.5)
```

**만약 문제가 있다면:**
```
[FootIK] LeftFoot 본 위치: (-0.2, 0.1, 0.5)
[FootIK] RightFoot 본 위치: (-0.2, 0.1, 0.5)  ← 같으면 문제!
```

---

### Scene 뷰에서 확인

```csharp
private void OnDrawGizmos()
{
    if (!Application.isPlaying || !animator) return;

    // 왼발 위치 - 빨간 구
    Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
    if (leftFoot)
    {
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(leftFoot.position, 0.05f);
        Gizmos.DrawLine(leftFoot.position, leftFootPosition);
    }

    // 오른발 위치 - 파란 구
    Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    if (rightFoot)
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(rightFoot.position, 0.05f);
        Gizmos.DrawLine(rightFoot.position, rightFootPosition);
    }

    // 목표 위치 - 초록 구
    Gizmos.color = Color.green;
    Gizmos.DrawSphere(leftFootPosition, 0.03f);
    Gizmos.DrawSphere(rightFootPosition, 0.03f);
}
```

**Scene 뷰에서 확인:**
- 빨간 구 (왼발 본 위치)
- 파란 구 (오른발 본 위치)
- 초록 구 (IK 목표 위치)

**만약 초록 구 2개가 같은 위치면 → 코드 문제**

---

## 📝 수정된 전체 코드

```csharp
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FootIKVelog : MonoBehaviour
{
    [Header("=== IK 설정 ===")]
    [SerializeField] private bool enableIK = true;
    [SerializeField] [Range(0f, 1f)] private float ikWeight = 1f;

    [Header("=== 골반 조정 ===")]
    [SerializeField] private bool adjustPelvis = true;
    [SerializeField] private float pelvisOffset = 0f;
    [SerializeField] [Range(0f, 1f)] private float pelvisSpeed = 0.5f;

    [Header("=== 레이캐스트 설정 ===")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float rayStartHeight = 1.0f;
    [SerializeField] private float rayDistance = 1.5f;
    [SerializeField] private float footOffset = 0.05f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugRays = false;
    [SerializeField] private bool showDebugLogs = false;

    private Animator animator;

    // 발 IK 데이터
    private Vector3 leftFootPosition;
    private Vector3 rightFootPosition;
    private Quaternion leftFootRotation;
    private Quaternion rightFootRotation;
    private float leftFootIkWeight;
    private float rightFootIkWeight;

    // 골반 조정
    private float pelvisHeightOffset;

    private void Start()
    {
        animator = GetComponent<Animator>();

        if (!animator || !animator.isHuman)
        {
            Debug.LogError("[FootIK] Animator 또는 Humanoid 리그 문제!");
            enabled = false;
            return;
        }

        Debug.Log("[FootIK] 초기화 완료");
    }

    private void Update()
    {
        if (!enableIK) return;

        // Step 1: 발 위치와 회전 계산
        SolveFeetPositions();
    }

    private void SolveFeetPositions()
    {
        // 왼발 계산
        SolveFootPosition(
            AvatarIKGoal.LeftFoot,
            HumanBodyBones.LeftFoot,
            out leftFootPosition,
            out leftFootRotation,
            out leftFootIkWeight
        );

        // 오른발 계산
        SolveFootPosition(
            AvatarIKGoal.RightFoot,
            HumanBodyBones.RightFoot,
            out rightFootPosition,
            out rightFootRotation,
            out rightFootIkWeight
        );

        // Step 2: 골반 높이 조정
        if (adjustPelvis)
        {
            MovePelvisHeight();
        }
    }

    private void SolveFootPosition(
        AvatarIKGoal ikGoal,
        HumanBodyBones bone,
        out Vector3 position,
        out Quaternion rotation,
        out float weight)
    {
        // ✅ GetBoneTransform으로 실제 본 위치 가져오기
        Transform footBone = animator.GetBoneTransform(bone);

        if (!footBone)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            weight = 0f;
            Debug.LogError($"[FootIK] {bone} 본을 찾을 수 없습니다!");
            return;
        }

        // 현재 발의 실제 위치
        position = footBone.position;
        rotation = footBone.rotation;
        weight = 0f;

        // 레이캐스트 시작 위치 (각 발의 위치 기준)
        Vector3 rayStart = footBone.position + Vector3.up * rayStartHeight;

        if (showDebugRays)
        {
            Color rayColor = ikGoal == AvatarIKGoal.LeftFoot ? Color.red : Color.blue;
            Debug.DrawRay(rayStart, Vector3.down * rayDistance, rayColor);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[FootIK] {ikGoal} 본 위치: {footBone.position}");
        }

        // 지면 검색
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
            rayDistance, groundLayer))
        {
            // 목표 위치
            position = hit.point + Vector3.up * footOffset;

            // 목표 회전
            rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal),
                hit.normal
            );

            weight = 1f;

            if (showDebugLogs)
            {
                Debug.Log($"[FootIK] {ikGoal} 지면 감지: {hit.point}, 최종: {position}");
            }
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning($"[FootIK] {ikGoal} 지면 감지 실패!");
        }
    }

    private void MovePelvisHeight()
    {
        float leftOffsetY = leftFootPosition.y - transform.position.y;
        float rightOffsetY = rightFootPosition.y - transform.position.y;

        float targetPelvisOffset = Mathf.Min(leftOffsetY, rightOffsetY);

        pelvisHeightOffset = Mathf.Lerp(
            pelvisHeightOffset,
            targetPelvisOffset + pelvisOffset,
            pelvisSpeed
        );
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!animator || !enableIK) return;

        // 골반 높이 적용
        if (adjustPelvis)
        {
            Vector3 bodyPosition = animator.bodyPosition;
            bodyPosition.y += pelvisHeightOffset;
            animator.bodyPosition = bodyPosition;
        }

        // 왼발 IK 적용
        ApplyFootIK(AvatarIKGoal.LeftFoot, leftFootPosition, leftFootRotation, leftFootIkWeight);

        // 오른발 IK 적용
        ApplyFootIK(AvatarIKGoal.RightFoot, rightFootPosition, rightFootRotation, rightFootIkWeight);
    }

    private void ApplyFootIK(AvatarIKGoal foot, Vector3 position, Quaternion rotation, float weight)
    {
        float finalWeight = weight * ikWeight;

        animator.SetIKPositionWeight(foot, finalWeight);
        animator.SetIKRotationWeight(foot, finalWeight);

        if (finalWeight > 0)
        {
            animator.SetIKPosition(foot, position);
            animator.SetIKRotation(foot, rotation);
        }
    }

    public void SetIKEnabled(bool enabled)
    {
        enableIK = enabled;
    }

    public void SetIKWeight(float weight)
    {
        ikWeight = Mathf.Clamp01(weight);
    }

    // 디버그용 Gizmos
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !animator) return;

        // 왼발 - 빨간색
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (leftFoot)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(leftFoot.position, 0.05f);
            Gizmos.DrawLine(leftFoot.position, leftFootPosition);
        }

        // 오른발 - 파란색
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (rightFoot)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(rightFoot.position, 0.05f);
            Gizmos.DrawLine(rightFoot.position, rightFootPosition);
        }

        // 목표 위치 - 초록색
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(leftFootPosition, 0.03f);
        Gizmos.DrawSphere(rightFootPosition, 0.03f);
    }
}
```

---

## ✅ 핵심 수정 사항

### 1. GetBoneTransform 사용
```csharp
// ❌ Before
position = animator.GetIKPosition(foot);

// ✅ After
Transform footBone = animator.GetBoneTransform(bone);
position = footBone.position;
```

### 2. HumanBodyBones 명시
```csharp
SolveFootPosition(
    AvatarIKGoal.LeftFoot,
    HumanBodyBones.LeftFoot,  // ← 명시적으로 전달
    ...
);
```

### 3. 디버그 로그 추가
```csharp
[SerializeField] private bool showDebugLogs = false;

if (showDebugLogs)
{
    Debug.Log($"[FootIK] {ikGoal} 본 위치: {footBone.position}");
}
```

---

**요약:**
- **원인:** animator.GetIKPosition()을 Update()에서 호출 (타이밍 문제)
- **해결:** animator.GetBoneTransform()으로 실제 본 Transform 사용
- **확인:** 디버그 로그로 양발 위치가 다른지 확인

이 수정된 코드로 테스트해보시겠어요?
