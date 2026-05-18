using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우발상황 오버레이 — TimeLimitUI 패턴(Appear/Running gauge/Success/Fail/Fade) + 비상연출(빨강 블렌딩 깜빡, scale 펄스, 사이렌 SFX 1회).
/// duration 자체 추적. StateManager_New 의 코루틴과 병행 — Deactivate(bool success) 외부 강제 종료는 멱등.
/// FailShake 의 offsetX 는 root.localScale 역수로 보정 — WorldSpace base(0.0005) 에서 시각 단위 일관.
/// </summary>
[DisallowMultipleComponent]
public class ContingencyOverlay : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private RectTransform root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI txtContent;
    [SerializeField] private TextMeshProUGUI timeTxt;
    [SerializeField] private Image imgGauge;
    [SerializeField] private Image imgSuccess;
    [SerializeField] private Image imgFail;
    [SerializeField] private Image redOverlay;

    [Header("Audio")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip sirenClip;

    [Header("Animation")]
    [SerializeField] private float appearDuration = 0.25f;
    [SerializeField] private float iconPulseDuration = 0.25f;
    [SerializeField] private float shakeDuration = 0.30f;
    [SerializeField] private float shakeAmplitude = 20f;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Emergency Effect")]
    [SerializeField] private float emergencyPulseSpeed = 2f;
    [SerializeField] private float scalePulseAmount = 0.05f;
    [SerializeField] private float redOverlayMaxAlpha = 0.5f;

    public enum State { Idle, Appearing, Running, SuccessPulse, FailShake, Fading }
    public State curState = State.Idle;

    private float durationSec;
    private float startTime;
    private float animStart;
    private bool _isSuccess;
    private Vector2 originPos;
    private Vector3 _originalScale = Vector3.one;

    void Awake()
    {
        if (root != null) _originalScale = root.localScale;
    }

    public void Activate(Contingency c)
    {
        gameObject.SetActive(true);

        // CD_Instruction.csv 매핑 — 우발상황 UI 데이터 흐름화 (작업 1)
        // 절차 path(case PullSubCord) 가 ApplyContingency 위임 시에도 동일 매핑 경로로 표시되도록 통일.
        var instData = DataManager.Inst != null ? DataManager.Inst.GetInstruction(c.instructionId) : null;

        if (instData != null)
        {
            if (txtContent != null) txtContent.text = instData.contentTxtKey;

            if (!float.TryParse(instData.timeLimit, out durationSec) || durationSec <= 0f)
            {
                if (!float.TryParse(c.duration, out durationSec) || durationSec <= 0f)
                    durationSec = 1f;
            }
        }
        else
        {
            // 안전망 fallback — instructionId 매핑 실패 시 CSV 원본값 사용
            Debug.LogWarning($"[ContingencyOverlay] instructionId='{c.instructionId}' 매핑 실패 — c.stepNameKey/c.duration fallback");
            if (txtContent != null) txtContent.text = c.stepNameKey;
            if (!float.TryParse(c.duration, out durationSec) || durationSec <= 0f)
                durationSec = 1f;
        }

        if (timeTxt != null) timeTxt.text = Mathf.FloorToInt(durationSec).ToString();

        if (imgGauge != null) imgGauge.fillAmount = 1f;
        if (imgSuccess != null) imgSuccess.gameObject.SetActive(false);
        if (imgFail != null) imgFail.gameObject.SetActive(false);

        if (redOverlay != null)
        {
            var c0 = redOverlay.color;
            c0.a = 0f;
            redOverlay.color = c0;
        }

        if (root != null)
        {
            root.localScale = Vector3.zero;
            originPos = root.anchoredPosition;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = false;
        }
        _isSuccess = false;

        curState = State.Appearing;
        animStart = Time.time;

        if (sfxSource != null && sirenClip != null) sfxSource.PlayOneShot(sirenClip);

        Debug.Log($"[ContingencyOverlay] Activate — id={c.id}, duration={durationSec}");
    }

    /// <summary>
    /// 외부 강제 종료. 현재 state ≤ Running 일 때만 success/fail 분기 진입. 이미 종료 페이즈면 무시(멱등).
    /// </summary>
    public void Deactivate(bool success)
    {
        if (curState == State.Idle || curState >= State.Fading) return;

        if (success && curState <= State.Running)
        {
            if (imgSuccess != null) imgSuccess.gameObject.SetActive(true);
            if (imgFail != null) imgFail.gameObject.SetActive(false);
            EnterEndPhase(State.SuccessPulse);
            _isSuccess = true;
        }
        else if (!success && curState <= State.Running)
        {
            if (imgFail != null) imgFail.gameObject.SetActive(true);
            if (imgSuccess != null) imgSuccess.gameObject.SetActive(false);
            EnterEndPhase(State.FailShake);
        }

        Debug.Log($"[ContingencyOverlay] Deactivate — success={success}, state={curState}");
    }

    /// <summary>
    /// Running 비상연출(scale 펄스/redOverlay 깜빡)에서 종료 페이즈로 전이.
    /// scale/redOverlay 잔여물 제거.
    /// </summary>
    private void EnterEndPhase(State next)
    {
        if (root != null) root.localScale = _originalScale;
        if (redOverlay != null)
        {
            var col = redOverlay.color;
            col.a = 0f;
            redOverlay.color = col;
        }
        curState = next;
        animStart = Time.time;
    }

    void Update()
    {
        switch (curState)
        {
            case State.Appearing:    RunAppear();       break;
            case State.Running:      RunGauge();        break;
            case State.SuccessPulse: RunSuccessPulse(); break;
            case State.FailShake:    RunFailShake();    break;
            case State.Fading:       RunFade();         break;
        }
    }

    private void RunAppear()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / appearDuration);
        if (root != null) root.localScale = Vector3.LerpUnclamped(Vector3.zero, _originalScale, UIUtils.EaseOutBack(t));

        if (t >= 1f)
        {
            curState = State.Running;
            startTime = Time.time;
        }
    }

    private void RunGauge()
    {
        float elapsed = Time.time - startTime;
        float remain = Mathf.Clamp01(1f - elapsed / durationSec);
        if (imgGauge != null) imgGauge.fillAmount = remain;
        if (timeTxt != null) timeTxt.text = Mathf.FloorToInt(durationSec - elapsed).ToString();

        // 비상연출: scale 펄스 (커졋다 작아졋다)
        float pulse = 1f + Mathf.Sin(Time.time * emergencyPulseSpeed * Mathf.PI) * scalePulseAmount;
        if (root != null) root.localScale = _originalScale * pulse;

        // 비상연출: 빨강 블렌딩 깜빡 (alpha PingPong 0 → maxAlpha)
        if (redOverlay != null)
        {
            var col = redOverlay.color;
            col.a = Mathf.PingPong(Time.time * emergencyPulseSpeed, redOverlayMaxAlpha);
            redOverlay.color = col;
        }

        if (remain <= 0f)
        {
            if (timeTxt != null) timeTxt.text = "0";
            if (imgFail != null) imgFail.gameObject.SetActive(true);
            if (imgSuccess != null) imgSuccess.gameObject.SetActive(false);
            EnterEndPhase(State.FailShake);
        }
    }

    private void RunSuccessPulse()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / iconPulseDuration);
        float scale = Mathf.LerpUnclamped(1f, 1.3f, UIUtils.EaseOutQuad(t));
        if (imgSuccess != null) imgSuccess.rectTransform.localScale = new Vector3(scale, scale, 1f);

        if (t >= 1f)
        {
            curState = State.Fading;
            animStart = Time.time;
        }
    }

    private void RunFailShake()
    {
        float t = (Time.time - animStart) / shakeDuration;
        if (t < 1f)
        {
            // ContingencyOverlay 의 root 는 부모가 비-RectTransform(PositionT, scale 1) 이라
            // anchoredPosition 변화가 localPosition 변화로 1:1 매핑 → world m 단위.
            // shakeAmplitude(px 의도 값) 에 _originalScale.x 를 곱해 world m 으로 환산.
            float strength = (1f - t) * shakeAmplitude * _originalScale.x;
            float offsetX = Mathf.Sin(t * Mathf.PI * 10f) * strength;
            if (root != null) root.anchoredPosition = originPos + Vector2.right * offsetX;
        }
        else
        {
            if (root != null) root.anchoredPosition = originPos;
            curState = State.Fading;
            animStart = Time.time;
        }
    }

    private void RunFade()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / fadeDuration);
        float eased = UIUtils.EaseInQuad(t);
        if (canvasGroup != null) canvasGroup.alpha = 1f - eased;

        if (t >= 1f)
        {
            curState = State.Idle;
            gameObject.SetActive(false);
            Debug.Log($"[ContingencyOverlay] 종료 — success={_isSuccess}");
        }
    }
}
