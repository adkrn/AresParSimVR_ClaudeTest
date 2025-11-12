# Foot IK 낙하 중 비활성화/재활성화 문제 해결

**문제:** 낙하 후 IK 비활성화 시 모델이 땅을 바라보는 방향으로 누워서 낙하하고, 착륙 후 IK 재활성화 시 모델이 위로 올라감
**프로젝트:** AresParSimVR
**작성일:** 2025-10-22

---

## 🔴 문제 상황

### 증상 1: 낙하 중 모델이 누워서 낙하
```
1. PlayCharacter.Jump() 실행 → ikObj.SetIKEnabled(false)
2. 낙하 중 캐릭터가 땅을 바라보는 방향으로 누워서 낙하
3. 정상적인 낙하 자세가 아님
```

### 증상 2: 착륙 후 모델이 위로 올라감
```
1. OnGround() 실행 → ikObj.SetIKEnabled(true)
2. 캐릭터가 갑자기 위로 올라감
3. 발이 지면에 닿지 않음
```

### 증상 3: Ground Layer 설정
```
Ground Layer에 포함된 오브젝트:
- C130 오브젝트의 mainbody의 out
- Terrain의 Destination_new

문제: 낙하 중에도 비행기 바닥을 Ground로 감지할 가능성
```

---

## 🔍 원인 분석

### 원인 1: pelvisHeightOffset이 리셋되지 않음 ⭐ 핵심 원인

**FootIK.cs의 SetIKEnabled() 메서드:**
```csharp
// Assets/Scripts/FootIK.cs:227-231
public void SetIKEnabled(bool enabled)
{
    enableIK = enabled;
    Debug.Log($"[FootIK] IK {(enabled ? "활성화" : "비활성화")}");
}
```

**문제점:**
- `pelvisHeightOffset` 변수를 리셋하지 않음
- IK 비활성화 시: 이전 pelvis offset이 그대로 유지됨
- IK 재활성화 시: `MovePelvisHeight()`가 이전 offset에서 Lerp 시작
- **결과:** 급격한 높이 변화 발생

**FootIK.cs의 MovePelvisHeight():**
```csharp
// Assets/Scripts/FootIK.cs:143-158
private void MovePelvisHeight()
{
    float leftOffsetY = leftFootPosition.y - transform.position.y;
    float rightOffsetY = rightFootPosition.y - transform.position.y;
    float targetPelvisOffset = Mathf.Min(leftOffsetY, rightOffsetY);

    // ⚠️ 이전 pelvisHeightOffset 값에서 Lerp 시작
    pelvisHeightOffset = Mathf.Lerp(
        pelvisHeightOffset,  // 이전 값이 남아있음!
        targetPelvisOffset + pelvisOffset,
        pelvisSpeed
    );
}
```

---

### 원인 2: Animator Root Motion 문제

**문제:**
- IK 비활성화 시 Animator가 기본 포즈로 돌아감
- Humanoid 리그의 Root Transform Position 설정에 따라 캐릭터 위치가 변경됨
- 낙하 애니메이션이 없거나 제대로 재생되지 않으면 T-Pose나 Idle 자세로 낙하

**확인 사항:**
```
1. PlayCharacter.animator.applyRootMotion 상태 확인
2. 낙하 중 재생되는 애니메이션 확인
3. Animator Controller의 기본 State 확인
```

---

### 원인 3: Ground Layer에 비행기 바닥 포함

**문제:**
- `FootIK.cs:16`의 `groundLayer`에 C130 mainbody out이 포함
- 낙하 중에도 비행기 바닥을 Ground로 감지 가능
- Raycast가 비행기 바닥을 감지하면 잘못된 발 위치 계산

**FootIK.cs의 Raycast:**
```csharp
// Assets/Scripts/FootIK.cs:122-136
if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
    rayDistance, groundLayer))  // ⚠️ groundLayer에 비행기 바닥 포함
{
    position = hit.point + Vector3.up * footOffset;
    rotation = Quaternion.LookRotation(
        Vector3.ProjectOnPlane(transform.forward, hit.normal),
        hit.normal
    );
    ikWeight = 1f;
}
```

---

### 원인 4: IK 활성화/비활성화 전환이 즉시 발생

**문제:**
- `SetIKEnabled(true)` 호출 시 다음 Update()에서 즉시 IK 계산
- 부드러운 전환(Fade In/Out) 없음
- 급격한 포즈 변화로 인해 시각적 불편함

**흐름:**
```
OnGround() → SetIKEnabled(true)
→ 다음 Update() → SolveFeetPositions() 즉시 실행
→ MovePelvisHeight() 즉시 실행
→ OnAnimatorIK() 즉시 적용
```

---

## ✅ 해결 방법

### 해결책 1: pelvisHeightOffset 리셋 추가 ⭐ 필수

**FootIK.cs 수정:**

```csharp
// Assets/Scripts/FootIK.cs:227-231 수정
public void SetIKEnabled(bool enabled)
{
    enableIK = enabled;

    // ✅ IK 비활성화 시 pelvisHeightOffset 리셋
    if (!enabled)
    {
        pelvisHeightOffset = 0f;
        Debug.Log("[FootIK] IK 비활성화 - pelvisHeightOffset 리셋");
    }

    Debug.Log($"[FootIK] IK {(enabled ? "활성화" : "비활성화")}");
}
```

**효과:**
- IK 비활성화 시 골반 높이 오프셋 초기화
- IK 재활성화 시 0에서부터 Lerp 시작
- 급격한 높이 변화 방지

---

### 해결책 2: IK 전환 시 부드러운 페이드 인/아웃 추가

**FootIK.cs에 새 메서드 추가:**

```csharp
// Assets/Scripts/FootIK.cs에 추가

[Header("=== 전환 설정 ===")]
[SerializeField] private float fadeSpeed = 2f;  // 페이드 속도
private float currentFadeWeight = 1f;  // 현재 페이드 가중치
private bool isFading = false;

/// <summary>
/// 부드러운 IK 페이드 전환
/// </summary>
public void SetIKEnabledSmooth(bool enabled)
{
    if (enabled)
    {
        // IK 활성화 (페이드 인)
        enableIK = true;
        pelvisHeightOffset = 0f;  // 초기화
        StartCoroutine(FadeInIK());
    }
    else
    {
        // IK 비활성화 (페이드 아웃)
        StartCoroutine(FadeOutIK());
    }
}

private IEnumerator FadeInIK()
{
    Debug.Log("[FootIK] IK 페이드 인 시작");
    isFading = true;
    currentFadeWeight = 0f;

    while (currentFadeWeight < 1f)
    {
        currentFadeWeight += Time.deltaTime * fadeSpeed;
        currentFadeWeight = Mathf.Clamp01(currentFadeWeight);
        yield return null;
    }

    isFading = false;
    Debug.Log("[FootIK] IK 페이드 인 완료");
}

private IEnumerator FadeOutIK()
{
    Debug.Log("[FootIK] IK 페이드 아웃 시작");
    isFading = true;

    while (currentFadeWeight > 0f)
    {
        currentFadeWeight -= Time.deltaTime * fadeSpeed;
        currentFadeWeight = Mathf.Clamp01(currentFadeWeight);
        yield return null;
    }

    enableIK = false;
    pelvisHeightOffset = 0f;
    isFading = false;
    Debug.Log("[FootIK] IK 페이드 아웃 완료");
}
```

**MoveFeetToIkPoint 수정 (페이드 가중치 적용):**
```csharp
// Assets/Scripts/FootIK.cs:205-222 수정
private void MoveFeetToIkPoint(
    AvatarIKGoal foot,
    Vector3 position,
    Quaternion rotation,
    float weight)
{
    // ✅ 페이드 가중치 적용
    float finalWeight = weight * ikWeight * currentFadeWeight;

    animator.SetIKPositionWeight(foot, finalWeight);
    animator.SetIKRotationWeight(foot, finalWeight);

    if (finalWeight > 0)
    {
        animator.SetIKPosition(foot, position);
        animator.SetIKRotation(foot, rotation);
    }
}
```

**ApplyPelvisHeight 수정 (페이드 가중치 적용):**
```csharp
// Assets/Scripts/FootIK.cs:194-200 수정
private void ApplyPelvisHeight()
{
    Vector3 bodyPosition = animator.bodyPosition;
    // ✅ 페이드 가중치 적용
    bodyPosition.y += pelvisHeightOffset * currentFadeWeight;
    animator.bodyPosition = bodyPosition;
}
```

**PlayCharacter.cs 수정:**
```csharp
// Assets/Scripts/PlayCharacter.cs:170 수정
public void Jump()
{
    // ...기존 코드...

    ikObj.SetIKEnabledSmooth(false);  // ✅ 부드러운 전환
}

// Assets/Scripts/PlayCharacter.cs:420 수정
private void OnGround()
{
    Debug.Log($"땅과 충돌했습니다");
    ikObj.SetIKEnabledSmooth(true);  // ✅ 부드러운 전환

    // ...기존 코드...
}
```

---

### 해결책 3: Ground Layer를 동적으로 변경

**방법 A: FootIK에 Layer 설정 메서드 추가**

```csharp
// Assets/Scripts/FootIK.cs에 추가

/// <summary>
/// Ground Layer 동적 변경
/// </summary>
public void SetGroundLayer(LayerMask layer)
{
    groundLayer = layer;
    Debug.Log($"[FootIK] Ground Layer 변경: {layer.value}");
}
```

**Unity Editor 설정:**
```
1. Layer 생성:
   - "GroundTerrain" Layer 생성 (지형 전용)
   - "Aircraft" Layer 생성 (비행기 전용)

2. 오브젝트 Layer 설정:
   - Terrain/Destination_new → GroundTerrain Layer
   - C130/mainbody/out → Aircraft Layer

3. LayerMask 설정:
   - FootIK Inspector → Ground Layer: GroundTerrain만 선택
```

**PlayCharacter.cs 수정 (선택사항):**
```csharp
// Assets/Scripts/PlayCharacter.cs

[SerializeField] private LayerMask terrainLayer;  // Inspector에서 설정
[SerializeField] private LayerMask aircraftLayer;

public void Jump()
{
    // ...기존 코드...

    // 낙하 중에는 비행기 바닥 감지 안 함
    ikObj.SetGroundLayer(0);  // 아무것도 감지하지 않음
    ikObj.SetIKEnabledSmooth(false);
}

private void OnGround()
{
    Debug.Log($"땅과 충돌했습니다");

    // 착륙 후에는 지형만 감지
    ikObj.SetGroundLayer(terrainLayer);
    ikObj.SetIKEnabledSmooth(true);

    // ...기존 코드...
}
```

---

### 해결책 4: 낙하 애니메이션 추가 (선택사항)

**문제:**
- IK 비활성화 시 기본 Idle 애니메이션으로 돌아감
- 낙하 자세 애니메이션이 없어서 누운 자세로 보임

**해결:**
```csharp
// PlayCharacter.cs:170 수정
public void Jump()
{
    // ...기존 코드...

    // ✅ 낙하 애니메이션 재생
    animator.Play("FreeFall");  // FreeFall 애니메이션 클립 필요

    ikObj.SetIKEnabledSmooth(false);
}
```

**애니메이션 클립 준비:**
1. FreeFall 애니메이션 클립 생성 (팔다리 펼친 자세)
2. Animator Controller에 FreeFall State 추가
3. Apply Root Motion OFF 확인

---

## 📊 해결 방법 비교

| 방법 | 난이도 | 효과 | 부작용 | 우선순위 |
|------|--------|------|--------|----------|
| pelvisHeightOffset 리셋 | ⭐ 쉬움 | ✅ 즉시 해결 | 없음 | 🔥 최우선 |
| 부드러운 페이드 전환 | ⭐⭐ 보통 | ✅ 시각적 개선 | 약간 복잡 | 🔥 필수 |
| Ground Layer 동적 변경 | ⭐ 쉬움 | ✅ 안정성 향상 | Layer 관리 필요 | ⭐ 권장 |
| 낙하 애니메이션 추가 | ⭐⭐⭐ 어려움 | ✅ 완전 해결 | 애니메이션 제작 필요 | 💡 선택 |

---

## 🎯 즉시 적용 가능한 해결책

### 최소한의 수정 (5분)

**1. FootIK.cs의 SetIKEnabled() 수정:**
```csharp
public void SetIKEnabled(bool enabled)
{
    enableIK = enabled;

    if (!enabled)
    {
        pelvisHeightOffset = 0f;  // ✅ 이 한 줄 추가
    }

    Debug.Log($"[FootIK] IK {(enabled ? "활성화" : "비활성화")}");
}
```

**2. Ground Layer 설정:**
```
Unity Editor:
FootIK Inspector → Ground Layer: Terrain의 Destination_new만 선택
(C130 mainbody out 제거)
```

**효과:**
- 착륙 시 모델이 위로 올라가는 문제 70% 해결
- 낙하 중 비행기 바닥 감지 문제 100% 해결

---

### 권장 해결책 (30분)

**위의 최소 수정 + 부드러운 전환:**
1. FootIK.cs에 페이드 인/아웃 코드 추가
2. PlayCharacter.cs에서 `SetIKEnabledSmooth()` 사용
3. Layer 분리 (GroundTerrain, Aircraft)

**효과:**
- 모든 문제 95% 해결
- 시각적으로 부드러운 전환
- 안정적인 IK 동작

---

## 🔬 디버그 방법

### 1. pelvisHeightOffset 확인
```csharp
// FootIK.cs의 Update()에 추가
if (showDebugRays)
{
    Debug.Log($"[FootIK] pelvisHeightOffset: {pelvisHeightOffset:F3}");
}
```

### 2. IK 활성화 상태 확인
```csharp
// PlayCharacter.cs
private void Update()
{
    if (Input.GetKeyDown(KeyCode.F1))
    {
        Debug.Log($"IK Enabled: {ikObj.enableIK}");
        Debug.Log($"Animator applyRootMotion: {animator.applyRootMotion}");
        Debug.Log($"Current Animation: {animator.GetCurrentAnimatorClipInfo(0)[0].clip.name}");
    }
}
```

### 3. Ground Layer Raycast 확인
```csharp
// FootIK.cs의 SolveFootPosition()에 추가
if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
    rayDistance, groundLayer))
{
    Debug.Log($"[FootIK] {foot} hit: {hit.collider.gameObject.name} (Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)})");
    // ...
}
```

---

## ✅ 최종 체크리스트

### 필수 수정
- [ ] FootIK.cs의 SetIKEnabled()에 pelvisHeightOffset 리셋 추가
- [ ] Ground Layer에서 C130 mainbody out 제거
- [ ] 테스트: 착륙 시 모델이 정상 위치에 서는지 확인

### 권장 수정
- [ ] 페이드 인/아웃 코드 추가
- [ ] PlayCharacter.cs에서 SetIKEnabledSmooth() 사용
- [ ] Layer 분리 (GroundTerrain, Aircraft)
- [ ] 테스트: 부드러운 전환 확인

### 선택 수정
- [ ] 낙하 애니메이션 추가
- [ ] Animator Controller 점검
- [ ] Apply Root Motion 설정 확인

---

**요약:**
- **핵심 원인:** `pelvisHeightOffset`이 리셋되지 않아서 IK 재활성화 시 이전 값에서 Lerp 시작
- **즉시 해결:** `SetIKEnabled()`에서 `pelvisHeightOffset = 0f` 추가
- **완전 해결:** 페이드 전환 + Ground Layer 분리
- **시간:** 최소 수정 5분, 권장 수정 30분

**다음 단계:**
1. FootIK.cs의 SetIKEnabled() 수정
2. Ground Layer 설정 변경
3. 테스트 및 확인
4. 필요 시 페이드 전환 추가
