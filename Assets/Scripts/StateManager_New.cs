using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEditor.Rendering.Universal;
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
    [SerializeField] private WearingSet _wearingSet;
    [SerializeField] private DisplayCamCtrl camCtrl;
    public VolumeCtrl volumeCtrl;
    public LightCtrl lightCtrl;
    private WS_DB_Client _wsDBClient;
    private TrainingState _trainingState;
    
    [SerializeField] private List<TimeLine> _timeLineSorted;
    [SerializeField] private List<Procedure> _procedureList;
    private int _timelineIndex = 0;
    [SerializeField] private int _procedureIndex = 0;
    private TimeLine _currentTimeline;
    private Procedure _currentProcedure;
    private Coroutine _timeRoutine;
    private Coroutine _jumpRoutine;
    private bool _isSuccess = false;

    public bool isJump = false;
    [SerializeField] private FadeController fadeCtrl;

    public RiserGrabFollower leftFollower;
    public RiserGrabFollower rightFollower;

    private string _activeContingencyId = "";
    public string ActiveContingencyId => _activeContingencyId;   // AresHardwareParagliderController 옵션 C / F12 일반화에서 조회
    private Contingency _pendingContingency = null;
    private Coroutine _contingencyDurationCoroutine = null;
    private Coroutine _contingencyQueueExpireCoroutine = null;

    // 교관 송신 동적 malfunctionId — case PullSubCord 진입 시 1회성 소비
    // CSV 캐싱 객체(_currentProcedure) mutation 회피 위해 별도 필드로 격리
    private string _pendingMalfunctionId = "";

    // 우발상황 위임 결과 — CompleteContingency 가 세팅, WaitProcedureCompleteFromContingency 가 OnSuccess 판단에 사용
    private bool _lastContingencyResult = false;

    // 우발상황 2단계 완료 (action + completeCondition) 추적
    private bool _actionPerformed = false;
    private bool _conditionMet = false;
    private ContingencyAction _expectedAction = ContingencyAction.None;
    public bool IsContingencyActionPerformed => _actionPerformed;   // paraCtrl F12/chestTrigger 순서 강제 가드용

    private void Awake()
    {
        if (Inst == null)
        {
            Inst = this;
        }

        AirPlane.OnAirPlaneReady += HandleAirPlaneReady;
    }

    private void OnDestroy()
    {
        AirPlane.OnAirPlaneReady -= HandleAirPlaneReady;
    }

    private void Start()
    {
        _wsDBClient = FindAnyObjectByType<WS_DB_Client>();
        camCtrl = FindAnyObjectByType<DisplayCamCtrl>();
        volumeCtrl = FindAnyObjectByType<VolumeCtrl>();
        lightCtrl = FindAnyObjectByType<LightCtrl>();
        fadeCtrl = FindAnyObjectByType<FadeController>(FindObjectsInactive.Include);
        _procedureIndex = 0;
    }

    /// <summary>
    /// AirPlane 초기화 완료 시 호출. 정상 흐름이면 DoorOpen 시작(햇빛 애니메이션), 스킵 펜딩이면 문 즉시 열림(완료 상태).
    /// 스킵 흐름에서 SetProcedure가 ThreeMinutes 등 어두운 preset을 다시 적용하는 문제는
    /// ExecuteSkip / CompleteSkipAfterSceneLoad 끝에서 OpenDoorEnd로 다시 한 번 강제 적용해 처리한다.
    /// </summary>
    private void HandleAirPlaneReady()
    {
        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
        if (_airPlane == null) return;

        if (_isSkipPending)
        {
            Debug.Log("[StateManager] 스킵 중 - 문 즉시 열림 처리");
            _airPlane.OpenDoorImmediately();
        }
        else
        {
            Debug.Log("[StateManager] 정상 흐름 - DoorOpen 시작");
            _airPlane.DoorOpen();
        }
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
                    // 영상 녹화 시작
                    FFMPEGRecorder.Inst?.StartRecording();
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
                if (_trainingState != TrainingState.Start && _trainingState != TrainingState.Resume) return;
                
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
                if (_trainingState != TrainingState.Start)
                {
                    Debug.LogWarning("훈련 종료 처리 정보 : 현재 훈련중인 상태가 아니므로 종료 상태로 바꾸지 않습니다.");
                    return;
                }
                _wsDBClient.CurParticipantData.trainingState = TrainingState.End;
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                // 영상 녹화 종료 → CaptureLoop 정리 후 UploadVideo 자동 호출
                FFMPEGRecorder.Inst?.StopRecording();
                ResetAllStates();
                //LoadLobbyIfNeeded(); // 저장이 끝난 뒤 씬 전환
                SceneLoadManager.Inst.LoadLobbyScene();
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

        leftFollower.OnGrabBegin();
        rightFollower.OnGrabBegin();
    }

    /// <summary>
    /// 강제 종료
    /// </summary>
    public void ForceTrainingEnd()
    {
        _trainingState = TrainingState.End;

        // 영상 녹화 종료 → CaptureLoop 정리 후 UploadVideo 자동 호출
        FFMPEGRecorder.Inst?.StopRecording();

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
        SceneLoadManager.Inst.LoadMainScene(TakeOffComplete);
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
        if (_jumpRoutine != null)
        {
            StopCoroutine(_jumpRoutine);
            _jumpRoutine = null;
        }

        // 이전 절차의 stale MoveTo/MoveToAndWait 명령이 자연 도달 시 새 절차를 잘못 완료시키는 걸 방지
        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
        _airPlane?.ClearCommandQueue();

        // TimeLimitUI 이벤트 정리
        UIManager.Inst.CleanTimeUIEvent();
    }
    
    public void ProcessProcedureRequest(string procedureId, string malfunctionId = "")
    {
        // 교관 송신 동적 ID 저장 — case PullSubCord 진입 시 소비
        _pendingMalfunctionId = malfunctionId ?? "";
        Debug.Log("malFuncID : " + _pendingMalfunctionId);

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
    [SerializeField] private bool _isSkipPending = false;       // 스킵 대기 중 플래그
    
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
        _isSkipPending = true;

        // 비행기 명령 큐와 stale 핸들러를 즉시 정리.
        // 진행 중이던 절차의 MoveTo가 자연 도달 시 새 절차를 잘못 완료 처리하는 것을 방지.
        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();
        _airPlane?.ClearCommandQueue();

        // 진행 중이던 현재 절차의 essential action(ForceEquip/SkipSitDown 등)도 적용 +
        // 평가 결과를 "스킵"으로 기록. (skippedProcedures가 startIdx 제외하므로 별도 처리)
        if (_currentProcedure != null && _currentProcedure.completeCondition != CompleteCondition.None)
        {
            Debug.Log($"<color=cyan>[StateManager] 진행 중이던 절차({_currentProcedure.stepName}) 스킵 처리</color>");
            ExecuteEssentialActions(new List<Procedure> { _currentProcedure });
        }

        //스킵 전에 씬 전환이 필요한지 체크
        if (IsSceneChangeRequired(targetIndex))
        {
            Debug.Log($"<color=yellow>[StateManager] 씬 전환 필요 - 스킵 정보 저장</color>");

            // 스킵 정보 저장
            _pendingSkipTargetIndex = targetIndex;

            // TakeOff 실행 (씬 전환)
            TakeOff();
            return;
        }

        Debug.Log($"<color=cyan>[StateManager] 스킵 시작: {_currentProcedure?.stepName} → {targetProcedure?.stepName}</color>");

        // 1. 중간 절차들의 필수 동작(장비/자세/평가)만 실행
        var skippedProcedures = DataManager.Inst.GetProceduresBetween(currentIndex, targetIndex);
        ExecuteEssentialActions(skippedProcedures);

        // 2. 비행기 위치를 target 절차의 시작 지점으로 1회 동기화 + 출발
        SyncAirplaneForSkip(targetProcedureId);
        if (_airPlane != null) AirPlaneDeparture();

        // 3. 스킵 완료 → 플래그 해제 후 target 진입
        if (_pendingContingency != null)
        {
            var pending = _pendingContingency;
            _pendingContingency = null;
            if (_contingencyQueueExpireCoroutine != null)
            {
                StopCoroutine(_contingencyQueueExpireCoroutine);
                _contingencyQueueExpireCoroutine = null;
            }
            Debug.Log($"[StateManager] 큐잉 우발상황({pending.id}) 적용 (ExecuteSkip)");
            _isSkipPending = false;
            ApplyContingency(pending);
        }
        else
        {
            _isSkipPending = false;
        }
        _procedureIndex = targetIndex;
        SetProcedure(targetIndex);

        // 스킵 = 절차 완료 상태로 점프. 햇빛은 항상 OpenDoorEnd(0). ThreeMinutes preset(-10)이 덮어쓰는 것 방지.
        volumeCtrl?.ApplyPreset("OpenDoorEnd", 0f);
    }

    private bool IsSceneChangeRequired(int targetIndex)
    {
        string currentScene = SceneManager.GetActiveScene().name;

        // Main 씬 로드 여부 검사 (multi-scene loading: AppBootstrap이 active이고 Lobby/Main이 additive로 로드되는 구조 대응)
        bool mainSceneLoaded = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == mainSceneName)
            {
                mainSceneLoaded = true;
                break;
            }
        }

        // [진단] 씬 비교 정합성 체크용 로그
        Debug.Log($"[IsSceneChangeRequired] currentScene='{currentScene}' (len={currentScene?.Length}), lobbySceneName='{lobbySceneName}', mainSceneName='{mainSceneName}', mainSceneLoaded={mainSceneLoaded}, targetIndex={targetIndex}");

        // Main 씬이 이미 로드돼 있으면 전환 불필요
        if (mainSceneLoaded)
        {
            Debug.Log($"[IsSceneChangeRequired] Main 씬 이미 로드됨 → 씬 전환 불필요(false 반환)");
            return false;
        }

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
        
        camCtrl.SetPosition(_currentProcedure.stepName);
        volumeCtrl.ApplyPreset(_currentProcedure.stepName);
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
                // ========================================================
                // [리팩토링] 기존 이벤트 방식 → 명령 큐 방식으로 변경
                // ========================================================
                // 기존 방식:
                //   - PointProcedureComplete 이벤트 핸들러 등록
                //   - 중복 등록 가능, 해제 타이밍 복잡
                //
                // 새로운 방식:
                //   - StartPointProcedure 호출
                //   - 명령 객체 생성 → AirPlane 큐에 추가
                //   - 완료 시 콜백 자동 호출
                // ========================================================

                if (_currentProcedure.stepName == "StandUp")
                {
                    StandUp();
                }
                StartPointProcedure();
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
            case CompleteCondition.StandUp:
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
            case CompleteCondition.EndAltitude:
            {
                // D1/D3 — STANDARD FreeFall 자동 종료. Deploy 미호출, 절차 완료 신호만 (메인 펴기는 후속 절차/Landing 가드에 위임)
                if (_character == null)
                {
                    Debug.LogError("[StateManager] EndAltitude — _character null, 즉시 완료");
                    OnProcedureComplete();
                    break;
                }
                int targetAlt = DataManager.Inst.scenario.autoActiveAltitude;
                Debug.Log($"[StateManager] EndAltitude — scenario.autoActiveAltitude={targetAlt}m, Deploy 미호출, 절차 완료 신호만 등록");
                _character.AddEndAltitudeTrigger(targetAlt, () =>
                {
                    OnSuccess();
                    OnProcedureComplete();
                });
                break;
            }
            case CompleteCondition.Landing:
            {
                // D7 — InAir(Landing) 진입 시 메인 미전개면 자동 Deploy.
                //   정상 시나리오: STANDARD EndAltitude 후 TotalMalfunction 미수신 → 메인 펴기 위임 받음.
                //   HAHO/HALO: 이미 PullCord 단계에서 Deploy → isParaDeployed=true → noop.
                //   MainParaOff 우발상황: MarkParaDeployedSuppressed 또는 DeploySubPara 가 isParaDeployed=true → noop.
                if (_character != null && !_character.isParaDeployed)
                {
                    Debug.Log("[StateManager] Landing 진입 — 메인 미전개 상태 감지, 자동 Deploy 실행");
                    _character.Deploy();
                }

                _character.OnGroundCollision += () =>
                {
                    leftFollower.OnGrabEnd();
                    rightFollower.OnGrabEnd();
                    OnSuccess();
                    OnProcedureComplete();
                    UIManager.Inst.AddAfterAction(() =>
                    {
                        UIManager.Inst.ShowResultUI();
                    });
                };
                break;
            }
            case CompleteCondition.PullSubCord:
            {
                // paraCtrl null guard
                if (_character == null || _character.paraCtrl == null)
                {
                    Debug.LogError("[StateManager] PullSubCord — paraCtrl null, 즉시 종료");
                    OnProcedureComplete();
                    break;
                }

                // TotalMalfunction 일반화 dispatch — 교관 송신 malfunctionId 로 우발상황 위임
                // 1회성 소비 (잔재 leak 방지)
                string malfunctionId = _pendingMalfunctionId;
                _pendingMalfunctionId = "";

                // (A) malfunctionId 없음 = 정상 시나리오 (D4) — 메인 펴기 결정권자.
                //     STANDARD: EndAltitude 직후 절차로 메인 전개 위임 받은 정상 경로.
                if (string.IsNullOrEmpty(malfunctionId))
                {
                    Debug.Log("[StateManager] PullSubCord — malfunctionId 없음(정상), 메인 Deploy 후 절차 완료");
                    if (!_character.isParaDeployed) _character.Deploy();
                    OnSuccess();
                    OnProcedureComplete();
                    break;
                }

                // (B) malfunctionId 있음 — Contingency 매핑
                Contingency mapped = DataManager.Inst.GetContingency(malfunctionId);
                if (mapped == null)
                {
                    Debug.LogError($"[StateManager] PullSubCord — malfunctionId='{malfunctionId}' 매핑 실패, 안전 fallback 으로 메인 Deploy");
                    if (!_character.isParaDeployed) _character.Deploy();
                    OnProcedureComplete();
                    break;
                }

                // (C) action 분기 (D5, D6) — 전체 jumpType 공통
                bool isMainOff = mapped.action == "MainParaOff";
                if (!isMainOff)
                {
                    Debug.Log($"[StateManager] PullSubCord — action='{mapped.action}' (≠MainParaOff), 메인 Deploy 후 ApplyContingency");
                    if (!_character.isParaDeployed) _character.Deploy();
                }
                else
                {
                    Debug.Log($"[StateManager] PullSubCord — action=MainParaOff, 메인 미전개 의도. MarkParaDeployedSuppressed (Landing 가드 비활성)");
                    _character.MarkParaDeployedSuppressed();
                }

                Debug.Log($"[StateManager] PullSubCord — malfunctionId={malfunctionId} → ApplyContingency 위임");

                // 이전 절차/우발상황 결과 잔재 차단 — WaitProcedureCompleteFromContingency 가 _lastContingencyResult 로만 OnSuccess 판단
                _isSuccess = false;
                _lastContingencyResult = false;

                // 우발상황 흐름 위임 — UI(ShowContingencyOverlay) / 효과(ApplyContingencyHardware) / 자동실패(WaitForContingencyDuration) 모두 ApplyContingency 가 처리
                ApplyContingency(mapped);

                // Sd2 — 절차 측 timeLimit 만료 시 자동 reserve 전개 (G_d-1 PlayMode 시나리오)
                UIManager.Inst.AddFailAction(() =>
                {
                    Debug.Log("[StateManager] LineTwist failCondition.Time 발화 — 자동 실패 → reserve 자동 전개");
                    _isSuccess = false;
                    if (_timeRoutine != null) StopCoroutine(_timeRoutine);

                    if (_character?.paraCtrl != null)
                    {
                        if (!_character.paraCtrl.isSubPara)
                            _character.paraCtrl.DeploySubPara();
                        _character.paraCtrl.EndLineTwistProcedure();
                    }
                    OnProcedureComplete();
                });

                // 우발상황 종료 시점에 절차도 완료 처리 (CompleteContingency 가 _activeContingencyId = "" 로 리셋)
                _timeRoutine = StartCoroutine(WaitProcedureCompleteFromContingency());
                break;
            }
        }
    }

    private void StandUp()
    {
        AresHardwareService.Inst.SetEvent(AresEvent.None);
        _cameraController = FindAnyObjectByType<CameraController>();
        _cameraController.MoveToPoint(CamPos.InAirPlane);
        _character = FindAnyObjectByType<PlayCharacter>();
        _character?.Stand();
        Debug.Log("<color=cyan>[StateManager] 서기");
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

            // 문 앞 줄 세우기 - 더미 모델 뒤에 배치
            var participantManager = ParticipantManager.Inst;
            if (participantManager != null)
            {
                Transform doorExitPoint = _cameraController.GetExitPoint();
                if (doorExitPoint != null)
                {
                    participantManager.LineupAtDoor(doorExitPoint, 1.0f); // 1m 간격
                }
                else
                {
                    Debug.LogWarning("[StateManager] 문(Exit) 위치를 찾을 수 없어 줄 세우기를 실행하지 못했습니다.");
                }
            }
        }

        var duration = float.TryParse(_currentProcedure.duration, out var v) ? v : 0f;
        yield return new WaitForSeconds(duration);

        OnSuccess();
        OnProcedureComplete();
    }

    /// <summary>
    /// 완료조건이 PullSubCord 일 때 Action — 우발상황 위임 흐름.
    /// case PullSubCord 가 ApplyContingency(mapped) 호출 → _activeContingencyId 세팅됨.
    /// CompleteContingency 가 성공/실패 양쪽 path 에서 _activeContingencyId = "" 로 리셋 →
    /// 본 polling 종료 → 절차도 OnProcedureComplete.
    /// 효과 종료(EndLineTwistProcedure) 와 결과 송신은 CompleteContingency 가 단일 책임.
    /// </summary>
    private IEnumerator WaitProcedureCompleteFromContingency()
    {
        // case PullSubCord 진입 시 ApplyContingency 가 _activeContingencyId 세팅 → CompleteContingency 가 "" 로 리셋
        while (!string.IsNullOrEmpty(_activeContingencyId))
        {
            yield return null;
        }

        // 우발상황 결과를 절차 결과로 전파
        if (_lastContingencyResult) OnSuccess();

        Debug.Log($"[StateManager] WaitProcedureCompleteFromContingency 종료 — 결과={_lastContingencyResult}");
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
        lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f));
        // 이미 메인씬에 있는 경우
        if (SceneManager.GetActiveScene().name == mainSceneName)
        {
            Debug.Log("[StateManager.TakeOff()] 메인씬 로드 됨. 비행기 확인");
            
            // AirPlane이 이미 존재하고 초기화되었는지 확인
            var airplane = FindAnyObjectByType<AirPlane>();
            if (airplane != null && airplane.GetCurrentRouteIndex() >= 0)
            {
                // 이미 초기화 완료된 경우 바로 처리
                Debug.Log("[StateManager.TakeOff()] AirPlane이 이미 초기화됨. 바로 Point 체크 시작");
            }
            else
            {
                // 아직 초기화되지 않은 경우 이벤트 대기
                Debug.Log("[StateManager.TakeOff()] AirPlane 초기화 대기");
            }
            return;
        }
        
        // 씬 전환 시작을 교관에게 알림
        if (_wsDBClient != null)
        {
            _wsDBClient.SendCurSceneState(SceneState.SceneLoading);
            Debug.Log("[StateManager] 씬 전환 시작 신호 전송 (SceneLoading)");
        }
        
        // 현재는 페이드 아웃 후 씬 넘기는걸로 처리
        fadeCtrl = FindAnyObjectByType<FadeController>(FindObjectsInactive.Include);
        fadeCtrl.Init(FadeDir.Out, () =>
        {
            LoadSceneWithCleanup(mainSceneName);
        });
    }

    /// <summary>
    /// 비행기 이륙 완료 처리
    /// 메인씬이 로드가 완료되면 TakeOff를 완료처리한다.
    /// </summary>
    private void TakeOffComplete()
    {
        Debug.Log("비행기 이륙 완료");
        
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

        int targetIndex = _pendingSkipTargetIndex;
        var targetProcedure = DataManager.Inst.GetProcedureList()[targetIndex];

        // 1. 중간 절차들의 필수 동작 실행
        var skippedProcedures = DataManager.Inst.GetProceduresBetween(0, targetIndex);
        ExecuteEssentialActions(skippedProcedures);

        // 2. 비행기 위치를 target 절차의 시작 지점으로 1회 동기화 + 출발
        SyncAirplaneForSkip(targetProcedure.id);
        if (_airPlane != null) AirPlaneDeparture();

        // 3. 스킵 완료 → 플래그/펜딩 해제 후 target 진입
        _pendingSkipTargetIndex = -1;
        if (_pendingContingency != null)
        {
            var pending = _pendingContingency;
            _pendingContingency = null;
            if (_contingencyQueueExpireCoroutine != null)
            {
                StopCoroutine(_contingencyQueueExpireCoroutine);
                _contingencyQueueExpireCoroutine = null;
            }
            Debug.Log($"[StateManager] 큐잉 우발상황({pending.id}) 적용 (CompleteSkipAfterSceneLoad)");
            _isSkipPending = false;
            ApplyContingency(pending);
        }
        else
        {
            _isSkipPending = false;
        }
        _procedureIndex = targetIndex;
        SetProcedure(targetIndex);

        // 스킵 = 절차 완료 상태로 점프. 햇빛은 항상 OpenDoorEnd(0). ThreeMinutes preset(-10)이 덮어쓰는 것 방지.
        volumeCtrl?.ApplyPreset("OpenDoorEnd", 0f);

        Debug.Log($"<color=green>[StateManager] 스킵 완료: {targetProcedure.stepName}</color>");
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
        if (_jumpRoutine != null) StopCoroutine(_jumpRoutine);
        _jumpRoutine = StartCoroutine(TempJumpDelay());
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
    /// Deploy 애니메이션 종료(EndDeployParachute)가 현재 절차 완료 트리거인지 여부.
    /// HAHO/HALO FreeFall(PullCord) 전용 — Deploy가 절차 완료 신호.
    /// STANDARD InAir 자동 Deploy 가드 / PullSubCord 동기 Deploy / ForceMainParachute 등에서는 false.
    /// </summary>
    public bool IsDeployProcedureCompleter()
    {
        return _currentProcedure != null
            && _currentProcedure.stepName == "FreeFall"
            && _currentProcedure.completeCondition == CompleteCondition.PullCord;
    }

    /// <summary>
    /// 절차 완료 이벤트
    /// </summary>
    public void OnProcedureComplete()
    {
        Debug.Log(_currentProcedure.stepName + ":" + "완료");

        // GoJump(Fall) 절차가 외부 성공신호로 끝났는데 아직 점프 안 한 상태면 Jump() 강제 호출.
        // (_jumpRoutine이 CleanPrevEvents에서 stop되므로 5초 timeout 자동 점프가 발화하지 않음 → 비행기 안에서 낙하산만 펴지는 증상 방지)
        if (_currentProcedure != null
            && _currentProcedure.completeCondition == CompleteCondition.Fall
            && _character != null
            && _character.paraCtrl != null
            && !_character.paraCtrl.isJumpStart)
        {
            Debug.Log("[StateManager] GoJump 외부 종료 - Jump() 강제 실행");
            _character.Jump();
            _cameraController?.OnJumpNoiseCam();
        }

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
    /// 우발상황 수신 (WS_DB_Client → StateManager_New)
    /// </summary>
    public void ReceiveContingency(Contingency c)
    {
        if (c == null)
        {
            Debug.LogWarning("[StateManager] ReceiveContingency: null contingency 무시");
            return;
        }

        if (_currentTimeline != null && c.timelineId != _currentTimeline.timelineID)
        {
            Debug.LogWarning($"[StateManager] 우발상황({c.id}) timelineId 불일치 — c.timelineId={c.timelineId}, current={_currentTimeline.timelineID}");
        }
        if (_currentProcedure != null && c.procedureId != _currentProcedure.id)
        {
            Debug.LogWarning($"[StateManager] 우발상황({c.id}) procedureId 불일치 — c.procedureId={c.procedureId}, current={_currentProcedure.id}");
        }

        if (string.IsNullOrEmpty(_activeContingencyId) == false)
        {
            Debug.LogWarning($"[StateManager] 활성 중({_activeContingencyId}) — 새 우발상황({c.id}) 무시");
            return;
        }

        if (_isSkipPending)
        {
            if (_pendingContingency != null)
            {
                Debug.LogWarning($"[StateManager] 큐잉 슬롯 점유 중({_pendingContingency.id}) — 새 우발상황({c.id}) 무시");
                return;
            }
            Debug.Log($"[StateManager] 스킵 진행 중 — 우발상황({c.id}) 큐잉");
            _pendingContingency = c;
            _contingencyQueueExpireCoroutine = StartCoroutine(ExpireQueuedContingency(c.id));
            return;
        }

        ApplyContingency(c);
    }

    private void ApplyContingency(Contingency c)
    {
        _activeContingencyId = c.id;

        // 2단계 완료 추적 초기화 — action 은 MainParaOff 면 자동 충족, MainParaCut 이면 학생 입력 대기
        _expectedAction = ParseContingencyAction(c.action);
        _actionPerformed = (_expectedAction == ContingencyAction.MainParaOff);
        _conditionMet = false;
        Debug.Log($"[StateManager] ApplyContingency — id={c.id}, expectedAction={_expectedAction}, actionPerformed(init)={_actionPerformed}");

        ApplyContingencyHardware(c);
        UIManager.Inst.ShowContingencyOverlay(c);
        if (_contingencyDurationCoroutine != null) StopCoroutine(_contingencyDurationCoroutine);
        _contingencyDurationCoroutine = StartCoroutine(WaitForContingencyDuration(c));
        Debug.Log($"[StateManager] 우발상황({c.id}) 활성 — duration={c.duration}");
    }

    private static ContingencyAction ParseContingencyAction(string action)
    {
        if (string.IsNullOrEmpty(action)) return ContingencyAction.None;
        if (action == "MainParaOff") return ContingencyAction.MainParaOff;
        if (action == "MainParaCut") return ContingencyAction.MainParaCut;
        return ContingencyAction.None;
    }

    private void CompleteContingency(Contingency c, bool success)
    {
        _activeContingencyId = "";   // ★ 1순위 절대 유지 — W-pre6 race 회피
        Debug.Log($"[StateManager] CompleteContingency entry — _activeContingencyId cleared, c.id={c.id}, success={success}");

        // ★ 우발상황 효과 종료 — LineTwist 는 yaw 이관 포함 EndLineTwistProcedure, 나머지는 EndContingencyEffects
        if (_character?.paraCtrl != null)
        {
            // 실패 path 자동 reserve 전개 (모든 우발상황 공통) — 성공 path 는 이미 isSubPara=true
            if (!success && !_character.paraCtrl.isSubPara)
            {
                Debug.Log("[StateManager] CompleteContingency — 실패 path 자동 reserve 전개");
                _character.paraCtrl.DeploySubPara();
            }

            if (c.id.EndsWith("_LineTwist"))
            {
                _character.paraCtrl.EndLineTwistProcedure();
            }
            else
            {
                _character.paraCtrl.EndContingencyEffects();
            }
        }

        if (_contingencyDurationCoroutine != null)
        {
            StopCoroutine(_contingencyDurationCoroutine);
            _contingencyDurationCoroutine = null;
        }
        UIManager.Inst.HideContingencyOverlay(success);
        UIManager.Inst.AddResult(c.evaluationId, success ? "성공" : "실패");
        _wsDBClient.SendSituationResultData(new SituationResultData() { resultData = success });

        // 절차 위임 path (case PullSubCord) 에서 OnProcedureComplete 시 _isSuccess 판단에 사용
        _lastContingencyResult = success;

        Debug.Log($"[StateManager] 우발상황({c.id}) 종료 — {(success ? "성공" : "실패")}");
    }

    /// <summary>
    /// CSV 4컬럼(leftCtrLine/rightCtrLine/dropSpeed/action) → AresHardwareParagliderController 효과 매핑.
    /// LineTwist 만 회전 효과 + dropBonus 55f 하드코딩(D4=a) — BeginLineTwistProcedure.
    /// 나머지 11종 → BeginContingencyEffects(per-side / dropBonus / needCutaway).
    /// 2단계 완료 도입 후: action(MainParaCut) 자동 실행 제거 — 학생 F11/chestTrigger 입력 대기.
    /// </summary>
    private void ApplyContingencyHardware(Contingency c)
    {
        bool leftOn  = (c.leftCtrLine  == "ON");
        bool rightOn = (c.rightCtrLine == "ON");
        float dropBonus = float.TryParse(c.dropSpeed, out var v) ? v : 0f;
        // 2단계 완료: 컷어웨이는 학생 입력으로만 실행. chestTrigger/조종줄 차단/dropSpeed 만 세팅.
        bool needCutaway = false;

        Debug.Log($"[StateManager] ApplyContingencyHardware — c.id={c.id}, leftOn={leftOn}, rightOn={rightOn}, dropBonus={dropBonus}, needCutaway={needCutaway} (학생 입력 대기)");

        if (_character?.paraCtrl == null)
        {
            // 진단 (임시) — _character 상태 + 씬 내 PlayCharacter 인스턴스 추적
            var allChars = UnityEngine.Object.FindObjectsByType<PlayCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            string charsInfo = "";
            foreach (var c2 in allChars)
            {
                charsInfo += $"[id={c2.GetInstanceID()}, active={c2.gameObject.activeInHierarchy}, paraCtrl={(c2.paraCtrl != null ? c2.paraCtrl.GetInstanceID().ToString() : "null")}] ";
            }
            Debug.LogWarning($"[StateManager] ApplyContingencyHardware — _character.paraCtrl null. _character={(_character != null ? _character.GetInstanceID().ToString() : "null")}, 씬 내 PlayCharacter 총 {allChars.Length}개: {charsInfo}");
            return;
        }

        // LineTwist — 회전 효과 + dropBonus 55f 오버라이드 (D4=a 기존 동작 보존, CSV dropSpeed=10 무시)
        if (c.stepName == "LineTwist")
        {
            int direction = UnityEngine.Random.Range(0, 2) == 0 ? -1 : +1;
            _character.paraCtrl.BeginLineTwistProcedure(direction, 55f);
        }
        else
        {
            _character.paraCtrl.BeginContingencyEffects(leftOn, rightOn, dropBonus, needCutaway);
        }
    }

    /// <summary>
    /// 우발상황 1단계 — 학생 즉시 조치(action) 완료 hook.
    /// paraCtrl.CutawayMainPara() 또는 향후 다른 action 발화 path 에서 호출.
    /// 일치하지 않는 action 은 무시 (e.g., MainParaOff 활성 중 MainParaCut 입력).
    /// </summary>
    public void OnContingencyActionPerformed(ContingencyAction action)
    {
        if (string.IsNullOrEmpty(_activeContingencyId)) return;
        if (action != _expectedAction)
        {
            Debug.LogWarning($"[StateManager] 입력 action({action}) ≠ 활성 우발상황 expectedAction({_expectedAction}) — 무시");
            return;
        }
        if (_actionPerformed) return;   // idempotent
        _actionPerformed = true;
        Debug.Log($"[StateManager] Contingency action 완료 — {action}");
        TryCompleteIfReady();
    }

    /// <summary>
    /// 우발상황 2단계 — 해결 조건(completeCondition) 충족 hook.
    /// paraCtrl.DeploySubPara() 등에서 호출.
    /// 순서 강제: _actionPerformed=false 면 거부 (이중 안전망 — paraCtrl 입력 가드와 함께 작동).
    /// </summary>
    public void OnContingencyConditionMet(ContingencyCompleteCondition condition)
    {
        if (string.IsNullOrEmpty(_activeContingencyId)) return;

        var active = DataManager.Inst.GetContingency(_activeContingencyId);
        if (active == null) return;

        if (active.completeCondition != condition)
        {
            Debug.LogWarning($"[StateManager] 감지 조건({condition}) ≠ 활성 우발상황 조건({active.completeCondition}) — 무시");
            return;
        }

        if (!_actionPerformed)
        {
            Debug.LogWarning($"[StateManager] condition({condition}) 무시 — action({_expectedAction}) 선행 필요");
            return;
        }

        if (_conditionMet) return;   // idempotent
        _conditionMet = true;
        Debug.Log($"[StateManager] Contingency condition 충족 — {condition}");
        TryCompleteIfReady();
    }

    /// <summary>
    /// 2단계(action + condition) 모두 충족 시 우발상황 성공 완료.
    /// </summary>
    private void TryCompleteIfReady()
    {
        if (!_actionPerformed || !_conditionMet) return;
        var active = DataManager.Inst.GetContingency(_activeContingencyId);
        if (active == null) return;
        Debug.Log($"[StateManager] TryCompleteIfReady — 2단계 충족, 성공 완료 ({active.id})");
        CompleteContingency(active, success: true);
    }

    /// <summary>
    /// 우발상황 duration 만료 코루틴 (실패 종료).
    /// </summary>
    private IEnumerator WaitForContingencyDuration(Contingency c)
    {
        var seconds = int.TryParse(c.duration, out var v) ? v : 10;
        yield return new WaitForSeconds(seconds);
        Debug.Log($"[StateManager] 우발상황({c.id}) duration({seconds}s) 만료 — 실패 종료");
        CompleteContingency(c, success: false);
        _contingencyDurationCoroutine = null;
    }

    /// <summary>
    /// 큐잉된 우발상황 20초 만료 폐기 (설계서 §9.2).
    /// </summary>
    private IEnumerator ExpireQueuedContingency(string queuedId)
    {
        yield return new WaitForSeconds(20f);
        if (_pendingContingency != null && _pendingContingency.id == queuedId)
        {
            Debug.LogWarning($"[StateManager] 큐잉 우발상황({queuedId}) 20초 만료 — 폐기");
            _pendingContingency = null;
        }
        _contingencyQueueExpireCoroutine = null;
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
    /// <summary>
    /// [리팩토링] 비행기 출발 - 명령 큐 방식
    ///
    /// 기존 방식:
    ///   SetWaitingMode(false) + OnAirPlaneRoutePointReached 이벤트 등록
    ///
    /// 새로운 방식:
    ///   Continue 명령 추가 (대기 해제)
    /// </summary>
    private void AirPlaneDeparture()
    {
        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();

        Debug.Log("[StateManager] 비행기 출발 명령");

        var command = new AirplaneCommand(
            AirplaneCommandType.Continue,
            -1,  // 목표 없음
            null // 완료 콜백 없음 (단순 대기 해제)
        );

        _airPlane.EnqueueCommand(command);
    }
    
    // ============================================================
    // [삭제] 기존 이벤트 핸들러 메서드들
    // ============================================================
    // OnAirPlaneRoutePointReached: 비행기 도착 시 대기 처리
    // → 명령 큐 방식에서는 AirPlane이 자체적으로 대기 처리
    //
    // PointProcedureComplete: 포인트 절차 완료 처리
    // → StartPointProcedure로 대체 (명령 큐 방식)
    // ============================================================

    /// <summary>
    /// [리팩토링] Point 절차 시작 - 명령 큐 방식
    ///
    ///
    /// 새로운 방식:
    /// 1. 3가지 케이스를 명확히 구분
    /// 2. 각 케이스마다 적절한 명령 생성
    /// 3. 명령 완료 시 콜백으로 절차 완료 처리
    /// </summary>
    private void StartPointProcedure()
    {
        // skipAircraftPosition이 NONE이면 처리 불가
        if (_currentProcedure.skipAircraftPosition == "NONE")
        {
            Debug.LogWarning($"[StateManager] {_currentProcedure.stepName}은 skipAircraftPosition이 NONE입니다.");
            return;
        }

        if (_airPlane == null) _airPlane = FindAnyObjectByType<AirPlane>();

        // 인덱스 계산
        int scenarioRouteIndex = DataManager.Inst.GetScenarioRouteIndex();
        int offset = int.Parse(_currentProcedure.skipAircraftPosition);
        int departureIdx = scenarioRouteIndex + offset;      // 출발 지점 (대기)
        int targetIdx = departureIdx + 1;                    // 목표 지점 (완료)
        int currentIdx = _airPlane.GetCurrentRouteIndex();

        Debug.Log($"[StateManager] Point 절차 시작: {_currentProcedure.stepName}");
        Debug.Log($"  - 현재 위치: {currentIdx}");
        Debug.Log($"  - 출발 지점: {departureIdx} (대기)");
        Debug.Log($"  - 목표 지점: {targetIdx} (완료)");

        // ========================================
        // 케이스 1: 이미 목표를 지나침
        // ========================================
        // 기존 문제: PointProcedureComplete에서 return만 하고 완료 처리 안 함
        // 해결: 즉시 절차 완료 처리
        if (currentIdx >= targetIdx)
        {
            Debug.Log($"[StateManager] 이미 목표 지점을 지나침 ({currentIdx} >= {targetIdx}) - 즉시 완료");
            OnProcedureComplete();
            return;
        }

        // ========================================
        // 케이스 2: 출발 지점과 목표 지점 사이
        // ========================================
        // 비행기가 이미 출발 지점을 지나서 목표로 가는 중
        // → MoveTo 명령: 목표까지 이동 (대기 없음)
        if (currentIdx >= departureIdx && currentIdx < targetIdx)
        {
            Debug.Log($"[StateManager] 출발 지점과 목표 지점 사이 - 목표까지 이동");

            // 목표 지점까지 이동 명령
            var command = new AirplaneCommand(
                AirplaneCommandType.MoveTo,
                targetIdx,
                (success) =>
                {
                    if (success && !_isSkipPending)
                    {
                        Debug.Log($"[StateManager] 목표 지점({targetIdx}) 도달 - 절차 완료");
                        OnProcedureComplete();
                    }
                }
            );

            _airPlane.EnqueueCommand(command);
            return;
        }

        // ========================================
        // 케이스 3: 출발 지점 전
        // ========================================
        // 비행기가 아직 출발 지점 전에 있음
        // → MoveToAndWait 명령: 출발 지점까지 이동 후 대기
        //   (교관 신호를 OnInstructorSignal로 받음)
        if (currentIdx < departureIdx)
        {
            Debug.Log($"[StateManager] 출발 지점 전 - 목표 지점까지 이동 후 대기");

            // 1단계: 목표 지점까지 이동 후 대기
            var moveToWaitCommand = new AirplaneCommand(
                AirplaneCommandType.MoveToAndWait,
                targetIdx,
                (success) =>  // ✅ 출발 지점 도달 시 절차 완료 처리
                {
                    if (success && !_isSkipPending)
                    {
                        Debug.Log($"[StateManager] 출발 지점({departureIdx}) 도달 - 절차 완료");
                        OnProcedureComplete();  // 교관에게 완료 신호 전송
                    }
                }
            );

            _airPlane.EnqueueCommand(moveToWaitCommand);
        }
    }

    /// <summary>
    /// [신규] 교관 신호 수신 (외부에서 호출)
    ///
    /// 기존에는 이벤트 핸들러로 처리했지만,
    /// 이제는 명령 큐 방식으로 목표 지점까지 이동 명령 추가
    /// </summary>
    public void OnInstructorSignal()
    {
        if (_currentProcedure == null) return;
        if (_currentProcedure.completeCondition != CompleteCondition.Point) return;

        int scenarioRouteIndex = DataManager.Inst.GetScenarioRouteIndex();
        int offset = int.Parse(_currentProcedure.skipAircraftPosition);
        int targetIdx = scenarioRouteIndex + offset + 1;

        Debug.Log($"[StateManager] 교관 신호 수신 - 목표 지점({targetIdx})으로 이동 시작");

        // 목표 지점까지 이동 명령
        var command = new AirplaneCommand(
            AirplaneCommandType.MoveTo,
            targetIdx,
            (success) =>
            {
                if (success && !_isSkipPending)
                {
                    Debug.Log($"[StateManager] 목표 지점({targetIdx}) 도달 - 절차 완료");
                    OnProcedureComplete();
                }
            }
        );

        _airPlane.EnqueueCommand(command);
    }

    /// <summary>
    /// [리팩토링] 스킵 시 비행기 위치 동기화 - 명령 큐 방식
    ///
    /// 기존 방식:
    ///   MoveToPointImmediately 직접 호출
    ///
    /// 새로운 방식:
    ///   TeleportTo 명령 추가 (즉시 텔레포트)
    ///
    /// [버그 수정]
    ///   문제: TeleportTo와 StartPointProcedure가 동시에 실행되어 Race Condition 발생
    ///        → StartPointProcedure가 이동 전 위치를 읽어서 잘못된 Case 선택
    ///        → MoveToAndWait 명령이 중복 추가되어 무한 대기
    ///
    ///   해결: TeleportTo의 onComplete 콜백에서 StartPointProcedure 재호출
    ///        → TeleportTo 완료 후 업데이트된 위치로 올바른 Case 선택
    ///        → Race Condition 완전 해결
    /// </summary>
    private void SyncAirplaneForSkip(string targetProcedureId)
    {
        var targetProcedure = DataManager.Inst.GetProcedure(targetProcedureId);
        if (targetProcedure == null)
        {
            Debug.LogError("[StateManager] 목표 절차가 null 입니다.");
            return;
        }
        
        if (targetProcedure.skipAircraftPosition == "NONE")
        {
            Debug.LogWarning($"{targetProcedure.stepName}은 스킵 포인트가 없습니다.");
            return;
        }

        if (_airPlane == null)
        {
            Debug.LogWarning("[StateManager] airPlane이 null입니다.");
            _airPlane = FindAnyObjectByType<AirPlane>();
        }

        if (_airPlane != null)
        {
            int scenarioRouteIndex = DataManager.Inst.GetScenarioRouteIndex();
            var targetIndex = scenarioRouteIndex + Int32.Parse(targetProcedure.skipAircraftPosition);

            Debug.Log("[StateManager] 비행기 위치 동기화 시작");
            Debug.Log($"[StateManager] 스킵: {targetIndex}번 지점으로 텔레포트");

            var command = new AirplaneCommand(
                AirplaneCommandType.TeleportTo,
                targetIndex,
                (success) =>
                {
                    if (success)
                    {
                        Debug.Log($"[StateManager] TeleportTo 완료. currentIdx={_airPlane.GetCurrentRouteIndex()}");
                    }
                }
            );

            _airPlane.EnqueueCommand(command);
        }
    }
    
    /// <summary>
    /// 스킵 시 중간 절차들의 필수 동작(장비, 자세, 평가 기록) 실행.
    /// 비행기 위치 동기화/출발은 호출자가 target에 대해 1회만 처리.
    /// </summary>
    private void ExecuteEssentialActions(List<Procedure> skippedProcedures)
    {
        Debug.Log($"<color=cyan>[StateManager] {skippedProcedures.Count}개 절차의 필수 동작 실행</color>");

        foreach (var procedure in skippedProcedures)
        {
            Debug.Log($"<color=cyan>[StateManager] 스킵 절차: {procedure.stepName} (ID: {procedure.id})</color>");

            switch (procedure.completeCondition)
            {
                case CompleteCondition.Item:
                    if (_wearingSet == null) _wearingSet = FindAnyObjectByType<WearingSet>();
                    _wearingSet?.ForceEquip(procedure.item);
                    Debug.Log($"<color=cyan>[StateManager] 강제 착용: {procedure.item}");
                    break;

                case CompleteCondition.SitDown:
                    AresHardwareService.Inst.SetEvent(AresEvent.SitDown);
                    _character = FindAnyObjectByType<PlayCharacter>();
                    _character?.SkipSitDown();
                    Debug.Log("<color=cyan>[StateManager] 강제 앉기 처리");
                    break;

                case CompleteCondition.StandUp:
                    AresHardwareService.Inst.SetEvent(AresEvent.None);
                    _cameraController = FindAnyObjectByType<CameraController>();
                    _cameraController.MoveToPoint(CamPos.InAirPlane);
                    _character?.SkipStand();
                    Debug.Log("<color=cyan>[StateManager] 강제 서기 처리");
                    break;

                case CompleteCondition.Point:
                    if (procedure.stepName == "StandUp")
                    {
                        // 스킵 시에는 PlayCharacter.Stand()의 TempDelayAni 코루틴이 stale로 살아남아
                        // 후속 절차에서 OnSuccessAction을 잘못 발화시키는 버그가 있음 → SkipStand 사용
                        AresHardwareService.Inst.SetEvent(AresEvent.None);
                        if (_cameraController == null) _cameraController = FindAnyObjectByType<CameraController>();
                        _cameraController?.MoveToPoint(CamPos.InAirPlane);
                        if (_character == null) _character = FindAnyObjectByType<PlayCharacter>();
                        _character?.SkipStand();
                        Debug.Log("<color=cyan>[StateManager] 스킵 서기 (코루틴 없이)");
                    }
                    Debug.Log($"<color=cyan>[StateManager] Point 절차 스킵: {procedure.stepName}");
                    if (_airPlane != null) _airPlane.OpenDoorImmediately();
                    Debug.Log("<color=cyan>[StateManager] 문 여는 애니메이션 스킵");
                    break;

                case CompleteCondition.SceneLoading:
                    break;

                default:
                    Debug.Log($"<color=cyan>[StateManager] 일반 절차 스킵: {procedure.stepName}");
                    break;
            }

            UIManager.Inst.AddResult(procedure.evaluationId, "스킵");
        }
    }
}
