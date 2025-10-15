using System;
using System.Collections;
using System.Threading;
using UnityEngine;


/// <summary>
/// ARES 하드웨어 통신을 전담하는 독립 서비스
/// 별도 스레드에서 10Hz로 하드웨어와 통신
/// </summary>
public class AresHardwareService : MonoBehaviour
{
    public static AresHardwareService Inst { get; private set; }
    
    [Header("Hardware Settings")] 
    [SerializeField] private bool useHardware = false;

    [SerializeField] private uint comPort = 0; // COM 포트 번호 (0 = COM1)
    [SerializeField] private uint timeout = 100; // API 타임아웃 (ms)
    [SerializeField] private bool debugMode = false;
    
    [Header("Connection Settings")]
    [SerializeField] private bool autoReconnect = true; // 자동 재연결 활성화
    [SerializeField] private float reconnectInterval = 5f; // 재연결 시도 간격 (초)
    [SerializeField] private int maxReconnectAttempts = 3; // 최대 재연결 시도 횟수
    [SerializeField] private int interval = 20;

    // Thread Management
    private Thread communicationThread;
    private bool threadRunning = false;
    private bool isSend = false;
    private bool isNewMotionData = false;

    // Shared Data
    private AresMotionData outgoingData;
    public AresFeedbackData incomingData;

    // API Data Structures
    private ARES_PARASIM_MOTION_DATA apiMotionData;
    private ARES_PARASIM_FEEDBACK_DATA apiFeedbackData;

    // Public Properties
    public bool IsConnected { get; private set; }
    public bool UseHardware => useHardware;
    public AresFeedbackData LatestFeedback => incomingData;
    
    // Events
    public delegate void FeedbackReceivedHandler(AresFeedbackData feedback);
    public event FeedbackReceivedHandler OnFeedbackReceived;
    
    public delegate void ConnectionStatusHandler();
    public event ConnectionStatusHandler OnConnectionLost;
    public event ConnectionStatusHandler OnConnectionRestored;
    public event ConnectionStatusHandler OnConnectionFailed;

    // Event Flags for Main Thread
    private bool feedbackEventPending = false;
    private bool connectionLostEventPending = false;
    private AresFeedbackData pendingFeedbackData;

    public AresEvent eventType;
    public bool isJump = false;
    private bool isCheckJump = false;
    
    #region Unity Lifecycle

    void Awake()
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
        
        // 초기 데이터 구조 초기화
        outgoingData = new AresMotionData();
        incomingData = new AresFeedbackData();
        //apiMotionData.Init();
    }

    void Start()
    {
        if (useHardware)
        {
            Initialize();
        }
    }
    

    void OnDestroy()
    {
        Shutdown();
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopThread();
        }
        else if (useHardware && IsConnected)
        {
            StartThread();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// ARES 하드웨어 초기화
    /// </summary>
    public bool Initialize()
    {
        if (IsConnected)
        {
            Debug.LogWarning("[AresHardware] 이미 초기화됨");
            return true;
        }

        // DLL 로딩 전 환경 정보 출력
        Debug.Log($"[AresHardware] DLL 초기화 시도 중...");
        Debug.Log($"[AresHardware] 현재 플랫폼: {UnityEngine.Application.platform}");
        Debug.Log($"[AresHardware] Unity 버전: {UnityEngine.Application.unityVersion}");
        Debug.Log($"[AresHardware] 운영체제: {UnityEngine.SystemInfo.operatingSystem}");
        Debug.Log($"[AresHardware] Unity Editor 비트: {System.IntPtr.Size * 8}bit");
        
        // DLL 경로 확인
        string dllPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Plugins", "ARESParaSimDllMotionExternC.dll");
        Debug.Log($"[AresHardware] DLL 경로: {dllPath}");
        Debug.Log($"[AresHardware] DLL 파일 존재: {System.IO.File.Exists(dllPath)}");
        
        try
        {
            // AresParachuteAPI 초기화
            if (!AresParachuteAPI.Instance.Initialize(comPort))
            {
                Debug.LogError($"[AresHardware] COM{comPort + 1} 포트 초기화 실패");
                useHardware = false;
                return false;
            }
        }
        catch (DllNotFoundException dllEx)
        {
            Debug.LogError($"[AresHardware] DLL을 찾을 수 없습니다: {dllEx.Message}");
            Debug.LogError("[AresHardware] 해결방법:");
            Debug.LogError("1. Visual C++ 2015-2022 재배포 가능 패키지 (x64) 설치");
            Debug.LogError("   다운로드: https://aka.ms/vs/17/release/vc_redist.x64.exe");
            Debug.LogError("2. .NET Framework 4.7.2 이상 설치");
            Debug.LogError("3. DirectX End-User Runtime 설치");
            Debug.LogError("4. Windows 업데이트 확인");
            Debug.LogError("5. DLL 파일이 Assets/Plugins 폴더에 있는지 확인");
            useHardware = false;
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AresHardware] 초기화 중 오류 발생: {e.Message}");
            Debug.LogError($"[AresHardware] 타입: {e.GetType().Name}");
            useHardware = false;
            return false;
        }

        IsConnected = true;
        apiMotionData.Init();
        Debug.Log($"[AresHardware] COM{comPort + 1} 포트 연결 성공");

        // 통신 스레드 시작
        StartThread();
        
        return true;
    }

    public void ResetHardware()
    {
        var motionData = new AresMotionData
        {
            RollRightLength = 5000,
            RollRightSpeed = 1000,
            RollLeftLength = 5000,
            RollLeftSpeed = 1000,
            YawAngle = 180,
            YawSpeed = 1000,
            YawMode = 0
        };
        
        SendMotionData(motionData);
        Debug.Log($"[AresHardware] 하드웨어 초기화 전송");
    }

    /// <summary>
    /// 모션 데이터 전송 (메인 스레드에서 호출)
    /// </summary>
    public void SendMotionData(AresMotionData data)
    {
        Debug.Log("[AresHardware] SendMotionData 실행");
        if (!IsConnected)
        {
            Debug.Log("[AresHardware] 모션 데이터 전송 실패 : 통신 불량");
            return;
        }

        isNewMotionData = true;
        outgoingData = data;
        // var success = AresParachuteAPI.ARESParaSIM__SetMotionControl(sendData);
        //
        // if (!success)
        // {
        //     Debug.Log("[AresHardware] 모션 데이터 전송 실패 : 전송 오류");
        //     return;
        // }
        //
        // Debug.Log("[AresHardware] 모션 데이터 전송 성공");
        // if (debugMode)
        // {
        //     LogSendData(sendData);
        // }
    }

    /// <summary>
    /// 이벤트 설정 (낙하, 전개, 착륙 등)
    /// </summary>
    public void SetEvent(AresEvent pEventType)
    {
        if (!IsConnected)
        {
            Debug.Log("[AresHardware] 이벤트 설정 전송 실패 : 통신 불량");
            return;
        }

        eventType = pEventType;
        isSend = true;
        // var success = AresParachuteAPI.ARESParaSIM__SetEvent((uint)eventType);
        // if (!success)
        // {
        //     Debug.Log("[AresHardware] 이벤트 설정 전송 실패 : 전송 오류");
        //     return;
        // }
        //
        // Debug.Log($"[AresHardware] 이벤트 설정 전송 성공: {eventType}");
    }

    /// <summary>
    /// 점프 상태 받아오기
    /// </summary>
    /// <returns></returns>
    public void GetJump()
    {
        if (!IsConnected)
        {
            //Debug.Log("[AresHardware] 점프 상태 받아오기 실패 : 통신 불량");
            return;
        }

        isCheckJump = true;
    }

    /// <summary>
    /// 바람 설정
    /// </summary>
    public void SetWindControl(int strength, int direction, int variation)
    {
        if (!IsConnected) return;

        AresParachuteAPI.ARESParaSIM__SetWindControl(strength, direction, variation, 0);

        if (debugMode)
        {
            Debug.Log($"[AresHardware] 바람 설정 - 강도: {strength}, 방향: {direction}, 변화: {variation}");
        }
    }

    /// <summary>
    /// 최신 피드백 데이터 가져오기
    /// </summary>
    public bool GetLatestFeedback(out AresFeedbackData feedback)
    {
        feedback = incomingData;
        return true;
    }

    /// <summary>
    /// 연결 상태 확인
    /// </summary>
    public bool CheckConnection()
    {
        if (!IsConnected) return false;

        IsConnected = AresParachuteAPI.ARESParaSIM__StateCheck();
        return IsConnected;
    }

    #endregion

    #region Thread Management

    private void StartThread()
    {
        if (threadRunning)
        {
            Debug.LogWarning("[AresHardware] 스레드가 이미 실행 중");
            return;
        }

        threadRunning = true;
        communicationThread = new Thread(CommunicationLoop)
        {
            IsBackground = true,
            Name = "ARES Communication Thread"
        };
        communicationThread.Start();

        Debug.Log("[AresHardware] 통신 스레드 시작");
    }

    private void StopThread()
    {
        if (!threadRunning) return;

        threadRunning = false;

        if (communicationThread is { IsAlive: true })
        {
            // 최대 500ms 대기
            if (!communicationThread.Join(500))
            {
                Debug.LogWarning("[AresHardware] 스레드 정상 종료 실패, 강제 종료");
                communicationThread.Abort();
            }

            communicationThread = null;
        }

        Debug.Log("[AresHardware] 통신 스레드 정지");
    }

    /// <summary>
    /// 통신 스레드 메인 루프
    /// </summary>
    private void CommunicationLoop()
    {
        while (threadRunning)
        {
            try
            {
                var success =
                    AresParachuteAPI.ARESParaSIM__GetMotionControl(
                        out ARES_PARASIM_FEEDBACK_DATA localFeedbackData); // 수신된 원형 데이터
                if (success) // 유효성 검사
                {
                    if (debugMode) LogFeedbackData(localFeedbackData); // 로그 생성용 코드
                    apiFeedbackData = localFeedbackData; // 이전 프레임과 현재 프레임 상 데이터 비교용

                    // 회전값 받아서 게임에 바로 적용
                    var convertedData = ConvertFromApiFormat(localFeedbackData);
                    incomingData = convertedData;
                    OnFeedbackReceived?.Invoke(incomingData);
                }

                // 모션 데이터 전송
                if (isNewMotionData)
                {
                    var sendData = ConvertToApiFormat(outgoingData);
                    var sendSuccess = AresParachuteAPI.ARESParaSIM__SetMotionControl(sendData);
                    isNewMotionData = false;
                    if (sendSuccess)
                    {
                        Debug.Log("데이터 전송 성공");
                        Thread.Sleep(20);
                    }
                }

                // 이벤트 전송
                if (isSend)
                {
                    var eventSuc = AresParachuteAPI.ARESParaSIM__SetEvent((uint)eventType);
                    isSend = false;
                    if (eventSuc)
                    {
                        Debug.Log($"[AresHardware] 이벤트 설정 전송 성공: {eventType}");
                        Thread.Sleep(20);
                    }
                }

                if (isCheckJump)
                {
                    var jumpValue = AresParachuteAPI.ARESParaSIM__GetJump();
                    isJump = jumpValue > 0;
                    isCheckJump = false;

                    if (debugMode && isJump)
                    {
                        Debug.Log($"[AresHardware] 점프 상태 확인: {jumpValue}");
                    }
                    Thread.Sleep(20);
                }
            }
            catch (ThreadAbortException)
            {
                // 정상 종료
                Debug.Log("[AresHardware] 쓰레드 종료");
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AresHardware] 통신 오류: {e.Message}");
            }
        }
    }


    // private void TestT()
    // {
    //     while (threadRunning)
    //     {
    //         var successFeedback = AresParachuteAPI.ARESParaSIM__GetMotionControl(out ARES_PARASIM_FEEDBACK_DATA localFeedbackData); // 수신된 원형 데이터
    //         if(!successFeedback) print("수신 실패");
    //         print($"피드백 데이터 Yawing : {localFeedbackData.Yawing}, LRiser : {localFeedbackData.LTRiserLineCurrentLength}");
    //         
    //          var sendData = new ARES_PARASIM_MOTION_DATA()
    //          {
    //              RollSpeed = 0,
    //              Roll = 0,
    //              Yawing = 18000,
    //              YawingSpeed = 500,
    //              YawingMode = 0
    //          };
    //          var success = AresParachuteAPI.ARESParaSIM__SetMotionControl(sendData);
    //         if(!success) print("전송 실패");
    //         else Thread.Sleep(20);
    //     }
    // }

    private void Shutdown()
    {
        // 스레드 정지
        StopThread();
        threadRunning = false;

        if (IsConnected)
        {
            try
            {
                AresParachuteAPI.ARESParaSIM__Destroy();
                Debug.Log("[AresHardware] 하드웨어 종료 완료");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AresHardware] 리셋 실패: {e.Message}");
            }

            IsConnected = false;
        }
    }

    #endregion
    
    #region Validation

    /// <summary>
    /// 피드백 데이터 유효성 검증
    /// </summary>
    private bool ValidateFeedback(AresFeedbackData feedback)
    {
        // 라이저 길이 범위 검증 (0-100%)
        if (feedback.LeftRiserLength < 0 || feedback.LeftRiserLength > 100)
        {
            Debug.LogWarning($"[AresHardware] 비정상 왼쪽 라이저 값: {feedback.LeftRiserLength}%");
            return false;
        }

        if (feedback.RightRiserLength < 0 || feedback.RightRiserLength > 100)
        {
            Debug.LogWarning($"[AresHardware] 비정상 오른쪽 라이저 값: {feedback.RightRiserLength}%");
            return false;
        }

        // Yaw 위치 범위 검증 (0-360도)
        if (feedback.YawPosition < 0 || feedback.YawPosition > 360)
        {
            Debug.LogWarning($"[AresHardware] 비정상 Yaw 값: {feedback.YawPosition}°");
            return false;
        }


        // 이전 값과 비교하여 급격한 변화 감지
        float leftDelta = Mathf.Abs(feedback.LeftRiserLength - incomingData.LeftRiserLength);
        float rightDelta = Mathf.Abs(feedback.RightRiserLength - incomingData.RightRiserLength);

        const float MAX_DELTA = 50f; // 한번에 50% 이상 변화는 비정상

        if (leftDelta > MAX_DELTA || rightDelta > MAX_DELTA)
        {
            if (debugMode)
            {
                Debug.LogWarning($"[AresHardware] 급격한 변화 감지 - L:{leftDelta}%, R:{rightDelta}%");
            }
            // 급격한 변화는 경고만 하고 허용 (실제 빠른 동작일 수 있음)
        }
        
        return true;
    }
    
    private bool ValidateFeedback2(ARES_PARASIM_FEEDBACK_DATA feedback)
    {
        // 라이저 길이 범위 검증 (0-100%)
        if (feedback.LTRiserLineCurrentLength < 0 || feedback.LTRiserLineCurrentLength > 100)
        {
            Debug.LogWarning($"[AresHardware] 비정상 왼쪽 라이저 값: {feedback.LTRiserLineCurrentLength}%");
            return false;
        }

        if (feedback.RTRiserLineCurrentLength < 0 || feedback.RTRiserLineCurrentLength > 100)
        {
            Debug.LogWarning($"[AresHardware] 비정상 오른쪽 라이저 값: {feedback.RTRiserLineCurrentLength}%");
            return false;
        }

        // Yaw 위치 범위 검증 (0-360도)
        if (feedback.Yawing < 0 || feedback.Yawing > 36000)
        {
            Debug.LogWarning($"[AresHardware] 비정상 Yaw 값: {feedback.Yawing}°");
            return false;
        }


        // 이전 값과 비교하여 급격한 변화 감지
        float leftDelta = Mathf.Abs(feedback.LTRiserLineCurrentLength - apiFeedbackData.LTRiserLineCurrentLength);
        float rightDelta = Mathf.Abs(feedback.RTRiserLineCurrentLength - apiFeedbackData.RTRiserLineCurrentLength);

        const float MAX_DELTA = 50f; // 한번에 50% 이상 변화는 비정상

        if (leftDelta > MAX_DELTA || rightDelta > MAX_DELTA)
        {
            if (debugMode)
            {
                Debug.LogWarning($"[AresHardware] 급격한 변화 감지 - L:{leftDelta}%, R:{rightDelta}%");
            }
            // 급격한 변화는 경고만 하고 허용 (실제 빠른 동작일 수 있음)
        }
        
        return true;
    }


    #endregion

    #region Data Conversion

    /// <summary>
    /// 게임 데이터를 ARES API 형식으로 변환
    /// Roll 범위: 5000 (중립) ~ 15000 (최대 당김)
    /// Yawing 중간값: 18000으로 설정
    /// </summary>
    private ARES_PARASIM_MOTION_DATA ConvertToApiFormat(AresMotionData data)
    {
        // Roll 변환: 0~1 범위를 5000~15000으로 매핑
        // 0 = 5000 (중립), 1 = 15000 (최대 당김)
        var rollLeft = 10000 + (int)(data.RollLeftLength * 10000);
        var rollRight = 10000 + (int)(data.RollRightLength * 10000);

        // 범위 제한 (안전을 위해)
        rollLeft = Mathf.Clamp(rollLeft, 10000, 15000);
        rollRight = Mathf.Clamp(rollRight, 10000, 15000);

        var rollLeftSpeed = (int)data.RollLeftSpeed;
        var rollRightSpeed = (int)data.RollRightSpeed;

        // Yaw: 0~360도를 0~36000으로 변환
        float normalizedYaw = data.YawAngle % 360f;
        int yawingValue = (int)(normalizedYaw * 100f);
        if (yawingValue >= 36000) yawingValue = yawingValue - 36000;

        return new ARES_PARASIM_MOTION_DATA
        {
            RollLeft = rollLeft,
            RollLeftSpeed = rollLeftSpeed,
            RollRight = rollRight,
            RollRightSpeed = rollRightSpeed,
            Yawing = yawingValue,
            YawingSpeed = (int)data.YawSpeed,
            YawingMode = 0
        };
    }

    /// <summary>
    /// ARES API 피드백을 게임 데이터로 변환
    /// </summary>
    private AresFeedbackData ConvertFromApiFormat(ARES_PARASIM_FEEDBACK_DATA data)
    {
        return new AresFeedbackData
        {
            RollRight = (data.RollRight / 2f) / 100f,
            RollLeft = (data.RollLeft / 2f) / 100f,
            YawPosition = data.Yawing / 100f,
            LeftRiserLength = data.LTRiserLineCurrentLength,
            RightRiserLength = data.RTRiserLineCurrentLength,
            LeftRiserDetected = data.LTRiserLineDetect > 0,
            RightRiserDetected = data.RTRiserLineDetect > 0
        };
    }

    #endregion

    #region Debug Logging

    private void LogFeedbackData(ARES_PARASIM_FEEDBACK_DATA feedback)
    {
        if (apiFeedbackData.Yawing == feedback.Yawing) return;
        // 수신 데이터
        Debug.Log($"Yaw: {feedback.Yawing / 100f:F1}°, " +
                  $"RollRight: {feedback.RollRight}, " +
                  $"RollLeft: {feedback.RollLeft}," +
                  $"L-Riser: {feedback.LTRiserLineCurrentLength}% (Detect: {feedback.LTRiserLineDetect}), " +
                  $"R-Riser: {feedback.RTRiserLineCurrentLength}% (Detect: {feedback.RTRiserLineDetect})");
    }
    
    private void LogSendData(ARES_PARASIM_MOTION_DATA sendData)
    {
        // 송신 데이터
        float yawDegree = sendData.Yawing / 100f;

        Debug.Log($"RollRight:{sendData.RollRight}," +
                  $"RollRightSpeed:{sendData.RollRightSpeed}," +
                  $"RollLeft:{sendData.RollLeft},"+
                  $"RollLeftSpeed:{sendData.RollLeftSpeed},"+
                  $"Yaw: yaw:{sendData.Yawing}, " +
                  $"YawSpeed: {sendData.YawingSpeed}, " +
                  $"YawMode: {sendData.YawingMode}");
    }

    #endregion
}

    /// <summary>
    /// ARES로 전송할 모션 데이터
    /// </summary>
    [System.Serializable]
    public struct AresMotionData
    {
        public float RollLeftLength;      // 왼쪽 롤 길이
        public float RollLeftSpeed;
        public float RollRightLength;     // 오른쪽 롤 길이
        public float RollRightSpeed;
        public float YawAngle;            // Yaw 각도 (0 ~ 360)
        public int YawSpeed;              // Yaw Speed
        public int YawMode;
    }
    
    /// <summary>
    /// ARES로부터 받은 피드백 데이터
    /// </summary>
    [System.Serializable]
    public struct AresFeedbackData
    {
        public float RollLeft;
        public float RollRight;
        public float YawPosition;         // 현재 Yaw 위치
        public float LeftRiserLength;     // 왼쪽 라이저 길이
        public float RightRiserLength;    // 오른쪽 라이저 길이
        public bool LeftRiserDetected;    // 왼쪽 라이저 감지 여부
        public bool RightRiserDetected;   // 오른쪽 라이저 감지 여부
    }
    
    /// <summary>
    /// ARES 이벤트 타입
    /// </summary>
    public enum AresEvent
    {
        None = 0,
        SitDown = 1,
        FreeFall = 2,            // 낙하 (산개전)
        Deploy_Standard = 3,     // 낙하산 산개
        Deploy_High = 4,         // 낙하산 산개
        Landing = 5,             // 착륙 직전
        Landed = 6               // 착륙
    }
