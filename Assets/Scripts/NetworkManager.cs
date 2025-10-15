using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using System.Net;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Inst { get; private set; }

    [Header("서버 주소 설정")] [SerializeField] public string serverIP = "192.168.0.156";
    public int serverPort = 3000;
    public string loginEndPath = "login";
    private bool isConnected = false;
    public bool isAutoLogIn = false;
    public string preparedId = "user010";
    public string preparedPw = "1234";
    
    [Header("SSL 설정")]
    [Tooltip("SSL 인증서 검증을 무시합니다. 개발 환경에서만 사용하세요.")]
    [SerializeField] private bool bypassSSLValidation = true;
    
    public CookieContainer cookieContainer = new CookieContainer();

    private void Awake()
    {
        // SSL 인증서 처리 설정 (HttpWebRequest용)
        if (bypassSSLValidation)
        {
            SSLCertificateHelper.ConfigureHttpWebRequest();
        }
        
        if (Inst == null)
        {
            Inst = this;
            DontDestroyOnLoad(gameObject);
            Init();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 서버 연결을 시도합니다.
    /// </summary>
    public void Init()
    {
        Debug.Log("[NetworkManager] Init(): 서버 연결 시도");
        StartCoroutine(ConnectToServer());
    }

    private IEnumerator ConnectToServer()
    {
        string url = $"https://{serverIP}:{serverPort}/";
        Debug.Log($"[NetworkManager] ConnectToServer(): {url}");
        UnityWebRequest request = UnityWebRequest.Get(url);
        
        // SSL 인증서 핸들러 설정 (UnityWebRequest용)
        if (bypassSSLValidation)
        {
            request.certificateHandler = SSLCertificateHelper.GetCertificateHandler();
            Debug.Log("[NetworkManager] SSL 인증서 검증 우회 활성화");
        }
        
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            OnServerConnected(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[NetworkManager] 서버 연결 실패: {request.error}");
        }
    }

    private void OnServerConnected(string response)
    {
        Debug.Log($"[NetworkManager] 서버 연결 성공: {response}");
        isConnected = true;

        if (isAutoLogIn)
        {
            StartCoroutine(LoginRequest(false, preparedId, preparedPw));
        }
        else
        {
            ShowLoginUI();
        }
    }

    private IEnumerator LoginRequest(bool isInstructor, string username, string password = "")
    {
        var loginData = new Dictionary<string, string>
        {
            { "username", username },
            { "password", password }
        };
        string jsonData = JsonConvert.SerializeObject(loginData);

        // 1) HttpWebRequest로 로그인 → 세션 쿠키 얻기
        var url = $"https://{serverIP}:{serverPort}/login";
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.CookieContainer = cookieContainer;
        using (var sw = new System.IO.StreamWriter(request.GetRequestStream()))
        {
            sw.Write(jsonData);
        }

        // 서버가 Set-Cookie 헤더를 응답으로 보내면 cookieContainer에 저장됨
        HttpWebResponse response = null;
        try
        {
            response = (HttpWebResponse)request.GetResponse();
        }
        catch (WebException ex)
        {
            Debug.LogError($"로그인 실패: {ex.Message}");
            yield break;
        }

        // 필요하다면 로그인 결과 판정 로직 (status code, body 등)
        response.Close();

        // 2) Unity 메인 스레드로 돌아가기
        yield return null;

        // 3) 이제 UnityWebSocket 연결 시, Cookie 헤더를 직접 추가할 수 있음
        Debug.Log("[NetworkManager] 로그인 성공");
        WebRTCManager.Inst.Init();
        _ = DataManager.Inst;
    }

    private void ShowLoginUI()
    {
        Debug.Log("[NetworkManager] 로그인 UI 표시");
        MainManager.Inst.ShowPopup(PopupType.Normal, "로그인", "아이디와 암호를 입력하세요.");
    }
}

public class PopupInfo: MonoBehaviour
{
    public TMP_Text titleText;
    public TMP_Text messageText;

    public void Init(string title, string message)
    {
        titleText.text = title;
        messageText.text = message;
    }
}

public enum PopupType
{
    Normal,
    Success,
    Fail,
    Error,
    Alert,
    Event,
    Alarm
}
