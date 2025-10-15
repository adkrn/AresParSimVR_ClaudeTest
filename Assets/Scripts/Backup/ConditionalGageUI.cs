using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;
using UnityEngine.Serialization;

public enum ResultPhase   { None, ScaleUp, ScaleDown, FadeOut, Complete }

[RequireComponent(typeof(CanvasGroup))]
public class ConditionalGageUI : MonoBehaviour
{
    /* ──────────────────────────── 필수 레퍼런스 ──────────────────────────── */
    [Header("UI 컴포넌트 (필수)")]
    [SerializeField] private CanvasGroup canvasGroup;     // Fade 처리용
    [SerializeField] private Image gageFill;              // 게이지 이미지 (fillAmount 사용)
    [SerializeField] private TMP_Text conditionText;      // 안내 텍스트

    [Header("결과 아이콘")]
    [SerializeField] private Image successIcon;             // 성공 아이콘
    [SerializeField] private Image failureIcon;             // 실패 아이콘
    [SerializeField] private Image textIcon;                // (선택) 텍스트 옆 아이콘

    private CameraFrontPlacer _cameraFrontPlacer;           // (옵션) 카메라 앞 배치 스크립트
    private StateManager _stateManager;

    /* ──────────────────────────── 타이밍 설정 ──────────────────────────── */
    [Header("결과 연출 설정")]
    [SerializeField] private float scaleAnimDuration = 0.4f; // 스케일 업+다운 총 시간
    [SerializeField] private float fadeDuration = 1f;        // 페이드아웃 시간

    [FormerlySerializedAs("gaugeNormalColor")]
    [Header("게이지 색상")]
    [SerializeField] private Color gageNormalColor = Color.white;
    [SerializeField] private Color gageSuccessColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color gageFailureColor = new Color(1f, 0.3f, 0.3f);

    [Header("스케일 설정")]
    [SerializeField] private float scaleUpMultiplier = 1.2f;

    public Action onComplete;
    public Coroutine delayRoutine;

    /* ──────────────────────────── 내부 상태 변수 ──────────────────────────── */

    // Hold 모드에서 누적된 true 유지 시간
    [SerializeField] private float _heldTime;           
    [SerializeField] private float _requireT;

    
    private bool  _resultSuccess;           // 결과 연출 상태 머신 관련
    private float _resultTimer;             // 각 페이즈별 경과 시간
    private float _scaleHalfDuration;       // 스케일 업 또는 스케일 다운에 걸리는 시간
    private Vector3 _baseScale;             // 기본 로컬 스케일
    private Vector3 _enlargedScale;         // 확대 시 로컬 스케일

    private void Start()
    {
        Debug.Log("[ConditionalGageUI] Start!!");
        // 컴포넌트 할당
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (!gageFill) gageFill = GetComponentInChildren<Image>(true);
        if (!conditionText) conditionText = GetComponentInChildren<TMP_Text>(true);
        _cameraFrontPlacer = GetComponent<CameraFrontPlacer>();
        _stateManager = FindAnyObjectByType<StateManager>();

        // 스케일 계산
        _baseScale = transform.localScale;
        _enlargedScale = _baseScale * scaleUpMultiplier;
        _scaleHalfDuration = scaleAnimDuration * 0.5f;
        
        StateManager.OnInit += ()=>
        {
            _updateAction = null;
            delayRoutine = null;
            StateManager.InstructionUIShown -= InitUI;
        };
    }
    
    public void InitUI(Procedure proc)
    {
        gameObject.SetActive(true);
        Debug.Log("[ConditionalGageUI] ProcName : " + proc.stepName);
        StopDelayRoutine();
        
        // 안내 UI 정보 불러오기.
        var inst = DataManager.Inst.GetInstruction(proc.instructionId);
        if (inst == null || inst.hudVisible == "false") return;
        
        conditionText.text = inst.mediaContent;
        if (!string.IsNullOrEmpty(inst.iconName) || inst.iconName != "none")
        {
            textIcon.sprite = FileUtils.GetSprite(inst.iconName, FilePath.UiData);
        }
        
        // UI를 정면에 나오도록 설정
        _cameraFrontPlacer?.Place();
        
        InitializeUI();
        
        float dur = float.TryParse(inst.displayDuration, out var d) ? d : 0f;
        delayRoutine = StartCoroutine(DelayUi(dur));
    }
    
    private void Update()
    {
        _updateAction?.Invoke();
    }

    private Action _updateAction;
    
    public void ActiveTrigger(float t = 0f)
    {
        Debug.Log("[ConditionalGageUI] Active Trigger");
        _requireT = t;
        _heldTime = 0f;
        _updateAction += UpdateGageVisual;
        Debug.Log("[ConditionalGageUI] UpdateGageVisual Start");
    }

    public void BreakTrigger()
    {
        _heldTime = 0f;
        gageFill.fillAmount = 0f;
        _updateAction -= UpdateGageVisual;
    }

    /// <summary>
    /// _heldTime / requiredHoldTime 비율만큼 게이지를 차오르게 합니다.
    /// </summary>
    private void UpdateGageVisual()
    {
        if (_requireT == 0f || gageFill.fillAmount >= 1f)
        {
            gageFill.fillAmount = 1f;
            StopDelayRoutine();
            ToggleIcon(true);
            _updateAction -= UpdateGageVisual;
            _updateAction += () => HandleResultScale(true, _baseScale, _enlargedScale);
            return;
        }

        _heldTime += Time.deltaTime;
        //Debug.Log("[UpdateGageVisual] : " + _heldTime);
        gageFill.fillAmount = Mathf.Clamp01(_heldTime / _requireT);
    }

    private IEnumerator DelayUi(float delayT)
    {
        yield return new WaitForSeconds(delayT);
        
        ToggleIcon(false);
        _updateAction -= UpdateGageVisual;
        _updateAction += HandleResultPhase_FadeOut;
        
        // 실패시 절차 완료 신호를 여기서 보냄.
        onComplete += _stateManager.OnProcedureComplete;
    }

    private void InitializeUI()
    {
        Debug.Log("[ConditionalGageUI] 게이지 UI 초기화");
        // 내부 상태 초기화
        _heldTime = 0f;
        _resultTimer = 0f;

        // UI 활성화
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        transform.localScale = _baseScale;

        gageFill.color = gageNormalColor;
        gageFill.fillAmount = 0f;

        if (successIcon) successIcon.enabled = false;
        if (failureIcon) failureIcon.enabled = false;
    }

    /// <summary>
    /// 성공 또는 실패 아이콘을 토글합니다.
    /// </summary>
    private void ToggleIcon(bool success)
    {
        if(success) _stateManager.OnSuccess();
        
        Debug.Log("[ConditionalGageUI] ToggleIcon : " + success);
        _resultSuccess = success;
        successIcon.enabled = success;
        failureIcon.enabled = !success;
    }

    private void HandleResultScale(bool isUp, Vector3 start, Vector3 end)
    {
        _resultTimer += Time.deltaTime/ _scaleHalfDuration;
        if (_resultTimer < 1f)
        {
            transform.localScale = Vector3.Lerp(start, end, _resultTimer);
        }
        else
        {
            Debug.Log("[ConditionalGageUI] HandleResultScale");
            
            // 스케일 업 완료 → 스케일 다운 단계로 전환
            transform.localScale = end;
            _resultTimer = 0f;
            if (isUp)
            {
                // 2) 델리게이트를 업→다운 호출로 교체
                //    다음 프레임부터는 isUp=false, start/end가 뒤바뀐 상태로 호출
                _updateAction = () => HandleResultScale(
                    false,   // 두 번째 호출에 false 전달
                    end,     // 첫 번째 end가 이제 start
                    start);  // 첫 번째 start가 이제 end
            }
            else
            {
                _updateAction = null;
                _updateAction += HandleResultPhase_FadeOut;
            }
        }
    }

    private void HandleResultPhase_FadeOut()
    {
        _resultTimer += Time.deltaTime;
        if (_resultTimer < fadeDuration)
        {
            float t = _resultTimer / fadeDuration;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
        }
        else
        {
            Debug.Log("[ConditionalGageUI] HandleResultPhase_FadeOut");
            
            // 페이드 아웃 완료 → 최종 완료 단계
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            _updateAction -= HandleResultPhase_FadeOut;
            _updateAction = null;
            onComplete?.Invoke();
            onComplete = null;
        }
    }
    
    private void StopDelayRoutine()
    {
        if (delayRoutine != null)
        {
            StopCoroutine(delayRoutine);
            delayRoutine = null;
        }
    }
}
