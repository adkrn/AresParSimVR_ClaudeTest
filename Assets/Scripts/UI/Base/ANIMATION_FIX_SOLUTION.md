# 🎯 UI 애니메이션 시스템 수정 완료

## 📋 문제 진단

### 근본 원인
UIAnimator가 런타임에 `AddComponent`로 추가될 때 SerializeField 변수들이 초기화되지 않아 발생한 문제

### 주요 이슈
1. **useUnscaledTime 기본값 문제**: Time.timeScale 영향을 받아 애니메이션이 의도와 다르게 동작
2. **AnimationCurve 초기화 누락**: customCurve가 null로 남아있는 문제
3. **애니메이션 타입 기본값**: None으로 설정되어 애니메이션이 실행되지 않는 문제

## ✅ 해결 방법

### 1. UIAnimator 자체 개선
```csharp
// Awake()에서 런타임 추가 감지 및 기본값 설정
if (appearType == AnimationType.None && disappearType == AnimationType.None)
{
    appearType = AnimationType.ScaleBounce;
    disappearType = AnimationType.FadeInOut;
    appearDuration = 0.4f;
    disappearDuration = 0.3f;
    useUnscaledTime = true;
    customCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
}
```

### 2. SetTimeMode 메서드 추가
```csharp
public void SetTimeMode(bool unscaledTime)
{
    useUnscaledTime = unscaledTime;
}
```

### 3. BaseUI.SetupAnimator 개선
```csharp
protected void SetupAnimator(
    UIAnimator.AnimationType appear, 
    UIAnimator.AnimationType disappear, 
    float appearDuration = 0.4f, 
    float disappearDuration = 0.3f, 
    bool useUnscaledTime = true)  // 새 파라미터 추가
{
    if (uiAnimator == null)
        uiAnimator = gameObject.AddComponent<UIAnimator>();
    
    uiAnimator.Configure(appear, disappear, appearDuration, disappearDuration);
    uiAnimator.SetTimeMode(useUnscaledTime);  // 시간 모드 설정
}
```

## 🔍 검증 방법

### TimeLimitUI 테스트
1. 시간제한 절차 실행
2. 시간 초과 대기
3. **예상 동작**: 
   - ✅ 좌우 흔들기 애니메이션 정상 실행
   - ✅ 흔들면서 동시에 페이드 아웃
   - ✅ 애니메이션 완료 후 UI 비활성화

### InstructionUI 테스트
1. 일반 instruction 절차 실행
2. **예상 동작**:
   - ✅ ScaleBounce로 등장
   - ✅ 2초 대기 후 FadeOut으로 사라짐
   - ✅ Time.timeScale 변경에도 영향받지 않음

## 🎨 장점

1. **별도 스크립트 불필요**: 기존 코드만 수정하여 해결
2. **자동 감지**: 런타임 추가 여부를 자동으로 감지하여 처리
3. **하위 호환성**: 기존 프리팹에 미리 설정된 UIAnimator도 정상 동작
4. **유연성**: useUnscaledTime을 필요에 따라 설정 가능

## 📝 권장사항

### Unity Editor에서
1. 자주 사용하는 UI 프리팹에는 UIAnimator를 미리 추가하고 설정
2. 런타임에 생성되는 UI는 SetupAnimator로 자동 설정

### 코드에서
- 시간이 중요한 UI (타이머, 카운트다운): `useUnscaledTime = true`
- 일반 UI 애니메이션: 기본값 사용

---
작성일: 2025-08-27
문제 해결 완료