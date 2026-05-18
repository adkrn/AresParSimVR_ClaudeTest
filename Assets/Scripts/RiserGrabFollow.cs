using UnityEngine;

public class RiserGrabFollower : MonoBehaviour
{
    [Header("기준점 (라이저가 붙어있는 위치)")]
    [SerializeField] private Transform riserRoot; //

    [SerializeField] private Transform handTransform;

    [Header("이 핸들이 움직일 최대 거리/속도")]
    [SerializeField] private float maxDistance = 0.5f;     // 기준점에서 최대 이동 거리
    [SerializeField] private float followLerp = 20f;       // 손을 따라가는 보간 속도
    [SerializeField] private float returnLerp = 5f;        // 놓았을 때 제자리로 돌아가는 속도

    private Transform grabbingHand; // 현재 잡고 있는 손 Transform
    [SerializeField] private bool isGrabbed = false;
    private Vector3 initialLocalPos; // 기준 로컬 위치
    private Vector3 grabOffsetLocal;   // 손 기준 오프셋 (로컬)

    private void Awake()
    {
        // 시작 기준 위치 저장 (보통 (0,0,0))
        initialLocalPos = transform.localPosition;
    }

    // === Grab 시작 시 호출 (이 함수는 이벤트에서 연결해서 호출) ===
    public void OnGrabBegin()
    {
        // 진단 (임시) — 누가 grab 시작시켰는지 추적
        Debug.Log($"[RiserGrab:{gameObject.name}] OnGrabBegin t={Time.time:F2}, handPos={handTransform?.position}, localPos={transform.localPosition}\nStack: {System.Environment.StackTrace}", this);
        isGrabbed = true;
    }

    // === Grab 종료 시 호출 ===
    public void OnGrabEnd()
    {
        Debug.Log($"[RiserGrab:{gameObject.name}] OnGrabEnd t={Time.time:F2}, localPos={transform.localPosition}", this);
        isGrabbed = false;
    }

    private void Update()
    {
        // 진단 (임시) — localPos 비정상 (잔재) 추적. 1초당 1회 + grab 상태/위치 캡처
        if (Time.frameCount % 60 == 0 && (isGrabbed || Mathf.Abs(transform.localPosition.y) > 0.01f))
        {
            Debug.Log($"[RiserGrab:{gameObject.name}] tick t={Time.time:F2}, isGrabbed={isGrabbed}, localPos={transform.localPosition}, initialLocalPos={initialLocalPos}");
        }

        if (isGrabbed)
        {
            // // 1) 현재 손의 로컬 좌표
            // Vector3 handLocal = riserRoot.InverseTransformPoint(handTransform.position);
            //
            // // 2) 현재 핸들의 로컬 좌표와의 차이를 오프셋으로 저장
            // grabOffsetLocal = transform.localPosition - handLocal;
            
            FollowHand();
        }
        else
        {
            ReturnToInitial();
        }
    }

    // 손을 따라가는 부분
    private void FollowHand()
    {
        // 손의 월드 좌표 → riserRoot 기준 로컬 좌표로 변환
        Vector3 targetLocal = riserRoot.InverseTransformPoint(handTransform.position);
        
        // 기준점(initialLocalPos)에서 얼마나 벗어났는지 벡터 계산
        Vector3 offset = targetLocal - initialLocalPos;
        
        // // 최대 거리 제한
        if (offset.magnitude > maxDistance)
        {
            offset = offset.normalized * maxDistance;
        }
        
        // 최종 목표 로컬 위치
        Vector3 clampedLocal = initialLocalPos + offset;
        
        // if (clampedLocal.y > 0.4f)
        // {
        //     clampedLocal.y = 0.4f;
        // }
        //
        // if (clampedLocal.y < -0.4f)
        // {
        //     clampedLocal.y = -0.4f;
        // }
        //
        // if (clampedLocal.x > 1.0f)
        // {
        //     clampedLocal.x = 1.0f;
        // }
        //
        // if (clampedLocal.x < -0.1f)
        // {
        //     clampedLocal.x = -0.1f;
        // }
        //
        // if (clampedLocal.z > 0.4f)
        // {
        //     clampedLocal.z = 0.4f;
        // }
        //
        // if (clampedLocal.z < -0.4f)
        // {
        //     clampedLocal.z = -0.4f;
        // }
        
        // "무조건 따라붙게" 하고 싶으면 보간 없이 바로 대입
        transform.localPosition = clampedLocal;

        // 현재 핸들 위치 → 목표 위치로 보간 이동
        // transform.localPosition = Vector3.Lerp(
        //     transform.localPosition,
        //     clampedLocal,
        //     Time.deltaTime * followLerp
        // );
        
        // // 현재 손의 로컬 좌표
        // Vector3 handLocal = riserRoot.InverseTransformPoint(handTransform.position);
        //
        // // 손 기준 오프셋을 더한 최종 목표 위치
        // Vector3 targetLocal = handLocal + grabOffsetLocal;
        //
        // // "무조건 따라붙게" 하고 싶으면 보간 없이 바로 대입
        // transform.localPosition = targetLocal;
    }

    // 손을 놓았을 때 원래 위치로 되돌리기
    private void ReturnToInitial()
    {
        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            initialLocalPos,
            Time.deltaTime * returnLerp
        );
    }
}
