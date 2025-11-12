# Foot IK 구현 - Velog 블로그 방법 (AresParSimVR 적용)

**참고 블로그:** https://velog.io/@eugene-doobu/Feet-ik-%EB%95%85-%EC%9C%84%EB%A5%BC-%EA%B1%B8%EC%96%B4%EB%8B%A4%EB%8B%88%EA%B3%A0-%EC%9E%88%EC%96%B4%EC%9A%94
**적용 프로젝트:** AresParSimVR - High_Jump_char_006
**방법:** Animator IK + 골반 자동 조정
**작성일:** 2025-10-22

---

## 🎯 블로그 방법의 핵심

### Velog 블로그의 3단계 프로세스

```
1. SolveFeetPositions()
   → 레이캐스트로 발 위치와 회전 계산

2. MovePelvisHeight()
   → 골반 높이를 자동으로 조정
   → 발 위치 변화에 따라 전체 스켈레톤이 자연스럽게 움직임

3. MoveFeetToIkPoint()
   → Animator.SetIKPosition() 사용
   → Animator.SetIKRotation() 사용
   → 선형보간으로 부드러운 적용
```

### 왜 이 방법이 좋은가?

```
✅ Unity Animator IK 시스템 활용 (안정적)
✅ 골반 높이 자동 조정 (매우 자연스러움)
✅ 전체 IK 체인 자동 계산
✅ 부드러운 전환 (선형보간)
```

---

## 🚀 AresParSimVR 적용 버전

### 전체 코드 (100줄)

```csharp
using UnityEngine;

/// <summary>
/// Velog 블로그 방법 기반 Foot IK 구현
/// - Animator IK 시스템 사용
/// - 골반 높이 자동 조정
/// - 3단계 프로세스: Solve → Move → Apply
/// </summary>
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

        if (!animator)
        {
            Debug.LogError("[FootIK] Animator 컴포넌트가 없습니다!");
            enabled = false;
            return;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("[FootIK] Humanoid 리그가 아닙니다!");
            enabled = false;
            return;
        }

        Debug.Log("[FootIK] 초기화 완료 (Velog 방법)");
    }

    private void Update()
    {
        if (!enableIK) return;

        // Step 1: 발 위치와 회전 계산 (레이캐스트)
        SolveFeetPositions();
    }

    /// <summary>
    /// Step 1: 레이캐스트로 발의 목표 위치와 회전 계산
    /// </summary>
    private void SolveFeetPositions()
    {
        // 왼발 계산
        SolveFootPosition(
            AvatarIKGoal.LeftFoot,
            out leftFootPosition,
            out leftFootRotation,
            out leftFootIkWeight
        );

        // 오른발 계산
        SolveFootPosition(
            AvatarIKGoal.RightFoot,
            out rightFootPosition,
            out rightFootRotation,
            out rightFootIkWeight
        );

        // Step 2: 골반 높이 조정 (발 위치 기반)
        if (adjustPelvis)
        {
            MovePelvisHeight();
        }
    }

    /// <summary>
    /// 개별 발의 위치와 회전 계산
    /// </summary>
    private void SolveFootPosition(
        AvatarIKGoal foot,
        out Vector3 position,
        out Quaternion rotation,
        out float ikWeight)
    {
        // 현재 발 위치 가져오기
        position = animator.GetIKPosition(foot);
        rotation = animator.GetIKRotation(foot);
        ikWeight = 0f;

        // 레이캐스트 시작 위치
        Vector3 rayStart = position + Vector3.up * rayStartHeight;

        // 디버그 레이
        if (showDebugRays)
        {
            Color rayColor = foot == AvatarIKGoal.LeftFoot ? Color.red : Color.blue;
            Debug.DrawRay(rayStart, Vector3.down * rayDistance, rayColor);
        }

        // 지면 검색
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
            rayDistance, groundLayer))
        {
            // 목표 위치 계산 (지면 + 오프셋)
            position = hit.point + Vector3.up * footOffset;

            // 목표 회전 계산 (지면 법선)
            rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal),
                hit.normal
            );

            // IK 가중치 설정 (지면 감지 시)
            ikWeight = 1f;
        }
    }

    /// <summary>
    /// Step 2: 골반 높이 조정
    /// 블로그 핵심: 발 위치 변화에 따라 골반도 이동
    /// </summary>
    private void MovePelvisHeight()
    {
        // 왼발과 오른발 중 더 낮은 위치 찾기
        float leftOffsetY = leftFootPosition.y - transform.position.y;
        float rightOffsetY = rightFootPosition.y - transform.position.y;

        // 더 낮은 발 기준으로 골반 조정
        float targetPelvisOffset = Mathf.Min(leftOffsetY, rightOffsetY);

        // 부드럽게 전환 (선형보간)
        pelvisHeightOffset = Mathf.Lerp(
            pelvisHeightOffset,
            targetPelvisOffset + pelvisOffset,
            pelvisSpeed
        );
    }

    /// <summary>
    /// Step 3: 계산된 IK 값을 Animator에 적용
    /// Unity가 OnAnimatorIK 호출 시 자동 실행
    /// </summary>
    private void OnAnimatorIK(int layerIndex)
    {
        if (!animator || !enableIK) return;

        // 골반 높이 적용
        if (adjustPelvis)
        {
            ApplyPelvisHeight();
        }

        // 왼발 IK 적용
        MoveFeetToIkPoint(
            AvatarIKGoal.LeftFoot,
            leftFootPosition,
            leftFootRotation,
            leftFootIkWeight
        );

        // 오른발 IK 적용
        MoveFeetToIkPoint(
            AvatarIKGoal.RightFoot,
            rightFootPosition,
            rightFootRotation,
            rightFootIkWeight
        );
    }

    /// <summary>
    /// 골반 높이를 Animator에 적용
    /// </summary>
    private void ApplyPelvisHeight()
    {
        // Body Position (골반) 조정
        Vector3 bodyPosition = animator.bodyPosition;
        bodyPosition.y += pelvisHeightOffset;
        animator.bodyPosition = bodyPosition;
    }

    /// <summary>
    /// 발 IK를 Animator에 적용
    /// </summary>
    private void MoveFeetToIkPoint(
        AvatarIKGoal foot,
        Vector3 position,
        Quaternion rotation,
        float weight)
    {
        // IK 가중치 설정
        float finalWeight = weight * ikWeight;
        animator.SetIKPositionWeight(foot, finalWeight);
        animator.SetIKRotationWeight(foot, finalWeight);

        // IK 위치와 회전 적용
        if (finalWeight > 0)
        {
            animator.SetIKPosition(foot, position);
            animator.SetIKRotation(foot, rotation);
        }
    }

    /// <summary>
    /// IK 활성화/비활성화
    /// </summary>
    public void SetIKEnabled(bool enabled)
    {
        enableIK = enabled;
        Debug.Log($"[FootIK] IK {(enabled ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// IK 가중치 설정
    /// </summary>
    public void SetIKWeight(float weight)
    {
        ikWeight = Mathf.Clamp01(weight);
    }

    /// <summary>
    /// 골반 조정 활성화/비활성화
    /// </summary>
    public void SetPelvisAdjustment(bool adjust)
    {
        adjustPelvis = adjust;
    }
}
```

---

## 🔧 Unity 설정 가이드

### Step 1: Animator Controller 준비

#### 1-1. 빈 Animator Controller 생성
```
Project 우클릭 → Create → Animator Controller
이름: "FootIK_Controller"
```

#### 1-2. IK Pass 활성화 (중요!)
```
1. FootIK_Controller 더블클릭
2. Animator 창에서 Base Layer 선택
3. Inspector → "IK Pass" 체크박스 활성화 ✓
```

**IK Pass를 활성화하지 않으면 OnAnimatorIK가 호출되지 않음!**

#### 1-3. Idle State 추가 (선택)
```
1. Animator 창에서 우클릭 → Create State → Empty
2. 이름: "Idle"
3. Entry → Idle 연결
```

### Step 2: Ground Layer 설정

```
1. Edit → Project Settings → Tags and Layers
2. Layer 8: "Ground"
3. 지면 오브젝트 Layer 변경:
   - Terrain → Ground
   - Floor → Ground
   - Landing Zone → Ground
```

### Step 3: 컴포넌트 추가 및 설정

#### 3-1. Animator 설정
```
High_Jump_char_006 선택
→ Animator 컴포넌트 (이미 있음)
→ Controller: FootIK_Controller
→ Apply Root Motion: ✗ (비활성화!) ← 중요!
```

**Apply Root Motion을 끄는 이유:**
- 물리 기반 이동 (Rigidbody) 사용 중
- 애니메이션 이동 불필요
- 골반 뒤틀림 방지

#### 3-2. FootIKVelog 컴포넌트 추가
```
High_Jump_char_006 선택
→ Add Component → FootIKVelog

Inspector 설정:
=== IK 설정 ===
- Enable IK: ✓
- IK Weight: 1.0

=== 골반 조정 ===
- Adjust Pelvis: ✓
- Pelvis Offset: 0
- Pelvis Speed: 0.5

=== 레이캐스트 설정 ===
- Ground Layer: Ground
- Ray Start Height: 1.0
- Ray Distance: 1.5
- Foot Offset: 0.05

=== 디버그 ===
- Show Debug Rays: ✓ (테스트용)
```

---

## 🎮 PlayCharacter 통합

### PlayCharacter.cs 수정

```csharp
// PlayCharacter.cs

[Header("Foot IK")]
[SerializeField] private FootIKVelog footIK;

private void Start()
{
    // 기존 코드...
    dragCoefficient = CalculateDragCoefficient(rho: rho, Cd: Cd, area: A, mass: m);
    _stateManager = _stateManagerInspector ? _stateManagerInspector : FindAnyObjectByType<StateManager_New>();
    _camera = _cameraInspector ? _cameraInspector : FindAnyObjectByType<CameraController>();

    // ===== Animator 설정 =====
    if (animator)
    {
        // Apply Root Motion 비활성화 (물리 기반 이동 사용)
        animator.applyRootMotion = false;
        Debug.Log("[PlayCharacter] Apply Root Motion 비활성화");
    }

    // ===== FootIK 초기 비활성화 =====
    if (footIK)
    {
        footIK.SetIKEnabled(false);
        Debug.Log("[PlayCharacter] FootIK 초기 비활성화");
    }

    StateManager.OnInit += Init;
}

/// <summary>
/// 점프 시 FootIK 비활성화
/// </summary>
public void Jump()
{
    Debug.Log("<color=yellow>[PlayCharacter]</color>Jump 실행");

    // FootIK 비활성화 (공중에서는 필요 없음)
    if (footIK)
        footIK.SetIKEnabled(false);

    // 기존 점프 코드...
    transform.parent = sceneRoot;
    cloud.transform.parent = sceneRoot;
    paraCtrl.JumpStart();
    startFallPos = transform.position;
    startFallTime = Time.time;
    _stateManager.isJump = true;
}

/// <summary>
/// 착지 시 FootIK 활성화
/// </summary>
private void OnCollisionEnter(Collision collision)
{
    // Ground Layer 확인
    if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
    {
        Debug.Log("[PlayCharacter] 착지 - FootIK 활성화");

        if (footIK)
        {
            footIK.SetIKEnabled(true);
            footIK.SetIKWeight(1f);
        }

        // 기존 착지 처리...
    }
}

/// <summary>
/// 낙하산 전개 시 FootIK 비활성화
/// </summary>
public void DeployParachute()
{
    Debug.Log("[PlayCharacter] 낙하산 전개");

    if (footIK)
        footIK.SetIKEnabled(false);

    // 기존 낙하산 전개 코드...
}
```

---

## 🔍 골반 뒤틀림 문제 해결

### 문제의 원인

이전에 골반이 뒤틀렸던 이유:
```
❌ Apply Root Motion이 켜져 있음
❌ Animator Controller에 이동 애니메이션 있음
❌ Root Transform Position 설정 문제
```

### 해결 방법

```
1. Apply Root Motion: ✗ (비활성화)
   → PlayCharacter.cs에서 animator.applyRootMotion = false;

2. 빈 Animator Controller 사용
   → 이동 애니메이션 없음
   → IK Pass만 활성화

3. 골반 높이만 조정
   → MovePelvisHeight()는 Y축만 조정
   → 회전은 건드리지 않음
   → 뒤틀림 발생 안 함
```

### 골반 조정 vs 골반 뒤틀림

```
골반 높이 조정 (O):
- Y축 위치만 변경
- 발 위치에 따라 위아래로만 이동
- 자연스러움 ✅

골반 뒤틀림 (X):
- 회전(Rotation) 변경
- Root Motion이 잘못 적용
- 부자연스러움 ❌
```

---

## 📊 블로그 방법의 장점

### 1. 골반 자동 조정

```csharp
private void MovePelvisHeight()
{
    // 양발 중 낮은 쪽 기준으로 골반 낮춤
    float leftOffsetY = leftFootPosition.y - transform.position.y;
    float rightOffsetY = rightFootPosition.y - transform.position.y;
    float targetPelvisOffset = Mathf.Min(leftOffsetY, rightOffsetY);

    // 부드럽게 전환
    pelvisHeightOffset = Mathf.Lerp(
        pelvisHeightOffset,
        targetPelvisOffset + pelvisOffset,
        pelvisSpeed
    );
}
```

**효과:**
- 경사면에서 발이 지면에 닿을 때 골반이 자동으로 낮아짐
- 전체 스켈레톤이 자연스럽게 움직임
- 무릎이 자연스럽게 구부러짐

### 2. 부드러운 전환

```csharp
// 선형보간으로 부드럽게
pelvisHeightOffset = Mathf.Lerp(current, target, pelvisSpeed);
```

**효과:**
- 급격한 변화 없음
- 자연스러운 움직임

### 3. IK 가중치 시스템

```csharp
float finalWeight = weight * ikWeight;
animator.SetIKPositionWeight(foot, finalWeight);
```

**효과:**
- IK 강도 조절 가능
- 필요시 IK 비활성화 가능

---

## 🧪 테스트 및 파라미터 튜닝

### 테스트 절차

#### 1. 평지 테스트
```
1. Play 모드 진입
2. 캐릭터가 평지에 서 있는지 확인
3. 발이 바닥에 정확히 닿는지 확인
4. 골반이 자연스러운 높이인지 확인
```

#### 2. 경사면 테스트
```
1. 경사진 지형 추가
2. 캐릭터를 경사면에 배치
3. 발이 경사면에 맞춰지는지 확인
4. 골반이 자동으로 낮아지는지 확인
5. 무릎이 자연스럽게 구부러지는지 확인
```

#### 3. 계단 테스트
```
1. 계단 오브젝트 배치
2. 한쪽 발이 높은 계단, 다른 발이 낮은 계단
3. 골반이 중간 높이로 조정되는지 확인
```

### 파라미터 튜닝 가이드

#### Pelvis Offset
```
값 범위: -0.2 ~ 0.2
기본값: 0

조정:
- 캐릭터가 너무 낮으면: +0.1
- 캐릭터가 너무 높으면: -0.1
```

#### Pelvis Speed
```
값 범위: 0.1 ~ 1.0
기본값: 0.5

조정:
- 골반이 천천히 움직이면: 0.7로 증가
- 골반이 너무 빨리 움직이면: 0.3으로 감소
```

#### Foot Offset
```
값 범위: 0 ~ 0.2
기본값: 0.05

조정:
- 발이 지면을 뚫으면: 0.1로 증가
- 발이 떠있으면: 0.02로 감소
```

#### Ray Distance
```
값 범위: 0.5 ~ 3.0
기본값: 1.5

조정:
- 지면 감지 안 되면: 2.0으로 증가
- 너무 멀리 감지하면: 1.0으로 감소
```

---

## ⚡ 성능 최적화

### 최적화 1: 착지 시에만 활성화

```csharp
// PlayCharacter.cs

private bool isGrounded = false;

private void Update()
{
    // 지면과의 거리 체크
    float distToGround = GetDistanceToGround();

    // 착지 직전/직후에만 FootIK 활성화
    if (distToGround < 1.0f && !isGrounded)
    {
        isGrounded = true;
        footIK?.SetIKEnabled(true);
    }
    else if (distToGround > 2.0f && isGrounded)
    {
        isGrounded = false;
        footIK?.SetIKEnabled(false);
    }
}

private float GetDistanceToGround()
{
    if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 100f))
        return hit.distance;
    return float.MaxValue;
}
```

### 최적화 2: 업데이트 간격 조정

```csharp
// FootIKVelog.cs에 추가

[Header("=== 성능 최적화 ===")]
[SerializeField] private float updateInterval = 0.1f;

private float lastUpdateTime = 0f;

private void Update()
{
    if (!enableIK) return;

    // 간격 체크
    if (Time.time - lastUpdateTime < updateInterval)
        return;

    lastUpdateTime = Time.time;

    // IK 계산
    SolveFeetPositions();
}
```

---

## ✅ 최종 체크리스트

### 구현 전
- [ ] Animator Controller 생성 (빈 Controller)
- [ ] IK Pass 활성화 확인
- [ ] Ground Layer 설정

### 구현 중
- [ ] FootIKVelog.cs 작성
- [ ] Apply Root Motion 비활성화
- [ ] 컴포넌트 추가 및 설정

### 구현 후
- [ ] 평지 테스트
- [ ] 경사면 테스트
- [ ] 골반 높이 자연스러운지 확인
- [ ] 골반 뒤틀림 없는지 확인
- [ ] PlayCharacter 통합
- [ ] 성능 확인 (60fps 유지)

---

## 🎯 핵심 요약

### Velog 블로그 방법의 강점

1. ✅ **골반 자동 조정** - 가장 큰 장점!
2. ✅ **전체 스켈레톤 자연스러움**
3. ✅ **Unity Animator IK 활용** (안정적)
4. ✅ **부드러운 전환** (선형보간)

### AresParSimVR 적용 포인트

1. ✅ **Apply Root Motion 비활성화** - 골반 뒤틀림 방지
2. ✅ **빈 Animator Controller** - 이동 애니메이션 없음
3. ✅ **착지 시에만 활성화** - 성능 최적화
4. ✅ **파라미터 튜닝** - 프로젝트에 맞게 조정

### 골반 문제 해결

```
골반 뒤틀림 (이전 문제):
❌ Apply Root Motion ON
❌ 이동 애니메이션 적용
❌ Root Transform 설정 잘못

골반 높이 조정 (블로그 방법):
✅ Apply Root Motion OFF
✅ Y축만 조정 (회전 X)
✅ 자연스럽게 위아래로만 이동
```

---

**다음 단계:**
1. FootIKVelog.cs 코드 작성
2. Animator Controller 설정 (IK Pass 활성화!)
3. Apply Root Motion 비활성화
4. 테스트 및 파라미터 조정

코드를 생성해드릴까요? 😊
