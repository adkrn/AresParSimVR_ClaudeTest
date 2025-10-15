using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// VR 환경에서 실시간 FPS를 표시하는 컴포넌트
/// Quest 2/3 최적화: 72-90 FPS 목표 모니터링
/// </summary>
public class VRFPSDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI fpsText;
    [SerializeField] private TextMeshProUGUI frameTimeText;
    [SerializeField] private Image backgroundPanel;
    
    [Header("Performance Monitoring")]
    [SerializeField] private bool showFPS = true;
    [SerializeField] private bool showFrameTime = true;
    [SerializeField] private bool showPerformanceWarnings = true;
    
    [Header("Update Settings")]
    [SerializeField] private float updateInterval = 0.5f; // 0.5초마다 업데이트
    [SerializeField] private int framesSample = 60; // 60프레임 평균으로 계산
    
    [Header("Performance Thresholds (VR Optimized)")]
    [SerializeField] private float goodFPS = 80f;    // 우수 (녹색)
    [SerializeField] private float okayFPS = 72f;    // 양호 (노란색) 
    [SerializeField] private float poorFPS = 60f;    // 불량 (빨간색)
    
    [Header("Display Colors")]
    [SerializeField] private Color excellentColor = Color.green;
    [SerializeField] private Color goodColor = Color.yellow;
    [SerializeField] private Color poorColor = Color.red;
    [SerializeField] private Color criticalColor = new Color(1f, 0.3f, 0.3f); // 진한 빨강
    
    // 성능 계산용 변수들
    private float[] frameTimes;
    private int frameIndex = 0;
    private float lastUpdateTime;
    private float currentFPS;
    private float currentFrameTime;
    private bool isInitialized = false;
    
    // 성능 경고 메시지
    private string performanceStatus = "";
    
    private void Start()
    {
        InitializeDisplay();
    }
    
    private void InitializeDisplay()
    {
        // 프레임 타임 배열 초기화
        frameTimes = new float[framesSample];
        frameIndex = 0;
        lastUpdateTime = Time.unscaledTime;
        
        // UI 컴포넌트 자동 찾기 (Inspector에서 할당하지 않은 경우)
        if (fpsText == null)
            fpsText = transform.Find("FPSText")?.GetComponent<TextMeshProUGUI>();
        if (frameTimeText == null)
            frameTimeText = transform.Find("FrameTimeText")?.GetComponent<TextMeshProUGUI>();
        if (backgroundPanel == null)
            backgroundPanel = GetComponent<Image>();
            
        // 초기 UI 설정
        SetupUI();
        
        isInitialized = true;
        Debug.Log("[VRFPSDisplay] FPS 표시 시스템 초기화 완료");
    }
    
    private void SetupUI()
    {
        // 배경 패널 설정 (반투명)
        if (backgroundPanel != null)
        {
            backgroundPanel.color = new Color(0, 0, 0, 0.7f);
        }
        
        // 텍스트 초기 설정
        if (fpsText != null)
        {
            fpsText.text = "FPS: --";
            fpsText.fontSize = 24;
            fpsText.color = Color.white;
        }
        
        if (frameTimeText != null)
        {
            frameTimeText.text = "Frame: -- ms";
            frameTimeText.fontSize = 20;
            frameTimeText.color = Color.white;
        }
    }
    
    private void Update()
    {
        if (!isInitialized) return;
        
        // 프레임 타임 기록
        RecordFrameTime();
        
        // 지정된 간격마다 UI 업데이트
        if (Time.unscaledTime - lastUpdateTime >= updateInterval)
        {
            UpdatePerformanceDisplay();
            lastUpdateTime = Time.unscaledTime;
        }
    }
    
    private void RecordFrameTime()
    {
        // 현재 프레임 타임을 배열에 기록
        frameTimes[frameIndex] = Time.unscaledDeltaTime;
        frameIndex = (frameIndex + 1) % framesSample;
    }
    
    private void UpdatePerformanceDisplay()
    {
        CalculatePerformanceMetrics();
        UpdateUI();
        CheckPerformanceStatus();
    }
    
    private void CalculatePerformanceMetrics()
    {
        // 평균 프레임 타임 계산
        float totalFrameTime = 0f;
        for (int i = 0; i < framesSample; i++)
        {
            totalFrameTime += frameTimes[i];
        }
        
        currentFrameTime = (totalFrameTime / framesSample) * 1000f; // ms 단위
        currentFPS = 1f / (totalFrameTime / framesSample);
    }
    
    private void UpdateUI()
    {
        // FPS 표시 업데이트
        if (showFPS && fpsText != null)
        {
            fpsText.text = $"FPS: {currentFPS:F1}";
            fpsText.color = GetPerformanceColor(currentFPS);
        }
        
        // 프레임 타임 표시 업데이트  
        if (showFrameTime && frameTimeText != null)
        {
            frameTimeText.text = $"Frame: {currentFrameTime:F2}ms";
            
            // 프레임 타임 색상 (낮을수록 좋음)
            if (currentFrameTime <= 12.5f) // 80+ FPS
                frameTimeText.color = excellentColor;
            else if (currentFrameTime <= 13.9f) // 72+ FPS
                frameTimeText.color = goodColor;
            else if (currentFrameTime <= 16.7f) // 60+ FPS
                frameTimeText.color = poorColor;
            else
                frameTimeText.color = criticalColor;
        }
    }
    
    private Color GetPerformanceColor(float fps)
    {
        if (fps >= goodFPS)
            return excellentColor;
        else if (fps >= okayFPS)
            return goodColor;
        else if (fps >= poorFPS)
            return poorColor;
        else
            return criticalColor;
    }
    
    private void CheckPerformanceStatus()
    {
        if (!showPerformanceWarnings) return;
        
        string newStatus = "";
        
        if (currentFPS >= goodFPS)
        {
            newStatus = "성능 우수";
        }
        else if (currentFPS >= okayFPS)
        {
            newStatus = "성능 양호 (VR 권장)";
        }
        else if (currentFPS >= poorFPS)
        {
            newStatus = "⚠️ 성능 주의 (VR 최소)";
        }
        else
        {
            newStatus = "🚨 성능 경고 (VR 부적합)";
        }
        
        // 상태가 변경되었을 때만 로그 출력
        if (newStatus != performanceStatus)
        {
            performanceStatus = newStatus;
            //Debug.Log($"[VRFPSDisplay] {performanceStatus} - FPS: {currentFPS:F1}, FrameTime: {currentFrameTime:F2}ms");
        }
    }
    
    // 외부에서 성능 정보를 가져올 수 있는 public 메서드들
    public float GetCurrentFPS() => currentFPS;
    public float GetCurrentFrameTime() => currentFrameTime;
    public string GetPerformanceStatus() => performanceStatus;
    
    // 표시 옵션 토글 메서드들
    public void ToggleFPSDisplay()
    {
        showFPS = !showFPS;
        if (fpsText != null)
            fpsText.gameObject.SetActive(showFPS);
    }
    
    public void ToggleFrameTimeDisplay()
    {
        showFrameTime = !showFrameTime;
        if (frameTimeText != null)
            frameTimeText.gameObject.SetActive(showFrameTime);
    }
    
    public void SetDisplayVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
    
    // VR 환경에 최적화된 위치 조정
    public void PositionForVR()
    {
        // 사용자의 시야 왼쪽 상단에 배치 (VR에서 방해되지 않는 위치)
        RectTransform rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0, 1);
            rectTransform.anchorMax = new Vector2(0, 1);
            rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(50, -50);
        }
    }
    
    // 성능 최적화: 불필요한 업데이트 방지
    public void SetUpdateInterval(float interval)
    {
        updateInterval = Mathf.Clamp(interval, 0.1f, 2f);
    }
    
    // 메모리 정리
    private void OnDestroy()
    {
        frameTimes = null;
        Debug.Log("[VRFPSDisplay] FPS 표시 시스템 정리 완료");
    }
}