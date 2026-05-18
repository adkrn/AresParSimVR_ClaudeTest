using UnityEngine;

/// <summary>
/// VR Hand 잡기 위치를 낙하산 조종 값으로 변환
/// 하드웨어 연결시 자동으로 비활성화됨
/// </summary>
public class ParachuteRiserControl : MonoBehaviour
{
    [Header("Riser Configuration")]
    [Tooltip("Left or Right riser")]
    public RiserSide side;

    [Header("Position Limits")]
    [Tooltip("Y position when not pulled (0)")]
    [SerializeField] private float upperLimit = 0f;

    [Tooltip("Y position when fully pulled (-0.25)")]
    [SerializeField] private float lowerLimit = -0.25f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    // Components
    private AresHardwareParagliderController controller;

    // Riser side enum
    public enum RiserSide { Left, Right }

    //--------------------------------------------------
    // Unity Lifecycle
    //--------------------------------------------------

    // 진단 (임시) — ParachuteRiserControl 생명주기 추적
    void Awake()
    {
        Debug.Log($"[DiagPRC:{gameObject.name}:{side}] Awake t={Time.time:F2}, GO activeInHierarchy={gameObject.activeInHierarchy}, comp enabled={enabled}");
    }
    void OnEnable()
    {
        Debug.Log($"[DiagPRC:{gameObject.name}:{side}] OnEnable t={Time.time:F2}, GO activeInHierarchy={gameObject.activeInHierarchy}, comp enabled={enabled}, localPos={transform.localPosition}");
    }
    void OnDisable()
    {
        Debug.Log($"[DiagPRC:{gameObject.name}:{side}] OnDisable t={Time.time:F2}, GO activeInHierarchy={gameObject.activeInHierarchy}");
    }

    void Start()
    {
        // Find controller
        controller = FindAnyObjectByType<AresHardwareParagliderController>();

        Debug.Log($"[DiagPRC:{gameObject.name}:{side}] Start t={Time.time:F2}, controller={(controller != null ? "FOUND" : "NULL")}, comp enabled={enabled}");

        if (controller == null)
        {
            Debug.LogError($"[ParachuteRiserControl] AresHardwareParagliderController not found! ({side} riser)");
            enabled = false;
            return;
        }

        if (showDebugLog)
            Debug.Log($"[ParachuteRiserControl] Initialized ({side} riser)");
    }

    private bool _diagFirstUpdateLogged = false;
    void Update()
    {
        // 진단 (임시) — 첫 Update 호출 시점 + 활성 상태 캡처
        if (!_diagFirstUpdateLogged)
        {
            Debug.Log($"[DiagPRC:{gameObject.name}:{side}] FirstUpdate t={Time.time:F2}, useHW={AresHardwareService.Inst?.UseHardware}, isConn={AresHardwareService.Inst?.IsConnected}, localPos={transform.localPosition}");
            _diagFirstUpdateLogged = true;
        }

        // 하드웨어 연결시 VR 입력 비활성화
        if (AresHardwareService.Inst.UseHardware &&
            AresHardwareService.Inst.IsConnected)
        {
            return;
        }

        // VR Hand 위치 → 조종 값 변환
        UpdateControllerFromVRPosition();
    }

    /// <summary>
    /// VR Hand 위치를 읽어서 조종 값으로 변환
    /// </summary>
    private void UpdateControllerFromVRPosition()
    {
        // 현재 Y 위치
        float yPos = transform.localPosition.y;
        
        float pullValue = Mathf.InverseLerp(upperLimit, lowerLimit, yPos);

        // 조종 값 전달
        if (side == RiserSide.Left)
        {
            controller.SetLeftPullFromVR(pullValue);
        }
        else
        {
            controller.SetRightPullFromVR(pullValue);
        }

        // Debug
        if (showDebugLog && pullValue > 0.01f)
        {
            Debug.Log($"[ParachuteRiserControl] {side} - Y: {yPos:F3}, Pull: {pullValue:F2}");
        }
    }
}
