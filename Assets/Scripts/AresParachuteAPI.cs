using System.Runtime.InteropServices;
using UnityEngine;

public class AresParachuteAPI : MonoBehaviour
{
    private static AresParachuteAPI _instance;
    public static AresParachuteAPI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AresParachuteAPI>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("AresParachuteAPI");
                    _instance = go.AddComponent<AresParachuteAPI>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    // Constants from the API documentation
    public const int ARES_PARASIM__MOTION_VALUE_SCALE_MIN = 0;
    public const int ARES_PARASIM__MOTION_VALUE_SCALE_CENTER = 10000;
    public const int ARES_PARASIM__MOTION_VALUE_SCALE_MAX = 20000;
    public const int ARES_PARASIM__ACTION_CIRCLING_VALUE_MIN = 0;
    public const int ARES_PARASIM__ACTION_CIRCLING_VALUE_CENTER = 18000;
    public const int ARES_PARASIM__ACTION_CIRCLING_VALUE_MAX = 36000;
    public const int ARES_PARASIM__MOTION_DOF_SPEED_SCALE_MIN = 0;
    public const int ARES_PARASIM__MOTION_DOF_SPEED_SCALE_CENTER = 3000;
    public const int ARES_PARASIM__MOTION_DOF_SPEED_SCALE_MAX = 6000;
    public const int ARES_PARASIM__RISER_LENGTH_SCALE_ZERO = 0;
    public const int ARES_PARASIM__RISER_LENGTH_SCALE_CENTER = 50;
    public const int ARES_PARASIM__RISER_LENGTH_SCALE_SPAN = 100;

    // Yawing modes
    public const int ARES_PARASIM__YAWING_MODE_DEFAULT = 0;
    public const int ARES_PARASIM__YAWING_MODE_STOP = 1;
    public const int ARES_PARASIM__YAWING_MODE_CW = 2;
    public const int ARES_PARASIM__YAWING_MODE_CCW = 3;
    public const int ARES_PARASIM__YAWING_MODE_EX_CW = 4;
    public const int ARES_PARASIM__YAWING_MODE_EX_CCW = 5;

    // Event values
    public enum MotionEvent
    {
        None = 0,
        SitDown = 1,
        FreeFall = 2,            // 낙하 (산개전)
        Deploy_Standard = 3,     // 낙하산 산개
        Deploy_High = 4,         // 낙하산 산개
        Landing = 5,             // 착륙 직전
        Landed = 6               // 착륙
    }

    // DLL Imports
    [DllImport("ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__Initial(uint nComport);

    [DllImport("ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__Destroy();

    [DllImport("ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__StateCheck();

    [DllImport("ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__SetWindControl(int windStrength, int windDirection, int windVariation, int reserved);

    [DllImport(dllName: "ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__SetMotionControl(ARES_PARASIM_MOTION_DATA motionData);

    [DllImport(dllName: "ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__GetMotionControl(out ARES_PARASIM_FEEDBACK_DATA feedbackData);

    [DllImport(dllName: "ARESParaSimDllMotionExternC.dll")]
    public static extern bool ARESParaSIM__SetEvent(uint eventNum);

    [DllImport(dllName: "ARESParaSimDllMotionExternC.dll")]
    public static extern uint ARESParaSIM__GetEvent();

    [DllImport(dllName: "ARESParaSimDllMotionExternC.dll")]
    public static extern int ARESParaSIM__GetJump();

    // Connection state
    private bool isConnected = false;
    private uint comPort = 0; // COM1
    private uint timeout = 1000; // 1 second

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public bool Initialize(uint port = 0)
    {
        comPort = port;
        isConnected = ARESParaSIM__Initial(comPort);
        
        if (isConnected)
            Debug.Log($"ARES Parachute Simulator connected on COM{comPort + 1}");
        else
            Debug.LogError($"Failed to connect to ARES Parachute Simulator on COM{comPort + 1}");
            
        return isConnected;
    }

    private void OnDestroy()
    {
        if (isConnected)
        {
            ARESParaSIM__Destroy();
            isConnected = false;
        }
    }

    private void OnApplicationQuit()
    {
        if (isConnected)
        {
            ARESParaSIM__Destroy();
            isConnected = false;
        }
    }

    public bool IsConnected()
    {
        if (isConnected)
        {
            isConnected = ARESParaSIM__StateCheck();
        }
        return isConnected;
    }

    // Helper methods for value conversion
    public static uint AngleToMotionValue(float angle)
    {
        // Convert 0-360 degrees to motion scale (0-360 maps to 0-36000 for yawing, 0-20000 for roll)
        return (uint)(angle * 100f);
    }

    public static float MotionValueToAngle(uint value)
    {
        // Convert motion scale back to degrees
        return value / 100f;
    }

    public static uint SpeedToRPM(float speed)
    {
        // Convert speed to RPM value (0-5000 range)
        return (uint)Mathf.Clamp(speed, 0, 5000);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ARES_PARASIM_MOTION_DATA
{
    public int RollLeft;             // 0-20000 (중심 10000)
    public int RollLeftSpeed;        // 0-6000 RPM
    public int RollRight;            // 0-20000 (중심 10000)
    public int RollRightSpeed;       // 0-6000 RPM
    public int Yawing;               // 0-36000 (중심 18000)
    public int YawingSpeed;          // 0-6000 RPM
    public int YawingMode;           // 0-5 (회전 모드)

    // Initialize with default values
    public void Init()
    {
        RollLeft = AresParachuteAPI.ARES_PARASIM__MOTION_VALUE_SCALE_CENTER;
        RollLeftSpeed = AresParachuteAPI.ARES_PARASIM__MOTION_DOF_SPEED_SCALE_MIN;
        RollRight = AresParachuteAPI.ARES_PARASIM__MOTION_VALUE_SCALE_CENTER;
        RollRightSpeed = AresParachuteAPI.ARES_PARASIM__MOTION_DOF_SPEED_SCALE_MIN;
        Yawing = AresParachuteAPI.ARES_PARASIM__ACTION_CIRCLING_VALUE_CENTER;
        YawingSpeed = AresParachuteAPI.ARES_PARASIM__MOTION_DOF_SPEED_SCALE_MIN;
        YawingMode = AresParachuteAPI.ARES_PARASIM__YAWING_MODE_DEFAULT;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ARES_PARASIM_FEEDBACK_DATA
{
    public int RollLeft;                  // 0-20000
    public int RollRight;                  // 0-20000
    public int Yawing;                    // 0-36000
    public int LTRiserLineCurrentLength;  // 0-100%
    public int LTRiserLineDetect;         // 0 or 1
    public int RTRiserLineCurrentLength;  // 0-100%
    public int RTRiserLineDetect;         // 0 or 1
}