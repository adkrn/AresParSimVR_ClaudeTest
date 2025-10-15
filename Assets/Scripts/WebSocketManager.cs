// WebSocketManager.cs
//
// - 기존 파일(:contentReference[oaicite:1]{index=1})을 기반으로, 
//   서버에서 보내는 메시지 포맷과 맞지 않던 JsonUtility 파싱을 모두 Newtonsoft.Json으로 교체했습니다.
// - 방 목록(room-list) 처리, viewer-ready 처리, signal 처리 코드를 통합/보강했습니다.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using UnityEngine.Networking;
using Random = System.Random; // 반드시 Unity 패키지 매니저에서 설치 필요 (Packages/manifest.json에 "com.unity.nuget.newtonsoft-json" 추가)

public class WebSocketManager : MonoBehaviour
{
    [Header("기본서버 WebSocket 설정")]
    public string serverUrl; // 반드시 ws://localhost:3000 형태로 설정
    [Header("파일서버 URL (예: https://192.168.0.172:3020/upload)")]
    public string uploadUrl;
    [Header("파일서버 포트")] 
    public string uploadUrlPort = "3020";
    
    public string clientId;        // 자동 생성
    public string role = "viewer"; // "vr" or "viewer"으로 UI에서 세팅
    public string roomId;          // Viewer라면 추가로 UI에서 세팅
    public bool isClosedNetwork;  
    public WebSocket ws;

    // 외부에서 구독할 이벤트
    public Action<List<string>> OnRoomListReceived;         // 서버 → room-list 수신
    public Action<string> OnViewerReady;                    // 서버 → viewer-ready 수신
    public Action<string, string> OnSignalReceived;         // 서버 → signal 수신 (signalJson, fromId)
    public Action<List<string>> OnClientListReceived;       // 서버 → client-list 수신

    static readonly Queue<Action> _jobs = new Queue<Action>();
    public static WebSocketManager Inst { get; private set; }

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
    void Update()
    {
        lock (_jobs)
        {
            while (_jobs.Count > 0)
                _jobs.Dequeue()();
        }
    }
    public static void Enqueue(Action job)
    {
        lock (_jobs) _jobs.Enqueue(job);
    }

    /// <summary>
    /// UI에서 “Connect” 또는 “Join Room” 직전에 호출합니다.
    /// </summary>
    public void Init()
    {
        Debug.Log("<color=magenta>[WebSocketManager]</color> Init()");
        serverUrl = $"wss://{NetworkManager.Inst.serverIP}:{NetworkManager.Inst.serverPort}";
        uploadUrl = $"https://{NetworkManager.Inst.serverIP}:{uploadUrlPort}/upload";
        ConnectToWebSocket();
    }

    public void ConnectToWebSocket()
    {
        Debug.Log("<color=magenta>[WebSocketManager]</color> ConnectToWebSocket()");
        try
        {
            Debug.Log($"<color=magenta>[WebSocketManager]</color> WebSocket 연결 시도: {serverUrl}");
            ws = new WebSocket(serverUrl);

            // ───────────────────────────────────────────────────────────────────
            // 로그인 때 cookieContainer에 저장된 'connect.sid' 쿠키를 꺼내고,
            // WebSocket 요청 헤더에 직접 'Cookie' 값을 붙여 준다.
            string cookieHost = $"https://{NetworkManager.Inst.serverIP}:{NetworkManager.Inst.serverPort}";
            var cookieHeader = NetworkManager.Inst.cookieContainer.GetCookieHeader(new Uri(cookieHost));
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                // 두 가지 선택 중 하나:
                // 1) ws.SetCookie(...) 를 사용하거나,
                // 2) CustomHeaders에 직접 붙이기
                ws.SetCookie(new WebSocketSharp.Net.Cookie("connect.sid", ExtractSessionId(cookieHeader)));
                // 또는: ws.CustomHeaders = new Dictionary<string,string>{{"Cookie", cookieHeader}};
            }
            // ───────────────────────────────────────────────────────────────────

            ws.OnOpen += (sender, e) =>
            {
                try
                {
                    Debug.Log($"<color=magenta>[WebSocketManager]</color> WebSocket 연결됨: {serverUrl}");
                    // RegisterClient를 메인 스레드에서 실행
                    Enqueue(() => RegisterClient());
                }
                catch (Exception ex)
                {
                    Debug.LogError($"<color=magenta>[WebSocketManager]</color> OnOpen 예외: {ex}");
                }
            };

            ws.OnMessage += (sender, e) => { HandleIncomingMessage(e.Data); };

            ws.OnError += (sender, e) => { Debug.LogError($"<color=magenta>[WebSocketManager]</color> WebSocket 오류: {e.Message}"); };

            ws.OnClose += (sender, e) => { Debug.Log("<color=magenta>[WebSocketManager]</color> WebSocket 연결 종료"); };

            ws.ConnectAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"<color=magenta>[WebSocketManager]</color> WebSocket 연결 중 예외 발생: {ex}");
        }
    }
    
    private string ExtractSessionId(string cookieHeader)
    {
        // 예시: "connect.sid=s%3Aabcdef1234; Path=/; HttpOnly"
        // 필요한 부분만 파싱해서 반환
        var parts = cookieHeader.Split(';');
        foreach (var p in parts)
        {
            if (p.Trim().StartsWith("connect.sid="))
                return p.Trim().Substring("connect.sid=".Length);
        }

        return "";
    }

    /// <summary>
    /// 연결 후, 반드시 register 메시지를 보내야 합니다. 
    /// 기존 파일(:contentReference[oaicite:2]{index=2})의 RegisterClient()와 로직이 유사하나, 
    /// “Viewer일 때는 방 목록 요청 → 이후 Join 으로 흐름 분기” 로 변경했습니다.
    /// </summary>
    private void RegisterClient()
    {
        // 1) 클라이언트 ID 생성 - WS_DB_Client에서 받은 participantId 사용
        var wsClient = FindAnyObjectByType<WS_DB_Client>();

        if (wsClient != null)
        {
            clientId = "client_" + wsClient.WebSocketID;
        }

        WebRTCManager.Inst.clientId = clientId;
        Debug.Log($"<color=magenta>[WebSocketManager]</color> RegisterClient(): clientId={clientId}, role={role}, roomId={roomId}");

        RequestRoomList();
    }

    /// <summary>
    /// Viewer가 Connect 버튼을 눌러 연결할 때, 서버로 부터 방 목록을 달라고 요청합니다.
    /// 웹: ws.send(JSON.stringify({ type:'room-list' }))
    /// </summary>
    public void RequestRoomList()
    {
        Debug.Log("<color=magenta>[WebSocketManager]</color> RequestRoomList() 실행");
        if (ws == null || ws.ReadyState != WebSocketState.Open) return;

        var payload = new Dictionary<string, object>()
        {
            ["type"] = "room-list"
        };
        string json = JsonConvert.SerializeObject(payload);
        ws.Send(json);
        Debug.Log($"<color=magenta>[WebSocketManager]</color> >> room-list 요청: {json}");
    }

    /// <summary>
    /// Viewer가 실제 “Join Room” 버튼을 눌렀을 때 호출해야 합니다.
    /// 기존 RegisterClient()에서 “방이 없는 Viewer”는 오류만 나고 종료되므로, 
    /// 실제 Join 요청 시에는 roomId를 UI로부터 다시 받아서 register 메시지를 재전송합니다.
    /// 
    /// (1) role=="viewer"인 상태에서, UI에서 roomId를 새로 세팅 → 아래 메서드 호출
    /// (2) register 메시지 재전송 → server에서 rooms.get(roomId).viewers.add(clientId)
    /// (3) 그 다음엔 local Stream 셋업을 WebRTCManager쪽에서 해 줍니다.
    /// </summary>
    public void JoinRoom(string chosenRoomId)
    {
        roomId = chosenRoomId;
        Debug.Log($"<color=magenta>[WebSocketManager]</color> JoinRoom(): clientId ={clientId} :: role = {role} :: roomId={roomId}");

        // 다시 register 메시지 전송 (서버에 Viewer로 정상 참여시키기 위함)
        var registerPayload = new Dictionary<string, object>()
        {
            ["type"] = "register",
            ["data"] = new Dictionary<string, object>()
            {
                ["clientId"] = clientId,
                ["role"]     = role,
                ["roomId"]   = roomId
            }
        };
        string json = JsonConvert.SerializeObject(registerPayload);
        ws.Send(json);
        Debug.Log($"<color=magenta>[WebSocketManager]</color> >> register(JoinRoom) 요청: {json}");

            WebRTCManager.Inst.SetupLocalStreamWith3DAndCanvas();
    }

    /// <summary>
    /// 서버로부터 들어오는 모든 메시지를 이곳에서 JSON으로 파싱하여 처리합니다.
    /// </summary>
    private void HandleIncomingMessage(string rawJson)
    {
        Debug.Log($"<color=magenta>[WebSocketManager]</color> 메시지 수신 → {rawJson}");

        // 최소 { "type":"xxx", ... } 형태인지 검사
        var baseMsg = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson);
        if (!baseMsg.ContainsKey("type")) return;
        string type = baseMsg["type"].ToString();

        switch (type)
        {
            // ───────────────────────────────────────────────────────────────────────────────────
            // 1) ROOM-LIST 수신 → 웹 UI의 방 목록 드롭다운에 뿌려야 함
            //    서버(app-wss.js)에서는 rooms Map의 key들(배열)로 보냄: { type:'room-list', rooms:[...string] }
            // ───────────────────────────────────────────────────────────────────────────────────
            case "room-list":
                var rooms = JsonConvert.DeserializeObject<List<string>>(baseMsg["rooms"].ToString());
                Debug.Log($"<color=magenta>[WebSocketManager]</color> room-list 응답 받음 → {string.Join(", ", rooms)}");
                OnRoomListReceived?.Invoke(rooms);
                roomId = rooms[0];
                //role = "instructor";

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    WebRTCManager.Inst.Init();
                    JoinRoom(roomId);
                });
                break;

            // ───────────────────────────────────────────────────────────────────────────────────
            // 2) VIEWER-READY 수신 → VR은 이 신호를 받으면 StartCall(targetId)로 Offer 생성
            //    서버: { type:'viewer-ready', data:{ clientId: "<viewerId>" } }
            // ───────────────────────────────────────────────────────────────────────────────────
            case "viewer-ready":
            {
                var dataObj = baseMsg["data"].ToString();
                var vrData = JsonConvert.DeserializeObject<ViewerReadyData>(dataObj);
                string viewerClientId = vrData.clientId;
                Debug.Log($"<color=magenta>[WebSocketManager]</color> viewer-ready 받음 → viewerId={viewerClientId}");
                OnViewerReady?.Invoke(viewerClientId);
                break;
            }

            // ───────────────────────────────────────────────────────────────────────────────────
            // 3) SIGNAL 중계 수신 → { type:'signal', from:'<fromId>', signal:{ ... } }
            //    - Offer/Answer/ICE candidate JSON이 “signal” 필드 안에 포함되어 있음
            //    - fromId도 baseMsg["from"] 혹은 baseMsg["fromId"]로 들어옴
            // ───────────────────────────────────────────────────────────────────────────────────
            case "signal":
            {
                // “from” 필드가 기본 레벨에 있음
                string fromId = baseMsg.ContainsKey("from")
                    ? baseMsg["from"].ToString()
                    : (baseMsg.ContainsKey("fromId") ? baseMsg["fromId"].ToString() : "");
                var signalPayload = JsonConvert.SerializeObject(baseMsg["signal"]);
                Debug.Log($"<color=magenta>[WebSocketManager]</color> signal 받음 (from={fromId}) → {signalPayload}");
                // OnSignalReceived?.Invoke(signalPayload, fromId); <- 잘못된 구조: 메인 스레드에서 실행해야 함
                UnityMainThreadDispatcher.Enqueue(() => { OnSignalReceived?.Invoke(signalPayload, fromId); });
                break;
            }

            // ───────────────────────────────────────────────────────────────────────────────────
            // (옵션) MOTION 데이터 수신: 사용자 요구에 따라 3D 위치/회전 정보 등을 주고받고 싶다면 
            //   server/app-wss.js에서 { type:'motion', pos:…, rot:… } 형태로 포워딩해주면,
            //   아래와 같이 처리할 수 있음. (아직 구현하지 않음)
            // ───────────────────────────────────────────────────────────────────────────────────
            // case "motion":
            //     {
            //         // var pos = baseMsg["pos"]; var rot = baseMsg["rot"]; 
            //         // Motion 데이터 UI 등에 뿌려줄 때 사용
            //         break;
            //     }

            case "client-list":
                var clients = JsonConvert.DeserializeObject<List<string>>(baseMsg["clients"].ToString());
                OnClientListReceived?.Invoke(clients);
                break;

            default:
            {
                Debug.LogWarning($"<color=magenta>[WebSocketManager]</color> 알 수 없는 메시지 타입: {type}");
                break;
            }
        }
    }

    /// <summary>
    /// Offer/Answer/ICE candidate을 상대에게 보낼 때 사용합니다.
    /// 호출 예) SendSignal(targetId, JsonUtility.ToJson(candidate))
    /// </summary>
    public void SendSignal(string targetId, string signalJson)
    {
        if (ws == null || ws.ReadyState != WebSocketState.Open) return;

        var payload = new Dictionary<string, object>()
        {
            ["type"]     = "signal",
            ["from"]     = clientId,
            ["targetId"] = targetId,
            ["signal"]   = JsonConvert.DeserializeObject(signalJson)
        };
        // string json = JsonConvert.SerializeObject(payload);
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        
        ws.Send(json);
        Debug.Log($"<color=magenta>[WebSocketManager]</color> >> signal 전송 → targetId:{targetId}, payload:{json}");
    }

    private void OnApplicationQuit()
    {
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // 서버에서 viewer-ready 데이터 구조
    // { type:'viewer-ready', data:{ clientId: "<viewerId>" } }
    // ───────────────────────────────────────────────────────────────────
    [Serializable]
    private class ViewerReadyData
    {
        public string clientId;
    }
    
    public void UploadVideo(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[VideoUploader] 파일을 찾을 수 없습니다: {filePath}");
            return;
        }
        StartCoroutine(UploadCoroutine(filePath, "Group1"));
    }

    
    /// <summary>
    /// 로컬에 저장된 비디오 파일 서버에 업로드
    /// </summary>
    /// <param name="filePath">파일위치경로-경로/파일이름/확장자 포함</param>
    /// <param name="groupName">교관이 지정한 그룹 이름을 받아서 전송: 차후 받아오도록 설정-20250701</param>
    /// <returns></returns>
    private IEnumerator UploadCoroutine(string filePath, string groupName)
    {
        Debug.Log($"[VideoUploader] ▶ 요청 보냄: {uploadUrl}");

        byte[] videoBytes = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);

        var formData = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("date", DateTime.Now.ToString("yyyy-MM-dd")),
            new MultipartFormDataSection("group",groupName),
            new MultipartFormDataSection("uploader", clientId),
            new MultipartFormFileSection("video", videoBytes, fileName, "video/mp4"),
        };

        using (var www = UnityWebRequest.Post(uploadUrl, formData))
        {
            www.timeout = 60;

            // 요청 전송 및 응답 대기
            yield return www.SendWebRequest();

            Debug.Log($"[VideoUploader] ◀ 응답 완료: 코드={www.responseCode}, 에러={www.error}");

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[VideoUploader] 업로드 성공: {www.downloadHandler.text}");
            }
            else
            {
                Debug.LogError($"[VideoUploader] 업로드 실패 ({www.responseCode}): {www.error}");
            }
        }
    }
}
