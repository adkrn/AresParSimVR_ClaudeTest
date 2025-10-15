using System;
using UnityEditor.Animations.Rigging;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 어떤 오브젝트든 실행 시(또는 임의 호출) 카메라 전방에 자동 배치해 주는 컴포넌트.
/// 
/// ▸ Canvas·3D 모델 등 위치‑기준이 필요한 모든 오브젝트에 부착 가능
/// ▸ <paramref name="AutoPlaceOnEnable"/> 옵션으로 활성화 시 자동 배치 가능
/// ▸ <see cref="Place()"/> 메서드를 호출하여 언제든 수동 배치 가능
/// </summary>
[DisallowMultipleComponent]
public class CameraFrontPlacer : MonoBehaviour
{
    /* ────────────────────────────── 인스펙터 옵션 ────────────────────────────── */

    [FormerlySerializedAs("_targetCamera")] [Tooltip("기준이 되는 카메라(비워 두면 Camera.main 사용).")] [SerializeField]
    private Camera targetCamera;

    [Tooltip("카메라로부터의 전방 거리(m).")] [SerializeField]
    private float distance = 1.2f;

    [Tooltip("카메라 기준 세로 오프셋(+ 위, – 아래, m).")] [SerializeField]
    private float verticalOffset = 0f;

    [Tooltip("OnEnable 시 자동으로 배치할지 여부.")] [SerializeField]
    private bool autoPlaceOnEnable = true;

    [Tooltip("항상 카메라 방향을 바라보게 회전시킬지 여부.")] [SerializeField]
    private bool faceCamera = true;
    
    [SerializeField] private Transform parentPos;

    [Header("▼ Down-Look 옵션")] [Tooltip("아래를 바라보고 있다고 판단할 최대 각도(°).")] [SerializeField, Range(0f, 90f)]
    private float downAngleThreshold = 60f;

    [SerializeField] private Transform pelvisT;

    /* 내부 상태 */
    Transform _origParent; // 원래 부모
    bool _attachedToCam = false;

    void Awake()
    {
        _origParent = transform.parent;
    }

    private void OnEnable()
    {
        if (autoPlaceOnEnable)
        {
            Place();
        }
        
        // 씬 전환 직전 이벤트 구독
        StateManager_New.OnBeforeSceneChange += ReturnToOriginalParent;
    }
    
    private void OnDisable()
    {
        ReturnToOriginalParent();
        // 이벤트 구독 해제
        StateManager_New.OnBeforeSceneChange -= ReturnToOriginalParent;
    }
    
    /// <summary>
    /// 원래 부모로 즉시 복귀
    /// </summary>
    public void ReturnToOriginalParent()
    {
        if (_origParent != null && _origParent.gameObject != null)
        {
            transform.SetParent(_origParent);
            Debug.Log($"[CameraFrontPlacer] {gameObject.name}을(를) 원래 부모로 복귀시킴");
        }
    }

    /// <summary>카메라 전방(또는 아래-전방)에 즉시 배치</summary>
    public void Place()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        Camera cam = targetCamera;
        if (!cam)
        {
            Debug.LogError($"[CameraFrontPlacer] {gameObject.name}에서 카메라 등록이 안됨.");
            return;
        }

        /* 1. 아래를 보고 있는가? */
        // bool isLookingDown =
        //     Vector3.Angle(cam.transform.forward, Vector3.down) < downAngleThreshold;
        //bool isLookingDown = _playManager.planeStatus >= PlaneStatus.Jump;
        bool isLookingDown = false; //FindAnyObjectByType<StateManager_TimeLine>().Current >= PlaneStatus.Jump;

        Vector3 targetPos;
        if (isLookingDown)
        {
            // transform.SetParent(cam.transform, worldPositionStays: false);
            // _attachedToCam = true;
            //
            // // 카메라 로컬 공간에서 distance 만큼 앞으로
            // transform.localPosition = new Vector3(0, 0, distance);
            // transform.transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            // transform.SetParent(_origParent);

            var pos = pelvisT.position;
            transform.position = pos;
            transform.localPosition += Vector3.forward * distance;
            transform.localPosition += Vector3.up * 0.2f;
        }
        else
        {
            /* ▼ 평상시(정면·상향 시선) : 기존 로직 그대로 ▼ */
            Vector3 forwardXZ = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (forwardXZ.sqrMagnitude < 0.001f) forwardXZ = cam.transform.forward;

            targetPos = cam.transform.position + forwardXZ * distance + Vector3.up * verticalOffset;
            transform.position = targetPos;

            if (faceCamera)
            {
                transform.rotation = Quaternion.LookRotation(forwardXZ);
            }
        }
        
        // parentPos가 null이면 "PositionT" 태그를 가진 오브젝트 찾기
        if (parentPos == null)
        {
            GameObject tagged = GameObject.FindWithTag("PositionT");
            if (tagged != null)
            {
                parentPos = tagged.transform;
            }
            else
            {
                Debug.LogWarning("[CameraFrontPlacer] 'PositionT' 태그를 가진 오브젝트를 찾을 수 없습니다.");
            }
        }
        
        transform.SetParent(parentPos);
    }

    /* ────────────────────────────── 에디터 • 디버그 ────────────────────────────── */
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Camera cam = Application.isPlaying ? (targetCamera != null ? targetCamera : Camera.main) : Camera.main;
        if (cam == null) return;

        Vector3 forwardXZ = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 targetPos = cam.transform.position + forwardXZ * distance + Vector3.up * verticalOffset;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(cam.transform.position, targetPos);
        Gizmos.DrawWireSphere(targetPos, 0.05f);
    }
#endif
}
