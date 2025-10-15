using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StateManager_New : MonoBehaviour
{
    public static StateManager_New Inst { get; private set; }

    public Action OnFailAction;
    public static event Action OnBeforeSceneChange;  // 씬 전환 직전 이벤트

    [SerializeField] private string lobbySceneName;
    [SerializeField] private string mainSceneName;
    
    private PlayCharacter _character;
    private CameraController _cameraController;
    private AirPlane _airPlane;
    private WearingSet _wearingSet;
    private WS_DB_Client _wsDBClient;
    private TrainingState _trainingState;
    
    [SerializeField] private List<TimeLine> _timeLineSorted;
    [SerializeField] private List<Procedure> _procedureList;
    private int _timelineIndex = 0;
    [SerializeField] private int _procedureIndex = 0;
    private TimeLine _currentTimeline;
    private Procedure _currentProcedure;
    private Coroutine _timeRoutine;
    private bool _isSuccess = false;

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
        _procedureIndex = 0;
    }
    
    public async void ReceiveTrainingState(TrainingState state)
    {
        switch (state)
        {
            case TrainingState.Ready:
            {
                _wsDBClient.CurParticipantData.trainingState = TrainingState.Ready;
                AresHardwareService.Inst.ResetHardware();
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                break;
            }
            case TrainingState.Start:
            {
                Debug.Log("훈련 시작 요청 받음");

                // DataManager의 데이터 로딩 상태 확인
                if (DataManager.Inst.IsDataLoaded)
                {
                    Debug.Log("데이터 로딩 완료 - 훈련 시작 가능 상태로 응답");
                    // 데이터가 준비되었으므로 Start 상태로 응답
                    _wsDBClient.CurParticipantData.trainingState = TrainingState.Start;
                    AresHardwareService.Inst.SetEvent(AresEvent.SitDown);
                }
                else if (DataManager.Inst.IsLoadingData)
                {
                    Debug.LogWarning("데이터 로딩 중 - Ready 상태로 응답");
                    // 데이터 로딩 중이므로 Ready 상태로 응답
                    _wsDBClient.CurParticipantData.trainingState = TrainingState.Ready;
                }
                else
                {
                    Debug.LogError("데이터가 로드되지 않았습니다 - Ready 상태로 응답");
                    // 데이터가 없으므로 Ready 상태로 응답
                    _wsDBClient.CurParticipantData.trainingState = TrainingState.Ready;
                }
                break;
            }
            case TrainingState.Pause:
            {
                if (_trainingState != TrainingState.Start) return;
                
                _wsDBClient.CurParticipantData.trainingState = TrainingState.Pause;
                UIManager.Inst.ShowPauseUI();
                break;
            }
            case TrainingState.Resume:
            {
                if (_trainingState != TrainingState.Pause) return;
                
                _wsDBClient.CurParticipantData.trainingState = TrainingState.Resume;
                UIManager.Inst.HidePauseUI();
                break;
            }
            case TrainingState.End:
            {
                Debug.Log("훈련 종료");

                _wsDBClient.CurParticipantData.trainingState = TrainingState.End;
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                ResetAllStates();
                LoadLobbyIfNeeded(); // 저장이 끝난 뒤 씬 전환
                break;
            }
        }

        _trainingState = state;
        _wsDBClient.SendTraningStateResponse(false, _wsDBClient.CurParticipantData);
    }

    // 강제 이탈 플래그
    private bool _isForceExitRequested = false;

    /// <summary>
    /// 강제 이탈
    /// </summary>
    public void ForceExit()
    {
        if (_currentProcedure.stepName != "GoJump") return;

        Debug.Log("강제이탈");
        _isForceExitRequested = true;  // 플래그 설정
        _character.Jump();
        _cameraController.OnJumpNoiseCam();

        UIManager.Inst.OnSuccessAction();
    }

    /// <summary>
    /// 주 낙하산 전개
    /// </summary>
    public void ForceMainParachute()
    {
        if(_currentProcedure.stepName != "FreeFall") return;
        
        _character.Deploy();
    }

    /// <summary>
    /// 강제 종료
    /// </summary>
    public void ForceTrainingEnd()
    {
        _trainingState = TrainingState.End;
        
        ResetAllStates();
        AresHardwareService.Inst.SetEvent(AresEvent.None);
        LoadLobbyIfNeeded();
    }

    public async void SaveResultData()
    {
        var ok = await SaveEvaluationDataAsync();
    }

    /// <summary>
    /// 현재 씬이 로비가 아니면 로비 씬을 로드
    /// </summary>
    private void LoadLobbyIfNeeded()
    {
        if (SceneManager.GetActiveScene().name == lobbySceneName)
        {
            Debug.Log("이미 로비 씬이므로 로드 생략");
            return;
        }
        
        LoadSceneWithCleanup(lobbySceneName);
    }

    /// <summary>
    /// 훈련 종료 후 초기화
    /// </summary>
    private void ResetAllStates()
    {
        StopAllCoroutines();
        UIManager.Inst?.HideAllInstructionUI();
        _timelineIndex = 0;
        _procedureIndex = 0;
        _currentProcedure = null;
        _currentTimeline = null;
        _timeRoutine = null;
        _isSkipPending = false;

        _timeLineSorted = null;
        _procedureList = null;
    }
    
    /// <summary>
    /// 씬 전환 전에 CameraFrontPlacer를 정리하고 씬을 로드하는 통합 메서드
    /// </summary>
    private void LoadSceneWithCleanup(string sceneName)
    {
        // 씬 전환 직전 이벤트 발생
        OnBeforeSceneChange?.Invoke();
        
        // 모든 CameraFrontPlacer를 원래 부모로 복귀
        var cameraPlacers = FindObjectsByType<CameraFrontPlacer>(FindObjectsSortMode.None);
        foreach (var placer in cameraPlacers)
        {
            placer.ReturnToOriginalParent();
        }
        
        Debug.Log($"[StateManager] {cameraPlacers.Length}개의 CameraFrontPlacer를 원래 부모로 복귀 후 {sceneName} 씬 로드");
        SceneManager.LoadScene(sceneName);
    }
    
    /// <summary>
    /// 교관쪽에서 타임라인ID를 받았을때 해당 타임라인 실행
    /// </summary>
    /// <param name="id"></param>
    public void ReceiveTimeLineID(string id)
    {
        if(_timeLineSorted is not { Count: > 0 }) _timeLineSorted = DataManager.Inst.GetTimelineList();
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

        int stageIndex = (int)Enum.Parse(enumType, tlRow.name, true);
        
        SetTimeLine(stageIndex);
    }

    /// <summary>
    /// 타임라인 설정
    /// </summary>
    /// <param name="order"></param>
    public void SetTimeLine(int order)
    {
        // 1. 이미 진행 중이거나 마지막 order면 리턴
        if (order < 0 || order >= _timeLineSorted.Count) return;
        if (_currentTimeline != null && _timelineIndex == order) return;

        // 2. order로 내려온 타임라인 시작
        Debug.Log(_timeLineSorted[order].name + " 타임라인 시작");
        StartTimeLine(order);
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
        
        // 2. 이전 이벤트 초기화
        CleanPrevEvents();
        
        SetProcedure(_procedureIndex);
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
    }
    
    public void ProcessProcedureRequest(string procedureId)
    {
        // 절차 인덱스 확인
        int currentIndex = DataManager.Inst.GetProcedureIdx(_currentProcedure.id);
        int targetIndex = DataManager.Inst.GetProcedureIdx(procedureId);
        
        if (targetIndex == -1)
        {
            Debug.LogError($"[StateManager] 절차 ID를 찾을 수 없습니다: {procedureId}");
            return;
        }
        
        Debug.Log($"[StateManager] 절차 진행 시작 - 현재:{_currentProcedure.id}, 목표:{procedureId}");
        
        // 현재 신호받기 전 절차가 이륙이면 비행기 출발
        if (_currentProcedure.stepName == ("TakeOff"))
        {
            AirPlaneDeparture();
        }
        
        // 신호받은 절차 인덱스가 다음 인덱스보다 높을떄
        // 스킵 실행
        if (targetIndex > currentIndex + 1)
        {
            Debug.Log($"<color=cyan>[StateManager] 스킵 실행: {currentIndex} → {targetIndex}</color>");
            SyncTimeline(procedureId);
            ExecuteSkip(procedureId, currentIndex, targetIndex);
        }
        else
        {
            Debug.Log("<color=white>[StateManager] 다음 절차로 정상 진행</color>");
            UIManager.Inst.HideAllInstructionUI();
            SetProcedure(targetIndex);
        }
    }
    
    // 클래스 멤버 변수에 추가
    private int _pendingSkipTargetIndex = -1;  // 스킵 목표 인덱스
    private bool _isSkipPending = false;       // 스킵 대기 중 플래그
    
    /// <summary>
    /// 절차 스킵 기능
    /// </summary>
    /// <param name="targetProcedureId"></param>
    /// <param name="currentIndex"></param>
    /// <param name="targetIndex"></param>
    private void ExecuteSkip(string targetProcedureId, int currentIndex, int targetIndex)
    {
        UIManager.Inst.HideAllInstructionUI();
        var targetProcedure = DataManager.Inst.GetProcedure(targetProcedureId);

        // 스킵 전에 씬 전환이 필요한지 체크
        if (IsSceneChangeRequired(targetIndex))
        {
            Debug.Log($"<color=yellow>[StateManager] 씬 전환 필요 - 스킵 정보 저장</color>");

            // 스킵 정보 저장
            _pendingSkipTargetIndex = targetIndex;
            _isSkipPending = true;

            // TakeOff 실행 (씬 전환)
            TakeOff();
            return;
        }
        
        Debug.Log($"<color=cyan>[StateManager] 스킵 시작: {_currentProcedure?.stepName} → {targetProcedure?.stepName}</color>");
        
        // 1. 중간 절차들 가져오기
        var skippedProcedures = DataManager.Inst.GetProceduresBetween(currentIndex, targetIndex);
        // 2. 필수 동작들 실행
        ExecuteEssentialActions(skippedProcedures);
        // 3. 목표 절차로 인덱스 이동
        _procedureIndex = targetIndex;
        // 4. 목표 절차 시작
        SetProcedure(targetIndex);
    }

    private bool IsSceneChangeRequired(int targetIndex)
    {
        string currentScene = SceneManager.GetActiveScene().name;

        // Lobby 씬이 아니면 전환 불필요
        if (currentScene != lobbySceneName)
            return false;

        int _takeOffProcedureIndex = 0;

        // TakeOff 인덱스 캐싱 (한 번만 찾기)
        var procedureList = DataManager.Inst.GetProcedureList();
        for (int i = 0; i < procedureList.Count; i++)
        {
            var stepName = procedureList[i].stepName;
            if (stepName.Contains("TakeOff"))
            {
                _takeOffProcedureIndex = i;
                
                // 이륙으로 스킵했을때 씬 전환 이후 정상 진행을 위해 현재 절차 인덱스를 이륙 절차 인덱스로 설정
                //_procedureIndex = _takeOffProcedureIndex;
                Debug.Log($"[StateManager] TakeOff 인덱스 캐싱: {i}번({procedureList[i].stepName})");
                break;
            }
        }
        
        Debug.Log($"씬 전환 프로시저 인덱스 : {_takeOffProcedureIndex} 목표 프로시저 인덱스 : {targetIndex}");
        // 목표가 TakeOff 이상이면 Main 씬 필요
        return targetIndex > _takeOffProcedureIndex;
    }




    /// <summary>
    /// 절차 진입
    /// </summary>
    /// <param name="step"></param>
    private void SetProcedure(int step)
    {
        // 이전 이벤트 초기화
        CleanPrevEvents();
        
        _procedureIndex = step;
        _procedureList = DataManager.Inst.GetProcedureList();
        _currentProcedure = _procedureList[step];
        
        Debug.Log("[StateManager] " + _currentProcedure.stepName + " 진입");
        Debug.Log("[StateManager] CompleteCondition : " + _currentProcedure.completeCondition);
        
        // InstructionUI를 표시한다.
        UIManager.Inst.ShowInstructionUI(_currentProcedure);
        if(_character== null) _character = FindAnyObjectByType<PlayCharacter>();

       CompleteTriggerAction();
    }
    
    private void SyncTimeline(string targetProcedureId)
    {
        // 목표 절차 찾기
        var targetProcedure = DataManager.Inst.GetProcedure(targetProcedureId);
        if (targetProcedure == null)
        {
            Debug.LogError($"[StateManager] 절차를 찾을 수 없습니다: {targetProcedureId}");
            return;
        }
        
        // 목표 타임라인 찾기
        string targetTimelineId = targetProcedure.parentTimelineId;
        int targetTimelineIndex = -1;
        
        for (int i = 0; i < _timeLineSorted.Count; i++)
        {
            if (_timeLineSorted[i].timelineID == targetTimelineId)
            {
                targetTimelineIndex = i;
                break;
            }
        }
        
        if (targetTimelineIndex == -1)
        {
            Debug.LogError($"[StateManager] 타임라인을 찾을 수 없습니다: {targetTimelineId}");
            return;
        }
        
        Debug.Log($"[StateManager] 타임라인 전환: {_currentTimeline?.timelineID} → {targetTimelineId}");
        
        // 현재 타임라인 완료 처리
        if (_currentTimeline != null)
        {
            _isSuccess = true;  // 스킵으로 인한 타임라인 전환은 성공으로 처리
            OnTimelineComplete();
        }
        
        // 새 타임라인으로 전환
        _timelineIndex = targetTimelineIndex;
        
        // 새 타임라인의 절차 목록 불러오기
        //_procedureList = DataManager.Inst.GetProceduresOfTL(targetTimelineId).ToList();
    }

    private void CompleteTriggerAction()
    {
        // 완료 조건 따라 절차 매서드 실행
        switch (_currentProcedure.completeCondition)
        {
            case CompleteCondition.None:
            {
                OnProcedureComplete();
                break;
            }
            case CompleteCondition.Time:
            {
                // 설정된 시간 후 자동 완료되는 절차 수행
                _timeRoutine = StartCoroutine(TimeAction());
                break;
            }
            case CompleteCondition.Animation:
            {
                switch (_currentProcedure.stepName)
                {
                    case "TakeOff":
                    {
                        TakeOff();
                        break;
                    }
                    case "FreeFall":
                    {
                        _character.Deploy();
                        break;
                    }
                }
                break;
            }
            case CompleteCondition.Point:
            {
                // Route 포인트 도달 시 완료되는 절차
                if(_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
                if (_airPlane.GetWaitingMode()) PointProcedureComplete(_airPlane.GetCurrentRouteIndex());
                else _airPlane.OnRoutePointReached += PointProcedureComplete;
                
                break;
            }
            case CompleteCondition.Item:
            {
                // 각 아이템에 해당하는 절차를 설정하는 매서드 실행
                ItemAction();
                break;
            }
            case CompleteCondition.SitDown:
            {
                AresHardwareService.Inst.SetEvent(AresEvent.SitDown);
                _character = FindAnyObjectByType<PlayCharacter>();
                _character?.SitDown();
                break;
            }
            case CompleteCondition.SceneLoading:
            {
                TakeOff();
                break;
            }
            case CompleteCondition.Stand:
            {
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                _cameraController = FindAnyObjectByType<CameraController>();
                _cameraController.MoveToPoint(CamPos.InAirPlane);
                _character = FindAnyObjectByType<PlayCharacter>();
                _character?.Stand();
                Debug.Log("<color=cyan>[StateManager] 서기");
                break;
            }
            case CompleteCondition.Fall:
            {
                StartFreeFall();
                break;
            }
            case CompleteCondition.PullCord:
            {
                _character.AddPullCordTrigger();
                break;
            }
            case CompleteCondition.Landing:
            {
                _character.OnGroundCollision += () =>
                {
                    OnSuccess();
                    OnProcedureComplete();
                    UIManager.Inst.AddAfterAction(() =>
                    {
                        UIManager.Inst.ShowResultUI();
                        SaveResultData();
                    });
                };
                break;
            }
        }
    }

    /// <summary>
    /// 완료조건이 Time일때 Action
    /// duration 후에 절차완료 처리한다.
    /// </summary>
    private IEnumerator TimeAction()
    {
        if (_currentProcedure.stepName == "StandDoor")
        {
            if (_cameraController == null) _cameraController = FindAnyObjectByType<CameraController>();

            _cameraController.MoveJumpArea();
        }

        var duration = float.TryParse(_currentProcedure.duration, out var v) ? v : 0f;
        yield return new WaitForSeconds(duration);

        OnSuccess();
        OnProcedureComplete();
    }

    /// <summary>
    /// 완료조건이 Item일때 Action
    /// 각 아이템에 따라서 진행할 절차를 설정하고, 완료 이벤트를 등록한다.
    /// </summary>
    private void ItemAction()
    {
        if (_wearingSet == null) _wearingSet = FindAnyObjectByType<WearingSet>();
        
        Debug.Log($"[StateManager] {_currentProcedure.item} 착용하기 ");
        _wearingSet.ShowStandby(_currentProcedure.item);
        
        // 시간안에 착용 못할시 완료처리할 매서드 등록
        UIManager.Inst.AddFailAction(()=>
        {
            _wearingSet.ForceEquip(_currentProcedure.item);
        });
    }
    
    /// <summary>
    /// 비행기 이륙하기
    /// </summary>
    public void TakeOff()
    {
        // 이미 메인씬에 있는 경우
        if (SceneManager.GetActiveScene().name == mainSceneName)
        {
            Debug.Log("[StateManager] 이미 메인씬에 있음. AirPlane 확인");
            
            // AirPlane이 이미 존재하고 초기화되었는지 확인
            var airplane = FindAnyObjectByType<AirPlane>();
            if (airplane != null && airplane.GetCurrentRouteIndex() >= 0)
            {
                // 이미 초기화 완료된 경우 바로 처리
                Debug.Log("[StateManager] AirPlane이 이미 초기화됨. 바로 Point 체크 시작");
            }
            else
            {
                // 아직 초기화되지 않은 경우 이벤트 대기
                Debug.Log("[StateManager] AirPlane 초기화 대기");
                AirPlane.OnAirPlaneReady += OnAirPlaneInitialized;
            }
            return;
        }
        
        SceneManager.sceneLoaded += TakeOffComplete;
        
        // 씬 전환 시작을 교관에게 알림
        if (_wsDBClient != null)
        {
            _wsDBClient.SendCurSceneState(SceneState.SceneLoading);
            Debug.Log("[StateManager] 씬 전환 시작 신호 전송 (SceneLoading)");
        }
        
        // 현재는 페이드 아웃 후 씬 넘기는걸로 처리
        var fadeCtrl = FindAnyObjectByType<FadeController>(FindObjectsInactive.Include);
        fadeCtrl.Init(FadeDir.Out, () =>
        {
            LoadSceneWithCleanup(mainSceneName);
        });
    }

    /// <summary>
    /// 비행기 이륙 완료 처리
    /// 메인씬이 로드가 완료되면 TakeOff를 완료처리한다.
    /// </summary>
    private void TakeOffComplete(Scene scene, LoadSceneMode mode)
    {
        Debug.Log("비행기 이륙 완료");
        SceneManager.sceneLoaded -= TakeOffComplete;
        
        // 씬 전환 완료를 교관에게 알림
        if (_wsDBClient != null)
        {
            _wsDBClient.SendCurSceneState(SceneState.SceneComplete);
            Debug.Log("[StateManager] 씬 전환 완료 신호 전송 (SceneComplete)");
        }
        
        // 스킵이 대기 중인 경우 처리
        if (_isSkipPending && _pendingSkipTargetIndex > _procedureIndex)
        {
            Debug.Log($"<color=yellow>[StateManager] 씬 전환 완료 - 스킵 처리 재개 (목표:{_pendingSkipTargetIndex})</color>");

            // 약간의 대기 (씬 오브젝트 초기화)
            StartCoroutine(CompleteSkipAfterSceneLoad());
            return;
        }

        
        // TakeOff가 Point 타입인 경우 AirPlane 초기화 완료를 기다림
        if (_currentProcedure is { stepName: "TakeOff", completeCondition: CompleteCondition.Point })
        {
            Debug.Log("[StateManager] TakeOff Point 체크를 위해 AirPlane 초기화 대기");
            AirPlane.OnAirPlaneReady += OnAirPlaneInitialized;
            
            // 이미 초기화되었을 수도 있으므로 확인
            StartCoroutine(CheckAirPlaneReady());
        }
        else
        {
            // Animation 타입이거나 다른 경우는 즉시 완료
            Debug.Log("[StateManager] 이륙 완료");
            OnSuccess();
            OnProcedureComplete();
        }
    }
    
    private IEnumerator CompleteSkipAfterSceneLoad()
    {
        // 오브젝트 초기화 대기
        yield return new WaitForSeconds(0.5f);

        var targetProcedure = DataManager.Inst.GetProcedureList()[_pendingSkipTargetIndex];

        // 3. 스킵된 절차들의 필수 동작 실행 (장비 착용 등)
        var skippedProcedures = DataManager.Inst.GetProceduresBetween(0, _pendingSkipTargetIndex);        
        ExecuteEssentialActions(skippedProcedures);

        // 4. 목표 절차로 이동
        _procedureIndex = _pendingSkipTargetIndex;
        SetProcedure(_pendingSkipTargetIndex);

        // 5. 스킵 정보 초기화
        _pendingSkipTargetIndex = -1;
        _isSkipPending = false;

        Debug.Log($"<color=green>[StateManager] 스킵 완료: {targetProcedure.stepName}</color>");
    }

    
    /// <summary>
    /// AirPlane 초기화 완료 시 호출
    /// </summary>
    private void OnAirPlaneInitialized()
    {
        Debug.Log("[StateManager] AirPlane 초기화 완료 - Point 체크 시작");
        AirPlane.OnAirPlaneReady -= OnAirPlaneInitialized;
        
    }
    
    /// <summary>
    /// AirPlane 준비 상태 확인 후 처리
    /// </summary>
    private System.Collections.IEnumerator CheckAirPlaneReady()
    {
        // 잠시 대기 후 AirPlane 확인
        yield return new WaitForSeconds(0.1f);
        
        var airplane = FindAnyObjectByType<AirPlane>();
        if (airplane != null && airplane.GetCurrentRouteIndex() >= 0)
        {
            Debug.Log("[StateManager] AirPlane이 이미 준비됨. Point 체크 시작");
        }
    }


    /// <summary>
    /// 점프하기
    /// 교육생이 점프하는 절차를 임시로 1초 딜레이 후 자동 강하로 대체해서 처리.
    /// </summary>
    private void StartFreeFall()
    {
        StartCoroutine(TempJumpDelay());
    }

    /// <summary>
    /// 교육생이 점프하는 모션을 임시로 1초 딜레이로 대체해서 강하 처리를 한다.
    /// </summary>
    /// <returns></returns>
    private IEnumerator TempJumpDelay()
    {
        float waitTime = 0;
        const float maxWaitTime = 5.0f;  // 최대 5초 대기
        const float checkInterval = 0.1f; // 0.1초마다 체크

        Debug.Log("[StateManager] 점프 대기 시작 - 최대 5초 대기");

        // 이미 점프 중이면 종료
        if (_character.paraCtrl.isJumpStart)
        {
            Debug.Log("[StateManager] 이미 점프 상태");
            yield break;
        }

        // 강제 이탈 플래그 초기화
        _isForceExitRequested = false;

        // 하드웨어 점프 상태 확인 시작
        while (waitTime < maxWaitTime)
        {
            // 강제 이탈 체크
            if (_isForceExitRequested)
            {
                Debug.Log($"[StateManager] 강제 이탈 감지! ({waitTime:F1}초)");

                // 강제 이탈은 이미 ForceExit()에서 Jump와 OnSuccessAction을 처리함
                // 플래그만 리셋하고 종료
                _isForceExitRequested = false;
                yield break;
            }

            // 하드웨어에 점프 상태 체크 요청
            AresHardwareService.Inst.GetJump();

            // 짧은 대기 (하드웨어 응답 시간)
            yield return new WaitForSeconds(checkInterval);
            waitTime += checkInterval;

            // 점프 감지됨
            if (AresHardwareService.Inst.isJump)
            {
                Debug.Log($"[StateManager] 하드웨어 점프 감지! ({waitTime:F1}초)");

                // 점프 실행
                _character.Jump();
                _cameraController.OnJumpNoiseCam();

                // 성공 처리
                UIManager.Inst.OnSuccessAction();

                // 점프 플래그 리셋
                AresHardwareService.Inst.isJump = false;
                yield break;
            }
        }

        // 5초 타임아웃 - 자동 점프
        Debug.LogWarning("[StateManager] 5초 대기 시간 초과 - 자동 점프 실행");

        // 점프 실행 (실패 처리)
        _character.Jump();
        _cameraController.OnJumpNoiseCam();
    }
    
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

        if (_currentProcedure.completeCondition != CompleteCondition.None)
        {
            // 절차 실패시 실패 로직 실행하기
            if (_isSuccess == false)
            {
                OnFailAction?.Invoke();
            }
        
            UIManager.Inst.AddResult(_currentProcedure.evaluationId, _isSuccess ? "성공" : "실패");
        }

        var data = new ProcedureData()
        {
            procedureId = _currentProcedure.id,
        };

        // 스킵 상태가 아니면 완료 신호를 보냄
        if (_isSkipPending == false)
        {
            _wsDBClient.SendProcedureData(data);
        }
    }
    
    /// <summary>
    /// 타임라인 완료 이벤트
    /// </summary>
    private void OnTimelineComplete()
    {
        CleanPrevEvents();
        Debug.Log(_currentTimeline.name + " 교관쪽에 타임라인 완료 했다고 보내줌.");
        //_wsDBClient.SendTimelineComplete(_wsDBClient.WebSocketID,_currentTimeline.timelineID, _isSuccess);
        Debug.Log($"{_currentTimeline.timelineID} 타임라인 결과 정보(isSuccess : {_isSuccess}) 추가");
        _isSuccess = false;
    }

    /// <summary>
    /// UIManager가 누적한 EvaluationListData를 DB(evaluationList 테이블)로 INSERT
    /// </summary>
    private async Task<bool> SaveEvaluationDataAsync()
    {
        if (_wsDBClient == null)
        {
            Debug.LogError("[StateManager] WS_DB_Client를 찾지 못했습니다.");
            return false;
        }

        var data = UIManager.Inst?.evalData;
        if (data == null)
        {
            Debug.LogWarning("[StateManager] 저장할 평가 데이터가 없습니다.");
            return false;
        }

        // evaluationData에 현재시간과 교육생 id를 설정 후 DB에 추가
        data.jumpType = DataManager.Inst.scenario.jumpType;
        
        // WS_DB_Client에서 받은 evaluationIndex 사용
        string evalIndex = _wsDBClient.GetEvaluationIndex();
        Debug.Log($"[StateManager] GetEvaluationIndex() 결과: '{evalIndex}'");
        
        if (!string.IsNullOrEmpty(evalIndex))
        {
            data.evalParticipantId = evalIndex;
            Debug.Log($"[StateManager] 평가 인덱스 사용: {evalIndex}");
        }
        else
        {
            // fallback: evaluationIndex가 없으면 participantId 사용
            var participantData = _wsDBClient.GetParticipantData();
            Debug.Log($"[StateManager] GetParticipantData() 결과: {(participantData != null ? $"participantId='{participantData.participantId}'" : "null")}");
            
            if (participantData != null && !string.IsNullOrEmpty(participantData.participantId))
            {
                data.evalParticipantId = participantData.participantId;
                Debug.LogWarning($"[StateManager] evaluationIndex가 없어서 participantId 사용: {participantData.participantId}");
            }
            else
            {
                // CurParticipantData 체크
                var curParticipant = _wsDBClient.CurParticipantData;
                Debug.Log($"[StateManager] CurParticipantData 상태: {(curParticipant != null ? $"id='{curParticipant.id}', name='{curParticipant.name}'" : "null")}");
                
                data.evalParticipantId = curParticipant?.id ?? $"SIM{_wsDBClient.GetSimulatorNumber()}_NODATA";
                
                if (data.evalParticipantId.Contains("NODATA"))
                {
                    Debug.LogError($"[StateManager] 경고: 평가 데이터 저장 시 유효한 ID가 없습니다!");
                    Debug.LogError($"[StateManager] - evaluationIndex: '{evalIndex}'");
                    Debug.LogError($"[StateManager] - participantData: {(participantData != null ? "있음" : "없음")}");
                    Debug.LogError($"[StateManager] - CurParticipantData.id: '{curParticipant?.id}'");
                    Debug.LogError($"[StateManager] 기본값 사용: {data.evalParticipantId}");
                }
                else
                {
                    Debug.LogWarning($"[StateManager] evaluationIndex와 participantData가 모두 없습니다. CurParticipantData.id 사용: {data.evalParticipantId}");
                }
            }
        }
        
        data.createTime    = DateTime.Now;
        
        // 평가 ID가 "NONE"인 경우 DB 저장 스킵
        if (data.evalParticipantId == "None")
        {
            Debug.Log("[StateManager] 평가 ID가 'NONE'이므로 DB 저장을 건너뜁니다.");
            return true;  // 성공으로 처리하여 훈련이 정상적으로 종료되도록 함
        }
        
        try
        {
            bool ok = await _wsDBClient.InsertDataAsync(TableInfo.evaluationList, data);
            Debug.Log(ok
                ? "[StateManager] EvaluationList INSERT 성공"
                : "[StateManager] EvaluationList INSERT 실패");
            return ok;
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
    }
    /// <summary>
    /// 비행기 출발
    /// </summary>
    private void AirPlaneDeparture()
    {
        if(_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
        Debug.Log("[StateManager] 비행기 출발");
        _airPlane.SetWaitingMode(false);
        _airPlane.OnRoutePointReached += OnAirPlaneRoutePointReached;
    }
    
    /// <summary>
    /// 비행기가 route 포인트에 도착했을때 대기
    /// </summary>
    private void OnAirPlaneRoutePointReached(int routeIndex)
    {
        Debug.Log($"[StateManager] Route 포인트 {routeIndex}번 도달 이벤트 수신.");
        
        // 목표 인덱스에 도달했는지 체크
        if (!DataManager.Inst.routes[routeIndex].isCompletePoint) return;
            
        // 이벤트 구독 해제
        if (_airPlane != null)
            _airPlane.OnRoutePointReached -= OnAirPlaneRoutePointReached;
            
        // 목표 인덱스에 도달하면 포인트 절차가 완료될때까지 대기
        _airPlane?.SetWaitingMode(true);
    }

    /// <summary>
    /// 포인트 절차 완료처리
    /// </summary>
    private void PointProcedureComplete(int routeIndex)
    {
        var route = DataManager.Inst.routes[routeIndex];
        var targetRoute = CalculateTargetRouteForProcedure(_currentProcedure.stepName);

        if (routeIndex > targetRoute)
        {
            Debug.LogError("[StateManager] 오류 : 이미 목표 포인트를 지나침");
            return;
        }

        if (routeIndex < targetRoute)
        {
            if (_airPlane.GetWaitingMode())
            {
                Debug.LogWarning($"[StateManager] 현재 위치{routeIndex} 비행기가 목표 포인트({targetRoute})전에 있는데 출발을 안함");
                Debug.LogWarning("[StateManager] 스킵으로 비행기 위치 동기화 후 출발");
                AirPlaneDeparture();
                _airPlane.OnRoutePointReached += PointProcedureComplete;
                return;
            }
        }

        if (!route.isCompletePoint)
        {
            Debug.LogError($"[StateManager]  {routeIndex}번은 완료 조건에 해당하는 목표 포인트가 아님");
            return;
        }
        
        Debug.Log($"[StateManager]{routeIndex}번 포인트 도착 {_currentProcedure.stepName} 절차 완료");
        if (_isSkipPending == false)
        {
            OnProcedureComplete();
        }

        AirPlaneDeparture();
        _airPlane.OnRoutePointReached -= PointProcedureComplete;
    }

    /// <summary>
    /// 스킵 시 비행기 위치 동기화
    /// </summary>
    private void SyncAirplaneForSkip(string targetProcedureId)
    {
        Debug.Log("[StateManager] 비행기 위치 동기화 시작");
        var targetProcedure = DataManager.Inst.GetProcedure(targetProcedureId);
        if (targetProcedure == null) return;

        if (_airPlane == null)
            _airPlane = FindAnyObjectByType<AirPlane>();

        if (_airPlane != null)
        {
            int targetRouteIndex = CalculateTargetRouteForProcedure(targetProcedure.stepName);
            if (targetRouteIndex >= 0)
            {
                Debug.Log($"[StateManager] 비행기를 {targetProcedure.stepName} 절차 목표지점 {targetRouteIndex}로 즉시 이동");
                _airPlane.MoveToPointImmediately(targetRouteIndex);
            }
        }
    }

    /// <summary>
    /// 절차별 목표 루트 계산
    /// </summary>
    private int CalculateTargetRouteForProcedure(string stepName)
    {
        int scenarioRouteIndex = DataManager.Inst.GetScenarioRouteIndex();
        
        // Route 오프셋 딕셔너리
        var routeOffsets = new Dictionary<string, int>
        {
            { "TakeOff", -2 },
            { "ThreeMinutes", -1 },
            { "OneMinutes", 0 }
        };
        
        if (routeOffsets.TryGetValue(stepName, out int offset))
        {
            return scenarioRouteIndex + offset;
        }
        
        return -1;
    }
    
    /// <summary>
    /// 스킵 시 필수 동작들 실행
    /// </summary>
    private void ExecuteEssentialActions(List<Procedure> skippedProcedures)
    {
        Debug.Log($"<color=cyan>[StateManager] {skippedProcedures.Count}개 절차의 필수 동작 실행</color>");
        
        foreach (var procedure in skippedProcedures)
        {
            Debug.Log($"<color=cyan>[StateManager] 스킵 절차: {procedure.stepName} (ID: {procedure.id})</color>");
            
            // CompleteCondition에 따른 필수 동작 처리
            switch (procedure.completeCondition)
            {
                case CompleteCondition.Item:
                    // 착용 절차는 강제 착용
                    if (_wearingSet == null) _wearingSet = FindAnyObjectByType<WearingSet>();
                    _wearingSet?.ForceEquip(procedure.item);
                    Debug.Log($"<color=cyan>[StateManager] 강제 착용: {procedure.item}");
                    break;
                    
                case CompleteCondition.SitDown:
                    // 앉기 상태 설정
                    AresHardwareService.Inst.SetEvent(AresEvent.SitDown);
                    _character = FindAnyObjectByType<PlayCharacter>();
                    _character?.SkipSitDown();
                    Debug.Log("<color=cyan>[StateManager] 강제 앉기 처리");
                    break;
                    
                case CompleteCondition.Stand:
                    // 서기 상태 설정
                    AresHardwareService.Inst.SetEvent(AresEvent.None);
                    _cameraController = FindAnyObjectByType<CameraController>();
                    _cameraController.MoveToPoint(CamPos.InAirPlane);
                    _character?.SkipStand();
                    Debug.Log("<color=cyan>[StateManager] 강제 서기 처리");
                    break;
                    
                case CompleteCondition.Point:
                    // 비행기 위치는 별도 처리
                    SyncAirplaneForSkip(procedure.id);
                    Debug.Log($"<color=cyan>[StateManager] Point 절차 스킵: {procedure.stepName}");
                    break;
                case CompleteCondition.SceneLoading:
                    break;
                default:
                    // 기타 절차들은 로깅만
                    Debug.Log($"<color=cyan>[StateManager] 일반 절차 스킵: {procedure.stepName}");
                    break;
            }
            
            // 평가 결과 저장 (스킵됨)
            UIManager.Inst.AddResult(procedure.evaluationId, "스킵");
        }
    }
}
