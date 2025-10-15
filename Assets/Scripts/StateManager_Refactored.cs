using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// StateManager_New.cs의 리팩토링 버전
/// 주요 개선사항:
/// 1. 초기화 로직 개선
/// 2. ForceCompleteProcedure 단순화
/// 3. 중복 코드 제거
/// </summary>
public class StateManager_Refactored : MonoBehaviour
{
    public static StateManager_Refactored Inst { get; private set; }

    public Action OnFailAction;
    public static event Action OnBeforeSceneChange;

    [Header("Scene Settings")]
    [SerializeField] private string lobbySceneName;
    [SerializeField] private string mainSceneName;
    
    // Components
    private PlayCharacter _character;
    private CameraController _cameraController;
    private AirPlane _airPlane;
    private WearingSet _wearingSet;
    private WS_DB_Client _wsDBClient;
    
    // Timeline & Procedure Management
    [SerializeField] private List<TimeLine> _timeLineSorted;
    [SerializeField] private List<Procedure> _procedureList;
    private int _timelineIndex = 0;
    private int _procedureIndex = 0;
    private TimeLine _currentTimeline;
    private Procedure _currentProcedure;
    
    // State Management
    private Coroutine _timeRoutine;
    private bool _isSuccess = false;
    private bool _isAllTimelineComplete = false;
    
    // Point Check Variables
    private Coroutine _pointCheckRoutine;
    private int _targetRouteIndex = -1;

    #region Unity Lifecycle

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
        _wsDBClient = FindAnyObjectByType<WS_DB_Client>();
        InitializeTimelines();
        
        // 비디오 녹화 시작
        FFMPEGRecorder.Inst?.StartRecording();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 타임라인 초기화 - 중앙화된 초기화 로직
    /// </summary>
    private void InitializeTimelines()
    {
        if (_timeLineSorted == null || _timeLineSorted.Count == 0)
        {
            _timeLineSorted = DataManager.Inst.GetTimelineList();
            Debug.Log($"[StateManager] 타임라인 초기화 완료. 개수: {_timeLineSorted?.Count ?? 0}");
        }
    }

    /// <summary>
    /// 필요한 컴포넌트들을 캐싱
    /// </summary>
    private void CacheComponents()
    {
        if (_character == null) _character = FindAnyObjectByType<PlayCharacter>();
        if (_cameraController == null) _cameraController = FindAnyObjectByType<CameraController>();
        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
        if (_wearingSet == null) _wearingSet = FindAnyObjectByType<WearingSet>();
    }

    #endregion

    #region Training State Management

    public async void ReceiveTrainingState(TrainingState state)
    {
        Debug.Log($"[StateManager] 훈련 상태 수신: {state}");
        
        switch (state)
        {
            case TrainingState.Ready:
                HandleReadyState();
                break;
            case TrainingState.Start:
                HandleStartState();
                break;
            case TrainingState.Pause:
                HandlePauseState();
                break;
            case TrainingState.Resume:
                HandleResumeState();
                break;
            case TrainingState.End:
                await HandleEndState();
                break;
            case TrainingState.Restart:
                HandleRestartState();
                break;
        }

        _wsDBClient.SendTraningStateResponse(false, _wsDBClient.CurParticipantData);
    }

    private void HandleReadyState()
    {
        _wsDBClient.CurParticipantData.trainingState = TrainingState.Ready;
    }

    private void HandleStartState()
    {
        Debug.Log("훈련 시작 요청 받음");
        
        if (DataManager.Inst.IsDataLoaded)
        {
            Debug.Log("데이터 로딩 완료 - 훈련 시작");
            _wsDBClient.CurParticipantData.trainingState = TrainingState.Start;
            InitializeTimelines(); // 훈련 시작 시 타임라인 확실히 초기화
        }
        else
        {
            Debug.LogWarning("데이터가 준비되지 않음 - Ready 상태 유지");
            _wsDBClient.CurParticipantData.trainingState = TrainingState.Ready;
        }
    }

    private void HandlePauseState()
    {
        _wsDBClient.CurParticipantData.trainingState = TrainingState.Pause;
        UIManager.Inst.ShowPauseUI();
    }

    private void HandleResumeState()
    {
        _wsDBClient.CurParticipantData.trainingState = TrainingState.Resume;
        UIManager.Inst.HidePauseUI();
    }

    private async Task HandleEndState()
    {
        Debug.Log("훈련 종료");
        bool ok = await SaveEvaluationDataAsync();
        _wsDBClient.CurParticipantData.trainingState = TrainingState.End;
        LoadLobbyIfNeeded();
    }

    private void HandleRestartState()
    {
        Debug.Log("훈련 재시작");
        _wsDBClient.CurParticipantData.trainingState = TrainingState.Restart;
        LoadLobbyIfNeeded();
    }

    #endregion

    #region Procedure Command Handling

    /// <summary>
    /// 교관이 프로시저를 강제로 실행/완료시킴
    /// 개선된 로직: 더 단순하고 명확한 처리
    /// </summary>
    public void ForceCompleteProcedure(string procedureId)
    {
        Debug.Log($"[StateManager] 프로시저 강제 실행 명령: {procedureId}");
        
        // 1. 초기화 확인
        InitializeTimelines();
        
        // 2. 목표 프로시저 찾기
        var targetProcedure = FindProcedure(procedureId);
        if (targetProcedure == null)
        {
            Debug.LogError($"[StateManager] 프로시저를 찾을 수 없음: {procedureId}");
            return;
        }
        
        // 3. 목표 타임라인 찾기
        var targetTimeline = FindTimeline(targetProcedure.parentTimelineId);
        if (targetTimeline == null)
        {
            Debug.LogError($"[StateManager] 타임라인을 찾을 수 없음: {targetProcedure.parentTimelineId}");
            return;
        }
        
        // 4. 목표로 이동
        MoveToTargetProcedure(targetProcedure, targetTimeline);
    }

    /// <summary>
    /// 목표 프로시저로 이동하는 통합 로직
    /// </summary>
    private void MoveToTargetProcedure(Procedure targetProcedure, TimeLine targetTimeline)
    {
        // 타임라인 인덱스 찾기
        int targetTimelineIndex = GetTimelineIndex(targetTimeline.timelineID);
        if (targetTimelineIndex == -1)
        {
            Debug.LogError($"[StateManager] 타임라인 인덱스를 찾을 수 없음: {targetTimeline.timelineID}");
            return;
        }
        
        // 현재 상태 로그
        LogCurrentState(targetTimeline, targetProcedure);
        
        // 다른 타임라인으로 이동하는 경우에만 스킵 처리 및 타임라인 시작
        if (targetTimelineIndex != _timelineIndex)
        {
            // 스킵 처리
            SkipToTargetTimeline(targetTimelineIndex);
            
            // 목표 타임라인 시작
            StartTimeLine(targetTimelineIndex);
        }
        
        // 목표 프로시저로 이동
        MoveToSpecificProcedure(targetProcedure.id);
    }

    /// <summary>
    /// 특정 타임라인까지 스킵
    /// </summary>
    private void SkipToTargetTimeline(int targetTimelineIndex)
    {
        // 현재 타임라인이 없으면 스킵할 것도 없음
        if (_currentTimeline == null)
        {
            Debug.Log("[StateManager] 현재 진행 중인 타임라인 없음. 스킵 생략");
            return;
        }
        
        // 같은 타임라인이면 스킵 불필요
        if (targetTimelineIndex == _timelineIndex)
        {
            Debug.Log("[StateManager] 같은 타임라인 내 이동. 스킵 생략");
            return;
        }
        
        // 타겟이 이전 타임라인이면 스킵 불필요
        if (targetTimelineIndex < _timelineIndex)
        {
            Debug.Log("[StateManager] 이전 타임라인으로 이동. 스킵 생략");
            return;
        }
        
        // 현재 타임라인의 남은 절차들 스킵
        SkipRemainingProceduresInCurrentTimeline();
        
        // 중간 타임라인들 스킵
        SkipIntermediateTimelines(targetTimelineIndex);
    }

    /// <summary>
    /// 현재 타임라인의 남은 절차들 스킵
    /// </summary>
    private void SkipRemainingProceduresInCurrentTimeline()
    {
        if (_procedureList == null) return;
        
        for (int i = _procedureIndex; i < _procedureList.Count; i++)
        {
            var procedure = _procedureList[i];
            Debug.Log($"[StateManager] 스킵: {procedure.stepName} (ID: {procedure.id})");
            ExecuteEssentialAction(procedure);
            RecordSuccess(procedure);
        }
    }

    /// <summary>
    /// 중간 타임라인들 스킵
    /// </summary>
    private void SkipIntermediateTimelines(int targetTimelineIndex)
    {
        for (int i = _timelineIndex + 1; i < targetTimelineIndex; i++)
        {
            var timeline = _timeLineSorted[i];
            var procedures = DataManager.Inst.GetProceduresOfTL(timeline.timelineID);
            
            foreach (var procedure in procedures)
            {
                Debug.Log($"[StateManager] 타임라인 {timeline.name} 스킵: {procedure.stepName}");
                ExecuteEssentialAction(procedure);
                RecordSuccess(procedure);
            }
        }
    }

    /// <summary>
    /// 특정 프로시저로 이동
    /// </summary>
    private void MoveToSpecificProcedure(string procedureId)
    {
        int targetIndex = GetProcedureIndex(procedureId);
        if (targetIndex == -1)
        {
            Debug.LogError($"[StateManager] 현재 타임라인에서 프로시저를 찾을 수 없음: {procedureId}");
            return;
        }
        
        // 이미 목표 프로시저에 있거나 지나쳤으면 그냥 리턴
        if (targetIndex < _procedureIndex)
        {
            Debug.Log($"[StateManager] 이미 목표 프로시저({procedureId})를 지나쳤음. 현재: {_procedureIndex}, 목표: {targetIndex}");
            return;
        }
        
        // 현재 진행 중인 UI 정리
        CleanPrevEvents();
        
        // 현재 프로시저부터 목표 프로시저 이전까지만 스킵
        for (int i = _procedureIndex; i < targetIndex; i++)
        {
            var procedure = _procedureList[i];
            Debug.Log($"[StateManager] 목표 프로시저 이전 스킵: {procedure.stepName}");
            ExecuteEssentialAction(procedure);
            RecordSuccess(procedure);
        }
        
        // 목표 프로시저 실행
        _procedureIndex = targetIndex;
        SetProcedure(_procedureIndex);
    }

    #endregion

    #region Timeline Management

    /// <summary>
    /// 타임라인 ID를 받아서 해당 타임라인 실행
    /// </summary>
    public void ReceiveTimeLineID(string id)
    {
        InitializeTimelines();
        
        var timeline = _timeLineSorted.FirstOrDefault(t => t.timelineID == id);
        if (timeline == null)
        {
            Debug.LogError($"[StateManager] 타임라인을 찾을 수 없음: {id}");
            return;
        }
        
        int index = GetTimelineIndex(id);
        if (index >= 0)
        {
            SyncAirplanePosition(timeline);
            SetTimeLine(index);
        }
    }

    /// <summary>
    /// 타임라인 설정
    /// </summary>
    public void SetTimeLine(int order)
    {
        if (order < 0 || order >= _timeLineSorted.Count) return;
        if (_currentTimeline != null && _timelineIndex == order) return;

        Debug.Log($"[StateManager] {_timeLineSorted[order].name} 타임라인 시작");
        StartTimeLine(order);
    }

    /// <summary>
    /// 타임라인 시작
    /// </summary>
    private void StartTimeLine(int order)
    {
        // 초기화
        _timelineIndex = order;
        _currentTimeline = _timeLineSorted[order];
        _procedureIndex = 0;
        _procedureList = DataManager.Inst.GetProceduresOfTL(_currentTimeline.timelineID).ToList();
        
        // 이전 이벤트 정리
        CleanPrevEvents();
        
        // 절차가 없으면 완료 처리
        if (_procedureList.Count == 0)
        {
            _isSuccess = true;
            OnTimelineComplete();
            return;
        }
        
        // 첫 번째 절차 시작
        SetProcedure(_procedureIndex);
    }
    
    
    
    /// <summary>
    /// 다음 절차로 이동
    /// </summary>
    public void CompleteCurrentAndMoveToNext()
    { 
        // 다음 절차로 이동 및 실행 보장
        if (_procedureIndex + 1 < _procedureList.Count)
        {
            _procedureIndex++;
            SetProcedure(_procedureIndex);
        }
        else
        {
            // 타임라인 종료
            Debug.Log(_currentTimeline.name + " 완료");
            OnTimelineComplete();
        }
    }

    #endregion

    #region Procedure Management

    /// <summary>
    /// 절차 진입
    /// </summary>
    private void SetProcedure(int step)
    {
        _procedureIndex = step;
        _currentProcedure = _procedureList[step];
        
        Debug.Log($"[StateManager] {_currentProcedure.stepName} 진입 (CompleteCondition: {_currentProcedure.completeCondition})");
        
        // UI 표시
        UIManager.Inst.ShowInstructionUI(_currentProcedure);
        
        // 컴포넌트 캐싱
        CacheComponents();
        
        // 완료 조건에 따른 액션 실행
        ExecuteProcedureAction();
    }

    /// <summary>
    /// 절차 액션 실행 - 완료 조건에 따른 처리
    /// </summary>
    private void ExecuteProcedureAction()
    {
        switch (_currentProcedure.completeCondition)
        {
            case CompleteCondition.None:
                break;
                
            case CompleteCondition.Time:
                StartCoroutine(TimeAction());
                break;
                
            case CompleteCondition.Animation:
                HandleAnimationAction();
                break;
                
            case CompleteCondition.Point:
                StartPointCheck();
                break;
                
            case CompleteCondition.Item:
                ItemAction();
                break;
                
            case CompleteCondition.SitDown:
                if (_character != null)
                {
                    _character.SitDown();
                    // SitDown 완료 처리는 PlayCharacter의 TempDelayAni 코루틴에서 처리됨
                    // UIManager.Inst.OnSuccessAction() 호출 시 TimeLimitUI에서 OnProcedureComplete 호출
                }
                break;
                
            case CompleteCondition.Stand:
                _character?.Stand();
                break;
                
            case CompleteCondition.Fall:
                StartFreeFall();
                break;
                
            case CompleteCondition.PullCord:
                _character?.AddPullCordTrigger();
                break;
                
            case CompleteCondition.Landing:
                RegisterLandingEvent();
                break;
        }
    }

    /// <summary>
    /// 애니메이션 타입 액션 처리
    /// </summary>
    private void HandleAnimationAction()
    {
        switch (_currentProcedure.stepName)
        {
            case "TakeOff":
                TakeOff();
                break;
            case "FreeFall":
                if (_character != null)
                {
                    _character.Deploy();
                    // FreeFall은 낙하산 펼침 애니메이션이 끝난 후 PlayCharacter.EndDeployParachute에서 완료 처리
                }
                break;
        }
    }
    

    /// <summary>
    /// 착지 이벤트 등록
    /// </summary>
    private void RegisterLandingEvent()
    {
        if (_character != null)
        {
            _character.OnGroundCollision += () =>
            {
                OnSuccess();
                OnProcedureComplete();
                UIManager.Inst.AddAfterAction(UIManager.Inst.ShowResultUI);
            };
        }
    }

    
    /// <summary>
    /// 현재 절차 반환
    /// </summary>
    public Procedure GetCurrentProcedure()
    {
        return _currentProcedure;
    }

    #endregion

    #region Essential Actions (For Skipping)

    /// <summary>
    /// 스킵된 절차의 필수 동작만 즉시 실행
    /// </summary>
    private void ExecuteEssentialAction(Procedure procedure)
    {
        Debug.Log($"[StateManager] 필수 동작 실행: {procedure.stepName} - {procedure.completeCondition}");
        
        CacheComponents();
        
        switch (procedure.completeCondition)
        {
            case CompleteCondition.Item:
                _wearingSet?.ForceEquip(procedure.item);
                break;
                
            case CompleteCondition.Point:
                // 스킵 시에는 이벤트 구독 없이 바로 이동만 처리
                MoveAirplaneToTargetRouteWithoutEvent(procedure);
                break;
                
            case CompleteCondition.Animation:
                // TakeOff는 씬 전환이 필요하므로 스킵 시 특별 처리
                if (procedure.stepName == "TakeOff")
                {
                    Debug.Log("[StateManager] TakeOff 스킵 - 메인씬 전환 필요 여부 확인");
                    // 메인씬이 아니면 씬 전환이 필요하지만, 스킵 시에는 목표 프로시저에서 처리
                }
                break;
                
            default:
                Debug.Log($"[StateManager] 스킵 동작 없음: {procedure.completeCondition}");
                break;
        }
    }
    
    /// <summary>
    /// 비행기를 목표 루트로 이동 (이벤트 구독 없이)
    /// </summary>
    private void MoveAirplaneToTargetRouteWithoutEvent(Procedure procedure)
    {
        if (_airPlane == null) return;
        
        int targetRoute = CalculateTargetRoute(procedure.stepName);
        if (targetRoute >= 0)
        {
            // 이벤트 구독 해제 먼저
            _airPlane.OnRoutePointReached -= OnAirPlaneRoutePointReached;
            
            _airPlane.MoveToPointImmediately(targetRoute);
            Debug.Log($"[StateManager] {procedure.stepName} - 비행기를 route {targetRoute}로 이동 (이벤트 없이)");
        }
    }


    /// <summary>
    /// 절차명에 따른 목표 루트 계산
    /// </summary>
    private int CalculateTargetRoute(string stepName)
    {
        int scenarioRouteIndex = GetScenarioRouteIndex();
        
        return stepName switch
        {
            "TakeOff" => scenarioRouteIndex - 2,
            "ThreeMinutes" => scenarioRouteIndex - 1,
            "OneMinutes" => scenarioRouteIndex,
            _ => -1
        };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 프로시저 찾기
    /// </summary>
    private Procedure FindProcedure(string procedureId)
    {
        return DataManager.Inst.procedures.FirstOrDefault(p => p.id == procedureId);
    }

    /// <summary>
    /// 타임라인 찾기
    /// </summary>
    private TimeLine FindTimeline(string timelineId)
    {
        return DataManager.Inst.timeLines.FirstOrDefault(t => t.timelineID == timelineId);
    }

    /// <summary>
    /// 타임라인 인덱스 찾기
    /// </summary>
    private int GetTimelineIndex(string timelineId)
    {
        for (int i = 0; i < _timeLineSorted.Count; i++)
        {
            if (_timeLineSorted[i].timelineID == timelineId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 현재 프로시저 리스트에서 프로시저 인덱스 찾기
    /// </summary>
    private int GetProcedureIndex(string procedureId)
    {
        if (_procedureList == null) return -1;
        
        for (int i = 0; i < _procedureList.Count; i++)
        {
            if (_procedureList[i].id == procedureId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 시나리오 루트 인덱스 찾기
    /// </summary>
    private int GetScenarioRouteIndex()
    {
        var routes = DataManager.Inst.routes;
        string targetRouteId = DataManager.Inst.scenario.routeId;
        
        for (int i = 0; i < routes.Count; i++)
        {
            if (routes[i].routeId == targetRouteId)
                return i;
        }
        
        Debug.LogError($"[StateManager] 시나리오 routeId '{targetRouteId}'를 찾을 수 없음");
        return -1;
    }

    /// <summary>
    /// 성공 기록
    /// </summary>
    private void RecordSuccess(Procedure procedure)
    {
        UIManager.Inst.AddResult(procedure.evaluationId, "성공");
    }

    /// <summary>
    /// 현재 상태 로그
    /// </summary>
    private void LogCurrentState(TimeLine targetTimeline, Procedure targetProcedure)
    {
        Debug.Log($"[StateManager] 현재 상태:");
        Debug.Log($"  - 현재 타임라인: {_currentTimeline?.timelineID ?? "없음"} (인덱스: {_timelineIndex})");
        Debug.Log($"  - 현재 프로시저: {_currentProcedure?.id ?? "없음"} (인덱스: {_procedureIndex})");
        Debug.Log($"  - 목표 타임라인: {targetTimeline.timelineID}");
        Debug.Log($"  - 목표 프로시저: {targetProcedure.id}");
    }

    #endregion

    #region Event Cleanup

    /// <summary>
    /// 이전 이벤트 정리
    /// </summary>
    private void CleanPrevEvents()
    {
        // 시간 코루틴 정리
        if (_timeRoutine != null)
        {
            StopCoroutine(_timeRoutine);
            _timeRoutine = null;
        }
        
        // 포인트 체크 정리
        if (_pointCheckRoutine != null)
        {
            StopCoroutine(_pointCheckRoutine);
            _pointCheckRoutine = null;
        }
        
        // 비행기 이벤트 구독 해제
        if (_airPlane != null)
        {
            _airPlane.OnRoutePointReached -= OnAirPlaneRoutePointReached;
        }
        
        _targetRouteIndex = -1;
        
        // UI 정리 - 모든 UI 숨기기
        if (UIManager.Inst != null)
        {
            //UIManager.Inst.HideAllInstructionUI();
        }
    }

    #endregion

    #region Completion Handlers

    /// <summary>
    /// 절차 완료 처리
    /// </summary>
    public void OnProcedureComplete()
    {
        Debug.Log($"[StateManager] {_currentProcedure.stepName} 완료");
        CleanPrevEvents();
        
        // 실패 처리
        if (!_isSuccess)
        {
            OnFailAction?.Invoke();
        }
        
        // 결과 기록
        UIManager.Inst.AddResult(_currentProcedure.evaluationId, _isSuccess ? "성공" : "실패");
        
        // 프로시저 데이터 전송
        var data = new ProcedureData { procedureId = _currentProcedure.id };
        _wsDBClient.SendProcedureData(data);
        
        // 다음 절차로
        if (_procedureIndex + 1 < _procedureList.Count)
        {
            SetProcedure(++_procedureIndex);
        }
        else
        {
            OnTimelineComplete();
        }
    }

    /// <summary>
    /// 타임라인 완료 처리
    /// </summary>
    private void OnTimelineComplete()
    {
        CleanPrevEvents();
        Debug.Log($"[StateManager] {_currentTimeline.name} 타임라인 완료");
        
        _wsDBClient.SendTimelineComplete(_wsDBClient.WebSocketID, _currentTimeline.timelineID, _isSuccess);
        _isSuccess = false;
        
        if (_timelineIndex >= _timeLineSorted.Count - 1)
        {
            _isAllTimelineComplete = true;
        }
    }

    public void OnSuccess()
    {
        _isSuccess = true;
    }

    #endregion

    #region Specific Actions

    /// <summary>
    /// 시간 기반 액션
    /// </summary>
    private IEnumerator TimeAction()
    {
        if (_currentProcedure.stepName == "StandDoor")
        {
            _cameraController?.MoveJumpArea();
        }

        float duration = float.TryParse(_currentProcedure.duration, out var v) ? v : 0f;
        yield return new WaitForSeconds(duration);

        OnSuccess();
        OnProcedureComplete();
    }

    /// <summary>
    /// 아이템 액션
    /// </summary>
    private void ItemAction()
    {
        CacheComponents();
        
        Debug.Log($"[StateManager] {_currentProcedure.item} 착용하기");
        _wearingSet?.ShowStandby(_currentProcedure.item);
        
        UIManager.Inst.AddFailAction(() =>
        {
            _wearingSet?.ForceEquip(_currentProcedure.item);
        });
    }

    /// <summary>
    /// 비행기 이륙
    /// </summary>
    public void TakeOff()
    {
        if (SceneManager.GetActiveScene().name == mainSceneName)
        {
            Debug.Log("[StateManager] 이미 메인씬에 있음");
            HandleTakeOffInMainScene();
            return;
        }
        
        SceneManager.sceneLoaded += TakeOffComplete;
        
        if (_wsDBClient != null)
        {
            _wsDBClient.SendCurSceneState(SceneState.SceneLoading);
        }
        
        var fadeCtrl = FindAnyObjectByType<FadeController>(FindObjectsInactive.Include);
        fadeCtrl?.Init(FadeDir.Out, () =>
        {
            LoadSceneWithCleanup(mainSceneName);
        });
    }

    private void HandleTakeOffInMainScene()
    {
        var airplane = FindAnyObjectByType<AirPlane>();
        if (airplane != null && airplane.GetCurrentRouteIndex() >= 0)
        {
            StartPointCheckForTakeOff();
        }
        else
        {
            AirPlane.OnAirPlaneReady += OnAirPlaneInitialized;
        }
    }

    private void TakeOffComplete(Scene scene, LoadSceneMode mode)
    {
        Debug.Log("[StateManager] 비행기 이륙 완료");
        SceneManager.sceneLoaded -= TakeOffComplete;
        
        if (_wsDBClient != null)
        {
            _wsDBClient.SendCurSceneState(SceneState.SceneComplete);
        }
        
        if (_currentProcedure != null && _currentProcedure.stepName == "TakeOff" && 
            _currentProcedure.completeCondition == CompleteCondition.Point)
        {
            AirPlane.OnAirPlaneReady += OnAirPlaneInitialized;
            StartCoroutine(CheckAirPlaneReady());
        }
        else
        {
            OnSuccess();
            OnProcedureComplete();
        }
    }

    private void OnAirPlaneInitialized()
    {
        AirPlane.OnAirPlaneReady -= OnAirPlaneInitialized;
        StartPointCheckForTakeOff();
    }

    private IEnumerator CheckAirPlaneReady()
    {
        yield return new WaitForSeconds(0.1f);
        
        var airplane = FindAnyObjectByType<AirPlane>();
        if (airplane != null && airplane.GetCurrentRouteIndex() >= 0)
        {
            StartPointCheckForTakeOff();
        }
    }

    private void StartPointCheckForTakeOff()
    {
        CacheComponents();
        
        if (_airPlane == null)
        {
            Debug.LogError("[StateManager] AirPlane을 찾을 수 없음");
            return;
        }
        
        _targetRouteIndex = GetScenarioRouteIndex() - 2;
        Debug.Log($"[StateManager] TakeOff - 목표 route: {_targetRouteIndex}");
        
        if (_targetRouteIndex >= 0 && _airPlane.IsAtRoutePoint(_targetRouteIndex))
        {
            OnSuccess();
            OnProcedureComplete();
        }
        else
        {
            _airPlane.OnRoutePointReached += OnAirPlaneRoutePointReached;
        }
    }

    /// <summary>
    /// 자유 낙하 시작
    /// </summary>
    private void StartFreeFall()
    {
        StartCoroutine(TempJumpDelay());
    }

    private IEnumerator TempJumpDelay()
    {
        yield return new WaitForSeconds(1.0f);
        
        _character?.Jump();
        _cameraController?.OnJumpNoiseCam();
        
        // Fall 조건은 점프 후 즉시 성공 처리
        // TimeLimitUI의 성공 신호를 보내서 UI를 성공 표시하고 페이드아웃 시작
        UIManager.Inst.OnSuccessAction();
        // OnProcedureComplete는 TimeLimitUI의 페이드 완료 후 자동 호출됨
    }

    /// <summary>
    /// 포인트 체크 시작
    /// </summary>
    private void StartPointCheck()
    {
        if (_currentProcedure.stepName == "TakeOff")
        {
            TakeOff();
            return;
        }
        
        CacheComponents();
        
        if (_airPlane == null)
        {
            Debug.LogError("[StateManager] AirPlane을 찾을 수 없음");
            return;
        }
        
        _targetRouteIndex = CalculateTargetRoute(_currentProcedure.stepName);
        
        if (_targetRouteIndex >= 0)
        {
            _airPlane.OnRoutePointReached += OnAirPlaneRoutePointReached;
            Debug.Log($"[StateManager] Route {_targetRouteIndex} 도달 대기");
        }
    }

    private void OnAirPlaneRoutePointReached(int routeIndex)
    {
        if (routeIndex >= _targetRouteIndex && _targetRouteIndex >= 0)
        {
            Debug.Log($"[StateManager] Route {_targetRouteIndex} 도달!");
            
            if (_airPlane != null)
                _airPlane.OnRoutePointReached -= OnAirPlaneRoutePointReached;
            
            _targetRouteIndex = -1;
            
            if (IsNextProcedureTimelineChange())
            {
                _airPlane?.SetWaitingMode(true);
            }
            
            OnSuccess();
            OnProcedureComplete();
        }
    }

    private bool IsNextProcedureTimelineChange()
    {
        return _procedureIndex + 1 >= _procedureList.Count;
    }

    /// <summary>
    /// 비행기 위치 동기화
    /// </summary>
    private void SyncAirplanePosition(TimeLine timeline)
    {
        if (SceneManager.GetActiveScene().name != mainSceneName)
        {
            Debug.Log("[StateManager] 메인씬이 아니므로 비행기 동기화 생략");
            return;
        }
        
        CacheComponents();
        
        if (_airPlane == null)
        {
            Debug.LogWarning("[StateManager] AirPlane을 찾을 수 없음");
            return;
        }
        
        int targetRouteIndex = CalculateTargetRoute(timeline.name);
        
        if (targetRouteIndex >= 0)
        {
            int currentIndex = _airPlane.GetCurrentRouteIndex();
            
            if (currentIndex < targetRouteIndex)
            {
                _airPlane.MoveToPointImmediately(targetRouteIndex);
            }
            else if (currentIndex == targetRouteIndex)
            {
                _airPlane.ResumeMovement();
            }
        }
        else
        {
            _airPlane.ResumeMovement();
        }
    }

    #endregion

    #region Scene Management

    private void LoadLobbyIfNeeded()
    {
        if (SceneManager.GetActiveScene().name != lobbySceneName)
        {
            LoadSceneWithCleanup(lobbySceneName);
        }
    }

    private void LoadSceneWithCleanup(string sceneName)
    {
        // 씬 전환 전 이벤트 발생 - 구독한 컴포넌트들이 각자 정리 작업 수행
        // CameraFrontPlacer는 이벤트를 구독하고 있으므로 자동으로 ReturnToOriginalParent() 호출됨
        OnBeforeSceneChange?.Invoke();
        
        SceneManager.LoadScene(sceneName);
    }

    #endregion

    #region Evaluation Save

    private async Task<bool> SaveEvaluationDataAsync()
    {
        if (_wsDBClient == null)
        {
            Debug.LogError("[StateManager] WS_DB_Client를 찾을 수 없음");
            return false;
        }

        var data = UIManager.Inst?.evalData;
        if (data == null)
        {
            Debug.LogWarning("[StateManager] 저장할 평가 데이터 없음");
            return false;
        }

        data.jumpType = DataManager.Inst.scenario.jumpType;
        
        string evalIndex = _wsDBClient.GetEvaluationIndex();
        if (!string.IsNullOrEmpty(evalIndex))
        {
            data.evalParticipantId = evalIndex;
        }
        else
        {
            var participantData = _wsDBClient.GetParticipantData();
            data.evalParticipantId = participantData?.participantId ?? 
                                    _wsDBClient.CurParticipantData?.id ?? 
                                    $"SIM{_wsDBClient.GetSimulatorNumber()}_NODATA";
        }
        
        data.createTime = DateTime.Now;
        
        if (data.evalParticipantId == "None")
        {
            Debug.Log("[StateManager] 평가 ID가 'None'이므로 DB 저장 건너뜀");
            return true;
        }
        
        try
        {
            return await _wsDBClient.InsertDataAsync(TableInfo.evaluationList, data);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
    }

    #endregion
}