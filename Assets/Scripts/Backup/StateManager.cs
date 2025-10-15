using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StateManager : MonoBehaviour
{
    public static StateManager Inst { get; private set; }
    
    /* ─────────── 전역 UI 표시 이벤트 ──────────── */
    public static event Action<Procedure> InstructionUIShown;
    
    // 전역 초기화 이벤트
    public static event Action OnInit;

    /* ──────────────── Inspector ──────────────── */
    [SerializeField] private int startTimeLineIndex;
    [SerializeField] private ResultUI resultUI;
    [SerializeField] private PlayCharacter character;

    // 스페이스를 눌렀을때 한번에 넘어갈 타임라인 설정값
    // 테스트를 위해 임시로 만듬.
    [SerializeField] private int testNextId = 1;
    
    private CameraController _cameraController;
    private AirPlane _airPlane;
    private ProcedureActionHub _actionHub;

    /* ──────────────── 내부 필드 ──────────────── */
    private List<TimeLine> _timeLineSorted;
    private List<Procedure> _procedureList;
    
    private int _timelineIndex = 0;
    private int _procedureIndex = 0;
    private TimeLine _currentTimeline;
    private Procedure _currentProcedure;
    
    private Coroutine _timeRoutine;

    private WS_DB_Client _wsDBClient;

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
        _airPlane = FindAnyObjectByType<AirPlane>();
        _cameraController = FindAnyObjectByType<CameraController>();
        _actionHub = FindAnyObjectByType<ProcedureActionHub>();
        _wsDBClient = FindAnyObjectByType<WS_DB_Client>();
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SetTimeLine(_timelineIndex+testNextId);
        }
    }

    /// <summary>
    /// 교관이 훈련제어 상태를 설정했을때 실행 
    /// </summary>
    /// <param name="state"></param>
    public void ReceiveTrainingState(TrainingState state)
    {
        switch (state)
        {
            case TrainingState.Ready:
                break;
            case TrainingState.Start:
                Debug.Log("훈련 시작");
                break;
            case TrainingState.End:
                Debug.Log("훈련 종료");
                StopAllCoroutines();
                OnInit?.Invoke();
                break;
            case TrainingState.Restart:
                Debug.Log("훈련 재시작");
                StopAllCoroutines();
                OnInit?.Invoke();
                break;
        }
    }

    /// <summary>
    /// 교관쪽에서 타임라인ID를 받았을때 해당 타임라인 실행
    /// </summary>
    /// <param name="id"></param>
    public void ReceiveTimeLineID(string id)
    {
        if(_timeLineSorted == null) _timeLineSorted = DataManager.Inst.GetTimelineList();
        var tlRow = _timeLineSorted.Single(t => t.timelineID == id);
        var enumType = EnumUtils.GetEnumType(tlRow.jumpType);
        
        if (enumType == null)
        {
            Debug.LogError($"[TimeLine] jumpType '{tlRow.jumpType}' → 대응되는 Enum 타입을 찾지 못했습니다.");
            return;
        }

        if (!enumType.IsEnum)
        {
            Debug.LogError($"[TimeLine] '{enumType.FullName}' 타입은 Enum이 아닙니다. (jumpType = '{tlRow.jumpType}')");
            return;
        }

        if (!Enum.IsDefined(enumType, tlRow.name))
        {
            Debug.LogError($"[TimeLine] '{tlRow.name}' 값은 {enumType.Name} enum에 정의돼 있지 않습니다.");
            return;
        }

        int stageIndex = (int)Enum.Parse(enumType, tlRow.name, true); // 파싱 후 int 변환
        SetTimeLine(stageIndex);
    }

    /// <summary>
    /// 타임라인 설정
    /// </summary>
    /// <param name="order"></param>
    public void SetTimeLine(int order)
    {
        // 0. 이미 진행 중이거나 마지막 order면 리턴
        if (order < 0 || order >= _timeLineSorted.Count) return;
        if (_currentTimeline != null && _timelineIndex == order) return;
        
        Debug.Log("타임라인 실행할 준비가 됐다.");
        
        // 1. order로 내려온 id가 현재 단계보다 2단계 이상이면 건너뛰기 수행
        if (order <= _timelineIndex + 1)
        {
            Debug.Log(_timeLineSorted[order].name + " 타임라인 시작");
            StartTimeLine(order);
        }
        else
        {
            StartCoroutine(SkipTimeLine(order));
        }
    }

    /// <summary>
    /// 타임라인 시작
    /// </summary>
    /// <param name="order"></param>
    private void StartTimeLine(int order)
    {
        // 1. 타임라인, 절차 초기화
        _timelineIndex = order;
        _currentTimeline = _timeLineSorted[order];
        _procedureIndex = 0;
        // 1-1. 타임라인에 속한 절차 정렬, 필터링
        _procedureList = DataManager.Inst.GetProceduresOfTL(_currentTimeline.timelineID).ToList();
        
        // 2. 이전 이벤트 초기화
        CleanPrevEvents();
        
        // 3. 첫 번째 절차로 진입
        // 3-1. 타임라인에 절차가 없으면 완료처리 
        if (_procedureList.Count == 0)
        {
            // 절차가 없으면 자동으로 타임라인 성공처리
            _isSuccess = true;
            OnTimelineComplete();
            return;
        }
        
        SetProcedure(_procedureIndex);
    }

    /// <summary>
    /// 타임라인 스킵
    /// </summary>
    /// <param name="order"></param>
    private IEnumerator SkipTimeLine(int order)
    {
        Debug.Log("어디까지 스킵해야하나");
        for (int i = _timelineIndex; i < order; i++)
        {
            var procs = DataManager.Inst.GetProceduresOfTL(_timeLineSorted[i].timelineID);
            foreach (var p in procs)
            {
                _actionHub.TryInvoke(this, p.stepName, true);
                yield return null;
            }
        }
        StartTimeLine(order);
    }

    /// <summary>
    /// 모든 이벤트 코루틴 초기화
    /// </summary>
    private void CleanPrevEvents()
    {
        if (_timeRoutine != null)
        {
            StopCoroutine(_timeRoutine);
            _timeRoutine = null;
        }
        // 다른 이벤트/코루틴 정리 지점
    }

    /// <summary>
    /// 절차 진입
    /// </summary>
    /// <param name="step"></param>
    private void SetProcedure(int step)
    {
        _procedureIndex = step;
        _currentProcedure = _procedureList[step];
        
        Debug.Log(_currentProcedure.stepName + " 진입");

        // 실행 조건 트리거 연결
        StartCoroutine(TriggerAction());
    }

    /// <summary>
    /// 안내 UI 표시
    /// </summary>
    private void ShowInstruction()
    {
        Debug.Log(_currentProcedure.stepName + ":" + "Show Instruction");
        InstructionUIShown?.Invoke(_currentProcedure);
    }

    /// <summary>
    /// 절자 실행 조건 이벤트 실행
    /// </summary>
    private IEnumerator TriggerAction()
    {
        yield return null;
        Debug.Log("[TriggerAction] : " + _currentProcedure.stepName);
        if (Enum.TryParse(_currentProcedure.triggerCondition, ignoreCase: true, out TriggerCondition condition))
        {
            Debug.Log("절차 실행 조건 : " + condition.ToString());
        }
        else
        {
            // 빈값이면 조건이 없다고 생각하고 바로 실행
            ProcedureAction();
            yield break;
        }

        switch (condition)
        {
            case TriggerCondition.None:
            {
                ProcedureAction();
                break;
            }
            case TriggerCondition.Time:
            {
                float duration = float.TryParse(_currentProcedure.duration, out var v) ? v : 0f;
                _timeRoutine  = StartCoroutine(TimeWait(duration));
                break;
            }
            case TriggerCondition.Alt:
            {
                float alt = float.TryParse(_currentProcedure.requiredAltitude, out var v) ? v : 0f;
                character.AltTrigger(alt);
                break;
            }
        }
    }
    
    /// <summary>
    /// 절차 수행
    /// </summary>
    public void ProcedureAction()
    {
        // 1. 절차 수행 매서드 실행
        var result = _actionHub.TryInvoke(this, _currentProcedure.stepName);
        
        // 2. 안내 UI 표시
        ShowInstruction();
        
        // // 3. 완료 처리
        // // 3-1. 일반 매서드는 즉시 완료처리
        // if (result is bool and true)
        // {
        //     OnProcedureComplete();
        // }
        // // 3-2. 코루틴 형태는 대기 전부 완료될때까지 대기
        // else if (result is List<Coroutine> cos)
        // {
        //     _pendingHandlers = cos.Count;
        // }
    }
    
    private IEnumerator TimeWait(float sec)
    {
        yield return new WaitForSeconds(sec);
        
        ProcedureAction();
    }

    private bool _isSuccess = false;
    
    public void OnSuccess()
    {
        _isSuccess = true;
    }
    
    /// <summary>
    /// 절차 완료 이벤트
    /// </summary>
    public void OnProcedureComplete()
    {
        Debug.Log(_currentProcedure.stepName + ":" + "완료");
        CleanPrevEvents();
        
        // 절차 실패시 실패 로직 실행해서 다음 절차로 넘어가기.
        if (_isSuccess == false)
        {
            _actionHub.TryInvoke(this, _currentProcedure.stepName, false, true);
        }

        // 다음 절차가 존재하면 다음 절차 실행
        if (_procedureIndex + 1 < _procedureList.Count)
        {
            SetProcedure(++_procedureIndex);
        }
        else
        {
            Debug.Log(_currentTimeline.name + " 완료");
            OnTimelineComplete();
        }
    }
    
    /// <summary>
    /// 타임라인 완료 이벤트
    /// </summary>
    private void OnTimelineComplete()
    {
        CleanPrevEvents();
        Debug.Log(_currentTimeline.name + " 교관쪽에 타임라인 완료 했다고 보내줌.");
        _wsDBClient.SendTimelineComplete(_wsDBClient.WebSocketID, _currentTimeline.timelineID, _isSuccess);
        Debug.Log($"{_currentTimeline.timelineID} 타임라인 결과 정보(isSuccess : {_isSuccess}) 추가");
        //resultUI?.AddResult(_currentTimeline.timelineID, _isSuccess);
        _isSuccess = false;
    }

    /// <summary>
    /// 모든 타임라인 완료시 결과 UI 표시
    /// </summary>
    private void FinishScenario()
    {
        resultUI.Init();
    }
}
