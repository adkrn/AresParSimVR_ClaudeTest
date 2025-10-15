using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class TimeLimitUI : MonoBehaviour
{
    // ---------- 외부에서 연결 ----------
    [Header("UI Refs")]
    [SerializeField] private RectTransform root;          // 전체 패널
    [SerializeField] private CanvasGroup canvasGroup;     // 페이드용
    [SerializeField] private TextMeshProUGUI txtContent;  // 지시 텍스트
    [SerializeField] private TextMeshProUGUI timeTxt;
    [SerializeField] private Image imgIco;
    [SerializeField] private Image imgGauge;              // 게이지 (fillAmount)
    [SerializeField] private Image imgSuccess;            // 성공 아이콘
    [SerializeField] private Image imgFail;               // 실패 아이콘

    [Header("Animation")]
    [SerializeField] private float appearDuration = 0.25f;  // UI 확대 등장
    [SerializeField] private float iconPulseDuration = 0.25f; // 성공 아이콘 크기 변화
    [SerializeField] private float shakeDuration    = 0.30f;   // ← 추가 : 흔들기 길이
    [SerializeField] private float shakeAmplitude   = 20f;     // ← 추가 : 픽셀 단위 진폭
    [SerializeField] private float fadeDuration = 0.3f;      // 종료 페이드

    // ---------- 콜백 ----------
    public Action OnFail;          // 제한시간 초과 시 호출
    public Action OnSuccess;       // 외부에서 성공 처리 후 후속 로직 필요하면 사용

    // ---------- 내부 상태 ----------
    public enum State { Idle, Appearing, Running, SuccessPulse, FailShake, Fading }
    public State curState = State.Idle;

    private float timeLimit;           // sec
    private float startTime;           // Running 시작 시각
    private float animStart;           // 현재 State 진입 시각
    private bool _isSuccess;
    private Vector2 originPos;               // ← 추가 : 흔들기 후 원위치 복구용
    private StateManager_New stateManager;

    private void Awake()
    {
        stateManager = FindAnyObjectByType<StateManager_New>();
    }

    // ----------- 초기화 -----------
    public void Init(Instruction data)
    {
        Debug.Log("[TimeLimitUI] 초기화 시작");
        // 1) 지시 UI 세팅
        txtContent.text = data.mediaContent;
        
        // 1-2) 아이콘 세팅
        if (string.IsNullOrWhiteSpace(data.iconName))
        {
            // 아이콘 이름이 없으면 이미지 전체를 숨김
            imgIco.gameObject.SetActive(false);
        }
        else
        {
            imgIco.gameObject.SetActive(true);
            imgIco.sprite = FileUtils.GetSprite(data.iconName, FilePath.UiData);
        }

        // 2) 제한시간
        if (!float.TryParse(data.timeLimit, out timeLimit) || timeLimit <= 0f)
            timeLimit = 1f; // 방어
        timeTxt.text = data.timeLimit;
        
        Debug.Log("[TimeLimitUI] 표시할 UI 설정 완료");

        // 3) UI 초기 상태
        imgGauge.fillAmount = 1f;
        imgSuccess.gameObject.SetActive(false);
        imgFail.gameObject.SetActive(false);
        root.localScale = Vector3.zero;
        canvasGroup.alpha = 1f;
        originPos = root.anchoredPosition;   // 기준 위치 저장
        _isSuccess = false;
        
        Debug.Log("[TimeLimitUI] UI 초기상태 설정 완료");
        
        // 4) 상태 머신 시작
        curState = State.Appearing;
        animStart = Time.time;
        Debug.Log("[TimeLimitUI] Appearing 설정 완료");
    }

    // ----------- 외부 성공 트리거 -----------
    public void MarkSuccess()
    {
        if (curState != State.Running)
        {
            Debug.Log("[TimeLimitUI] 현재 게이지 상태가 아닙니다.");
            return;
        }

        Debug.Log("[TimeLimitUI] 절차 수행 성공 신호를 받음");
        imgSuccess.gameObject.SetActive(true);
        imgFail.gameObject.SetActive(false);
        curState = State.SuccessPulse;
        animStart = Time.time;
        _isSuccess = true;
    }

    // ----------- Unity Loop -----------
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

    // ---------- State 별 로직 ----------
    private void RunAppear()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / appearDuration);
        root.localScale = Vector3.LerpUnclamped(Vector3.zero, Vector3.one, UIUtils.EaseOutBack(t));

        if (t >= 1f)
        {
            Debug.Log("[TimeLimitUI] RunAppear 완료 Running으로 전환한다.");
            curState = State.Running;
            startTime = Time.time;
        }
    }

    private void RunGauge()
    {
        float elapsed = Time.time - startTime;
        float remain = Mathf.Clamp01(1f - elapsed / timeLimit);
        imgGauge.fillAmount = remain;
        timeTxt.text = Mathf.FloorToInt(timeLimit - elapsed).ToString();

        if (remain <= 0f)
        {
            // 실패 처리 → FailShake로 진입
            timeTxt.text = "0";
            imgFail.gameObject.SetActive(true);
            imgSuccess.gameObject.SetActive(false);

            OnFail?.Invoke();
            
            curState     = State.FailShake;
            animStart = Time.time;
        }
    }

    private void RunSuccessPulse()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / iconPulseDuration);
        float scale = Mathf.LerpUnclamped(1f, 1.3f, UIUtils.EaseOutQuad(t));
        imgSuccess.rectTransform.localScale = new Vector3(scale, scale, 1f);

        if (t >= 1f)
        {
            Debug.Log("[TimeLimitUI] RunSuccessPulse 완료 페이드아웃으로 전환한다.");
            // 페이드 아웃 시작
            curState = State.Fading;
            animStart = Time.time;
        }
    }
    
    // ---------- 새로 추가된 흔들기 ----------
    void RunFailShake()
    {
        float t = (Time.time - animStart) / shakeDuration;
        if (t < 1f)
        {
            // 감쇠(linear) + 사인파로 좌우 흔들기
            float strength = (1f - t) * shakeAmplitude;
            float offsetX  = Mathf.Sin(t * Mathf.PI * 10f) * strength;
            root.anchoredPosition = originPos + Vector2.right * offsetX;
        }
        else
        {
            // 원위치 복구 후 페이드로
            root.anchoredPosition = originPos;
            curState     = State.Fading;
            animStart = Time.time;
        }
    }

    private void RunFade()
    {
        float t = Mathf.Clamp01((Time.time - animStart) / fadeDuration);
        float eased = UIUtils.EaseInQuad(t); // 부드럽게 사라짐
        canvasGroup.alpha = 1f - eased;

        if (t >= 1f)
        {
            curState = State.Idle;
            
            if (_isSuccess)
            {
                OnSuccess?.Invoke();
                _isSuccess = false;
                stateManager.OnSuccess();
            }
            
            // 카메라 앞에 배치할때 바꾼 부모를 원래대로
            transform.SetParent(UIManager.Inst.transform);
            Debug.Log("[TimeLimitUI] 애니메이션 종료 Idle 상태로 전환 후 비활성화 완료");
            gameObject.SetActive(false);
            
            stateManager.OnProcedureComplete();
        }
    }
}
