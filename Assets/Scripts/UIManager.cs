using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    public static UIManager Inst { get; private set; }

    [SerializeField] private TimeLimitUI timeLimitUI;
    [SerializeField] private InstructionUI instructionUI;
    [SerializeField] private ResultUI resultUI;
    [SerializeField] private GameObject pauseUI;
    private FadeController fadeController;
    
    public List<EvalResult> evalRList = new();
    public EvaluationListData evalData;

    private void Awake()
    {
        if (Inst == null)
        {
            Inst = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        fadeController = FindAnyObjectByType<FadeController>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[UIManager] 씬 전환됨: {scene.name}. FadeController 재검색");
        fadeController = FindAnyObjectByType<FadeController>();
        if (fadeController == null)
        {
            Debug.LogWarning("[UIManager] FadeController를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 절차의 실패 조건에 따라서 UI를 다르게 표시한다.
    /// </summary>
    /// <param name="procedure"></param>
    public void ShowInstructionUI(Procedure procedure)
    {
        var instData = DataManager.Inst.GetInstruction(procedure.instructionId);

        if (instData.hudVisible == "0")
        {
            Debug.Log($"[UIManager] {procedure.stepName}은 UI를 표시하지 않는 절차다.");
            return;
        }
        
        switch (procedure.failCondition)
        {
            case FailCondition.None:
            {
                // instructionUI가 파괴되는 오류 수정해야함
                instructionUI?.gameObject.SetActive(true);
                instructionUI?.Init(instData);
                break;
            }
            case FailCondition.Time:
            {
                timeLimitUI?.gameObject.SetActive(true);
                timeLimitUI?.Init(instData);
                break;
            }
            case FailCondition.Altitude:
            {
                // 추후 고도 표시 UI 제작
                break;
            }
        }
        
        // 사운드 재생
        var audio = FileUtils.GetVoice(instData.voiceContent);
    }

    /// <summary>
    /// 시간 제한이 있는 UI에 제한시간이 초과됐을때
    /// 완료 처리 할 매서드를 등록한다.
    /// </summary>
    public void AddFailAction(Action failAction)
    {
        timeLimitUI.OnFail = failAction;
    }

    /// <summary>
    /// 시간 제한이 있는 UI에서 제한시간 안에 성공 했을때
    /// UI 성공 신호를 보낸다 
    /// </summary>
    public void OnSuccessAction()
    {
        Debug.Log("성공신호 응답 받음");
        timeLimitUI.MarkSuccess();
    }

    /// <summary>
    /// 설명 UI 표시가 완료된후 실행할 매서드 등록
    /// </summary>
    /// <param name="afterAction"></param>
    public void AddAfterAction(Action afterAction)
    {
        instructionUI.OnFadeComplete = afterAction;
    }

    // 점수 계산을 전담하는 private 헬퍼
    private string CalcScore(Evaluation eval, string result)
    {
        switch (eval.scoreRuleType)
        {
            case ScoreRuleType.Fixed:
                return result == "성공" ? eval.maxScore : eval.minScore;

            case ScoreRuleType.Range:
            {
                int value = int.Parse(result);
                int min   = int.Parse(eval.minValueRange);
                int avg   = int.Parse(eval.avgValueRange);
                int max   = int.Parse(eval.maxValueRange);

                if (value < min || value > max)        return "0";
                else if (value <= avg)                 return eval.maxScore;
                else                                   return eval.maxScore;
            }

            case ScoreRuleType.None:
            default:
                return "0";
        }
    }

    // Evaluation 객체를 직접 받는 핵심 메서드
    public void AddResult(Evaluation eval, string result)
    {
        if (eval == null) return;

        // 1) 점수 계산 + UI 리스트(EvalResult) 저장 --------------------------
        string score = CalcScore(eval, result);
        evalRList.Add(new EvalResult
        {
            name = eval.nameKey,
            category = eval.category,
            result = result,
            score = score
        });

        // 2) 점수·결과를 EvaluationListData에도 반영 ------------------------
        if (evalData == null) return; // 아직 초기화 안 됐다면 패스

        // nameKey → EvName 변환 (대소문자 무시)
        if (!Enum.TryParse<EvName>(eval.name, true, out var evName))
            return; // 매칭 실패 시 무시

        switch (evName)
        {
            case EvName.FallTime:
                evalData.evalFallTimeResult = result;
                evalData.evalFallTimeScore = score;
                break;
            case EvName.TotalDistance:
                evalData.evalTotalDistanceResult = result;
                evalData.evalTotalDistanceScore = score;
                break;
            case EvName.AltimeterOn:
                evalData.evalAltimeterOnResult = result;
                evalData.evalAltimeterOnScore = score;
                break;
            case EvName.HelmetOn:
                evalData.evalHelmetOnResult = result;
                evalData.evalHelmetOnScore = score;
                break;
            case EvName.OxyMask:
                evalData.evalOxyMaskResult = result;
                evalData.evalOxyMaskScore = score;
                break;
            case EvName.SitDownComplete:
                evalData.evalSitDownCompleteResult = result;
                evalData.evalSitDownCompleteScore = score;
                break;
            case EvName.StandUpComplete:
                evalData.evalStandUpCompleteResult = result;
                evalData.evalStandUpCompleteScore = score;
                break;
            case EvName.HookUpComplete:
                evalData.evalHookUpCompleteResult = result;
                evalData.evalHookUpCompleteScore = score;
                break;
            case EvName.GoJumpComplete:
                evalData.evalGoJumpCompleteResult = result;
                evalData.evalGoJumpCompleteScore = score;
                break;
            case EvName.DeployAltitude:
                evalData.evalDeployAltitudeResult = result;
                evalData.evalDeployAltitudeScore = score;
                break;
            case EvName.DeployTargetDistance:
                evalData.evalDeployTargetDistanceResult = result;
                evalData.evalDeployTargetDistanceScore = score;
                break;
            case EvName.EventComplete:
                evalData.evalEventCompleteResult = result;
                evalData.evalEventCompleteScore = score;
                break;
            case EvName.FlareComplete:
                evalData.evalFlareCompleteResult = result;
                evalData.evalFlareCompleteScore = score;
                break;
            case EvName.LandingType:
                evalData.evalLandingTypeResult = result;
                evalData.evalLandingTypeScore = score;
                break;
            case EvName.TargetDistance:
                evalData.evalTargetDistanceResult = result;
                evalData.evalTargetDistanceScore = score;
                break;
            case EvName.LandingSpeed:
                evalData.evalLandingSpeedResult = result;
                evalData.evalLandingSpeedScore = score;
                break;
        }
    }

    public void AddResult(EvName evName, string result)
    {
        AddResult(DataManager.Inst.GetEvaluation(evName), result);
    }
    
    public void AddResult(string evaluationId, string result)
    {
        AddResult(DataManager.Inst.GetEvaluation(evaluationId), result);
    }

    public void ShowPauseUI()
    {
        fadeController.Init(FadeDir.Out, () =>
        {
            Time.timeScale = 0;
            pauseUI.SetActive(true);
        }, 0.3f, 0.7f);
    }

    public void HidePauseUI()
    {
        pauseUI.SetActive(false);
        Time.timeScale = 1;
        fadeController.Init(FadeDir.In, null, 0.2f, 0.7f);
    }

    public void ShowResultUI()
    {
        resultUI.gameObject.SetActive(true);
        resultUI.Init();
    }
    
    /// <summary>
    /// 내부 결과 초기화
    /// </summary>
    public void Clear()
    {
        evalRList.Clear();
    }
    
    /// <summary>
    /// 모든 Instruction 관련 UI 숨기기
    /// </summary>
    public void HideAllInstructionUI()
    {
        if (timeLimitUI != null && timeLimitUI.gameObject.activeSelf)
        {
            // 실행 중인 코루틴 정지
            timeLimitUI.curState = TimeLimitUI.State.Idle;
            timeLimitUI.gameObject.SetActive(false);
        }
        
        if (instructionUI != null && instructionUI.gameObject.activeSelf)
        {
            // 실행 중인 코루틴 정지
            instructionUI.updateAction = null;
            instructionUI.gameObject.SetActive(false);
        }
    }
}
