using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using WebSocketSharp;
using System.Collections;
using static WS_DB_Client;
using Palmmedia.ReportGenerator.Core.Reporting.Builders;

public class WS_DB_Client : MonoBehaviour
{
    // 싱글톤 인스턴스
    private static WS_DB_Client _instance;
    public static WS_DB_Client Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<WS_DB_Client>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("WS_DB_Client");
                    _instance = go.AddComponent<WS_DB_Client>();
                }
            }
            return _instance;
        }
    }
    public WebSocket ws;
    private TaskCompletionSource<List<AddInstructorEval>> addInstructorEvalTcs;
    private TaskCompletionSource<List<AircraftData>> aircraftTcs;
    private TaskCompletionSource<List<DropSystemData>> dropSystemTcs;
    private TaskCompletionSource<List<DropVisualSignalsData>> dropVisualSignalsTcs;
    private TaskCompletionSource<List<EvaluationParticipantListData>> evaluationParticipantListDataTcs;
    private TaskCompletionSource<List<InstructorData>> instructorTcs;
    private TaskCompletionSource<List<RouteData>> routeTcs;
    private TaskCompletionSource<List<ScenarioData>> scenarioTcs;
    private TaskCompletionSource<List<ParachuteData>> parachuteTcs;
    private TaskCompletionSource<List<ParticipantData>> participantTcs;
    private TaskCompletionSource<List<ParticipantGroupData>> participantGroupTcs;
    private TaskCompletionSource<List<WeatherData>> weatherTcs;
    private TaskCompletionSource<List<FailureGrantHistoryData>> failureHistoryTcs;
    private TaskCompletionSource<List<EvaluationListData>> evaluationListTcs;
    private TaskCompletionSource<List<MapData>> mapTcs;
    private TaskCompletionSource<List<ToParticipantData>> toParaticipantTcs;
    private TaskCompletionSource<List<ToInstructorData>> toInstructorTcs;

    private TaskCompletionSource<bool> insertTcs;
    private TaskCompletionSource<bool> updateTcs;
    private TaskCompletionSource<bool> deleteTcs;

    public string WebSoeketServer;
    public string WebSocketID;
    public TrainingState CurTrainingState;
    
    [Header("시뮬레이터 설정")]
    [Tooltip("인스펙터에서 설정하면 CSV 파일을 무시합니다. 비워두면 CSV에서 읽어옵니다.")]
    [SerializeField] private string inspectorSimulatorNumber = "";

    private StateManager_New _stateManager;
    public CurrentParticipantData CurParticipantData;
    private List<CurrentParticipantData> ConnectedParticipantList;
    
    // 시뮬레이터 번호 및 참가자 데이터
    private string simulatorNumber = "1";
    private SetParticipantData participantData;
    private ConnectStateData connectStateData;
    private string evaluationIndex = "";

    void Awake()
    {
        // 싱글톤 설정
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        try
        {
            // CD_SimInfo.csv에서 시뮬레이터 번호 읽기
            LoadSimulatorNumber();
            
            // 참가자 데이터 초기화
            CurParticipantData = new CurrentParticipantData();
            ConnectedParticipantList = new List<CurrentParticipantData>();
            
            // 테스트를 위한 기본값 설정 (실제 운영시에는 교관에서 전송해야 함)
            CurParticipantData.id = $"TEST_SIM{simulatorNumber}";
            CurParticipantData.name = $"테스트사용자{simulatorNumber}";
            Debug.LogWarning($"[WS_DB_Client] 테스트용 기본값 설정 - ID: {CurParticipantData.id}, 이름: {CurParticipantData.name}");
            
            // 웹소켓 연결 시도
            ConnectToServer();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_DB_Client] 초기화 도중 에러: {ex.Message}");
        }

        _stateManager = FindAnyObjectByType<StateManager_New>();
    }
    void OnDestroy()
    {
        // 싱글톤 인스턴스가 파괴될 때만 WebSocket을 닫음
        if (_instance == this)
        {
            if (ws != null && ws.IsAlive) 
            {
                ws.Close();
                Debug.Log("[WS_DB_Client] WebSocket 연결을 종료했습니다.");
            }
            StopAllCoroutines();
            _instance = null;
        }
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 시뮬레이터 정보를 CD_SimInfo csv 파일에서 로드한다.
    /// </summary>
    private void LoadSimulatorNumber()
    {
        // 인스펙터에서 설정된 값이 있으면 우선 사용
        if (!string.IsNullOrEmpty(inspectorSimulatorNumber))
        {
            simulatorNumber = inspectorSimulatorNumber;
            Debug.Log($"[WS_DB_Client] 인스펙터에서 설정된 시뮬레이터 번호 사용: {simulatorNumber}");
            return;
        }
        
        // 인스펙터에 설정이 없으면 CSV에서 읽기
        try
        {
            var csvData = CsvParser.ReadWithHeader(DataName.CD_SimInfo, out var header);
            var simInfoData = CsvParser.UpdateData<SimInfo>(csvData, header);
            
            if (simInfoData is { Count: > 0 })
            {
                simulatorNumber = simInfoData[0].simNo;
                WebSocketID = simInfoData[0].clientId;
                Debug.Log($"[WS_DB_Client] CSV에서 시뮬레이터 번호 로드 완료: {simulatorNumber}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WS_DB_Client] CD_SimInfo.csv 파일 로드 실패: {e.Message}");
            simulatorNumber = "999"; // 기본값 사용
            WebSocketID = "9999";
        }
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 시뮬레이터 상태를 초기화 후 2초마다 상태정보를 교관에게 전송하는 매서드 실행
    /// 현재는 시뮬레이터가 켜지면 무조건 true 설정 추후에 하드웨어 상태에 따라서 설정해야함.
    /// </summary>
    private void InitializeConnectStateData()
    {
        connectStateData = new ConnectStateData
        {
            type = "connectStateData",
            simNo = simulatorNumber,
            hwState = "true",
            subPara = "true",
            readyState = "true"
        };
        
        // 시뮬레이터 시작 시 연결 상태 전송
        SendConnectStateDate(connectStateData);
        
        // 2초마다 상태 정보 전송하는 코루틴 시작
        StartCoroutine(SendConnectStateRoutine());
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 2초마다 상태정보를 전송하는 매서드
    /// </summary>
    private IEnumerator SendConnectStateRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);
            
            if (ws is { IsAlive: true } && connectStateData != null)
            {
                SendConnectStateDate(connectStateData);
            }
        }
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 외부에서 상태정보를 설정하는 매서드 추가
    /// </summary>
    public void UpdateHardwareState(string state)
    {
        if (connectStateData != null)
        {
            connectStateData.hwState = state;
            SendConnectStateDate(connectStateData);
        }
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 외부에서 상태정보를 설정하는 매서드 추가
    /// </summary>
    public void UpdateSubParachuteState(string state)
    {
        if (connectStateData != null)
        {
            connectStateData.subPara = state;
            SendConnectStateDate(connectStateData);
        }
    }
    
    /// <summary>
    /// 류성희 20250723 추가
    /// 외부에서 상태정보를 설정하는 매서드 추가
    /// </summary>
    public void UpdateReadyState(string state)
    {
        if (connectStateData != null)
        {
            connectStateData.readyState = state;
            SendConnectStateDate(connectStateData);
        }
    }
    
    public string GetSimulatorNumber()
    {
        return simulatorNumber;
    }
    
    public SetParticipantData GetParticipantData()
    {
        return participantData;
    }
    
    public string GetEvaluationIndex()
    {
        return evaluationIndex;
    }

    async void InsertTest()
    {
        //EvaluationListData data = new EvaluationListData();
        //data.rating_history_id = "Test";
        //data.scenario_id = "Test";
        //data.instructor_id = "test";
        //data.participant_id = "test";
        //data.open_parachute = true;
        //data.fan_out = false;
        //data.open_lifebelt = true;
        //data.landing_position = 1311;
        //data.safty_landing = false;
        //data.landing_velocity = 12;
        //data.look_beware = true;
        //data.check_position = false;
        //data.control_parachute = false;
        //data.drop_leader = true;
        //data.emergency_landing = false;
        //data.description_key = "KOR";
        //data.create_time = DateTime.Now;
        //data.modify_time = DateTime.Now;

        //bool Ret = await InsertDataAsync(TableInfo.rating_history, (object)data);

        //Debug.Log(Ret);
    }

    public List<CurrentParticipantData> GetCurParticipantList()
    {
        return ConnectedParticipantList;
    }
    public CurrentParticipantData GetCurParticipantbyId(string participantId)
    {
        foreach (var item in ConnectedParticipantList)
            if (item.id == participantId)
                return item;

        return null;
    }

    #region 웹소켓 통신 기능
    
    /// <summary>
    /// 웹소켓 연결 상태를 확인합니다.
    /// </summary>
    public bool IsConnected()
    {
        if (ws == null)
        {
            Debug.Log("[WS_DB_Client] WebSocket 객체가 null입니다.");
            return false;
        }
        
        bool isAlive = ws.IsAlive;
        var readyState = ws.ReadyState;
        
        Debug.Log($"[WS_DB_Client] WebSocket 상태 - IsAlive: {isAlive}, ReadyState: {readyState}");
        
        // Open이거나 Connecting 상태면 연결 중인 것으로 간주
        return ws.ReadyState == WebSocketState.Open || ws.ReadyState == WebSocketState.Connecting;
    }
    
    /// <summary>
    /// 웹소켓에 연결을 시도합니다. 이미 연결되어 있으면 false를 반환합니다.
    /// </summary>
    public bool ConnectToServer()
    {
        Debug.Log("[WS_DB_Client] 웹소켓 연결 시작");
        
        if (IsConnected())
        {
            Debug.LogWarning("[WS_DB_Client] 이미 WebSocket이 연결되어 있습니다.");
            return false;
        }
        
        try
        {
            if (ws == null)
            {
                ws = new WebSocket(WebSoeketServer);
                ws.OnOpen += (s, e) => { Debug.Log("WebSocket 연결 열림"); };
                ws.OnError += (s, e) => { Debug.LogError("WebSocket 에러: " + e.Message); };
                ws.OnClose += (s, e) => { Debug.Log("WebSocket 연결 종료"); };
                ws.OnMessage += OnServerMessage;
            }
            
            ws.Connect();
            SendConnect();
            
            // ConnectStateData 초기화 (연결 성공 후)
            InitializeConnectStateData();
            
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_DB_Client] 연결 실패: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 웹소켓 연결을 끊습니다.
    /// </summary>
    public void DisconnectFromServer()
    {
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
            Debug.Log("[WS_DB_Client] WebSocket 연결을 종료했습니다.");
        }
    }
    
    public void SendConnect()
    {
        var conn = new ConnectSend
        {
            type = "connectEvent",
            connector = "participant",
            connectorId = WebSocketID,
        };

        ws.Send(JsonUtility.ToJson(conn));
    }

    public void SendCurState(TrainingState state)
    {
        var curSend = new CurParticipantDataSend
        {
            type = "curTraningState",
            simNo = simulatorNumber,
            trainingState = state
        };

        SafeSend(curSend, $"TrainingState({state})");
    }
    public void SendCurSceneState(SceneState state)
    {
        var curSend = new CurSceneStateSend
        {
            type = "curSceneState",
            simNo = simulatorNumber,
            sceneState = state
        };

        SafeSend(curSend, $"SceneState({state})");
    }

    /// <summary>
    /// 타임라인, 시나리오 데이터 전송
    /// </summary>
    /// <param name="isTimeline"> 타임라인 여부 false면 시나리오</param>
    /// <param name="simNo">시뮬레이터 번호</param>
    /// <param name="participantId">교육생 id</param>
    /// <param name="command"></param>
    /// <param name="commaldFlag"></param>
    public void SendCommand(CommandType commandType, string participantId, string command, bool commaldFlag)
    {
        string SendType = "";

        switch(commandType)
        {
            case CommandType.Timeline: SendType = "timelineCommand"; break;
            case CommandType.Scenario: SendType = "scenarioCommand"; break;
            case CommandType.ProcedureCommand: SendType = "procedureCommand"; break;
            case CommandType.ProcedureResp: SendType = "procedureResp"; break;
        }

        var sendCmd = new CommandData
        {
            type = SendType,
            simNo = simulatorNumber,
            participantId = participantId,
            command = command,
            commandFlag = commaldFlag
        };

        ws.Send(JsonUtility.ToJson(sendCmd));
    }

    public void SendTimelineComplete(string participantId, string command, bool commaldFlag)
    {
        var sendCmd = new CommandData
        {
            type = "timelineComplete",
            simNo = simulatorNumber,
            participantId = participantId,
            command = command,
            commandFlag = commaldFlag
        };

        Debug.Log("보냈음");
        ws.Send(JsonUtility.ToJson(sendCmd));
    }

    public void SendCommandResponse(bool isTimeline, CurrentParticipantData data)
    {
        var sendCmdResp = new CommandData
        {
            type = isTimeline ? "timelineResp" : "ScenarioResp",
            simNo = data.simNo,
            participantId = data.id,
            command = isTimeline ? data.timelineId : data.scenarioId,
            commandFlag = isTimeline ? data.isSuccess : data.isSetting
        };

        ws.Send(JsonUtility.ToJson(sendCmdResp));
    }

    public void SendTraningStateResponse(bool isTimeline, CurrentParticipantData data)
    {
        var stateResponse = new TraningStateResponse
        {
            type = "trainingStateResp",
            simNo = data.simNo,     // 류성희 20250724 simNo 추가
            participantId = data.id,
            trainingState = data.trainingState,
        };

        Debug.Log("훈련상태를 정상적으로 변경 후 교관에 알려줌");
        ws.Send(JsonUtility.ToJson(stateResponse));
    }
    
    /// <summary>
    /// json 데이터를 주고받을때 문자열 버퍼를 객체를 미리 만들어서 재활용
    /// </summary>
    private readonly StringBuilder  _jsonSB  = new(128);// 문자열 버퍼

    public void SendMonitoringData(MonitoringData data)
    {
        data.type = "monitoringData";
        _jsonSB.Clear();
        _jsonSB.Append(JsonUtility.ToJson(data, false));
        //Debug.Log($"MonitoringData ::: participantId: {data.participantId}, altitude : {data.altitude}, distance : {data.distance}, forwardSpeed : {data.forwardSpeed}, fallingSpeed : {data.fallingSpeed}");
        ws.Send(_jsonSB.ToString());
    }
    
    /// <summary>
    /// 관절 회전 데이터 전송 메서드
    /// </summary>
    public void SendJointRotationData(JointRotation data)
    {
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogWarning("[WS_DB_Client] WebSocket이 연결되어 있지 않습니다.");
            return;
        }
        
        data.type = "jointRotation";
        _jsonSB.Clear();
        //_jsonSB.Append(JsonUtility.ToJson(data, false));
        _jsonSB.Append(JsonConvert.SerializeObject(data));

        
        ws.Send(_jsonSB.ToString());
        
        #if UNITY_EDITOR
        // Debug.Log($"[WS_DB_Client] JointRotation 데이터 전송: {_jsonSB.Length} bytes");
        #endif
    }
    
    /// 시뮬레이터 상태 전송 매서드
    public void SendConnectStateDate(ConnectStateData data)
    {
        data.type = "connectStateData";
        ws.Send(JsonUtility.ToJson(data));
    }
    
    // 
    public void SendSetParticipantDate(SetParticipantData data)
    {
        data.type = "setParticipantData";
        ws.Send(JsonUtility.ToJson(data));
    }
    public void SendEvalIndexDate(EvalIndexData data)
    {
        data.type = "evalIndexData";
        ws.Send(JsonUtility.ToJson(data));
    }

    public void SendTimelineError(CurrentParticipantData data, string errorCode = "")
    {
        var errorMessage = new TimelineErrorMessage
        {
            type = "timelineError",
            simNo = data.simNo,
            participantId = data.id,
            timelineId = data.timelineId,
            errorCode = errorCode,
        };

        ws.Send(JsonUtility.ToJson(errorMessage));
    }
    
    /// <summary>
    /// 교육절차 전송 함수(교육생 -> 교관)
    /// </summary>
    /// <param name="data"></param>
    public void SendProcedureData(ProcedureData data)
    {
        data.type = "procedureData";
        data.simNo = simulatorNumber;
        
        SafeSend(data, $"ProcedureData({data.procedureId})");
    }
    /// <summary>
    /// 우발상황 부여 함수(교관 -> 교육생)
    /// </summary>
    /// <param name="data"></param>
    public void SendSetSituationData(SetSituationData data)
    {
        data.type = "setSituationData";
        ws.Send(JsonUtility.ToJson(data));
    }

    public void SendSituationResultData(SituationResultData data)
    {
        data.type = "situationResultData";
        ws.Send(JsonUtility.ToJson(data));
    }

    public void SendJointData(JointRotation data)
    {
        data.type = "jointRotation";
        ws.Send(JsonUtility.ToJson(data));
    }
    
    //강제 이탈 신호 전송
    public void SendForcedDeparture(SetForcedDeparture data)
    {
        data.type = "setForcedDeparture";
        ws.Send(JsonUtility.ToJson(data));
    }
    #endregion

    #region Safe Send Helper
    /// <summary>
    /// WebSocket 연결 상태를 확인하고 안전하게 데이터를 전송하는 헬퍼 메서드
    /// </summary>
    /// <param name="data">전송할 데이터 (JSON으로 변환 가능한 객체)</param>
    /// <param name="dataType">데이터 타입 설명 (로깅용)</param>
    /// <returns>전송 성공 여부</returns>
    private bool SafeSend(object data, string dataType = "Data")
    {
        // WebSocket 연결 상태 확인
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogError($"[WS_DB_Client] WebSocket이 연결되어 있지 않습니다. ReadyState: {ws?.ReadyState}");
            Debug.Log("[WS_DB_Client] 재연결을 시도합니다...");
            
            // 재연결 시도
            ConnectToServer();
            
            // 재연결 후 잠시 대기 (비동기 처리를 위해 코루틴 사용이 더 좋지만 현재 구조상 Thread.Sleep 사용)
            System.Threading.Thread.Sleep(500);
            
            // 재연결 후에도 연결되지 않았다면 데이터를 전송하지 않음
            if (ws == null || ws.ReadyState != WebSocketState.Open)
            {
                Debug.LogError($"[WS_DB_Client] 재연결 실패. {dataType}를 전송할 수 없습니다.");
                return false;
            }
        }
        
        try
        {
            string jsonData = JsonUtility.ToJson(data);
            ws.Send(jsonData);
            Debug.Log($"[WS_DB_Client] {dataType} 전송 성공");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WS_DB_Client] {dataType} 전송 실패: {e.Message}");
            return false;
        }
    }
    #endregion

    #region Get from DB
    // 원하는 SQL을 보내는 함수
    private void SendSelect(TableInfo table, string sql)
    {
        string ClientType = "";

        switch (table)
        {
            case TableInfo.addInstructorEval: ClientType = "selectAddInstructorEval"; break;
            case TableInfo.aircraft: ClientType = "selectAircraftData"; break;
            case TableInfo.dropSystem: ClientType = "selectDropSystemData"; break;
            case TableInfo.instructor: ClientType = "selectInstructorData"; break;
            case TableInfo.dropVisualSignals: ClientType = "selectDropVisualSignalsData"; break;
            case TableInfo.route: ClientType = "selectRouteData"; break;
            case TableInfo.scenario: ClientType = "selectScenarioData"; break;
            case TableInfo.parachute: ClientType = "selectParachuteData"; break;
            case TableInfo.participant: ClientType = "selectParticipantData"; break;
            case TableInfo.participantGroup: ClientType = "selectParticipantGroupData"; break;
            case TableInfo.weather: ClientType = "selectWeatherData"; break;
            case TableInfo.failure_grant_history: ClientType = "selectFailureGrantHistoryData"; break;
            case TableInfo.evaluationList: ClientType = "selectEvaluationList"; break;
            case TableInfo.map: ClientType = "selectMapData"; break;
            case TableInfo.toInstructor: ClientType = "selecttoInstructor"; break;
            case TableInfo.toParticipant: ClientType = "selecttoParticipant"; break;
            case TableInfo.evaluationParticipantList: ClientType = "selectEvaluationParticipantListData"; break;
            default: ClientType = ""; break;
        }

        var req = new ClientRequest
        {
            type = ClientType,
            sql = sql
        };

        ws.Send(JsonUtility.ToJson(req));
    }


    public Task<List<AddInstructorEval>> GetAddInstructorEvalID()
    {
        addInstructorEvalTcs = new TaskCompletionSource<List<AddInstructorEval>>();
        SendSelect(TableInfo.addInstructorEval, "select id from vw_addinstructoreval;");

        return addInstructorEvalTcs.Task;
    }
    public Task<List<AddInstructorEval>> GetAddInstructorEvalData(string whereConst = "")
    {
        // 이전에 대기 중이던 Task가 있다면 취소
        addInstructorEvalTcs = new TaskCompletionSource<List<AddInstructorEval>>();

        // 요청 타입과 SQL은 기존 SendSelect 로 보내되,
        // TableInfo.aircraft → "AirCraftData" 타입으로
        SendSelect(TableInfo.addInstructorEval, string.Format("select * from vw_addinstructoreval {0};", whereConst));

        return addInstructorEvalTcs.Task;
    }
    public Task<List<AddInstructorEval>> GetAddInstructorEvalDatabyID(string addInstructorId)
    {
        addInstructorEvalTcs = new TaskCompletionSource<List<AddInstructorEval>>();
        SendSelect(TableInfo.addInstructorEval, string.Format("select * from vw_addinstructoreval where id = '{0}';", addInstructorId));

        return addInstructorEvalTcs.Task;
    }

    public Task<List<AircraftData>> GetAircraftID()
    {
        aircraftTcs = new TaskCompletionSource<List<AircraftData>>();
        SendSelect(TableInfo.aircraft, "select id from vw_aircraft;");

        return aircraftTcs.Task;
    }
    public Task<List<AircraftData>> GetAircraftData(string whereConst = "")
    {
        // 이전에 대기 중이던 Task가 있다면 취소
        aircraftTcs = new TaskCompletionSource<List<AircraftData>>();

        // 요청 타입과 SQL은 기존 SendSelect 로 보내되,
        // TableInfo.aircraft → "AirCraftData" 타입으로
        SendSelect(TableInfo.aircraft, string.Format("select * from vw_aircraft {0};", whereConst));

        return aircraftTcs.Task;
    }
    public Task<List<AircraftData>> GetAircraftDatabyID(string aricraftID)
    {
        aircraftTcs = new TaskCompletionSource<List<AircraftData>>();
        SendSelect(TableInfo.aircraft, string.Format("select * froM vm_aircraft where id = '{0}';", aricraftID));

        return aircraftTcs.Task;
    }

    public Task<List<DropSystemData>> GetDropSystemID()
    {
        dropSystemTcs = new TaskCompletionSource<List<DropSystemData>>();
        SendSelect(TableInfo.dropSystem, "select id from dropsystem;");

        return dropSystemTcs.Task;
    }
    public Task<List<DropSystemData>> GetDropSystemData(string whereConst = "")
    {
        dropSystemTcs = new TaskCompletionSource<List<DropSystemData>>();
        SendSelect(TableInfo.dropSystem, string.Format("select * from vw_dropsystem {0};", whereConst));

        return dropSystemTcs.Task;
    }
    public Task<List<DropSystemData>> GetDropSystemDatabyID(string dropSystemID)
    {
        dropSystemTcs = new TaskCompletionSource<List<DropSystemData>>();
        SendSelect(TableInfo.dropSystem, string.Format("select * from vw_dropsystem where id = '{0}';", dropSystemID));

        return dropSystemTcs.Task;
    }

    public Task<List<DropVisualSignalsData>> GetDroupVisualSignalsID()
    {
        dropVisualSignalsTcs = new TaskCompletionSource<List<DropVisualSignalsData>>();
        SendSelect(TableInfo.dropVisualSignals, "select id from vm_dropvisualsignals;");

        return dropVisualSignalsTcs.Task;
    }
    public Task<List<DropVisualSignalsData>> GetDroupVisualSignalsData(string whereConst = "")
    {
        dropVisualSignalsTcs = new TaskCompletionSource<List<DropVisualSignalsData>>();
        SendSelect(TableInfo.dropVisualSignals, string.Format("select * from vw_dropvisualsignals {0};", whereConst));

        return dropVisualSignalsTcs.Task;
    }
    public Task<List<DropVisualSignalsData>> GetDroupVisualSignalsbyID(string markerID)
    {
        dropVisualSignalsTcs = new TaskCompletionSource<List<DropVisualSignalsData>>();
        SendSelect(TableInfo.dropVisualSignals, string.Format("select * from vw_dropvisualsignals where id = '{0}';", markerID));

        return dropVisualSignalsTcs.Task;
    }

    public Task<List<EvaluationListData>> GetEvaluationListID()
    {
        evaluationListTcs = new TaskCompletionSource<List<EvaluationListData>>();
        SendSelect(TableInfo.evaluationList, "select id from vw_evaluationlist;");

        return evaluationListTcs.Task;
    }
    public Task<List<EvaluationListData>> GetEvaluationListData(string whereConst = "")
    {
        evaluationListTcs = new TaskCompletionSource<List<EvaluationListData>>();
        SendSelect(TableInfo.evaluationList, string.Format("select * from vw_evaluationlist {0};", whereConst));

        return evaluationListTcs.Task;
    }
    public Task<List<EvaluationListData>> GetEvaluationListDatabyID(string dropSystemID)
    {
        evaluationListTcs = new TaskCompletionSource<List<EvaluationListData>>();
        SendSelect(TableInfo.evaluationList, string.Format("select * from vw_evaluationlist where id = '{0}';", dropSystemID));

        return evaluationListTcs.Task;
    }
    public Task<List<EvaluationListData>> GetEvaluationListData(DateTime start, DateTime end)
    {
        evaluationListTcs = new TaskCompletionSource<List<EvaluationListData>>();
        SendSelect(TableInfo.evaluationList,
                   string.Format("select * from vw_evaluationlist where createTime between '{0}' and {1};",
                   start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss")));

        return evaluationListTcs.Task;
    }

    public Task<List<EvaluationParticipantListData>> GetEvaluationParticipantListID()
    {
        evaluationParticipantListDataTcs = new TaskCompletionSource<List<EvaluationParticipantListData>>();
        SendSelect(TableInfo.evaluationParticipantList, "select id from vw_evaluationparticipantlist;");

        return evaluationParticipantListDataTcs.Task;
    }
    public Task<List<EvaluationParticipantListData>> GetEvaluationParticipantListData(string whereConst = "")
    {
        evaluationParticipantListDataTcs = new TaskCompletionSource<List<EvaluationParticipantListData>>();
        SendSelect(TableInfo.evaluationParticipantList, string.Format("select * from vw_evaluationparticipantlist {0};", whereConst));

        return evaluationParticipantListDataTcs.Task;
    }
    public Task<List<EvaluationParticipantListData>> GetEvaluationParticipantListDatabyID(string dropSystemID)
    {
        evaluationParticipantListDataTcs = new TaskCompletionSource<List<EvaluationParticipantListData>>();
        SendSelect(TableInfo.evaluationParticipantList, string.Format("select * from vw_evaluationparticipantlist where id = '{0}';", dropSystemID));

        return evaluationParticipantListDataTcs.Task;
    }

    public Task<List<InstructorData>> GetInstructorID()
    {
        instructorTcs = new TaskCompletionSource<List<InstructorData>>();
        SendSelect(TableInfo.instructor, "select id from instructor;");

        return instructorTcs.Task;
    }
    public Task<List<InstructorData>> GetInstructorData(string whereConst = "")
    {
        instructorTcs = new TaskCompletionSource<List<InstructorData>>();
        SendSelect(TableInfo.instructor, string.Format("select * from vw_instructor {0};", whereConst));

        return instructorTcs.Task;
    }
    public Task<List<InstructorData>> GetInstructorDatabyID(string instructorID)
    {
        instructorTcs = new TaskCompletionSource<List<InstructorData>>();
        SendSelect(TableInfo.instructor, string.Format("select * from vw_instructor where id = '{0}';", instructorID));

        return instructorTcs.Task;
    }

    public Task<List<MapData>> GetMapID()
    {
        mapTcs = new TaskCompletionSource<List<MapData>>();
        SendSelect(TableInfo.map, "select id from map;");

        return mapTcs.Task;
    }
    public Task<List<MapData>> GetMapData(string whereConst = "")
    {
        mapTcs = new TaskCompletionSource<List<MapData>>();
        SendSelect(TableInfo.map, string.Format("select * from vw_map {0};", whereConst));

        return mapTcs.Task;
    }
    public Task<List<MapData>> GetMapDatabyID(string instructorID)
    {
        mapTcs = new TaskCompletionSource<List<MapData>>();
        SendSelect(TableInfo.map, string.Format("select * from vw_map where id = '{0}';", instructorID));

        return mapTcs.Task;
    }

    public Task<List<ParachuteData>> GetParachuteID()
    {
        parachuteTcs = new TaskCompletionSource<List<ParachuteData>>();
        SendSelect(TableInfo.parachute, "select id from parachute;");

        return parachuteTcs.Task;
    }
    public Task<List<ParachuteData>> GetParachuteData(string whereConst = "")
    {
        parachuteTcs = new TaskCompletionSource<List<ParachuteData>>();
        SendSelect(TableInfo.parachute, string.Format("select * from vw_parachute {0};", whereConst));

        return parachuteTcs.Task;
    }
    public Task<List<ParachuteData>> GetParachuteDatabyID(string parachuteID)
    {
        parachuteTcs = new TaskCompletionSource<List<ParachuteData>>();
        SendSelect(TableInfo.parachute, string.Format("select * from vw_parachute where id = '{0}';", parachuteID));

        return parachuteTcs.Task;
    }

    public Task<List<ParticipantData>> GetParticipantID()
    {
        participantTcs = new TaskCompletionSource<List<ParticipantData>>();
        SendSelect(TableInfo.participant, "select id from participant;");

        return participantTcs.Task;
    }
    public Task<List<ParticipantData>> GetParticipantData(string whereConst = "")
    {
        participantTcs = new TaskCompletionSource<List<ParticipantData>>();
        SendSelect(TableInfo.participant, string.Format("select * from vw_participant {0};", whereConst));

        return participantTcs.Task;
    }
    public Task<List<ParticipantData>> GetParticipantDatabyID(string participantID)
    {
        participantTcs = new TaskCompletionSource<List<ParticipantData>>();
        SendSelect(TableInfo.participant, string.Format("select * from vw_participant where id = '{0}';", participantID));

        return participantTcs.Task;
    }

    public Task<List<ParticipantGroupData>> GetParticipantGroupID()
    {
        participantGroupTcs = new TaskCompletionSource<List<ParticipantGroupData>>();
        SendSelect(TableInfo.participantGroup, "select id from participantgroup;");

        return participantGroupTcs.Task;
    }
    public Task<List<ParticipantGroupData>> GetParticipantGroupData(string whereConst = "")
    {
        participantGroupTcs = new TaskCompletionSource<List<ParticipantGroupData>>();
        SendSelect(TableInfo.participantGroup, string.Format("select * from vw_participantgroup {0};", whereConst));

        return participantGroupTcs.Task;
    }
    public Task<List<ParticipantGroupData>> GetParticipantGroupDatabyID(string participantGroupID)
    {
        participantGroupTcs = new TaskCompletionSource<List<ParticipantGroupData>>();
        SendSelect(TableInfo.participantGroup, string.Format("select * from vw_participantgroup where id = '{0}';", participantGroupID));

        return participantGroupTcs.Task;
    }

    public Task<List<RouteData>> GetRouteID()
    {
        routeTcs = new TaskCompletionSource<List<RouteData>>();
        SendSelect(TableInfo.route, "select id from route;");

        return routeTcs.Task;
    }
    public Task<List<RouteData>> GetRouteData(string whereConst = "")
    {
        routeTcs = new TaskCompletionSource<List<RouteData>>();
        SendSelect(TableInfo.route, string.Format("select * from vw_route {0};", whereConst));

        return routeTcs.Task;
    }
    public Task<List<RouteData>> GetRouteDatabyID(string routeID)
    {
        routeTcs = new TaskCompletionSource<List<RouteData>>();
        SendSelect(TableInfo.route, string.Format("select * from vw_route where id = '{0}';", routeID));

        return routeTcs.Task;
    }

    public Task<List<ScenarioData>> GetScenarioID()
    {
        scenarioTcs = new TaskCompletionSource<List<ScenarioData>>();
        SendSelect(TableInfo.scenario, "select id from scenario;");

        return scenarioTcs.Task;
    }
    public Task<List<ScenarioData>> GetScenarioData(string whereConst = "")
    {
        scenarioTcs = new TaskCompletionSource<List<ScenarioData>>();
        SendSelect(TableInfo.scenario, string.Format("select * from vw_scenario {0};", whereConst));

        return scenarioTcs.Task;
    }
    public Task<List<ScenarioData>> GetScenarioDatabyID(string scenarioID)
    {
        scenarioTcs = new TaskCompletionSource<List<ScenarioData>>();
        SendSelect(TableInfo.scenario, string.Format("select * from vw_scenario where id = '{0}';", scenarioID));

        return scenarioTcs.Task;
    }

    public Task<List<WeatherData>> GetWeatherID()
    {
        weatherTcs = new TaskCompletionSource<List<WeatherData>>();
        SendSelect(TableInfo.weather, "select id from weather;");

        return weatherTcs.Task;
    }
    public Task<List<WeatherData>> GetWeatherData(string whereConst = "")
    {
        weatherTcs = new TaskCompletionSource<List<WeatherData>>();
        SendSelect(TableInfo.weather, string.Format("select * from vw_weather {0};", whereConst));

        return weatherTcs.Task;
    }
    public Task<List<WeatherData>> GetWeatherDatabyID(string weatherID)
    {
        weatherTcs = new TaskCompletionSource<List<WeatherData>>();
        SendSelect(TableInfo.weather, string.Format("select * from vw_weather where id = '{0}';", weatherID));

        return weatherTcs.Task;
    }

    public Task<List<FailureGrantHistoryData>> GetFailureGrantHistoryData(string whereConst = "")
    {
        failureHistoryTcs = new TaskCompletionSource<List<FailureGrantHistoryData>>();
        SendSelect(TableInfo.failure_grant_history, string.Format("select * from vw_failuregranthistory {0};", whereConst));

        return failureHistoryTcs.Task;
    }

    public Task<List<ToInstructorData>> GetToInstructorData()
    {
        toInstructorTcs = new TaskCompletionSource<List<ToInstructorData>>();
        SendSelect(TableInfo.toInstructor, "select * from toinstructor;");

        return toInstructorTcs.Task;
    }
    public Task<List<ToInstructorData>> GetToInstructorDatabySwNo(int swNo)
    {
        toInstructorTcs = new TaskCompletionSource<List<ToInstructorData>>();
        SendSelect(TableInfo.toInstructor, string.Format("select * from toinstructor where swNo = {0};", swNo));

        return toInstructorTcs.Task;
    }

    public Task<List<ToParticipantData>> GetToParticipantData()
    {
        toParaticipantTcs = new TaskCompletionSource<List<ToParticipantData>>();
        SendSelect(TableInfo.toParticipant, "select * from toparticipant;");

        return toParaticipantTcs.Task;
    }
    public Task<List<ToParticipantData>> GetToParticipantDatabySwNo(int swNo)
    {
        toParaticipantTcs = new TaskCompletionSource<List<ToParticipantData>>();
        SendSelect(TableInfo.toParticipant, string.Format("select * from toparticipant where swNo = {0};", swNo));

        return toParaticipantTcs.Task;
    }

    public Task<List<FailureGrantHistoryData>> GetFailureGrantHistoryData(DateTime start, DateTime end)
    {
        failureHistoryTcs = new TaskCompletionSource<List<FailureGrantHistoryData>>();
        SendSelect(TableInfo.failure_grant_history,
                   string.Format("select * from vw_failuregranthistory where createTime between '{0}' and {1};",
                   start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss")));

        return failureHistoryTcs.Task;
    }
    #endregion

    #region Set to DB
    public Task<bool> InsertDataAsync(TableInfo table, object item)
    {
        insertTcs = new TaskCompletionSource<bool>();
        string makeSql = "";

        switch (table)
        {
            case TableInfo.addInstructorEval: makeSql = MakeInsertAddInstructorEvalMsg((AddInstructorEval)item); break;
            case TableInfo.aircraft: makeSql = MakeInsertAircraftMsg((AircraftData)item); break;
            case TableInfo.dropSystem: makeSql = MakeInsertDropSystemMsg((DropSystemData)item); break;
            case TableInfo.dropVisualSignals: makeSql = MakeInsertDropVisualSignalsMsg((DropVisualSignalsData)item); break;
            case TableInfo.evaluationList: makeSql = MakeInsertEvaluationListMsg((EvaluationListData)item); break;
            case TableInfo.evaluationParticipantList: makeSql = MakeInsertEvaluationParticipantListMsg((EvaluationParticipantListData)item); break;
            case TableInfo.instructor: makeSql = MakeInsertInstructorMsg((InstructorData)item); break;
            case TableInfo.map: makeSql = MakeInsertMapMsg((MapData)item); break;
            case TableInfo.parachute: makeSql = MakeInsertParachuteMsg((ParachuteData)item); break;
            case TableInfo.participant: makeSql = MakeInsertParticipantMsg((ParticipantData)item); break;
            case TableInfo.participantGroup: makeSql = MakeInsertParticipantGroupMsg((ParticipantGroupData)item); break;
            case TableInfo.route: makeSql = MakeInsertRouteMsg((RouteData)item); break;
            case TableInfo.scenario: makeSql = MakeInsertScenarioMsg((ScenarioData)item); break;
            case TableInfo.weather: makeSql = MakeInsertWeatherMsg((WeatherData)item); break;
            case TableInfo.failure_grant_history: makeSql = MakeInsertFailureGrantHistoryMsg((FailureGrantHistoryData)item); break;
            default: makeSql = ""; break;
        }

        // SQL 문자열 생성 (필드명·값 부분은 DTO에 맞게 조정)
        string sql = $"insert into {table.ToString()} {makeSql}";

        // INSERT 전용 타입으로 요청
        var req = new ClientRequest
        {
            type = "insertData",
            sql = sql
        };
        ws.Send(JsonUtility.ToJson(req));

        return insertTcs.Task;
    }
    public Task<bool> UpdateDataAsync(TableInfo table, object item)
    {
        updateTcs = new TaskCompletionSource<bool>();
        string makeSql = "";

        switch (table)
        {
            case TableInfo.addInstructorEval: makeSql = MakeUpdateAddInstructorEvalMsg((AddInstructorEval)item); break;
            case TableInfo.aircraft: makeSql = MakeUpdateAircraftMsg((AircraftData)item); break;
            case TableInfo.dropSystem: makeSql = MakeUpdateDropSystemMsg((DropSystemData)item); break;
            case TableInfo.dropVisualSignals: makeSql = MakeUpdateDropVisualSignalsMsg((DropVisualSignalsData)item); break;
            case TableInfo.evaluationList: makeSql = MakeUpdateEvaluationListMsg((EvaluationListData)item); break;
            case TableInfo.evaluationParticipantList: makeSql = MakeUpdateEvaluationParticipantListMsg((EvaluationParticipantListData)item); break;
            case TableInfo.instructor: makeSql = MakeUpdateInstructorMsg((InstructorData)item); break;
            case TableInfo.map: makeSql = MakeUpdateMapMsg((MapData)item); break;
            case TableInfo.parachute: makeSql = MakeUpdateParachuteMsg((ParachuteData)item); break;
            case TableInfo.participant: makeSql = MakeUpdateParticipantMsg((ParticipantData)item); break;
            case TableInfo.participantGroup: makeSql = MakeUpdateParticipantGroupMsg((ParticipantGroupData)item); break;
            case TableInfo.route: makeSql = MakeUpdateRouteMsg((RouteData)item); break;
            case TableInfo.scenario: makeSql = MakeUpdateScenarioMsg((ScenarioData)item); break;
            case TableInfo.weather: makeSql = MakeUpdateWeatherMsg((WeatherData)item); break;
            case TableInfo.toParticipant: makeSql = MakeUpdateToParticipantMsg((ToParticipantData)item); break;
            case TableInfo.toInstructor: makeSql = MakeUpdateToInstructorMsg((ToInstructorData)item); break;
            default: makeSql = ""; break;
        }

        // SQL 문자열 생성 (필드명·값 부분은 DTO에 맞게 조정)
        string sql = $"update {table.ToString()} {makeSql}";

        // UPDATE 전용 타입으로 요청
        var req = new ClientRequest
        {
            type = "updateData",
            sql = sql
        };
        ws.Send(JsonUtility.ToJson(req));
        Debug.Log(sql);
        return updateTcs.Task;
    }
    public Task<bool> DeleteDataAsync(TableInfo table, string id)
    {
        deleteTcs = new TaskCompletionSource<bool>();

        // SQL 문자열 생성 (필드명·값 부분은 DTO에 맞게 조정)
        string sql = $"update {table.ToString()} set isUse = false where id = '{id}'";

        // UPDATE 전용 타입으로 요청
        var req = new ClientRequest
        {
            type = "updateData",
            sql = sql
        };

        ws.Send(JsonUtility.ToJson(req));
        Debug.Log(sql);
        return deleteTcs.Task;
    }

    private string MakeInsertAddInstructorEvalMsg(AddInstructorEval addInstructorEval)
    {
        string ret = $"(id, evalParticipantId, evalName, evalResult, evalScore, isUse) values " +
                     $"('{addInstructorEval.id}', '{addInstructorEval.evalParticipantId}', '{addInstructorEval.evalName}', " +
                     $"'{addInstructorEval.evalResult}', '{addInstructorEval.evalScore}', {addInstructorEval.isUse})";

        return ret;
    }
    private string MakeUpdateAddInstructorEvalMsg(AddInstructorEval addInstructorEval)
    {
        string ret = $"set evalParticipantId = '{addInstructorEval.evalParticipantId}', evalName = '{addInstructorEval.evalName}', " +
                     $"evalResult = '{addInstructorEval.evalResult}', evalScore = '{addInstructorEval.evalScore}', " +
                     $"isUse = {addInstructorEval.isUse} " +
                     $"where id = '{addInstructorEval.id}'";

        return ret;
    }

    private string MakeInsertAircraftMsg(AircraftData aircraftData)
    {
        string ret = $"(id, name, nameKey, icon, prefabName, exitPosition, " +
                     $"type, minOperationalAltitude, maxOperationalAltitude, defaultSpeedKnots, " +
                     $"minAllowedSpeedKnots, maxAllowedSpeedKnots" +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{aircraftData.id}', '{aircraftData.name}', '{aircraftData.nameKey}', '{aircraftData.icon}', " +
                     $"'{aircraftData.prefabName}', '{aircraftData.exitPosition}','{aircraftData.type}', " +
                     $"{aircraftData.minOperationalAltitude}, {aircraftData.maxOperationalAltitude}, '{aircraftData.defaultSpeedKnots}'," +
                     $"{aircraftData.minAllowedSpeedKnots}, {aircraftData.maxAllowedSpeedKnots}, '{aircraftData.descriptionKey}'," +
                     $"'{aircraftData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{aircraftData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {aircraftData.isUse})";

        return ret;
    }
    private string MakeUpdateAircraftMsg(AircraftData aircraftData)
    {
        string ret = $"set name = '{aircraftData.name}', nameKey = '{aircraftData.nameKey}', " +
                     $"icon = {aircraftData.icon}, prefabName = {aircraftData.prefabName}, " +
                     $"exitPosition = '{aircraftData.exitPosition}', type = '{aircraftData.type}', " +
                     $"minAllowedSpeedKnots = {aircraftData.minAllowedSpeedKnots}, " +
                     $"maxOperationalAltitude = '{aircraftData.maxOperationalAltitude}', " +
                     $"maxOperationalAltitude = '{aircraftData.defaultSpeedKnots}', " +
                     $"maxOperationalAltitude = '{aircraftData.minAllowedSpeedKnots}', " +
                     $"maxOperationalAltitude = '{aircraftData.maxAllowedSpeedKnots}', " +
                     $"descriptionKey = '{aircraftData.descriptionKey}', " +
                     $"createTime = '{aircraftData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{aircraftData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {aircraftData.isUse} " +
                     $"where id = '{aircraftData.id}'";

        return ret;
    }

    private string MakeInsertDropSystemMsg(DropSystemData dropSystmeData)
    {
        string ret = $"(id, name, nameKey, systemType, markerRequired, marker1, marker2, marker3, " +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{dropSystmeData.id}', '{dropSystmeData.name}', '{dropSystmeData.nameKey}', " +
                     $"'{dropSystmeData.systemType}', {dropSystmeData.markerRequired}, '{dropSystmeData.marker1}', " +
                     $"'{dropSystmeData.marker2}', '{dropSystmeData.marker3}', '{dropSystmeData.descriptionKey}', " +
                     $"'{dropSystmeData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{dropSystmeData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {dropSystmeData.isUse})";

        return ret;
    }
    private string MakeUpdateDropSystemMsg(DropSystemData dropSystmeData)
    {
        string ret = $"set name = '{dropSystmeData.name}', nameKey = '{dropSystmeData.nameKey}', " +
                     $"systemType = '{dropSystmeData.systemType}', markerRequired = {dropSystmeData.markerRequired}, " +
                     $"marker1 = '{dropSystmeData.marker1}', marker2 = '{dropSystmeData.marker2}', marker3 = '{dropSystmeData.marker3}', " +
                     $"descriptionKey = '{dropSystmeData.descriptionKey}', " +
                     $"createTime = '{dropSystmeData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{dropSystmeData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {dropSystmeData.isUse} " +
                     $"where id = '{dropSystmeData.id}'";

        return ret;
    }

    private string MakeInsertDropVisualSignalsMsg(DropVisualSignalsData dropVisualSignalsData)
    {
        string ret = $"(id, name, nameKey, signalType, shape, color, " +
                     $"dayUse, nightUse, usedIn, descriptionKey, " +
                     $"createTime, modifyTime, isUse) values " +
                     $"('{dropVisualSignalsData.id}', '{dropVisualSignalsData.name}', '{dropVisualSignalsData.nameKey}', " +
                     $"'{dropVisualSignalsData.signalType}', '{dropVisualSignalsData.shape}', '{dropVisualSignalsData.color}'," +
                     $"{dropVisualSignalsData.dayUse}, {dropVisualSignalsData.nightUse}, " +
                     $"'{dropVisualSignalsData.usedIn}', '{dropVisualSignalsData.descriptionKey}', " +
                     $"'{dropVisualSignalsData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{dropVisualSignalsData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {dropVisualSignalsData.isUse})";

        return ret;
    }
    private string MakeUpdateDropVisualSignalsMsg(DropVisualSignalsData dropVisualSignalsData)
    {
        string ret = $"set name = '{dropVisualSignalsData.name}', nameKey = '{dropVisualSignalsData.nameKey}', " +
                     $"signalType = '{dropVisualSignalsData.signalType}', shape = '{dropVisualSignalsData.shape}', " +
                     $"color = '{dropVisualSignalsData.color}', dayUse = {dropVisualSignalsData.dayUse}, nightUse = {dropVisualSignalsData.nightUse}, " +
                     $"usedIn = '{dropVisualSignalsData.usedIn}', descriptionKey = '{dropVisualSignalsData.descriptionKey}', " +
                     $"createTime = '{dropVisualSignalsData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{dropVisualSignalsData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {dropVisualSignalsData.isUse} " +
                     $"where id = '{dropVisualSignalsData.id}'";

        return ret;
    }

    private string MakeInsertEvaluationListMsg(EvaluationListData evaluationListData)
    {
        string ret = $"(instructorName, evalParticipantId, jumpType, evalFallTimeResult, evalFallTimeScore, " +
                     $"evalTotalDistanceResult, evalTotalDistanceScore, evalAltimeterOnResult, evalAltimeterOnScore, " +
                     $"evalHelmetOnResult, evalHelmetOnScore, evalOxyMaskResult, evalOxyMaskScore, " +
                     $"evalSitDownCompleteResult, evalSitDownCompleteScore, " +
                     $"evalStandUpCompleteResult, evalStandUpCompleteScore, evalHookUpCompleteResult, evalHookUpCompleteScore, " +
                     $"evalGoJumpCompleteResult, evalGoJumpCompleteScore, evalDeployAltitudeResult, evalDeployAltitudeScore, " +
                     $"evalDeployTargetDistanceResult, evalDeployTargetDistanceScore, evalEventCompleteResult, evalEventCompleteScore, " +
                     $"evalFlareCompleteResult, evalFlareCompleteScore, " +
                     $"evalLandingTypeResult, evalLandingTypeScore, evalTargetDistanceResult, evalTargetDistanceScore, " +
                     $"evalLandingSpeedResult, evalLandingSpeedScore, createTime) values " +
                     $"('{evaluationListData.instructorName}', '{evaluationListData.evalParticipantId}', '{evaluationListData.jumpType}', " +
                     $"'{evaluationListData.evalFallTimeResult}', '{evaluationListData.evalFallTimeScore}', " +
                     $"'{evaluationListData.evalTotalDistanceResult}', '{evaluationListData.evalTotalDistanceScore}', " +
                     $"'{evaluationListData.evalAltimeterOnResult}', '{evaluationListData.evalAltimeterOnScore}', " +
                     $"'{evaluationListData.evalHelmetOnResult}', '{evaluationListData.evalHelmetOnScore}', " +
                     $"'{evaluationListData.evalOxyMaskResult}', '{evaluationListData.evalOxyMaskScore}', " +
                     $"'{evaluationListData.evalSitDownCompleteResult}', '{evaluationListData.evalSitDownCompleteScore}', " +
                     $"'{evaluationListData.evalStandUpCompleteResult}', '{evaluationListData.evalStandUpCompleteScore}', " +
                     $"'{evaluationListData.evalHookUpCompleteResult}', '{evaluationListData.evalHookUpCompleteScore}', " +
                     $"'{evaluationListData.evalGoJumpCompleteResult}', '{evaluationListData.evalGoJumpCompleteScore}', " +
                     $"'{evaluationListData.evalDeployAltitudeResult}', '{evaluationListData.evalDeployAltitudeScore}', " +
                     $"'{evaluationListData.evalDeployTargetDistanceResult}', '{evaluationListData.evalDeployTargetDistanceScore}', " +
                     $"'{evaluationListData.evalEventCompleteResult}', '{evaluationListData.evalEventCompleteScore}', " +
                     $"'{evaluationListData.evalFlareCompleteResult}', '{evaluationListData.evalFlareCompleteScore}', " +
                     $"'{evaluationListData.evalLandingTypeResult}', '{evaluationListData.evalLandingTypeScore}', " +
                     $"'{evaluationListData.evalTargetDistanceResult}', '{evaluationListData.evalTargetDistanceScore}', " +
                     $"'{evaluationListData.evalLandingSpeedResult}', '{evaluationListData.evalLandingSpeedScore}', " +
                     $"'{evaluationListData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}')";

        return ret;
    }
    private string MakeUpdateEvaluationListMsg(EvaluationListData evaluationListData)
    {
        string ret = $"set evalParticipantId = '{evaluationListData.evalParticipantId}', instructorName = '{evaluationListData.instructorName}', jumpType = '{evaluationListData.jumpType}', " +
                     $"evalFallTimeResult = '{evaluationListData.evalFallTimeResult}', evalFallTimeScore = '{evaluationListData.evalFallTimeScore}', " +
                     $"evalTotalDistanceResult = '{evaluationListData.evalTotalDistanceResult}', evalTotalDistanceScore = '{evaluationListData.evalTotalDistanceScore}', " +
                     $"evalAltimeterOnResult = '{evaluationListData.evalAltimeterOnResult}', evalAltimeterOnScore = '{evaluationListData.evalAltimeterOnScore}', " +
                     $"evalHelmetOnResult = '{evaluationListData.evalHelmetOnResult}', evalHelmetOnScore = '{evaluationListData.evalHelmetOnScore}', " +
                     $"evalOxyMaskResult = '{evaluationListData.evalOxyMaskResult}', evalOxyMaskScore = '{evaluationListData.evalOxyMaskScore}', " +
                     $"evalSitDownCompleteResult = '{evaluationListData.evalSitDownCompleteResult}', evalSitDownCompleteScore = '{evaluationListData.evalSitDownCompleteScore}', " +
                     $"evalStandUpCompleteResult = '{evaluationListData.evalStandUpCompleteResult}', evalStandUpCompleteScore = '{evaluationListData.evalStandUpCompleteScore}', " +
                     $"evalHookUpCompleteResult = '{evaluationListData.evalHookUpCompleteResult}', evalHookUpCompleteScore = '{evaluationListData.evalHookUpCompleteScore}', " +
                     $"evalGoJumpCompleteResult = '{evaluationListData.evalGoJumpCompleteResult}', evalGoJumpCompleteScore = '{evaluationListData.evalGoJumpCompleteScore}', " +
                     $"evalDeployAltitudeResult = '{evaluationListData.evalDeployAltitudeResult}', evalDeployAltitudeScore = '{evaluationListData.evalDeployAltitudeScore}', " +
                     $"evalDeployTargetDistanceResult = '{evaluationListData.evalDeployTargetDistanceResult}', evalDeployTargetDistanceScore = '{evaluationListData.evalDeployTargetDistanceScore}', " +
                     $"evalEventCompleteResult = '{evaluationListData.evalEventCompleteResult}', evalEventCompleteScore = '{evaluationListData.evalEventCompleteScore}', " +
                     $"evalFlareCompleteResult = '{evaluationListData.evalFlareCompleteResult}', evalFlareCompleteScore = '{evaluationListData.evalFlareCompleteScore}', " +
                     $"evalLandingTypeResult = '{evaluationListData.evalLandingTypeResult}', evalLandingTypeScore = '{evaluationListData.evalLandingTypeScore}', " +
                     $"evalTargetDistanceResult = '{evaluationListData.evalTargetDistanceResult}', evalTargetDistanceScore = '{evaluationListData.evalTargetDistanceScore}', " +
                     $"evalLandingSpeedResult = '{evaluationListData.evalLandingSpeedResult}', evalLandingSpeedScore = '{evaluationListData.evalLandingSpeedScore}', " +
                     $"createTime = '{evaluationListData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}' " +
                     $"where id = '{evaluationListData.id}'";

        return ret;
    }

    private string MakeInsertEvaluationParticipantListMsg(EvaluationParticipantListData evaluationParticipantListData)
    {
        string ret = $"(id, participantId, participantName, participantNo, participantHeight, participantWeight, " +
                     $"participantGroupName, participantGroupCode, evaluationTime, scenarioName, " +
                     $"parachuteId, scheduleDatetime, mapId, aircraftId, weatherId, windDirction, " +
                     $"windSpeed, dropSystemId, exitPosition, markerId, fogType, allowedSpeedKnots, " +
                     $"operationalAltitude, endOperationalAltitude, autoActiveAltitude, jumpType, createTime) values " +
                     $"('{evaluationParticipantListData.id}', '{evaluationParticipantListData.participantId}', '{evaluationParticipantListData.participantName}', " +
                     $"'{evaluationParticipantListData.participantNo}', {evaluationParticipantListData.participantHeight}, " +
                     $"{evaluationParticipantListData.participantWeight}, '{evaluationParticipantListData.participantGroupName}', " +
                     $"'{evaluationParticipantListData.participantGroupCode}', '{evaluationParticipantListData.evaluationTime}', " +
                     $"'{evaluationParticipantListData.scenarioName}', '{evaluationParticipantListData.parachuteId}', " +
                     $"'{evaluationParticipantListData.scheduleDatetime.ToString("yyyy-MM-dd HH:mm:ss")}', '{evaluationParticipantListData.mapId}', " +
                     $"'{evaluationParticipantListData.aircraftId}', '{evaluationParticipantListData.weatherId}', " +
                     $"'{evaluationParticipantListData.windDirction}', '{evaluationParticipantListData.windSpeed}', " +
                     $"'{evaluationParticipantListData.dropSystemId}', '{evaluationParticipantListData.exitPosition}', " +
                     $"'{evaluationParticipantListData.markerId}', '{evaluationParticipantListData.fogType}', " +
                     $"{evaluationParticipantListData.allowedSpeedKnots}, {evaluationParticipantListData.operationalAltitude}, " +
                     $"{evaluationParticipantListData.endOperationalAltitude}, {evaluationParticipantListData.autoActiveAltitude}, " +
                     $"'{evaluationParticipantListData.jumpType}', '{evaluationParticipantListData.brifingPlace}', " +
                     $"'{evaluationParticipantListData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}')";

        return ret;
    }
    private string MakeUpdateEvaluationParticipantListMsg(EvaluationParticipantListData evaluationParticipantListData)
    {
        string ret = $"set participantName = '{evaluationParticipantListData.participantName}', participantId = '{evaluationParticipantListData.participantId}', " +
                     $"participantNo = '{evaluationParticipantListData.participantNo}', participantHeight = {evaluationParticipantListData.participantHeight}, " +
                     $"participantWeight = {evaluationParticipantListData.participantWeight}, participantGroupName = '{evaluationParticipantListData.participantGroupName}', " +
                     $"participantGroupCode = '{evaluationParticipantListData.participantGroupCode}', evaluationTime = '{evaluationParticipantListData.evaluationTime}', " +
                     $"scenarioName = '{evaluationParticipantListData.scenarioName}', parachuteId = '{evaluationParticipantListData.parachuteId}', " +
                     $"scheduleDatetime = '{evaluationParticipantListData.scheduleDatetime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"mapId = '{evaluationParticipantListData.mapId}', aircraftId = '{evaluationParticipantListData.aircraftId}', " +
                     $"weatherId = '{evaluationParticipantListData.weatherId}', windDirction = '{evaluationParticipantListData.windDirction}', " +
                     $"windSpeed = '{evaluationParticipantListData.windSpeed}', dropSystemId = '{evaluationParticipantListData.dropSystemId}', " +
                     $"exitPosition = '{evaluationParticipantListData.exitPosition}', markerId = '{evaluationParticipantListData.markerId}', " +
                     $"fogType = '{evaluationParticipantListData.fogType}', allowedSpeedKnots = {evaluationParticipantListData.allowedSpeedKnots}, " +
                     $"operationalAltitude = {evaluationParticipantListData.operationalAltitude}, endOperationalAltitude = {evaluationParticipantListData.endOperationalAltitude}, " +
                     $"autoActiveAltitude = {evaluationParticipantListData.autoActiveAltitude}, jumpType = '{evaluationParticipantListData.jumpType}', " +
                     $"brifingPlace = '{evaluationParticipantListData.brifingPlace}', createTime = '{evaluationParticipantListData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}' " +
                     $"where id = '{evaluationParticipantListData.id}'";

        return ret;
    }

    private string MakeInsertInstructorMsg(InstructorData instructorData)
    {
        string ret = $"(id, name, pw, " +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{instructorData.id}', '{instructorData.name}', '{instructorData.pw}', " +
                     $"'{instructorData.descriptionKey}', '{instructorData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{instructorData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {instructorData.isUse})";

        return ret;
    }
    private string MakeUpdateInstructorMsg(InstructorData instructorData)
    {
        string ret = $"set name = '{instructorData.name}', pw = '{instructorData.pw}', " +
                     $"descriptionKey = '{instructorData.descriptionKey}', " +
                     $"createTime = '{instructorData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{instructorData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {instructorData.isUse} " +
                     $"where id = '{instructorData.id}'";

        return ret;
    }

    private string MakeInsertMapMsg(MapData mapData)
    {
        string ret = $"(id, name, nameKey, map2d, map3d, " +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{mapData.id}', '{mapData.name}', '{mapData.nameKey}', '{mapData.map2d}', '{mapData.map3d}', " +
                     $"'{mapData.descriptionKey}', '{mapData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{mapData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {mapData.isUse})";

        return ret;
    }
    private string MakeUpdateMapMsg(MapData mapData)
    {
        string ret = $"set name = '{mapData.name}', nameKey = '{mapData.nameKey}', " +
                     $"map2d = '{mapData.map2d}', map3d = '{mapData.map3d}', " +
                     $"descriptionKey = '{mapData.descriptionKey}', " +
                     $"createTime = '{mapData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{mapData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {mapData.isUse} " +
                     $"where id = '{mapData.id}'";

        return ret;
    }

    private string MakeInsertParachuteMsg(ParachuteData parachuteData)
    {
        string ret = $"(id, name, nameKey, icon, type" +
                     $"minOpenAltitudeFt, maxOpenAltitudeFt, descriptionKey, " +
                     $"createTime, modifyTime, isUse) values " +
                     $"('{parachuteData.id}', '{parachuteData.name}', '{parachuteData.nameKey}', " +
                     $"('{parachuteData.icon}', '{parachuteData.type}', " +
                     $"{parachuteData.minOpenAltitudeFt}, {parachuteData.maxOpenAltitudeFt}, " +
                     $"'{parachuteData.descriptionKey}', '{parachuteData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{parachuteData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {parachuteData.isUse})";

        return ret;
    }
    private string MakeUpdateParachuteMsg(ParachuteData parachuteData)
    {
        string ret = $"set name = '{parachuteData.name}', nameKey = '{parachuteData.nameKey}', " +
                     $"set icon = '{parachuteData.icon}', type = '{parachuteData.type}', " +
                     $"minOpenAltitudeFt = {parachuteData.minOpenAltitudeFt}, maxOpenAltitudeFt = {parachuteData.maxOpenAltitudeFt}, " +
                     $"descriptionKey = '{parachuteData.descriptionKey}', " +
                     $"createTime = '{parachuteData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{parachuteData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {parachuteData.isUse} " +
                     $"where id = '{parachuteData.id}'";

        return ret;
    }

    private string MakeInsertParticipantMsg(ParticipantData participantData)
    {
        string ret = $"(id, name, no, height, weight, groupId, " +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{participantData.id}', '{participantData.name}', '{participantData.no}', {participantData.height}, " +
                     $"{participantData.weight}, '{participantData.groupId}', '{participantData.descriptionKey}', '{participantData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{participantData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {participantData.isUse})";

        return ret;
    }
    private string MakeUpdateParticipantMsg(ParticipantData participantData)
    {
        string ret = $"set name = '{participantData.name}', no = '{participantData.no}', height = {participantData.height}, " +
                     $"weight = {participantData.weight}, groupId = '{participantData.groupId}', descriptionKey = '{participantData.descriptionKey}', " +
                     $"createTime = '{participantData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{participantData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {participantData.isUse} " +
                     $"where id = '{participantData.id}'";

        return ret;
    }

    private string MakeInsertParticipantGroupMsg(ParticipantGroupData participantGroupData)
    {
        string ret = $"(id, name, groupCode, leaderParticipantId," +
                     $"descriptionKey, createTime, modifyTime, isUse) values " +
                     $"('{participantGroupData.id}', '{participantGroupData.name}', '{participantGroupData.groupCode}', " +
                     $"'{participantGroupData.leaderParticipantId}', '{participantGroupData.descriptionKey}', " +
                     $"'{participantGroupData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{participantGroupData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {participantGroupData.isUse})";

        return ret;
    }
    private string MakeUpdateParticipantGroupMsg(ParticipantGroupData participantGroupData)
    {
        string ret = $"set name = '{participantGroupData.name}', groupCode = '{participantGroupData.groupCode}', " +
                     $"leaderParticipantId = '{participantGroupData.leaderParticipantId}', descriptionKey = '{participantGroupData.descriptionKey}', " +
                     $"createTime = '{participantGroupData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{participantGroupData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {participantGroupData.isUse} " +
                     $"where id = '{participantGroupData.id}'";

        return ret;
    }

    private string MakeInsertRouteMsg(RouteData routeData)
    {
        string ret = $"(id, name, startLat, startLon," +
                     $"endLat, endLon, waypoints, leavePointLat, " +
                     $"leavePointLon, startDropPoint, descriptionKey, " +
                     $"createTime, modifyTime, isUse) values " +
                     $"('{routeData.id}', '{routeData.name}', {routeData.startLat}, " +
                     $"{routeData.startLon}, {routeData.endLat}, {routeData.endLon}, " +
                     $"'{routeData.waypoints}', {routeData.leavePointLat}, {routeData.leavePointLon}, {routeData.startDropPoint}, " +
                     $"'{routeData.descriptionKey}', '{routeData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{routeData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {routeData.isUse})";

        return ret;
    }
    private string MakeUpdateRouteMsg(RouteData routeData)
    {
        string ret = $"set name = '{routeData.name}', startLat = {routeData.startLat}, " +
                     $"startLon = {routeData.startLon}, endLat = {routeData.endLat}, " +
                     $"endLon = {routeData.endLon}, waypoints = '{routeData.waypoints}', " +
                     $"leavePointLat = {routeData.leavePointLat}, leavePointLon = {routeData.leavePointLon}, " +
                     $"startDropPoint = {routeData.startDropPoint}, descriptionKey = '{routeData.descriptionKey}', " +
                     $"createTime = '{routeData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{routeData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {routeData.isUse} " +
                     $"where id = '{routeData.id}'";

        return ret;
    }

    private string MakeInsertScenarioMsg(ScenarioData scenarioData)
    {
        string ret = $"(id, name, nameKey, jumpType, aircraftId," +
                     $"parachuteId, routeId, weatherId, dropSystemId, " +
                     $"markerId, deploymenType, brifingPlace, instructorId, participantGroupId, " +
                     $"mapId, windDirction, windSpeed, fogType, autoActiveAltitude, " +
                     $"exitPosition, allowedSpeedKnots, operationalAltitude, endOperationalAltidute, " +
                     $"descriptionKey, scheduledDateTime, createTime, modifyTime, isUse, isPreset) values " +
                     $"('{scenarioData.id}', '{scenarioData.name}', '{scenarioData.nameKey}', '{scenarioData.jumpType}', " +
                     $"'{scenarioData.aircraftId}', '{scenarioData.parachuteId}', '{scenarioData.routeId}', " +
                     $"'{scenarioData.weatherId}', '{scenarioData.dropSystemId}', '{scenarioData.markerId}', " +
                     $"'{scenarioData.deploymenType}', '{scenarioData.brifingPlace}', '{scenarioData.instructorId}', " +
                     $"'{scenarioData.participantGroupId}', '{scenarioData.mapId}', '{scenarioData.windDirction}', " +
                     $"'{scenarioData.windSpeed}', '{scenarioData.fogType}', {scenarioData.autoActiveAltitude}, " +
                     $"'{scenarioData.exitPosition}', {scenarioData.allowedSpeedKnots}, {scenarioData.operationalAltitude}, " +
                     $"{scenarioData.endOperationalAltidute}, '{scenarioData.descriptionKey}', " +
                     $"'{scenarioData.scheduledDateTime.ToString("yyyy-MM-dd HH:mm:ss")}', '{scenarioData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{scenarioData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {scenarioData.isUse}, {scenarioData.isPreset})";

        return ret;
    }
    private string MakeUpdateScenarioMsg(ScenarioData scenarioData)
    {
        string ret = $"set name = '{scenarioData.name}', nameKey = '{scenarioData.nameKey}', jumpType = '{scenarioData.jumpType}', " +
                     $"aircraftId = '{scenarioData.aircraftId}', parachuteId = '{scenarioData.parachuteId}', " +
                     $"routeId = '{scenarioData.routeId}', weatherId = '{scenarioData.weatherId}', " +
                     $"dropSystemId = '{scenarioData.dropSystemId}', markerId = '{scenarioData.markerId}', " +
                     $"deploymenType = '{scenarioData.deploymenType}', brifingPlace = '{scenarioData.brifingPlace}', " +
                     $"instructorId = '{scenarioData.instructorId}', participantGroupId = '{scenarioData.participantGroupId}', " +
                     $"mapId = '{scenarioData.mapId}', windDirction = '{scenarioData.windDirction}', " +
                     $"windSpeed = '{scenarioData.windSpeed}', fogType = '{scenarioData.fogType}', " +
                     $"autoActiveAltitude = {scenarioData.autoActiveAltitude}, exitPosition = '{scenarioData.exitPosition}', " +
                     $"allowedSpeedKnots = {scenarioData.allowedSpeedKnots}, operationalAltitude = {scenarioData.operationalAltitude}, " +
                     $"instructorId = {scenarioData.endOperationalAltidute}, " +
                     $"descriptionKey = '{scenarioData.descriptionKey}', scheduledDateTime = '{scenarioData.scheduledDateTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"createTime = '{scenarioData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{scenarioData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {scenarioData.isUse}," +
                     $"isPreset = {scenarioData.isPreset} " +
                     $"where id = '{scenarioData.id}'";

        return ret;
    }

    private string MakeInsertWeatherMsg(WeatherData weatherData)
    {
        string ret = $"(id, name, nameKey, icon, cloudGroupId, windId," +
                     $"fogId, effectId, weatherType, timeOfDay, " +
                     $"vision, visibilty, descriptionKey, " +
                     $"createTime, modifyTime, isUse) values " +
                     $"('{weatherData.id}', '{weatherData.name}', '{weatherData.nameKey}', " +
                     $"'{weatherData.icon}', '{weatherData.cloudGroupId}', " +
                     $"'{weatherData.windId}', '{weatherData.fogId}', '{weatherData.effectId}', " +
                     $"'{weatherData.weatherType}', '{weatherData.timeOfDay}', '{weatherData.vision}', " +
                     $"{weatherData.visibilty}, '{weatherData.descriptionKey}', '{weatherData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{weatherData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', {weatherData.isUse})";

        return ret;
    }
    private string MakeUpdateWeatherMsg(WeatherData weatherData)
    {
        string ret = $"set name = '{weatherData.name}', nameKey = '{weatherData.nameKey}', " +
                     $"icon = '{weatherData.icon}', cloudGroupId = '{weatherData.cloudGroupId}', " +
                     $"windId = '{weatherData.windId}', fogId = '{weatherData.fogId}', " +
                     $"effectId = '{weatherData.effectId}', weatherType = '{weatherData.weatherType}', " +
                     $"timeOfDay = '{weatherData.timeOfDay}', vision = '{weatherData.vision}', " +
                     $"visibilty = {weatherData.visibilty}' descriptionKey = '{weatherData.descriptionKey}', " +
                     $"createTime = '{weatherData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"modifyTime = '{weatherData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}', isUse = {weatherData.isUse} " +
                     $"where id = '{weatherData.id}'";

        return ret;
    }

    private string MakeUpdateToInstructorMsg(ToInstructorData toInstructorData)
    {
        string ret = $"set isHwConnect = {toInstructorData.isHwConnect}, isBackupChute = {toInstructorData.isBackupChute}, " +
                     $"isReady = {toInstructorData.isReady}, participantId = '{toInstructorData.participantId}', " +
                     $"participantName = '{toInstructorData.participantName}', participantNo = '{toInstructorData.participantNo}', " +
                     $"scenarioId = '{toInstructorData.scenarioId}', isSetting = {toInstructorData.isSetting}, " +
                     $"timelineId = '{toInstructorData.timelineId}', isSuccess = {toInstructorData.isSuccess}, " +
                     $"trainingState = '{toInstructorData.trainingState}' " +
                     $"where swNo = {toInstructorData.swNo}";

        return ret;
    }
    private string MakeUpdateToParticipantMsg(ToParticipantData toParticipantData)
    {
        string ret = $"set participantId = '{toParticipantData.participantId}', participantName = '{toParticipantData.participantName}', " +
                     $"participantNo = '{toParticipantData.participantNo}', scenarioId = '{toParticipantData.scenarioId}', " +
                     $"isSetting = '{toParticipantData.isSetting}', timelineId = '{toParticipantData.timelineId}', " +
                     $"isSuccess = {toParticipantData.isSuccess}' trainingState = '{toParticipantData.trainingState}', " +
                     $"where swNo = {toParticipantData.swNo}";

        return ret;
    }

    private string MakeInsertFailureGrantHistoryMsg(FailureGrantHistoryData failureGrantHistoryData)
    {
        string ret = $"(id, scenarioId, participantId, grantId," +
                     $"descriptionKey, createTime, modifyTime) values " +
                     $"('{failureGrantHistoryData.id}', '{failureGrantHistoryData.scenarioId}', '{failureGrantHistoryData.participantId}', " +
                     $"'{failureGrantHistoryData.grantId}', '{failureGrantHistoryData.descriptionKey}', " +
                     $"'{failureGrantHistoryData.createTime.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                     $"'{failureGrantHistoryData.modifyTime.ToString("yyyy-MM-dd HH:mm:ss")}')";

        return ret;
    }
    #endregion

        // 서버 응답 처리
    void OnServerMessage(object sender, MessageEventArgs e)
    {
        var stringData = e.Data;
        //Debug.Log(stringData);
        var baseType = JsonUtility.FromJson<ServerResponse>(stringData);
        bool errorFlag = false;
        //string Message = "";

        //Debug.Log(string.Format("데이터 수신 : {0}", baseType.type));

        try
        {

            switch (baseType.type)
            {
                case "selectAddInstructorEval":
                    if (errorFlag)
                        Debug.Log("selectAddInstructorEval error");
                    else
                    {
                        var addInstructorEvalResp = JsonConvert.DeserializeObject<AddInstructorEvalDataResponse>(e.Data);
                        addInstructorEvalTcs.SetResult(addInstructorEvalResp.data);
                    }
                    break;

                case "selectAircraftData":
                    if (errorFlag)
                        Debug.Log("selectAircraftData error");
                    else
                    {
                        var aircraftResp = JsonConvert.DeserializeObject<AircraftDataResponse>(e.Data);
                        aircraftTcs.SetResult(aircraftResp.data);
                    }
                    break;

                case "selectDropSystemData":
                    if (errorFlag)
                        Debug.Log("selectDropSystemData error");
                    else
                    {
                        var dropResp = JsonConvert.DeserializeObject<DropSystemDataResponse>(e.Data);
                        dropSystemTcs.SetResult(dropResp.data);
                    }
                    break;

                case "selectDropVisualSignalsData":
                    if (errorFlag)
                        Debug.Log("selectDropVisualSignalsData error");
                    else
                    {
                        var markResp = JsonConvert.DeserializeObject<DropVisualSignalsDataResponse>(e.Data);
                        dropVisualSignalsTcs.SetResult(markResp.data);
                    }
                    break;

                case "selectEvaluationParticipantListData":
                    if (errorFlag)
                        Debug.Log("selectEvaluationParticipantListData error");
                    else
                    {
                        var evalPartListResp = JsonConvert.DeserializeObject<EvaluationParticipantListDataResponse>(e.Data);
                        evaluationParticipantListDataTcs.SetResult(evalPartListResp.data);
                    }
                    break;

                case "selectInstructorData":
                    if (errorFlag)
                        Debug.Log("selectInstructorData error");
                    else
                    {
                        var instResp = JsonConvert.DeserializeObject<InstructorDataResponse>(e.Data);
                        instructorTcs.SetResult(instResp.data);
                    }
                    break;

                case "selectMapData":
                    if (errorFlag)
                        Debug.Log("selectMapData error");
                    else
                    {
                        var mapResp = JsonConvert.DeserializeObject<MapDataResponse>(e.Data);
                        mapTcs.SetResult(mapResp.data);
                    }
                    break;

                case "selectParachuteData":
                    if (errorFlag)
                        Debug.Log("selectParachuteData error");
                    else
                    {
                        var paraResp = JsonConvert.DeserializeObject<ParachuteDataResponse>(e.Data);
                        parachuteTcs.SetResult(paraResp.data);
                    }
                    break;

                case "selectParticipantData":
                    if (errorFlag)
                        Debug.Log("selectParticipantData error");
                    else
                    {
                        var partResp = JsonConvert.DeserializeObject<ParticipantDataResponse>(e.Data);
                        participantTcs.SetResult(partResp.data);
                    }
                    break;

                case "selectParticipantGroupData":
                    if (errorFlag)
                        Debug.Log("selectParticipantGroupData error");
                    else
                    {
                        var pgroupResp = JsonConvert.DeserializeObject<ParticipantGroupDataResponse>(e.Data);
                        participantGroupTcs.SetResult(pgroupResp.data);
                    }
                    break;

                case "selectRouteData":
                    if (errorFlag)
                        Debug.Log("selectRouteData error");
                    else
                    {
                        var routeResp = JsonConvert.DeserializeObject<RouteDataResponse>(e.Data);
                        routeTcs.SetResult(routeResp.data);
                    }
                    break;

                case "selectScenarioData":
                    if (errorFlag)
                        Debug.Log("selectScenarioData error");
                    else
                    {
                        var scenResp = JsonConvert.DeserializeObject<ScenarioDataResponse>(e.Data);
                        scenarioTcs.SetResult(scenResp.data);
                    }
                    break;

                case "selectWeatherData":
                    if (errorFlag)
                        Debug.Log("selectWeatherData error");
                    else
                    {
                        var weatherResp = JsonConvert.DeserializeObject<WeatherDataResponse>(e.Data);
                        weatherTcs.SetResult(weatherResp.data);
                    }
                    break;

                case "selectFailureGrantHistoryData":
                    if (errorFlag)
                        Debug.Log("selectFailureGrantHistoryData error");
                    else
                    {
                        var failResp = JsonConvert.DeserializeObject<FailureGrantHistoryDataResponse>(e.Data);
                        failureHistoryTcs.SetResult(failResp.data);
                    }
                    break;

                case "selectEvaluationList":
                    if (errorFlag)
                        Debug.Log("selectEvaluationList error");
                    else
                    {
                        var ratingResp = JsonConvert.DeserializeObject<EvaluationListDataResponse>(e.Data);
                        evaluationListTcs.SetResult(ratingResp.data);
                    }
                    break;

                case "selecttoInstructor":
                    if (errorFlag)
                        Debug.Log("selecttoInstructor error");
                    else
                    {
                        var toInstructor = JsonConvert.DeserializeObject<ToInstructorDataResponse>(e.Data);
                        toInstructorTcs.SetResult(toInstructor.data);
                    }
                    break;

                case "selecttoParticipant":
                    if (errorFlag)
                        Debug.Log("selecttoParticipant error");
                    else
                    {
                        var toParticipant = JsonConvert.DeserializeObject<ToParticipantDataResponse>(e.Data);
                        toParaticipantTcs.SetResult(toParticipant.data);
                    }
                    break;

                case "insertData":
                    if (errorFlag)
                        Debug.Log("insertData error");
                    else
                    {
                        var insertResp = JsonConvert.DeserializeObject<InsertDataResponse>(e.Data);
                        insertTcs.SetResult(true);
                    }
                    break;

                case "updateData":
                    if (errorFlag)
                        Debug.Log("updateData error");
                    else
                    {
                        var updateResp = JsonConvert.DeserializeObject<UpdateDataResponse>(e.Data);
                        updateTcs.SetResult(true);
                    }
                    break;

                case "participantConnected":
                    var connectedResp = JsonConvert.DeserializeObject<ParticipantConnectedResponse>(e.Data);
                    bool CheckFlag = false;

                    foreach (var item in ConnectedParticipantList)
                    {
                        if (item.id == connectedResp.connectorId)
                        {
                            CheckFlag = true;
                            break;
                        }
                    }

                    if (!CheckFlag)
                    {
                        ConnectedParticipantList.Add(new CurrentParticipantData { id = connectedResp.connectorId });
                    }
                    break;

                case "timelineCommand":
                    var timelineCmdResp = JsonConvert.DeserializeObject<CommandResponse>(e.Data);

                    CurParticipantData.timelineId = timelineCmdResp.command;
                    CurParticipantData.isSuccess = timelineCmdResp.commandFlag ? false : true;

                    SendCommandResponse(true, CurParticipantData);

                    UnityMainThreadDispatcher.Enqueue((() =>
                    {
                        _stateManager.ReceiveTimeLineID(CurParticipantData.timelineId);
                    }));
                    break;

                case "timelineResp":
                    var timelineRespResp = JsonConvert.DeserializeObject<CommandResponse>(e.Data);

                    foreach (var item in ConnectedParticipantList)
                    {
                        if (item.id == timelineRespResp.participantId)
                        {
                            item.timelineId = timelineRespResp.command;
                            item.isSuccess = timelineRespResp.commandFlag ? false : true;

                            break;
                        }
                    }
                    break;

                case "scenarioCommand":
                    var scenarioCmdResp = JsonUtility.FromJson<CommandResponse>(e.Data);

                    if (CurParticipantData == null)
                        CurParticipantData = new CurrentParticipantData();

                    CurParticipantData.scenarioId = scenarioCmdResp.command;
                    CurParticipantData.isSetting = !scenarioCmdResp.commandFlag;

                    DataManager.Inst.ReceiveScenarioID(CurParticipantData.scenarioId);

                    SendCommandResponse(false, CurParticipantData);
                    break;

                case "ScenarioResp":
                    var scenaroiRespResp = JsonConvert.DeserializeObject<CommandResponse>(e.Data);

                    foreach (var item in ConnectedParticipantList)
                    {
                        if (item.id == scenaroiRespResp.participantId)
                        {
                            item.scenarioId = scenaroiRespResp.command;
                            item.isSetting = scenaroiRespResp.commandFlag ? false : true;

                            break;
                        }
                    }
                    break;

                case "curTraningState":
                    var traningStateResp = JsonConvert.DeserializeObject<TraningStateResponse>(e.Data);

                    Debug.Log($"[WS_DB_Client] curTraningState 수신 - SimNo: {traningStateResp.simNo}, 현재 시뮬레이터: {simulatorNumber}, TrainingState: {traningStateResp.trainingState}");
                    
                    // 수신한 시뮬레이터 번호가 현재 시뮬레이터 번호와 일치하는 경우에만 처리
                    if (traningStateResp.simNo == simulatorNumber)
                    {
                        // StateManager에게 상태를 전달하여 처리하도록 함
                        UnityMainThreadDispatcher.Enqueue((() =>
                        {
                            _stateManager.ReceiveTrainingState(traningStateResp.trainingState);
                        }));
                        Debug.Log($"[WS_DB_Client] 훈련상태 변경 완료: {traningStateResp.trainingState}");
                    }
                    else
                    {
                        Debug.LogWarning($"[WS_DB_Client] 훈련상태 시뮬레이터 번호 불일치! 수신: {traningStateResp.simNo}, 현재: {simulatorNumber}");
                    }

                    break;

                case "curSceneState":
                    var sceneStateResp = JsonConvert.DeserializeObject<SceneStateResponse>(e.Data);

                    // 먼저 StateManager에게 상태를 전달하여 처리하도록 함
                    UnityMainThreadDispatcher.Enqueue((() =>
                    {
                        //_stateManager.ReceiveTrainingState(sceneStateResp.trainingState);
                    }));
                    Debug.Log("신상태 받기 완료");
                    break;

                case "trainingStateResp":
                    var traningStateRespResp = JsonConvert.DeserializeObject<TraningStateResponse>(e.Data);

                    foreach (var item in ConnectedParticipantList)
                    {
                        if (item.id == traningStateRespResp.participantId)
                        {
                            item.trainingState = traningStateRespResp.trainingState;
                            break;
                        }
                    }
                    break;

                case "monitoringData":
                    var monitoringDataResp = JsonConvert.DeserializeObject<MonitoringData>(e.Data);
                    
                    // 다른 참가자들의 데이터 받아서 처리하는 매서드 실행
                    if (ParticipantManager.Inst != null)
                    {
                        ParticipantManager.Inst.UpdateOtherParticipantData(monitoringDataResp);
                    }
                    break;

                case "timelineError":
                    //교관 측에서 교육생의 타임라인 에러가 발생했을 때 처리하는 기능 추가 필요
                    break;
                case "connectStateData":
                    var connectStateDataResp = JsonConvert.DeserializeObject<ConnectStateData>(e.Data);
                    //각 기기에서 보내온 정보를 교관 측에서 정보를 모니터링 할 수 있도록 표시
                    break;

                // 교육생id와 교육생 이름 받아서 처리
                case "setParticipantData":
                    var setParticipantDataResp = JsonConvert.DeserializeObject<SetParticipantData>(e.Data);
                    string pSimNo = setParticipantDataResp.simNo;
                    
                    Debug.Log($"[WS_DB_Client] setParticipantData 수신 - SimNo: {pSimNo}, 현재 시뮬레이터: {simulatorNumber}, ID: {setParticipantDataResp.participantId}, 이름: {setParticipantDataResp.participantName}");
                    
                    // 수신한 시뮬레이터 번호가 현재 시뮬레이터 번호와 일치하는 경우에만 처리
                    if (pSimNo == simulatorNumber)
                    {
                        participantData = setParticipantDataResp;
                        
                        // CurrentParticipantData 업데이트
                        if (CurParticipantData != null)
                        {
                            CurParticipantData.id = setParticipantDataResp.participantId;
                            CurParticipantData.name = setParticipantDataResp.participantName;
                        }
                        
                        Debug.Log($"참가자 데이터 설정 완료 - ID: {setParticipantDataResp.participantId}, 이름: {setParticipantDataResp.participantName}");
                        
                        // ConnectStateData 업데이트 - 준비 상태를 "완료"로 변경
                        if (connectStateData != null)
                        {
                            SendConnectStateDate(connectStateData);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[WS_DB_Client] 시뮬레이터 번호 불일치! 수신: {pSimNo}, 현재: {simulatorNumber}");
                    }
                    break;

                // 평가 ID 받아서 DB에 저장할때 같이 삽입
                case "evalIndexData":
                    var evalIndexDataResp = JsonConvert.DeserializeObject<EvalIndexData>(e.Data);
                    
                    Debug.Log($"[WS_DB_Client] evalIndexData 수신 - SimNo: {evalIndexDataResp.simNo}, 현재 시뮬레이터: {simulatorNumber}, EvalIndex: {evalIndexDataResp.evaluationIndex}");
                    
                    // 수신한 시뮬레이터 번호가 현재 시뮬레이터 번호와 일치하는 경우에만 처리
                    if (evalIndexDataResp.simNo == simulatorNumber)
                    {
                        evaluationIndex = evalIndexDataResp.evaluationIndex;
                        Debug.Log($"평가 인덱스 설정 완료 - SimNo: {evalIndexDataResp.simNo}, EvalId: {evaluationIndex}");
                    }
                    else
                    {
                        Debug.LogWarning($"[WS_DB_Client] 평가 인덱스 시뮬레이터 번호 불일치! 수신: {evalIndexDataResp.simNo}, 현재: {simulatorNumber}");
                    }
                    break;
                case "procedureData":
                    //교관에서 교육생의 절차 정보를 받고 처리하는 함수 추가
                    break;

                case "setSituationData":
                    //교육생에서 우발상황이 입력되었을 때 처리하는 함수 추가
                    break;

                case "procedureCommand":
                    // 교육생이 절차 정보를 교관에게 받았을 경우 처리하는 함수 추가
                    // 교관으로부터 절차 명령 수신
                    var procedureCmdResp = JsonConvert.DeserializeObject<CommandResponse>(e.Data);

                    if (procedureCmdResp.simNo == simulatorNumber)
                    {
                        // 전체 처리를 메인 스레드로 위임 (Time.time 오류 방지)
                        UnityMainThreadDispatcher.Enqueue(() =>
                        {
                            _stateManager.ProcessProcedureRequest(procedureCmdResp.command);
                        });
                    }
                    break;

                case "procedureResp":
                    // 교관이 교육생의 절차 정보 응답을 했을 경우 처리하는 함후 추가
                    break;

                case "situationResultData":
                    // 교육생이 우발상황을 처리한 결과를 교관에게 보고한 내용을 처리하는 함수 추가
                    break;

                case "jointRotation":
                    // 관절 데이터 처리 - ParticipantManager로 전달
                    var jointRotationResp = JsonConvert.DeserializeObject<JointRotation>(e.Data);
                    
                    if (jointRotationResp != null)
                    {
                        ParticipantManager.Inst.ReceiveJointRotationData(jointRotationResp);
                    }

                    break;

                case "setForcedDeparture":
                    var forcedDepartureResp = JsonConvert.DeserializeObject<SetForcedDeparture>(e.Data);

                    if (forcedDepartureResp != null && forcedDepartureResp.simNo == simulatorNumber)
                    {
                        switch (forcedDepartureResp.departureType)
                        {
                            case "ForceExit":
                            {
                                UnityMainThreadDispatcher.Enqueue(() =>
                                {
                                    _stateManager.ForceExit();
                                });
                                break;
                            }
                            case "ForceMainParachute":
                            {
                                UnityMainThreadDispatcher.Enqueue(() =>
                                {
                                    _stateManager.ForceMainParachute();
                                });
                                break;
                            }
                            case "ForceSubParachute":
                            {
                                break;
                            }
                            case "ForceTrainingEnd":
                            {
                                UnityMainThreadDispatcher.Enqueue(() =>
                                {
                                    _stateManager.ForceTrainingEnd();
                                });
                                break;
                            }
                        }
                        
                    }
                    break;

                default:
                    //Debug.Log(string.Format("Not in type : {0}", e.Type));
                    break;
            }
        }
        catch (Exception ex)
        {
            //Debug.Log(string.Format("Error : {0}", ex.Message));
        }
    }
}

#region 데이터 관리용 클래스
[Serializable]
public enum TableInfo
{
    aircraft = 1,
    dropSystem = 2,
    instructor = 3,
    dropVisualSignals = 4,
    route = 5,
    scenario = 6,
    parachute = 7,
    participant = 8,
    participantGroup = 9,
    weather = 10,
    map = 11,
    evaluationParticipantList = 12,
    addInstructorEval = 13,

    failure_grant_history = 21,
    evaluationList = 22,

    toParticipant = 31,
    toInstructor = 32,
}

[Serializable]
public enum DBDataType
{
    AirCraftData = 1,
    DropSystemData = 2,
    InstructorData = 3,
    MarkerData = 4,
    RouteData = 5,
    ScenarioData = 6,
    ParachuteData = 7,
    ParticipantData = 8,
    ParticipantGroupData = 9,
    WeatherData = 10,

    FailureTrantHistory = 21,
    RatingHistory = 22,
}

[Serializable]
class ClientRequest
{
    public string type;
    public string sql;     // selectData 요청일 때만 사용
}

[Serializable]
class ConnectSend
{
    public string type;
    public string connector;
    public string connectorId;
}

[Serializable]
class CommandData
{
    public string type;
    public string simNo;
    public string participantId;
    public string command;
    public bool commandFlag;
}

[Serializable]
class TraningStateData
{
    public string type;
    public string participantId;
    public TrainingState trainingState;
}

[Serializable]
public class CurrentParticipantData
{
    public CurrentParticipantData()
    {
        monitoringData = new MonitoringData();
    }

    public string id;
    public string simNo;
    public string name;
    public string scenarioId;
    public bool isSetting;
    public string timelineId;
    public bool isSuccess;

    [JsonConverter(typeof(StringEnumConverter))]
    public TrainingState trainingState;

    public MonitoringData monitoringData;
    //public int fillingSpeed;
    //public int alaitude;
    //public int forwardSpeed;
    //public int distance;
}

[Serializable]
class CurParticipantDataSend
{
    public string type;
    public string simNo;
    [JsonConverter(typeof(StringEnumConverter))]
    public TrainingState trainingState;
}
[Serializable]
class CurSceneStateSend
{
    public string type;
    public string simNo;
    [JsonConverter(typeof(StringEnumConverter))]
    public SceneState sceneState;
}


[Serializable]
public class ServerResponse
{
    public string type;     // 요청과 동일하게 "selectData"
    public object data;  // 성공 시 결과 행 배열
    public string error;    // 오류 메시지
}

[Serializable]
public enum ExitPosition
{
    Left = 1,
    Right = 2,
    Both = 3,
    Ramp = 4,
}
[Serializable]
public enum AircraftType
{
    Fixed = 1,
    Rotary = 2,
    Mechanical = 3,
}
[Serializable]
public enum DropSystemType
{
    GMRS = 1,
    VIRS = 2,
    CARP = 3,
    MANUAL = 4,
    AUTONOMOUS = 5,
}
[Serializable]
public enum SignalType
{
    Marker = 1,
    Smoke = 2,
    Light = 3,
    Flare = 4,
}
[Serializable]
public enum UsedIn
{
    GMRS = 1,
    VRIS = 2,
    BOTH = 3,
}
[Serializable]
public enum ParachuteType
{
    Round = 1,
    RamAir = 2,
}
[Serializable]
public enum JumpType
{
    STANDARD = 1,
    HAHO = 2,
    HALO = 3,
}
[Serializable] 
public enum DeploymenType
{
    Manual = 1,
    Auto = 2,
    AltitudeBase = 3,
}
[Serializable]
public enum BrifingPlace
{
    Auditorium = 1,
    Runway = 2,
}
[Serializable]
public enum WindDirction
{
    N = 1,
    NE = 2,
    E = 3,
    SE = 4,
    S = 5,
    SW = 6,
    W = 7,
    NW = 8,
}
[Serializable]
public enum WindSpeed
{
    WindCalm = 1,
    WindLight = 2,
    WindStrong = 3,
    WindGusty = 4,
}
[Serializable]
public enum TimeOfDay
{
    Day = 1,
    Night = 2,
    Dawn = 3,
    Dusk = 4,
}
[Serializable]
public enum Vision
{
    None = 1,
    Fog = 2,
    YellowDust = 3,
}
[Serializable]
public enum Grant_ID
{
    One = 1,
    Two = 2,
}
[Serializable]
public enum Category
{
    Procedure = 1,
    Condition = 2,
    Reation = 3,
    Manual = 4,
}
[Serializable]
public enum EvaluationMode
{
    Auto = 1,
    Manual = 2,
}
[Serializable]
public enum ScoreRuleType
{
    None,
    Fixed,
    Range,
    Formula,
    Manual,
}
[Serializable]
public enum TrainingState
{
    Ready = 1,
    Start = 2,
    End = 3,
    Restart = 4,
    Pause = 5,
    Resume = 6,
}

[Serializable]
public enum ProcedureState
{
    None,
    Play,
    Complete
}

[Serializable]
public enum SceneState
{
    SceneLoading = 1,
    SceneComplete = 2,
}

[Serializable]
public enum CommandType
{
    Timeline = 1,
    Scenario = 2,
    ProcedureCommand = 3,
    ProcedureResp = 4,
}

[Serializable]
public class AddInstructorEval
{
    public string id;
    public string evalParticipantId;
    public string evalName;
    public string evalResult;
    public string evalScore;
    public bool isUse;
}

[Serializable]
public class AircraftData
{
    public string id;
    public string name;
    public string nameKey;
    public string icon;
    public string prefabName;
    [JsonConverter(typeof(StringEnumConverter))]
    public ExitPosition exitPosition;
    [JsonConverter(typeof(StringEnumConverter))]
    public AircraftType type;
    public int minOperationalAltitude;
    public int maxOperationalAltitude;
    public int defaultSpeedKnots;
    public int minAllowedSpeedKnots;
    public int maxAllowedSpeedKnots;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}


[Serializable]
public class AddInstructorEvalDataResponse
{
    public string type;
    public List<AddInstructorEval> data;
    public string error;
}

[Serializable]
public class AircraftDataResponse
{
    public string type;
    public List<AircraftData> data;
    public string error;
}

[Serializable]
public class DropSystemData
{
    public string id;
    public string name;
    public string nameKey;
    [JsonConverter(typeof(StringEnumConverter))]
    public DropSystemType systemType;
    public bool markerRequired;
    public string marker1;
    public string marker2;
    public string marker3;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class DropSystemDataResponse
{
    public string type;
    public List<DropSystemData> data;
    public string error;
}

[Serializable]
public class DropVisualSignalsData
{
    public string id;
    public string name;
    public string nameKey;
    [JsonConverter(typeof(StringEnumConverter))]
    public SignalType signalType;
    public string shape;
    public string color;
    public bool dayUse;
    public bool nightUse;
    [JsonConverter(typeof(StringEnumConverter))]
    public UsedIn usedIn;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class DropVisualSignalsDataResponse
{
    public string type;
    public List<DropVisualSignalsData> data;
    public string error;
}

[Serializable]
public class EvaluationParticipantListData
{
    public string id;
    public string participantId;
    public string participantName;
    public string participantNo;
    public float participantHeight;
    public float participantWeight;
    public string participantGroupName;
    public string participantGroupCode;
    public string evaluationTime;
    public string scenarioName;
    public string parachuteId;
    public DateTime scheduleDatetime;
    public string mapId;
    public string aircraftId;
    public string weatherId;
    [JsonConverter(typeof(StringEnumConverter))]
    public WindDirction windDirction;
    [JsonConverter(typeof(StringEnumConverter))]
    public WindSpeed windSpeed;
    public string dropSystemId;
    [JsonConverter(typeof(StringEnumConverter))]
    public ExitPosition exitPosition;
    public string markerId;
    public string fogType;
    public int allowedSpeedKnots;
    public int operationalAltitude;
    public int endOperationalAltitude;
    public int autoActiveAltitude;
    [JsonConverter(typeof(StringEnumConverter))]
    public JumpType jumpType;
    [JsonConverter(typeof(StringEnumConverter))]
    public BrifingPlace brifingPlace;
    public DateTime createTime;
}
[Serializable]
public class EvaluationParticipantListDataResponse
{
    public string type;
    public List<EvaluationParticipantListData> data;
    public string error;
}

[Serializable]
public class InstructorData
{
    public string id;
    public string name;
    public string pw;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class InstructorDataResponse
{
    public string type;
    public List<InstructorData> data;
    public string error;
}
     
[Serializable]
public class MapData
{
    public string id;
    public string name;
    public string nameKey;
    public string map2d;
    public string map3d;
    public string descriptionKey;
    public bool isDefault;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class MapDataResponse
{
    public string type;
    public List<MapData> data;
    public string error;
}

[Serializable]
public class RouteData
{
    public string id;
    public string name;
    public float startLat;
    public float startLon;
    public float endLat;
    public float endLon;
    public string waypoints;
    public float leavePointLat;
    public float leavePointLon;
    public int startDropPoint;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class RouteDataResponse
{
    public string type;
    public List<RouteData> data;
    public string error;
}

[Serializable]
public class ScenarioData
{
    public string id;
    public string name;
    public string nameKey;
    //[JsonConverter(typeof(StringEnumConverter))]
    public JumpType jumpType;
    public string aircraftId;
    public string parachuteId;
    public string routeId;
    public string weatherId;
    public string dropSystemId;
    public string markerId;
    [JsonConverter(typeof(StringEnumConverter))]
    public DeploymenType deploymenType;
    [JsonConverter(typeof(StringEnumConverter))]
    public BrifingPlace brifingPlace;
    public string instructorId;
    public string participantGroupId;
    public string mapId;
    public WindDirction windDirction;
    public WindSpeed windSpeed;
    public string fogType;
    public int autoActiveAltitude;
    public ExitPosition exitPosition;
    public int allowedSpeedKnots;
    public int operationalAltitude;
    public int endOperationalAltidute;
    public string descriptionKey;
    public DateTime scheduledDateTime;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
    public bool isPreset;
}
[Serializable]
public class ScenarioDataResponse
{
    public string type;
    public List<ScenarioData> data;
    public string error;
}

[Serializable]
public class ParachuteData
{
    public string id;
    public string name;
    public string nameKey;
    public string icon;
    [JsonConverter(typeof(StringEnumConverter))]
    public ParachuteType type;
    public int minOpenAltitudeFt;
    public int maxOpenAltitudeFt;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class ParachuteDataResponse
{
    public string type;
    public List<ParachuteData> data;
    public string error;
}

[Serializable]
public class ParticipantData
{
    public string id;
    public string name;
    public string no;
    public float height;
    public float weight;
    public string groupId;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class ParticipantDataResponse
{
    public string type;
    public List<ParticipantData> data;
    public string error;
}

[Serializable]
public class ParticipantGroupData
{
    public string id;
    public string name;
    public string groupCode;
    public string leaderParticipantId;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class ParticipantGroupDataResponse
{
    public string type;
    public List<ParticipantGroupData> data;
    public string error;
}

[Serializable]
public class WeatherData
{
    public string id;
    public string name;
    public string nameKey;
    public string icon;
    public string cloudGroupId;
    public string windId;
    public string fogId;
    public string effectId;
    [JsonConverter(typeof(StringEnumConverter))]
    public WeatherType weatherType;
    [JsonConverter(typeof(StringEnumConverter))]
    public TimeOfDay timeOfDay;
    [JsonConverter(typeof(StringEnumConverter))]
    public Vision vision;
    public int visibilty;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
    public bool isUse;
}
[Serializable]
public class WeatherDataResponse
{
    public string type;
    public List<WeatherData> data;
    public string error;
}

[Serializable]
public class ToParticipantData
{
    public int swNo;
    public string participantId;
    public string participantName;
    public string participantNo;
    public string scenarioId;
    public bool isSetting;
    public string timelineId;
    public bool isSuccess;
    public TrainingState trainingState;
}
[Serializable]
public class ToParticipantDataResponse
{
    public string type;
    public List<ToParticipantData> data;
    public string error;
}

[Serializable]
public class ToInstructorData
{
    public int swNo;
    public bool isHwConnect;
    public bool isBackupChute;
    public bool isReady;
    public string participantId;
    public string participantName;
    public string participantNo;
    public string scenarioId;
    public bool isSetting;
    public string timelineId;
    public bool isSuccess;
    public TrainingState trainingState;
}
[Serializable]
public class ToInstructorDataResponse
{
    public string type;
    public List<ToInstructorData> data;
    public string error;
}

[Serializable]
public class FailureGrantHistoryData
{
    public string id;
    public string scenarioId;
    public string participantId;
    [JsonConverter(typeof(StringEnumConverter))]
    public Grant_ID grantId;
    public string descriptionKey;
    public DateTime createTime;
    public DateTime modifyTime;
}
[Serializable]
public class FailureGrantHistoryDataResponse
{
    public string type;
    public List<FailureGrantHistoryData> data;
    public string error;
}

[Serializable]
public class EvaluationListData
{
    public string id;
    public string instructorName;
    public string evalParticipantId;
    public JumpType jumpType;
    public string evalFallTimeResult;
    public string evalFallTimeScore;
    public string evalTotalDistanceResult;
    public string evalTotalDistanceScore;
    public string evalAltimeterOnResult;
    public string evalAltimeterOnScore;
    public string evalHelmetOnResult;
    public string evalHelmetOnScore;
    public string evalOxyMaskResult;
    public string evalOxyMaskScore;
    public string evalSitDownCompleteResult;
    public string evalSitDownCompleteScore;
    public string evalStandUpCompleteResult;
    public string evalStandUpCompleteScore;
    public string evalHookUpCompleteResult;
    public string evalHookUpCompleteScore;
    public string evalGoJumpCompleteResult;
    public string evalGoJumpCompleteScore;
    public string evalDeployAltitudeResult;
    public string evalDeployAltitudeScore;
    public string evalDeployTargetDistanceResult;
    public string evalDeployTargetDistanceScore;
    public string evalEventCompleteResult;
    public string evalEventCompleteScore;
    public string evalFlareCompleteResult;
    public string evalFlareCompleteScore;
    public string evalLandingTypeResult;
    public string evalLandingTypeScore;
    public string evalTargetDistanceResult;
    public string evalTargetDistanceScore;
    public string evalLandingSpeedResult;
    public string evalLandingSpeedScore;
    public DateTime createTime;
}
[Serializable]
public class EvaluationListDataResponse
{
    public string type;
    public List<EvaluationListData> data;
    public string error;
}

[Serializable]
public class InsertDataResponse
{
    public string type;
    public string error;
}

[Serializable]
public class UpdateDataResponse
{
    public string type;
    public string error;
}

[Serializable]
public class CommandResponse
{
    public string type;
    public string simNo;
    public string participantId;
    public string command;
    public bool commandFlag;
}

[Serializable]
public class TraningStateResponse
{
    public string type;
    public string simNo;    // 류성희 20250724 simNo추가
    public string participantId;
    public TrainingState trainingState;
}
[Serializable]
public class SceneStateResponse
{
    public string type;
    public string simNo;
    public SceneState sceneState;
}

[Serializable]
public class ParticipantConnectedResponse
{
    public string type;
    public string connectorId;
}

[Serializable]
public class SituationResultDataResponse
{
    public string type;
    public string error;
}


[Serializable]
public class JointRotationResponse
{
    public string type;
    public JointRotation jointRotation;
    public string error;
}

[Serializable]
public class ForcedDepartureResponse
{
    public string type;
    public string error;
}

[Serializable]
class ServerMessage
{
    public string type;
    public string data;
    public string error;
}

[Serializable]
public class MonitoringData
{
    public string type;
    public string simNo;
    public string participantId;
    public int fallingSpeed;
    public int altitude;
    public int forwardSpeed;
    public int distance;

    //교육생 위치 및 관절 값
    public Vector3 pos;
    public Quaternion rotQ;
    public Vector3 eulerDeg;

    // 비행기 좌표
    public Vector3 planePos;
}

[Serializable]
public class JointRotation
{
    public string type;
    public int simNo;

    public Vector3S thighL;     // x,y,z
    public short calfL;         // z

    public Vector3S thighR;     // x,y,z
    public short calfR;         // z

    public Vector3S spine;      // x,y,z
    public Vector3S chest;      // x,y,z
    public Vector3S upperChest; // x,y,z

    public Vector2S clavicleL;  // y,z
    public Vector3S upperArmL;  // x,y,z
    public short forearmL;      // z

    public Vector3S neck;       // x,y,z
    public Vector3S head;       // x,y,z
    
    public Vector2S clavicleR;  // y,z
    public Vector3S upperArmR;  // x,y,z
    public short forearmR;      // z
}

[Serializable]
class TimelineErrorMessage
{
    public string type;
    public string simNo;
    public string participantId;
    public string timelineId;
    public string errorCode;
}

/// <summary>
/// 하드웨어 정보
/// true, false로 여부 전송
/// </summary>
[Serializable]
public class ConnectStateData
{
    public string type;
    public string simNo;
    public string hwState;
    public string subPara;
    public string readyState;
}

[Serializable]
public class SetParticipantData
{
    public string type;
    public string simNo;
    public string participantId;
    public string participantName;
}

[Serializable]
public class EvalIndexData
{
    public string type;
    public string simNo;
    public string evaluationIndex;
}

[Serializable]
public class ProcedureData
{
    public string type;
    public string simNo;
    public string procedureId;
}
[Serializable]
public class SetSituationData
{
    public string type;
    public string simNo;
    public string situationId;
}

[Serializable]
public class SituationResultData
{
    public string type;
    public string simNo;
    public bool resultData;
}

[Serializable]
public class SetForcedDeparture
{
    public string type;
    public string simNo;
    public string departureType;
}
#endregion
