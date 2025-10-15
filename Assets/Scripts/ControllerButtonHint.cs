using TMPro;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(LineRenderer))]
public class ControllerButtonHint : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("A 버튼 위치(컨트롤러 모델 안에 빈 오브젝트로 지정)")]
    [SerializeField] Transform buttonAnchor;

    [Tooltip("월드-스페이스 캔버스(텍스트가 들어 있음)")]
    [SerializeField] Canvas hintCanvas;

    [Tooltip("캔버스 안의 TextMeshProUGUI")] 
    [SerializeField] TMP_Text hintLabel;

    [Header("옵션")]
    [Tooltip("버튼을 누르면 힌트를 자동으로 끌지 여부")]
    [SerializeField] bool hideOnPress = true;

    [Tooltip("힌트 문구")]
    [SerializeField] string message = "Press A";

    [Tooltip("캔버스를 버튼에서 얼마나 위로 띄울지")]
    [SerializeField] Vector3 labelOffset = new Vector3(0f, 0.025f, 0f);

    LineRenderer _line;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        hintLabel.text = message;

        // (1) 두께
        _line.startWidth = _line.endWidth = 0.001f; // 약 1 mm

        // (2) 머티리얼
        if (_line.material == null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                color = Color.white
            };
            _line.material = mat;
        }

        // 선 설정(굵기·머티리얼 등은 인스펙터에서 조정 가능)
        if (_line.positionCount != 2)
            _line.positionCount = 2;
    }

    void LateUpdate()
    {
        if (buttonAnchor == null || hintCanvas == null) return;

        // 1) 캔버스 위치·회전 갱신
        Vector3 labelPos = buttonAnchor.position + labelOffset;
        hintCanvas.transform.position = labelPos;
        hintCanvas.transform.rotation =
            Quaternion.LookRotation(labelPos - Camera.main.transform.position);

        // 2) 라인 렌더러 갱신
        _line.SetPosition(0, buttonAnchor.position);
        _line.SetPosition(1, labelPos);

        // 3) A 버튼을 누르면 자동으로 숨김
        if (hideOnPress &&
            OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            SetHintActive(false);
        }
    }

    /// <summary>
    /// PlaneMove 측에서 힌트의 표시·숨김과 텍스트를 제어할 때 호출하는 단일 메서드.
    /// </summary>
    /// <param name="active">true면 힌트 표시, false면 숨김</param>
    /// <param name="newMessage">null이 아니면 텍스트를 이 문자열로 교체</param>
    public void SetHintActive(bool active, string newMessage = null)
    {
        if (newMessage != null)
            hintLabel.text = newMessage;

        // 캔버스·라인렌더러만 ON/OFF (GameObject는 비활성화하지 않음)
        hintCanvas.enabled = active;
        _line.enabled = active;
    }
}
