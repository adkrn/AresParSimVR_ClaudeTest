using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// UI 애니메이션을 담당하는 별도 컴포넌트
/// BaseUI와 함께 사용하거나 독립적으로 사용 가능
/// </summary>
public class UIAnimator : MonoBehaviour
{
    // ========== 애니메이션 타입 ==========
    public enum AnimationType
    {
        None,
        ScaleBounce,    // 스케일 바운스 효과
        FadeInOut,      // 페이드 인/아웃
        SlideIn,        // 슬라이드 인
        RotateIn,       // 회전하며 등장
        Custom          // 커스텀 애니메이션
    }
    
    // ========== 애니메이션 설정 ==========
    [Header("애니메이션 설정")]
    [SerializeField] private AnimationType appearType = AnimationType.ScaleBounce;
    [SerializeField] private AnimationType disappearType = AnimationType.FadeInOut;
    [SerializeField] private float appearDuration = 0.4f;
    [SerializeField] private float disappearDuration = 0.3f;
    [SerializeField] private AnimationCurve customCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private bool useUnscaledTime = true;
    
    // ========== 컴포넌트 캐시 ==========
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Transform cachedTransform;
    
    // ========== 원본 값 저장 ==========
    private Vector3 originalScale;
    private Vector2 originalPosition;
    private Quaternion originalRotation;
    private float originalAlpha;
    
    // ========== 상태 관리 ==========
    private bool isAnimating = false;
    private float animationTimer = 0f;
    private AnimationType currentAnimation = AnimationType.None;
    private Action onCompleteCallback;
    
    // ========== 초기화 ==========
    private void Awake()
    {
        // 컴포넌트 캐싱
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        cachedTransform = transform;
        
        // 런타임에 추가된 경우 기본값 설정
        if (appearType == AnimationType.None && disappearType == AnimationType.None)
        {
            // 기본 애니메이션 타입 설정
            appearType = AnimationType.ScaleBounce;
            disappearType = AnimationType.FadeInOut;
            appearDuration = 0.4f;
            disappearDuration = 0.3f;
            useUnscaledTime = true;
            
            // 기본 커브가 없으면 생성
            if (customCurve == null || customCurve.keys.Length == 0)
            {
                customCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            }
        }
        
        // 원본 값 저장
        originalScale = cachedTransform.localScale;
        if (rectTransform != null)
        {
            originalPosition = rectTransform.anchoredPosition;
        }
        originalRotation = cachedTransform.localRotation;
        if (canvasGroup != null)
        {
            originalAlpha = canvasGroup.alpha;
        }
    }
    
    // ========== 공개 메서드 ==========
    
    /// <summary>
    /// 등장 애니메이션 실행
    /// </summary>
    public void PlayAppear(Action onComplete = null)
    {
        if (isAnimating) StopAnimation();
        
        onCompleteCallback = onComplete;
        currentAnimation = appearType;
        animationTimer = 0f;
        isAnimating = true;
        
        // 초기 상태 설정
        SetupInitialState(appearType);
        
        // 애니메이션 시작
        StartCoroutine(AnimateCoroutine(appearDuration, true));
    }
    
    /// <summary>
    /// 사라짐 애니메이션 실행
    /// </summary>
    public void PlayDisappear(Action onComplete = null)
    {
        if (isAnimating) StopAnimation();
        
        onCompleteCallback = onComplete;
        currentAnimation = disappearType;
        animationTimer = 0f;
        isAnimating = true;
        
        // 애니메이션 시작
        StartCoroutine(AnimateCoroutine(disappearDuration, false));
    }
    
    /// <summary>
    /// 애니메이션 즉시 중지
    /// </summary>
    public void StopAnimation()
    {
        if (!isAnimating) return;
        
        isAnimating = false;
        StopAllCoroutines();
        ResetToOriginal();
    }
    
    /// <summary>
    /// 커스텀 애니메이션 설정
    /// </summary>
    public void SetCustomAnimation(AnimationCurve curve, float duration)
    {
        customCurve = curve;
        appearDuration = duration;
    }
    
    // ========== 애니메이션 로직 ==========
    
    private IEnumerator AnimateCoroutine(float duration, bool isAppearing)
    {
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            // 애니메이션 타입에 따른 처리
            ApplyAnimation(currentAnimation, t, isAppearing);
            
            yield return null;
        }
        
        // 최종 상태 적용
        ApplyAnimation(currentAnimation, 1f, isAppearing);
        
        // 완료 처리
        OnAnimationComplete();
    }
    
    private void SetupInitialState(AnimationType type)
    {
        switch (type)
        {
            case AnimationType.ScaleBounce:
                cachedTransform.localScale = Vector3.zero;
                break;
                
            case AnimationType.FadeInOut:
                if (canvasGroup != null)
                    canvasGroup.alpha = 0f;
                break;
                
            case AnimationType.SlideIn:
                if (rectTransform != null)
                    rectTransform.anchoredPosition = originalPosition + Vector2.down * 100f;
                break;
                
            case AnimationType.RotateIn:
                cachedTransform.localRotation = Quaternion.Euler(0, 0, 90f);
                cachedTransform.localScale = Vector3.zero;
                break;
        }
    }
    
    private void ApplyAnimation(AnimationType type, float t, bool isAppearing)
    {
        float value = isAppearing ? t : 1f - t;
        
        switch (type)
        {
            case AnimationType.ScaleBounce:
                float eased = isAppearing ? UIUtils.EaseOutBack(value) : UIUtils.EaseInQuad(value);
                cachedTransform.localScale = Vector3.LerpUnclamped(Vector3.zero, originalScale, eased);
                break;
                
            case AnimationType.FadeInOut:
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(0f, originalAlpha, value);
                }
                break;
                
            case AnimationType.SlideIn:
                if (rectTransform != null)
                {
                    float slideEased = UIUtils.EaseOutQuad(value);
                    Vector2 startPos = originalPosition + Vector2.down * 100f;
                    rectTransform.anchoredPosition = Vector2.Lerp(startPos, originalPosition, slideEased);
                }
                break;
                
            case AnimationType.RotateIn:
                float rotateEased = UIUtils.EaseOutBack(value);
                cachedTransform.localRotation = Quaternion.Lerp(Quaternion.Euler(0, 0, 90f), originalRotation, rotateEased);
                cachedTransform.localScale = Vector3.Lerp(Vector3.zero, originalScale, rotateEased);
                break;
                
            case AnimationType.Custom:
                float customValue = customCurve.Evaluate(value);
                cachedTransform.localScale = Vector3.LerpUnclamped(Vector3.zero, originalScale, customValue);
                break;
        }
    }
    
    private void OnAnimationComplete()
    {
        isAnimating = false;
        currentAnimation = AnimationType.None;
        
        // 콜백 실행
        onCompleteCallback?.Invoke();
        onCompleteCallback = null;
    }
    
    private void ResetToOriginal()
    {
        cachedTransform.localScale = originalScale;
        cachedTransform.localRotation = originalRotation;
        
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = originalPosition;
        }
        
        if (canvasGroup != null)
        {
            canvasGroup.alpha = originalAlpha;
        }
    }
    
    // ========== 유틸리티 메서드 ==========
    
    /// <summary>
    /// 현재 애니메이션 진행 중인지 확인
    /// </summary>
    public bool IsAnimating => isAnimating;
    
    /// <summary>
    /// 애니메이션 설정 변경
    /// </summary>
    public void Configure(AnimationType appear, AnimationType disappear, float appearTime, float disappearTime)
    {
        appearType = appear;
        disappearType = disappear;
        appearDuration = appearTime;
        disappearDuration = disappearTime;
    }
    
    /// <summary>
    /// 시간 모드 설정 (TimeScale 영향 여부)
    /// </summary>
    public void SetTimeMode(bool unscaledTime)
    {
        useUnscaledTime = unscaledTime;
    }
}