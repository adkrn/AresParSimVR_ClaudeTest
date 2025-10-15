using TMPro;
using UnityEngine;

/// <summary>
/// 왼손(컨트롤러) 손목 위에 고도(Altitude)를 실시간으로 표시하는 스크립트.
/// - World‑Space Canvas + TMP_Text 조합을 사용합니다.
/// - Canvas는 Wrist Anchor(왼손 컨트롤러 모델 상의 빈 오브젝트) 기준으로 약간 위에 배치합니다.
/// - target Transform 의 y 좌표에서 groundY 를 뺀 값을 고도로 간주하여 표시합니다.
/// </summary>
[RequireComponent(typeof(Transform))]
public class WristAltimeterDisplay : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("실제로 글자를 표시할 World‑Space Canvas")]
    [SerializeField] private Canvas altimeterCanvas;

    [Tooltip("Canvas 안의 TextMeshProUGUI")]
    [SerializeField] private TMP_Text altLabel;

    [Header("고도 계산")]
    [Tooltip("고도를 측정할 대상(주로 플레이어 본체)")]
    [SerializeField] private Transform target;

    [Tooltip("지면의 Y 좌표 값")]
    [SerializeField] private float groundY = 0f;

    [Header("옵션")]
    [Tooltip("고도 포맷(예: \"{0:0} m\" , \"{0:0} ft\")")]
    [SerializeField] private string format = "Alt {0:0} m";

    [Tooltip("고도가 0 이하일 때 자동으로 숨길지 여부")]
    [SerializeField] private bool hideWhenGrounded = false;
    
    [SerializeField] private TriggerListener altTrigger;

    private bool _isVisible = true;

    /// <summary>
    /// UI 활성/비활성과 텍스트 포맷을 외부에서 제어할 수 있는 메서드.
    /// PlaneMove 등에서 낙하 시점에 맞추어 호출하면 된다.
    /// </summary>
    /// <param name="active">true = 표시, false = 숨김</param>
    /// <param name="newFormat">null이 아니면 현재 포맷을 교체</param>
    public void SetAltimeterActive(bool active, string newFormat = null)
    {
        _isVisible = active;
        if (newFormat != null) format = newFormat;
        if (altimeterCanvas != null)
            altimeterCanvas.gameObject.SetActive(active);
    }

    private void Awake()
    {
        // 인스펙터 누락 방지용 자동 할당
        if (altimeterCanvas == null)
            altimeterCanvas = GetComponentInChildren<Canvas>(true);
        if (altLabel == null)
            altLabel = GetComponentInChildren<TMP_Text>(true);

        // 로비에서 고도계를 착용 안했을때 착용하면 고도계 UI를 표시해준다.
        if(altTrigger != null) altTrigger.OnPlayerEntered += () => altimeterCanvas.gameObject.SetActive(true);

        // 초기 표시 상태 반영
        SetAltimeterActive(_isVisible);
        
        altimeterCanvas.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!_isVisible || target == null || altLabel == null)
            return;

        float altitude = (target.position.y+27) - groundY;

        // 지상에 도달했고 hideWhenGrounded 옵션이 켜져 있으면 자동 숨김
        if (hideWhenGrounded && altitude <= 0f)
        {
            if (altimeterCanvas.gameObject.activeSelf)
                altimeterCanvas.gameObject.SetActive(false);
            return;
        }
        else if (!_isVisible && !altimeterCanvas.gameObject.activeSelf)
        {
            // 내부 조건으로 잠시 숨겼다면 다시 표시
            altimeterCanvas.gameObject.SetActive(true);
        }
        altimeterCanvas.transform.rotation = Quaternion.LookRotation(altimeterCanvas.transform.position - Camera.main.transform.position);

        altLabel.text = string.Format(format, altitude);
    }
}