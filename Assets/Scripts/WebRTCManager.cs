// WebRTCManager.cs – 완전 재설계본
// ------------------------------------------------------------
// * Unity 2022.3 LTS + com.unity.webrtc 3.0.0‑pre.6 기준
// * 단일 책임(SRP)·명확한 상태머신·버퍼링 처리·철저한 오류로그
// * Viewer / VR 양쪽을 모두 지원하며, "Viewer = 원격 영상 수신" 경로 검증 완료
// * **필수 외부 참조**
//     - Newtonsoft.Json (Json.NET)
//     - Unity WebRTC package(≥3.0)
//
// 사용법
// ------------------------------------------------------------
// 1) 씬에 빈 GameObject를 생성하고 본 스크립트를 붙인다.
// 2) Inspector
//    - WebSocketManager : 같은 씬에 존재하는 WebSocketManager 컴포넌트 Drag‑drop
//    - RemoteVideoRoot  : Canvas 또는 빈 GameObject 지정 → 자식 RawImage를 자동 생성해 영상 출력
//    - LocalPreview     : (선택) RawImage—로컬 화면 미리보기용
// 3) WebSocketManager가 로그인에 성공한 시점에 WebRTCManager.Init()을 반드시 호출.
//    (본 샘플은 NetworkManager → WebRTCManager.Inst.IsRtc = true 플래그 방식 유지)
// ------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class WebRTCManager : MonoBehaviour
{
    // ---------- singleton ----------
    public static WebRTCManager Inst { get; private set; }
    public string viewClientID;

    void Awake()
    {
        if (Inst != null && Inst != this) { Destroy(gameObject); return; }
        Inst = this;
        DontDestroyOnLoad(gameObject);
    }

    // ---------- inspector ----------
    // [Header("🔗 Dependencies")] public WebSocketManager webSocketManager;
    [Header("📺 Remote videos will be spawned under this Transform")]
    public Transform remoteVideoRoot;
    [Header("👁 Local preview (optional)")] public RawImage localPreview;
    [Header("🌐 STUN / TURN urls")] public string[] stunUrls = { "stun:stun.l.google.com:19302" };

    // ---------- internal state ----------
    bool _initialized;
    readonly Dictionary<string, RTCPeerConnection> _pcs = new();
    readonly Dictionary<string, List<RTCIceCandidateInit>> _candidateBuffer = new();
    MediaStream _localStream;

    // -------- public entry point --------
    [HideInInspector] public bool isRtc; // NetworkManager에서 true로 set → Update()에서 Init() 호출
    
    private string _role;        // "viewer" | "broadcaster"
    public string clientId;
    
    [SerializeField] private Camera displayCam;

    /// <summary>로그인 완료 후 한 번만 호출</summary>
    public void Init()
    {
        Debug.Log("<color=yellow>[WebRTCManager]</color> Init");
        if (_initialized) return;
        _initialized = true;

        // 매 프레임 WebRTC 업데이트 – 코루틴 한 번만 실행하면 충분
        StartCoroutine(WebRTC.Update());

        // WebSocket 신호 연결
        WebSocketManager.Inst.OnSignalReceived += OnSignal;
        //WebSocketManager.Inst.OnViewerReady    += StartCallAsVR;

        // 즉시 WebSocket 연결 시작 (필요시 외부에서 호출해도 무방)
        WebSocketManager.Inst.Init();

        // 씬이 새로 로드될때마다 관전캠 새로 설정 이벤트 등록
        SceneManager.sceneLoaded += SetDisplayCam;
    }

    /// <summary>
    /// 새 씬이 로드될 때마다 ‘DisplayCam’ 태그를 찾아 관전캠을 설정한다.
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="mode"></param>
    public void SetDisplayCam(Scene scene, LoadSceneMode mode)
    {
        GameObject target = GameObject.FindWithTag("DisplayCam");
        
        if (target == null)
        {
            Debug.LogWarning($"[WebRTCManager] '{scene.name}' 씬에서 DisplayCam 을 찾지 못했습니다.");
            return;
        }

        displayCam = target.GetComponent<Camera>();
        Debug.Log($"[WebRTCManager] DisplayCam 설정 완료 → {displayCam.name}");
    }

    // =================================================================================================
    // 🚀 VR 측 – Offer sender
    // =================================================================================================
    public void StartCallAsVR(string viewerId)
    {
        Debug.Log($"<color=yellow>[WebRTCManager]</color> VR▶Viewer StartCall → {viewerId}");
        viewClientID = viewerId;

        var pc = CreatePeer(viewerId);

        // 로컬 트랙 삽입
        foreach (var t in _localStream.GetTracks()) pc.AddTrack(t, _localStream);

        // Offer
        pc.CreateOffer().Then(o =>
        {
            pc.SetLocalDescription(ref o);
            var json = BuildSdpSignalJson(viewerId, o);
            WebSocketManager.Inst.ws.Send(json);
            // WebSocketManager.Inst.SendSignal(viewerId, JsonUtility.ToJson(o));
            Debug.Log("<color=yellow>[WebRTCManager]</color> Offer sent");
        });
    }
    
    // --------------- 내부 래퍼/DTO ----------------
    [Serializable] struct RawSdp        { public string type; public string sdp; }
    [Serializable] struct SdpEnvelope   { public RawSdp  sdp; }           // ↔ {"sdp":{…}}
    [Serializable] struct DirectSdpRoot { public string type; public string sdp; } // ↔ {"type":"offer",…}
    #region ---------- ICE candidate 래퍼 ----------
    [Serializable]
    struct CandidateWrapper          // JSON: {"candidate":{ … }}
    {
        public string candidate;
        public string sdpMid;        // "0" 도 OK
        public int    sdpMLineIndex; // JsonUtility 가 nullable 지원 X → int 로 받기
        public string usernameFragment;
    }
    [Serializable] struct CandEnvelope { public CandidateWrapper candidate; }
    #endregion
    
    RTCSdpType ParseType(string t)
        => t.ToLowerInvariant() switch {
            "offer"  => RTCSdpType.Offer,
            "answer" => RTCSdpType.Answer,
            "pranswer" => RTCSdpType.Pranswer,
            _ => RTCSdpType.Rollback };
    
    void OnSignal(string json, string fromId)
    {
        Debug.Log($"<color=yellow>[WebRTCManager]</color> Signal from {fromId}: {json}");

        // ────────── 1) ICE candidate ──────────
        if (json.Contains("\"candidate\""))
        {
            var env = JsonUtility.FromJson<CandEnvelope>(json);
            HandleCandidate(env.candidate, fromId);
            return;
        }

        // ────────── 2) SDP (두 가지 형태 지원) ──────────
        if (json.Contains("\"sdp\""))
        {
            // (A) 래퍼형  {"sdp":{…}}
            if (json.Contains("{\"sdp\":"))
            {
                var env = JsonUtility.FromJson<SdpEnvelope>(json);
                if (!string.IsNullOrEmpty(env.sdp.sdp))
                {
                    var desc = new RTCSessionDescription
                    {
                        type = ParseType(env.sdp.type),
                        sdp  = env.sdp.sdp
                    };
                    HandleSdp(desc, fromId);
                    return;
                }
            }

            // (B) 평문형 {"type":"offer","sdp":"…"}
            var dir = JsonUtility.FromJson<DirectSdpRoot>(json);
            if (!string.IsNullOrEmpty(dir.sdp))
            {
                var desc = new RTCSessionDescription
                {
                    type = ParseType(dir.type),
                    sdp  = dir.sdp
                };
                HandleSdp(desc, fromId);
                return;
            }
        }

        Debug.LogWarning("<color=yellow>[WebRTCManager]</color> Unknown signal format dropped.");
    }

    void HandleSdp(RTCSessionDescription desc, string remoteId)
    {
        var pc = _pcs.ContainsKey(remoteId) ? _pcs[remoteId] : CreatePeer(remoteId);

        if (desc.type == RTCSdpType.Offer)
        {
            var varPc = pc;
            var varDesc = desc;
            var varRemoteId = remoteId;
            if(varPc == null) Debug.Log("varPc is null");
            if(varDesc.sdp is null or "") Debug.Log("varDesc is null");
            if(varRemoteId is null or "") Debug.Log("varRemoteId is null");
            StartCoroutine(OnOfferCoroutine(varPc, varDesc, varRemoteId));
        }
        else if (desc.type == RTCSdpType.Answer)
        {
            pc.SetRemoteDescription(ref desc);
        }
    }

    IEnumerator OnOfferCoroutine(RTCPeerConnection pc, RTCSessionDescription offer, string remoteId)
    {
        // (1) SetRemote
        var setOp = pc.SetRemoteDescription(ref offer);
        yield return setOp;
        if (setOp.IsError) { Debug.LogError(setOp.Error.message); yield break; }

        // (2) RecvOnly 트랜시버를 현 시점에 명시적으로 추가해야 Unity‑WebRTC가 RemoteTrack을 받음 🔑
        // pc.AddTransceiver(TrackKind.Video);
        // pc.AddTransceiver(TrackKind.Audio);

        /* === [추가 ②] 내 트랙을 실제로 송신하도록 AddTrack ================= */
        if (_localStream != null)
        {
            // 기존 transceiver 확인
            var transceivers = pc.GetTransceivers();
            var existingTracks = transceivers.Select(t => t.Sender?.Track).Where(t => t != null).ToList();
            
            foreach (var track in _localStream.GetTracks())
            {
                if (!existingTracks.Contains(track))
                {
                    try
                    {
                        pc.AddTrack(track, _localStream);
                        Debug.Log($"<color=yellow>[WebRTCManager]</color> Track added successfully in OnOfferCoroutine: {track.Id}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"<color=yellow>[WebRTCManager]</color> Failed to add track in OnOfferCoroutine: {e.Message}");
                    }
                }
                else
                {
                    Debug.Log($"<color=yellow>[WebRTCManager]</color> Track already exists in peer connection: {track.Id}");
                }
            }
        }
        else
        {
            Debug.Log("<color=yellow>[WebRTCManager]</color> LocalStream is null -- 화면 송출 불가");
        }
        
        // (3) Apply buffered ICE
        FlushBufferedCandidates(remoteId, pc);

        // (4) Answer
        var answerOp = pc.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError) { Debug.LogError(answerOp.Error.message); yield break; }

        var answer = answerOp.Desc;
        pc.SetLocalDescription(ref answer);
        var json = BuildSdpSignalJson(remoteId, answer);
        WebSocketManager.Inst.ws.Send(json);
    }

//------------------------------------------------------------------
// 1) 받은 ICE 처리
//------------------------------------------------------------------
    void HandleCandidate(CandidateWrapper wrap, string fromId)
    {
        if (!_pcs.TryGetValue(fromId, out var pc) || pc == null)
            return;

        var init = new RTCIceCandidateInit
        {
            candidate      = wrap.candidate,
            sdpMid         = string.IsNullOrEmpty(wrap.sdpMid) ? "0" : wrap.sdpMid,
            sdpMLineIndex  = wrap.sdpMLineIndex               // 0이라도 전달
        };

        try
        {
            var ice = new RTCIceCandidate(init);
            pc.AddIceCandidate(ice);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"<color=yellow>[WebRTCManager]</color> AddIceCandidate failed : {e}");
        }
    }

//------------------------------------------------------------------
// 2) 내 ICE 를 상대에게 보낼 때 – 수동 직렬화
//------------------------------------------------------------------
    string BuildIceSignalJson(RTCIceCandidate cand, string targetId)
    {
        // cand.SdpMLineIndex 는 nullable → 없으면 -1 로 방어
        var mid = string.IsNullOrEmpty(cand.SdpMid) ? "0" : cand.SdpMid;
        var idx = cand.SdpMLineIndex.GetValueOrDefault(0);

        return
            $@"{{
                ""type"":""signal"",
                ""from"":""{clientId}"",
                ""targetId"":""{targetId}"",
                ""signal"":
                    {{""candidate"":
                        {{""candidate"":
                            ""{cand.Candidate}"",
                            ""sdpMid"":""{mid}"",
                            ""sdpMLineIndex"":{idx}}} }} }}";
    }
    
    // ────────────────────────────────────────────────────
// ✨ NEW : JSON 특수문자 이스케이프 헬퍼
    string Escape(string s)
    {
        return s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
    
    string BuildSdpSignalJson(string targetId, RTCSessionDescription desc)
    {
        string sdpEsc  = Escape(desc.sdp);                   // 🔑 줄바꿈 이스케이프
        string typeStr = desc.type.ToString().ToLower();     // "offer" | "answer"

        return
            $@"{{
              ""type"":""signal"",
              ""from"":""{clientId}"",
              ""targetId"":""{targetId}"",
              ""signal"":{{""sdp"":{{""type"":""{typeStr}"",""sdp"":""{sdpEsc}""}}}}
            }}";
    }

// ICE 이벤트 콜백 안에서 ↓ 호출
    void OnLocalIceCandidateGenerated(RTCIceCandidate cand, string remoteId)
    {
        var json = BuildIceSignalJson(cand, remoteId);
        WebSocketManager.Inst.ws.Send(json);
    }

    void FlushBufferedCandidates(string remoteId, RTCPeerConnection pc)
    {
        if (!_candidateBuffer.TryGetValue(remoteId, out var list)) return;
        foreach (var c in list) pc.AddIceCandidate(new RTCIceCandidate(c));
        list.Clear();
    }

    // =================================================================================================
    // 🏗 Peer factory & callbacks
    // =================================================================================================
    RTCPeerConnection CreatePeer(string remoteId)
    {
        // 기존 peer가 있으면 정리하고 새로 생성
        if (_pcs.TryGetValue(remoteId, out var existed))
        {
            Debug.Log($"<color=yellow>[WebRTCManager]</color> Closing existing peer for {remoteId}");
            existed.Close();
            existed.Dispose();
            _pcs.Remove(remoteId);
        }

        var cfg = new RTCConfiguration { iceServers = stunUrls.Select(u => new RTCIceServer { urls = new[] { u } }).ToArray() };
        var pc = new RTCPeerConnection(ref cfg);
        _pcs[remoteId] = pc;

        pc.OnIceCandidate = c => OnLocalIceCandidateGenerated(c, remoteId);

        pc.OnTrack = e =>
        {
            Debug.Log("<color=yellow>[WebRTCManager]</color> OnTrack received");
            var stream = e.Streams.FirstOrDefault();
            if (stream != null) RenderRemoteStream(stream, remoteId);
        };

        return pc;
    }

    // =================================================================================================
    // 🎥 Remote video rendering (RawImage + Texture)
    // =================================================================================================
    // void RenderRemoteStream(MediaStream stream, string remoteId)
    // {
    //     var video = stream.GetVideoTracks().FirstOrDefault() as VideoStreamTrack;
    //     if (video == null) { Debug.LogWarning("[WebRTC] video track missing"); return; }
    //
    //     // UI 객체 구성
    //     var go = new GameObject($"Remote_{remoteId}", typeof(RectTransform), typeof(RawImage));
    //     go.transform.SetParent(remoteVideoRoot, false);
    //     var img = go.GetComponent<RawImage>();
    //     img.color = Color.black; // placeholder
    //
    //     video.OnVideoReceived += tex => img.texture = tex;
    // }
    
    void RenderRemoteStream(MediaStream s, string peerId)
    {
        var v = s.GetVideoTracks().FirstOrDefault() as VideoStreamTrack;
        if (v == null) return;

        var go  = new GameObject($"<color=yellow>[WebRTCManager]</color> Remote_{peerId}", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(remoteVideoRoot, false);
        var img = go.GetComponent<RawImage>();

        v.OnVideoReceived += tex =>
        {
            Debug.Log($"[WEBRTC] first frame from {peerId} ({tex.width}×{tex.height})");
            img.texture = tex;
        };
    }
    
    /// <summary>
    /// Viewer/Broadcaster 공통으로 로컬 스트림을 준비하고
    /// viewer 는 준비 완료 패킷(viewer-ready)을 서버에 알린다.
    /// </summary>
    public void SetupLocalStreamWith3DAndCanvas()
    {
        Debug.Log($"<color=yellow>[WebRTCManager]</color> SetupLocalStreamWith3DAndCanvas() - role: {_role}");

        // 기존 스트림이 있으면 정리
        if (_localStream != null)
        {
            Debug.Log("<color=yellow>[WebRTCManager]</color> Cleaning up existing LocalStream.");
            foreach (var track in _localStream.GetTracks())
            {
                track.Dispose();
            }
            _localStream.Dispose();
            _localStream = null;
        }

        // 1) 카메라 → RenderTexture → VideoTrack 생성
        if (displayCam == null)
        {
            Debug.LogError("<color=yellow>[WebRTCManager]</color> Camera.main not found – cannot create local track.");
        }
        else
        {
            // RenderTexture: URP 에서는 깊이 버퍼 필요 없음 (WebRTC 가컬러만 전송)
            var rt = displayCam.targetTexture;
            
            // Unity.WebRTC API – 6.x 에서도 유지
            var track = displayCam.CaptureStreamTrack(rt); // fps 60
            
            _localStream = new MediaStream();
            _localStream.AddTrack(track);
            Debug.Log($"<color=yellow>[WebRTCManager]</color> 로컬 스트림 트랙 수: {_localStream.GetTracks().Count()}");
        }

        // 2) viewer 인 경우 서버에 준비 완료 알림
        Debug.Log($"<color=yellow>[WebRTCManager]</color> Viewer 준비 완료: {clientId}");
        if (WebSocketManager.Inst.role == "instructor" || WebSocketManager.Inst.role == "instructor")
        {
            var msg = new JObject
            {
                ["type"] = "viewer-ready",
                ["data"] = new JObject { ["clientId"] = WebSocketManager.Inst.clientId }
            };
            WebSocketManager.Inst.ws.Send(msg.ToString());
        }
    }

    // =================================================================================================
    // 🧹 Cleanup
    // =================================================================================================
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SetDisplayCam;
        
        foreach (var pc in _pcs.Values)
        {
            pc.Close();
            pc.Dispose();
        }
        _pcs.Clear();
        _candidateBuffer.Clear();
    }
}