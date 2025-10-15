using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 모든 UI의 공통 기능을 제공하는 베이스 클래스
/// 애니메이션은 UIAnimator 컴포넌트를 통해 처리
/// </summary>
public abstract class BaseUI : MonoBehaviour
{
    // ========== 공통 컴포넌트 ==========
    protected CanvasGroup canvasGroup;
    protected RectTransform rectTransform;
    protected UIAnimator uiAnimator;
    
    // ========== 상태 관리 ==========
    protected bool isVisible = false;
    
    // ========== 이벤트 ==========
    public event Action OnShowComplete;
    public event Action OnHideComplete;
    
    // ========== Unity 생명주기 ==========
    protected virtual void Awake()
    {
        // 컴포넌트 캐싱
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        rectTransform = GetComponent<RectTransform>();
        
        // UIAnimator 컴포넌트 확인 (선택적)
        uiAnimator = GetComponent<UIAnimator>();
        
        // 파생 클래스 초기화
        OnAwake();
    }
    
    protected virtual void Start()
    {
        OnStart();
    }
    
    protected virtual void OnDestroy()
    {
        OnCleanup();
    }
    
    // ========== 가상 메서드 (파생 클래스에서 선택적으로 구현) ==========
    protected virtual void OnAwake() { }
    protected virtual void OnStart() { }
    protected virtual void OnCleanup() { }
    
    // ========== 공통 기능 메서드 ==========
    
    /// <summary>
    /// UI 표시 (UIAnimator가 있으면 애니메이션, 없으면 즉시 표시)
    /// </summary>
    public virtual void Show(bool animated = true)
    {
        gameObject.SetActive(true);
        isVisible = true;
        
        if (animated && uiAnimator != null)
        {
            // UIAnimator를 통한 애니메이션
            uiAnimator.PlayAppear(() => {
                OnShowComplete?.Invoke();
                OnAfterShow();
            });
        }
        else
        {
            // 애니메이션 없이 즉시 표시
            SetVisibility(true);
            OnShowComplete?.Invoke();
            OnAfterShow();
        }
    }
    
    /// <summary>
    /// UI 숨기기 (UIAnimator가 있으면 애니메이션, 없으면 즉시 숨김)
    /// </summary>
    public virtual void Hide(bool animated = true)
    {
        if (animated && uiAnimator != null && isVisible)
        {
            // UIAnimator를 통한 애니메이션
            uiAnimator.PlayDisappear(() => {
                CompleteHide();
            });
        }
        else
        {
            // 애니메이션 없이 즉시 숨김
            CompleteHide();
        }
    }
    
    /// <summary>
    /// 숨기기 완료 처리
    /// </summary>
    private void CompleteHide()
    {
        SetVisibility(false);
        isVisible = false;
        
        // UIManager 부모 복원
        RestoreParent();
        
        OnHideComplete?.Invoke();
        OnAfterHide();
        gameObject.SetActive(false);
    }
    
    /// <summary>
    /// 즉시 가시성 설정
    /// </summary>
    protected virtual void SetVisibility(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
    }
    
    /// <summary>
    /// UI 리셋
    /// </summary>
    public virtual void Reset()
    {
        // 애니메이션 중지
        if (uiAnimator != null && uiAnimator.IsAnimating)
        {
            uiAnimator.StopAnimation();
        }
        
        // 상태 초기화
        SetVisibility(false);
        isVisible = false;
    }
    
    // ========== 파생 클래스 확장 포인트 ==========
    protected virtual void OnAfterShow() { }
    protected virtual void OnAfterHide() { }
    
    // ========== 유틸리티 메서드 ==========
    
    /// <summary>
    /// UIManager 부모 복원
    /// </summary>
    protected void RestoreParent()
    {
        if (UIManager.Inst != null && transform.parent != UIManager.Inst.transform)
        {
            transform.SetParent(UIManager.Inst.transform);
        }
    }
    
    /// <summary>
    /// 스프라이트 로드 헬퍼
    /// </summary>
    protected Sprite LoadSprite(string spriteName, FilePath path = FilePath.UiData)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;
        return FileUtils.GetSprite(spriteName, path);
    }
    
    /// <summary>
    /// UIAnimator 설정 (런타임에 애니메이션 추가/변경)
    /// </summary>
    protected void SetupAnimator(UIAnimator.AnimationType appear, UIAnimator.AnimationType disappear, 
        float appearDuration = 0.4f, float disappearDuration = 0.3f, bool useUnscaledTime = true)
    {
        if (uiAnimator == null)
        {
            uiAnimator = gameObject.AddComponent<UIAnimator>();
        }
        
        uiAnimator.Configure(appear, disappear, appearDuration, disappearDuration);
        uiAnimator.SetTimeMode(useUnscaledTime);
    }
}