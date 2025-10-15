using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 지정된 시간(기본 30초) 동안 카운트다운 텍스트를 표시하고,
/// 완료 후 CanvasGroup 페이드‑아웃으로 UI를 사라지게 한다.
/// </summary>
public class ScenarioInfoUI : MonoBehaviour
{
    [Header("CanvasGroup / 페이드 설정")]

    [Tooltip("투명도를 조절할 CanvasGroup. 지정하지 않으면 같은 오브젝트에서 자동으로 찾습니다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("CanvasGroup 페이드‑아웃이 진행될 총 시간(초).")]
    [SerializeField] private float fadeDuration = 1f;

    [Header("시나리오 정보 표시 UI")] 
    [SerializeField] private TMP_Text nameTxt;
    [SerializeField] private Image infoImg;

    [Header("카운트다운 텍스트 설정")]
    [Tooltip("남은 시간을 표시할 TextMeshProUGUI. 지정하지 않으면 자식 객체에서 자동 검색합니다.")]
    [SerializeField] private TMP_Text countdownText;
    
    private float _delay = 0;
    private Coroutine _fadeRoutine;
    private Coroutine _countdownRoutine;
    private StartManager _startManager;
    private StateManager _stateManager;

    private void Start()
    {
        // CanvasGroup 자동 할당
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
        if (canvasGroup == null)
        {
            Debug.LogError("ScenarioInfoUI: CanvasGroup 컴포넌트가 필요합니다.");
        }

        _startManager = FindAnyObjectByType<StartManager>();
        _stateManager = FindAnyObjectByType<StateManager>();
    }

    public void InitUI(Procedure proc)
    {
        // 시나리오 정보 불러오기.
        var inst = DataManager.Inst.GetInstruction(proc.instructionId);
        var scenario = DataManager.Inst.scenario;
        if (inst == null || scenario == null) return;
        _delay = float.TryParse(inst.displayDuration, out var d) ? d : 0f;
        if (_delay == 0) return;
        infoImg.sprite = FileUtils.GetSprite(inst.mediaContent, FilePath.UiData);
        nameTxt.text = scenario.name;
        Debug.Log("시나리오 정보 불러오기 완료");
        
        // 기존 코루틴 정리
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        if (_countdownRoutine != null) StopCoroutine(_countdownRoutine);

        // UI 초기 상태 세팅
        if (canvasGroup)
        {
            Debug.Log("UI 활성화");
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        // 텍스트 초기화
        if (countdownText)
        {
            countdownText.text = Mathf.CeilToInt(_delay).ToString();
            countdownText.enabled = true;
        }

        _countdownRoutine = StartCoroutine(CountdownCoroutine());
        _fadeRoutine = StartCoroutine(FadeOutCoroutine());
    }

    private IEnumerator CountdownCoroutine()
    {
        float remaining = _delay;
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            if (countdownText)
            {
                countdownText.text = Mathf.CeilToInt(remaining).ToString();
            }
            yield return null;
        }
        // 0초 표기 보정
        if (countdownText) countdownText.text = "0";
        _countdownRoutine = null;
    }

    private IEnumerator FadeOutCoroutine()
    {
        if (canvasGroup == null) yield break;

        // delay 동안 카운트다운을 수행하고 나서 바로 페이드‑아웃
        yield return new WaitForSeconds(_delay);

        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        // 최종 상태 보정 및 텍스트 숨김
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        if (countdownText) countdownText.enabled = false;

        _fadeRoutine = null;
        _startManager.StartFadeOut();
        
        // 일반 타임라인 실행시 UI를 켜주는게 브리핑 절차이기 때문에 여기서 절차 완료 처리를 해준다.
        StateManager.InstructionUIShown -= InitUI;
    }

    private void OnDisable()
    {
        // 씬 전환 등으로 비활성화될 때 코루틴 정리
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        if (_countdownRoutine != null) StopCoroutine(_countdownRoutine);
    }
}
