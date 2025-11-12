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
    [SerializeField] private GameObject prefab;
    [SerializeField] private GameObject textInst;
    [SerializeField] private TMP_Text desc;
    [SerializeField] private Transform prefabParent; // 프리팹이 생성될 부모 Transform

    private CanvasGroup cg;
    private Vector3 targetScale = Vector3.one;

    // 델리게이트: 현재 프레임에 실행할 애니메이션 단계
    public Action updateAction;
    public Action OnFadeComplete;

    // 공용 타이머
    private float timer;
    private MediaType _mediaType;

    // 생성된 프리팹 인스턴스 참조
    private GameObject instantiatedPrefab;

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
    }

    void OnDestroy()
    {
        // 안전하게 생성된 프리팹 정리
        if (instantiatedPrefab != null)
        {
            Destroy(instantiatedPrefab);
            instantiatedPrefab = null;
        }
    }

    /// <summary>
    /// UI 초기 설정
    /// </summary>
    /// <param name="data"></param>
    public void Init(Instruction data)
    {
        _mediaType = data.mediaType;
        if (_mediaType == MediaType.Prefab)
        {
            // 이전에 생성된 프리팹이 있다면 제거
            if (instantiatedPrefab != null)
            {
                Destroy(instantiatedPrefab);
                instantiatedPrefab = null;
            }

            // Resources 폴더에서 프리팹 로드 (예: Resources/Instructions/프리팹이름)
            GameObject prefabToLoad = Resources.Load<GameObject>($"Instructions/{data.mediaContent}");

            if (prefabToLoad != null)
            {
                // 프리팹 인스턴스 생성
                Transform parent = prefabParent != null ? prefabParent : transform;
                instantiatedPrefab = Instantiate(prefabToLoad, parent);

                // 로컬 위치 초기화
                instantiatedPrefab.transform.localPosition = Vector3.zero;
                instantiatedPrefab.transform.localRotation = Quaternion.identity;
                instantiatedPrefab.transform.localScale = Vector3.one;

                Debug.Log($"[InstructionUI] 프리팹 생성 완료: {data.mediaContent}");
            }
            else
            {
                Debug.LogWarning($"[InstructionUI] 프리팹을 찾을 수 없습니다: Resources/Instructions/{data.mediaContent}");
            }
            
            textInst.SetActive(false);
        }
        else
        {
            // 텍스트 타입인 경우
            desc.text = data.mediaContent;
            
            textInst.SetActive(true);
            if (instantiatedPrefab != null)
            {
                instantiatedPrefab.SetActive(false);
            }
        }

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
        if(_mediaType == MediaType.Prefab) return;
        
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

            // 생성된 프리팹 정리
            if (instantiatedPrefab != null)
            {
                Destroy(instantiatedPrefab);
                instantiatedPrefab = null;
            }

            // 카메라 앞에 배치할때 바꾼 부모를 원래대로
            transform.SetParent(UIManager.Inst.transform);
            gameObject.SetActive(false);
        }
    }
}
