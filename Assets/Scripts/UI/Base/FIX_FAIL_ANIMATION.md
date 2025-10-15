# 🔧 TimeLimitUI Fail 애니메이션 문제 해결

## 🐛 문제 상황
시간 초과로 실패 시 흔들기 애니메이션이 실행되지 않음

## 📍 원인 분석

### 1. 애니메이션 충돌
- RunFailShake()에서 흔들기 완료 후 Hide() 호출
- Hide()가 UIAnimator의 페이드 아웃 애니메이션 시작
- 두 애니메이션이 순차적으로 실행되어야 하는데 충돌 발생

### 2. 코드 흐름 문제
```csharp
// 기존 코드 (문제)
void RunFailShake() {
    if (t < 1f) {
        // 흔들기만 진행
    } else {
        Hide(); // UIAnimator가 다시 페이드 시작
    }
}
```

## ✅ 해결 방법

### 흔들기와 페이드 아웃 동시 진행
```csharp
void RunFailShake() {
    if (t < 1f) {
        // 흔들기 효과
        float offsetX = Mathf.Sin(t * Mathf.PI * 10f) * strength;
        rectTransform.anchoredPosition = originPos + Vector2.right * offsetX;
        
        // 동시에 페이드 아웃
        canvasGroup.alpha = 1f - UIUtils.EaseInQuad(t);
    } else {
        // 직접 완료 처리 (Hide() 호출 안함)
        CompleteFailAnimation();
    }
}
```

## 🎯 개선 효과

1. **자연스러운 애니메이션**: 흔들면서 점점 사라지는 효과
2. **충돌 방지**: UIAnimator와 독립적으로 동작
3. **성능 향상**: 중복 애니메이션 호출 제거

## 📝 테스트 방법

1. TimeLimitUI가 있는 절차 실행
2. 시간 초과까지 대기
3. 실패 시 다음 확인:
   - ✅ 좌우 흔들기 애니메이션 표시
   - ✅ 흔들면서 점점 투명해짐
   - ✅ 애니메이션 완료 후 UI 비활성화

## 🔍 추가 개선 가능 사항

1. **흔들기 강도 조정**: shakeAmplitude 값 조정 (현재 20f)
2. **페이드 속도 조정**: shakeDuration 값 조정 (현재 0.3f)
3. **Easing 함수 변경**: EaseInQuad 대신 다른 함수 사용 가능

---
작성일: 2025-08-27
문제 해결 완료