using System;
using UnityEngine;

public class FadeController : MonoBehaviour
{
    [Header("페이드 설정")]
    [Tooltip("페이드에 걸리는 시간(초)")]
    [SerializeField] private float fadeDuration = 1.0f;
    
    [SerializeField] private CanvasGroup cGroup;
    
    private float _startAlpha;
    private float _targetAlpha;
    private bool  _isFading;
    
    private Action _afterAction;
    private float _fadeTime = 0.0f;

    private void Awake()
    {
        if (cGroup == null) cGroup = GetComponent<CanvasGroup>();
    }
    /// <summary>
    /// Fade In/Out 호출 기능 시작
    /// </summary>
    /// <param name="fDir">in 또는 out</param>
    /// <param name="onComplete">완료 후 실행해야될 메서드</param>
    public void Init(FadeDir fDir, Action onComplete)
    {
        Init(fDir, onComplete, fadeDuration, 1f);
    }
    
    /// <summary>
    /// Fade In/Out 호출 기능 시작 (매개변수 조절 가능)
    /// </summary>
    /// <param name="fDir">in 또는 out</param>
    /// <param name="onComplete">완료 후 실행해야될 메서드</param>
    /// <param name="customDuration">페이드 지속시간</param>
    /// <param name="maxAlpha">최대 투명도 (0~1)</param>
    public void Init(FadeDir fDir, Action onComplete, float customDuration, float maxAlpha)
    {
        if (_isFading)
        {
            Debug.LogWarning("[FadeController] 이전 페이드가 아직 끝나지 않았습니다.");
            return;
        }

        maxAlpha = Mathf.Clamp01(maxAlpha);
        
        _startAlpha  = (fDir == FadeDir.In) ? maxAlpha : 0f;
        _targetAlpha = (fDir == FadeDir.In) ? 0f : maxAlpha;
        _fadeTime    = 0f;
        _afterAction = onComplete;
        _isFading    = true;
        
        // 커스텀 지속시간 적용
        fadeDuration = customDuration;

        // 시작 시점 알파 바로 세팅
        cGroup.alpha = _startAlpha;
    }

    private void Update()
    {
        if (!_isFading) return;

        _fadeTime += Time.deltaTime / fadeDuration;
        cGroup.alpha = Mathf.Lerp(_startAlpha, _targetAlpha, _fadeTime);

        if (_fadeTime >= 1f)
        {
            _isFading = false;
            _afterAction?.Invoke();
            _afterAction = null;
        }
    }
}
