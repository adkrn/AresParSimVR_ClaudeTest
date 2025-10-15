using UnityEngine;
using System;
using TMPro;

/// <summary>
/// 해당 절차 수행 내용을 설명하는 UI
/// 단순하게 정보만 표시할때 쓰인다.
/// </summary>
public class InstructionUI : MonoBehaviour
{
    [Header("Durations")]
    [SerializeField] private float scaleDuration = 0.4f;
    [SerializeField] private float holdDuration  = 2f;
    [SerializeField] private float fadeDuration  = 0.3f;

    [Header("UI")]
    [SerializeField] private TMP_Text desc;

    private CanvasGroup cg;
    private Vector3 targetScale = Vector3.one;

    // 델리게이트: 현재 프레임에 실행할 애니메이션 단계
    public Action updateAction;
    public Action OnFadeComplete;

    // 공용 타이머
    private float timer;

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// UI 초기 설정
    /// </summary>
    /// <param name="data"></param>
    public void Init(Instruction data)
    {
        if (data.mediaType == MediaType.Prefab)
        {
            
        }
        desc.text = data.mediaContent;
        
        transform.localScale = Vector3.zero;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        timer = 0f;
        updateAction = ScaleUpdate;
    }

    void Update()
    {
        updateAction?.Invoke();
    }
    
    /// <summary>
    /// 크기 키우는 애니메이션
    /// </summary>
    void ScaleUpdate()
    {
        timer += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(timer / scaleDuration);

        // EaseOutBack 적용
        float eased = UIUtils.EaseOutBack(t);
        transform.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, eased);

        if (t >= 1f)
        {
            timer = 0f;
            updateAction = HoldUpdate;
        }
    }

    /// <summary>
    /// 교육생에게 절차 내용을 표시하는 시간
    /// </summary>
    void HoldUpdate()
    {
        timer += Time.unscaledDeltaTime;
        if (timer >= holdDuration)
        {
            timer        = 0f;
            updateAction = FadeUpdate;
        }
    }

    /// <summary>
    /// 설명이 끝나고 UI를 투명하게 비활성화
    /// </summary>
    void FadeUpdate()
    {
        timer += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(timer / fadeDuration);

        // EaseInQuad 적용
        float eased = UIUtils.EaseInQuad(t);
        cg.alpha = 1f - eased;

        if (cg.interactable)
        {
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }

        if (t >= 1f)
        {
            updateAction = null;
            OnFadeComplete?.Invoke();
            OnFadeComplete = null;
            
            // 카메라 앞에 배치할때 바꾼 부모를 원래대로
            transform.SetParent(UIManager.Inst.transform);
            gameObject.SetActive(false);
        }
    }
}
