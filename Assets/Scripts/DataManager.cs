using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DataManager
{
    private WS_DB_Client ws;
    public ScenarioData scenario;
    public List<TimeLine> timeLines;
    public List<Procedure> procedures;
    public List<Instruction> instructions;
    public List<Evaluation> evaluations;
    public List<Contingency> contingencys;
    public List<Route> routes;
    private WeatherData _weather;
    private FogData _fog;
    
    // 데이터 로딩 상태 추적
    public bool IsDataLoaded { get; private set; } = false;
    public bool IsLoadingData { get; private set; } = false;
    
    private static DataManager inst;

    public static DataManager Inst
    {
        get { return inst ??= new DataManager(); }
    }

    /// <summary>
    /// Start 대신 생성자를 이용하여 초기화를 진행한다.
    /// </summary>
    public DataManager()
    {
        Debug.Log("TimeLineData");
        ws = UnityEngine.Object.FindAnyObjectByType<WS_DB_Client>();
        if(ws) Debug.Log("WS_DB_Client 연결");
        else Debug.Log("WS_DB_Client 연결 실패");
    }

    public async void ReceiveScenarioID(string id)
    {
        try
        {
            IsLoadingData = true;
            IsDataLoaded = false;
            
            await SelectScenario(id);
            bool loadSuccess = LoadData();
            
            if (loadSuccess)
            {
                IsDataLoaded = true;
                Debug.Log("[DataManager] 모든 데이터 로드 성공 - 훈련 시작 가능");
                Debug.Log($"[DataManager] 시나리오 id : {id}, 시나리오 타입 : {scenario.jumpType}");
            }
            else
            {
                Debug.LogError("[DataManager] 데이터 로드 실패 - 훈련을 시작할 수 없습니다.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"예외가 발생했습니다: {e.Message}.");
        }
        finally
        {
            IsLoadingData = false;
        }
    }

    async Task SelectScenario(string scenarioId)
    {
        var list = await ws.GetScenarioDatabyID(scenarioId);

        scenario = list[0];
    }

    private bool LoadData()
    {
        // 1. 교관에서 내려준 시나리오 jumpType 가져오기  
        string jt = scenario.jumpType.ToString();
        var enumType = EnumUtils.GetEnumType(jt);
        if (enumType == null)
        {
            Debug.LogError(jt +" 점프 타입이 존재하지 않습니다.");
            return false;
        }
        
        // 2. jumpType과 일치한 TL 필터링
        var csvData =
            CsvParser.ReadWithHeader(DataName.CD_TimeLine, out var header);
        var timeLineData = CsvParser.UpdateData<TimeLine>(csvData, header);
        // 2-1. jumpType 일치 + enum 순서(있으면) 또는 order 컬럼 순서로 정렬
        var tlFiltered = Enumerable.ToList(
            timeLineData
                .Where(tl => tl.jumpType == jt)
                .Select((tl, idx) => new { tl, idx })
                .OrderBy(pair =>
                {
                    int enumOrder = Array.IndexOf(Enum.GetNames(enumType), pair.tl.name);
                    return enumOrder >= 0 ? enumOrder : 1000 + pair.idx;
                })
                .Select(pair => pair.tl)
        );                            
        
        // 3. TL id 보유 Procedure 필터링
        var prcCsvData = 
            CsvParser.ReadWithHeader(DataName.CD_Procedure, out var prcHeader);
        var prcData = CsvParser.UpdateData<Procedure>(prcCsvData, prcHeader);
        var tlIds = tlFiltered.Select(t => t.timelineID).ToHashSet();
        var procFiltered = prcData
            .Where(p => tlIds.Contains(p.parentTimelineId)).ToList();
        
        // 4. Procedure 데이터의 Instruction id 필터링
        var instCsvData = 
            CsvParser.ReadWithHeader(DataName.CD_Instruction, out var instHeader);
        var instData = CsvParser.UpdateData<Instruction>(instCsvData, instHeader);
        var procIds = procFiltered.Select(p => p.instructionId).ToHashSet();
        var instFiltered = instData.Where(i => procIds.Contains(i.id)).ToList();

        // 5. Evaluation 데이터를 설정된 JumpType에 맞게 필터링
        var evalCsvData = CsvParser.ReadWithHeader(DataName.CD_Evaluation, out var evalHeader);
        var evalData = CsvParser.UpdateData<Evaluation>(evalCsvData, evalHeader);
        var evalFiltered = evalData.Where(ev => ev.jumpType == jt).ToList();
        
        // 6. 필터링한 데이터들 정리
        timeLines = tlFiltered;
        procedures = procFiltered;
        instructions = instFiltered;
        evaluations = evalFiltered;
        
        // 7. 시나리오의 날씨Id로 사용할 날씨 데이터 필터링
        var wId = scenario.weatherId;
        var weatherCsvData = 
            CsvParser.ReadWithHeader(DataName.CD_Weather, out var weatherHeader);
        var weatherDataList = CsvParser.UpdateData<WeatherData>(weatherCsvData, weatherHeader);
        _weather = weatherDataList.SingleOrDefault(w => w.id == wId);
        if (_weather == null)
        {
            Debug.LogError($"날씨 데이터를 찾을 수 없습니다. weatherId: {wId}");
            return false;
        }

        // 8. 필터링한 날씨 데이터의 안개id로 사용할 안개 데이터 필터링
        var fId = _weather.fogId;
        var fogCsvData = 
            CsvParser.ReadWithHeader(DataName.CD_Fogs, out var fogHeader);
        var fogList = CsvParser.UpdateData<FogData>(fogCsvData, fogHeader);
        _fog = fogList.SingleOrDefault(f => f.id == fId);
        if (_fog == null)
        {
            Debug.LogError($"안개 데이터를 찾을 수 없습니다. fogId: {fId}");
            return false;
        }
        
        // 9. 시나리오의 routeId를 바탕으로 비행기 경로 정보들을 필터링
        var routeId = scenario.routeId;
        var routeCsvData = CsvParser.ReadWithHeader(DataName.CD_Routes, out var routeHeader);
        var routeList = CsvParser.UpdateData<Route>(routeCsvData, routeHeader);
        var route = routeList.SingleOrDefault(r => r.routeId == routeId);
        if (route == null)
        {
            Debug.LogError($"경로 데이터를 찾을 수 없습니다. routeId: {routeId}");
            return false;
        }
        var routeFiltered = routeList.Where(r => r.routeGroup == route.routeGroup).ToList();
        routes = routeFiltered;
        // 9-1. route가 프로시저 완료 조건인지 체크해서 설정
        SetIsCompleteRoute();
        
        // 10. 우발상황 데이터를 JumpType에 맞게 필터링
        var contingencyCsvData = CsvParser.ReadWithHeader(DataName.CD_Contingency, out var contingencyHeader);
        var contingencyList = CsvParser.UpdateData<Contingency>(contingencyCsvData, contingencyHeader);
        var ctgyFiltered = contingencyList.Where(ct => ct.jumpType == jt).ToList();
        contingencys = ctgyFiltered;
        
        Debug.Log("[DataManager] 시나리오 LoadData 완료");
        return true;
    }

    /* ── 조회 API ── */
    public TimeLine GetTimeLine(string id) => timeLines.FirstOrDefault(t => t.timelineID == id);
    public Procedure GetProcedure(string id) => procedures.FirstOrDefault(p => p.id == id);
    public Instruction GetInstruction(string id) => instructions.FirstOrDefault(i => i.id == id);
    public List<TimeLine> GetTimelineList() => timeLines;
    public List<Procedure> GetProcedureList() => procedures;

    public List<Procedure> GetProceduresBetween(int startIdx, int endIdx)
    {
        // procedures 안에서 startIdx ~ endIdx 사이에 프로시저 리스트를 가져온다 (startIdx, endIdx 제외)
        if (procedures == null || procedures.Count == 0)
        {
            Debug.LogWarning("[DataManager] GetProceduresBetween: procedures 리스트가 비어있습니다.");
            return new List<Procedure>();
        }
        
        // 인덱스 범위 검증
        if (startIdx < -1)
        {
            Debug.LogWarning($"[DataManager] GetProceduresBetween: startIdx({startIdx})가 -1보다 작습니다. -1로 조정합니다.");
            startIdx = -1;
        }
        
        if (endIdx > procedures.Count)
        {
            Debug.LogWarning($"[DataManager] GetProceduresBetween: endIdx({endIdx})가 리스트 크기({procedures.Count})를 초과합니다. 최대값으로 조정합니다.");
            endIdx = procedures.Count;
        }
        
        // startIdx와 endIdx 사이에 프로시저가 없는 경우
        if (startIdx >= endIdx - 1)
        {
            Debug.LogWarning($"[DataManager] GetProceduresBetween: startIdx({startIdx})와 endIdx({endIdx}) 사이에 프로시저가 없습니다. 빈 리스트를 반환합니다.");
            return new List<Procedure>();
        }
        
        // startIdx+1부터 endIdx-1까지의 프로시저 반환 (startIdx, endIdx 제외)
        var fromIndex = startIdx;
        var count = endIdx - startIdx - 1;
        return procedures.GetRange(fromIndex, count);
        
    }

    public int GetProcedureIdx(string id)
    {
        // 해당 id의 프로시저가 procedures 안에서 몇번째 idx인지 가져온다
        if (procedures == null || procedures.Count == 0)
        {
            Debug.LogWarning("[DataManager] GetProcedureIdx: procedures 리스트가 비어있습니다.");
            return -1;
        }
        
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[DataManager] GetProcedureIdx: id가 null이거나 비어있습니다.");
            return -1;
        }
        
        // FindIndex를 사용하여 해당 id를 가진 프로시저의 인덱스 찾기
        int index = procedures.FindIndex(p => p.id == id);
        
        if (index == -1)
        {
            Debug.LogWarning($"[DataManager] GetProcedureIdx: id '{id}'에 해당하는 프로시저를 찾을 수 없습니다.");
        }
        
        return index;
    }

    public int GetScenarioRouteIndex()
    {
        for (int i = 0; i < routes.Count; i++)
        {
            if (routes[i].routeId == scenario.routeId)
            {
                Debug.Log($"[DataManager] 시나리오 routeId '{scenario.routeId}'는 route 리스트의 {i}번 인덱스");
                return i;
            }
        }
        
        Debug.LogError($"[DataManager] 시나리오 routeId '{scenario.routeId}'를 route 리스트에서 찾을 수 없습니다!");
        return -1;
    }

    public void SetIsCompleteRoute()
    {
        int scenarioRouteIndex = GetScenarioRouteIndex();
        
        var routeOffsets = new Dictionary<string, int>
        {
            //{ "TakeOff", -2 },
            { "ThreeMinutes", -1 },
            { "OneMinutes", 0 }
        };

        foreach (var proc in routeOffsets)
        {
            if (!routeOffsets.TryGetValue(proc.Key, out var routeIdx)) continue;
            var targetIdx = scenarioRouteIndex + routeIdx;
            Debug.Log($"[DataManager] {proc.Key} - 목표 route 인덱스: {targetIdx} (시나리오 routeId: {scenarioRouteIndex})");
            routes[targetIdx].isCompletePoint = true;
        }
    }
    
    // 평가 항목 조회 매서드
    public List<Evaluation> GetProcEvaluationList() => Enumerable.ToList(evaluations.Where(ev => ev.category == EvCategory.Procedure));
    public List<Evaluation> GetIncEvaluationList() => Enumerable.ToList(evaluations.Where(ev => ev.category == EvCategory.Incident));
    public List<Evaluation> GetValueEvaluationList() =>
        evaluations.Where(ev => ev.category is not EvCategory.Procedure and not EvCategory.Incident).ToList();
    public Evaluation GetEvaluation(string id) => evaluations.FirstOrDefault(e => e.id == id);
    public Evaluation GetEvaluation(EvName name) => evaluations.FirstOrDefault(e => e.name == name.ToString());
    public WeatherData GetWeatherData() => _weather;
    public FogData GetFogData() => _fog;

    /*  필요하다면 타임라인별 절차 리스트 캐시도 제공 */
    public IEnumerable<Procedure> GetProceduresOfTL(string tlId) =>
        procedures.Where(p => p.parentTimelineId == tlId);
}
