using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VR FPS 표시 시스템을 관리하는 매니저 클래스
/// 자동으로 VR Canvas를 생성하고 FPS 표시를 설정합니다
/// </summary>
public class VRFPSManager : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private bool createCanvasAutomatically = true;
    [SerializeField] private bool enableOnStart = true;
    
    [Header("Canvas Settings")]
    [SerializeField] private float canvasDistance = 2.0f; // VR 카메라로부터의 거리
    [SerializeField] private Vector3 canvasPosition = new Vector3(-1.5f, 1.2f, 2.0f); // 왼쪽 상단
    [SerializeField] private Vector3 canvasScale = Vector3.one * 0.01f;
    
    [Header("FPS Display Settings")]
    [SerializeField] private Vector2 panelSize = new Vector2(300, 120);
    [SerializeField] private int fontSize = 24;
    
    // 생성된 컴포넌트들 참조
    private Canvas vrCanvas;
    private VRFPSDisplay fpsDisplay;
    private Camera vrCamera;
    
    public static VRFPSManager Instance { get; private set; }
    
    private void Awake()
    {
        // 싱글톤 패턴
        if (Instance == null)
        {
            Instance = this;
        }
    }
    
    private void Start()
    {
        if (enableOnStart)
        {
            SetupVRFPSDisplay();
        }
    }
    
    [ContextMenu("Setup VR FPS Display")]
    public void SetupVRFPSDisplay()
    {
        // VR 카메라 찾기
        FindVRCamera();
        
        if (vrCamera == null)
        {
            Debug.LogError("[VRFPSManager] VR 카메라를 찾을 수 없습니다. CenterEyeAnchor 또는 Main Camera가 필요합니다.");
            return;
        }
        
        // 자동으로 Canvas 생성
        if (createCanvasAutomatically)
        {
            CreateVRCanvas();
            CreateFPSDisplay();
        }
        
        Debug.Log("[VRFPSManager] VR FPS 표시 시스템 설정 완료");
    }
    
    private void FindVRCamera()
    {
        // 일반적인 VR 카메라 이름들을 순서대로 검색
        string[] vrCameraNames = { 
            "CenterEyeAnchor", 
            "Main Camera", 
            "Camera", 
            "VRCamera" 
        };
        
        foreach (string cameraName in vrCameraNames)
        {
            GameObject cameraObj = GameObject.Find(cameraName);
            if (cameraObj != null)
            {
                vrCamera = cameraObj.GetComponent<Camera>();
                if (vrCamera != null)
                {
                    Debug.Log($"[VRFPSManager] VR 카메라 발견: {cameraName}");
                    return;
                }
            }
        }
        
        // 마지막으로 현재 활성화된 카메라 중에서 찾기
        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in allCameras)
        {
            if (cam.enabled && cam.gameObject.activeInHierarchy)
            {
                vrCamera = cam;
                Debug.Log($"[VRFPSManager] 활성 카메라 사용: {cam.name}");
                return;
            }
        }
    }
    
    private void CreateVRCanvas()
    {
        // Canvas GameObject 생성
        GameObject canvasObj = new GameObject("VR_FPS_Canvas");
        canvasObj.transform.parent = vrCamera.transform;
        
        // Canvas 컴포넌트 추가 및 설정
        vrCanvas = canvasObj.AddComponent<Canvas>();
        vrCanvas.renderMode = RenderMode.WorldSpace;
        vrCanvas.worldCamera = vrCamera;
        
        // CanvasScaler 추가 (선택사항)
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        // GraphicRaycaster 추가 (VR 상호작용용)
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // 위치 및 크기 설정 (VR에 최적화)
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.localPosition = canvasPosition;
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = canvasScale;
        canvasRect.sizeDelta = new Vector2(200, 90);

        canvasObj.layer = 5;
        
        Debug.Log("[VRFPSManager] VR Canvas 생성 완료");
    }
    
    private void CreateFPSDisplay()
    {
        // FPS Display Panel 생성
        GameObject panelObj = new GameObject("FPS_Panel");
        panelObj.transform.SetParent(vrCanvas.transform, false);
        
        // Panel 설정
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f); // 반투명 검은색 배경
        
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.anchoredPosition = Vector2.zero;
        
        // FPS Text 생성
        GameObject fpsTextObj = new GameObject("FPSText");
        fpsTextObj.transform.SetParent(panelObj.transform, false);
        
        TextMeshProUGUI fpsText = fpsTextObj.AddComponent<TextMeshProUGUI>();
        fpsText.text = "FPS: --";
        fpsText.fontSize = fontSize;
        fpsText.color = Color.white;
        fpsText.alignment = TextAlignmentOptions.TopLeft;
        
        RectTransform fpsTextRect = fpsTextObj.GetComponent<RectTransform>();
        fpsTextRect.anchorMin = new Vector2(0, 0.5f);
        fpsTextRect.anchorMax = new Vector2(1, 1);
        fpsTextRect.sizeDelta = Vector2.zero;
        fpsTextRect.anchoredPosition = new Vector2(10, -10);
        
        // Frame Time Text 생성
        GameObject frameTimeTextObj = new GameObject("FrameTimeText");
        frameTimeTextObj.transform.SetParent(panelObj.transform, false);
        
        TextMeshProUGUI frameTimeText = frameTimeTextObj.AddComponent<TextMeshProUGUI>();
        frameTimeText.text = "Frame: -- ms";
        frameTimeText.fontSize = fontSize - 4;
        frameTimeText.color = Color.white;
        frameTimeText.alignment = TextAlignmentOptions.BottomLeft;
        
        RectTransform frameTimeTextRect = frameTimeTextObj.GetComponent<RectTransform>();
        frameTimeTextRect.anchorMin = new Vector2(0, 0);
        frameTimeTextRect.anchorMax = new Vector2(1, 0.5f);
        frameTimeTextRect.sizeDelta = Vector2.zero;
        frameTimeTextRect.anchoredPosition = new Vector2(10, 10);
        
        // VRFPSDisplay 컴포넌트 추가
        fpsDisplay = panelObj.AddComponent<VRFPSDisplay>();
        
        // Inspector에서 컴포넌트 연결이 필요한 경우를 위해 reflection 사용
        var fpsTextField = typeof(VRFPSDisplay).GetField("fpsText", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var frameTimeTextField = typeof(VRFPSDisplay).GetField("frameTimeText", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var backgroundPanelField = typeof(VRFPSDisplay).GetField("backgroundPanel", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        fpsTextField?.SetValue(fpsDisplay, fpsText);
        frameTimeTextField?.SetValue(fpsDisplay, frameTimeText);
        backgroundPanelField?.SetValue(fpsDisplay, panelImage);
        
        Debug.Log("[VRFPSManager] FPS 표시 UI 생성 완료");
    }
    
    // Public API 메서드들
    public void ShowFPSDisplay()
    {
        if (vrCanvas != null)
            vrCanvas.gameObject.SetActive(true);
    }
    
    public void HideFPSDisplay()
    {
        if (vrCanvas != null)
            vrCanvas.gameObject.SetActive(false);
    }
    
    public void ToggleFPSDisplay()
    {
        if (vrCanvas != null)
            vrCanvas.gameObject.SetActive(!vrCanvas.gameObject.activeInHierarchy);
    }
    
    public VRFPSDisplay GetFPSDisplay()
    {
        return fpsDisplay;
    }
    
    // 위치 조정 메서드
    public void SetCanvasPosition(Vector3 newPosition)
    {
        canvasPosition = newPosition;
        if (vrCanvas != null)
        {
            vrCanvas.transform.localPosition = canvasPosition;
        }
    }
    
    public void SetCanvasScale(Vector3 newScale)
    {
        canvasScale = newScale;
        if (vrCanvas != null)
        {
            vrCanvas.transform.localScale = canvasScale;
        }
    }
    
    // 개발자 편의 메서드들
    [ContextMenu("Hide FPS Display")]
    public void DevHideFPSDisplay() => HideFPSDisplay();
    
    [ContextMenu("Show FPS Display")]
    public void DevShowFPSDisplay() => ShowFPSDisplay();
    
    [ContextMenu("Toggle FPS Display")]
    public void DevToggleFPSDisplay() => ToggleFPSDisplay();
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}