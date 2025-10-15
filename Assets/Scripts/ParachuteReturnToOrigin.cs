using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Grab 해제(Unselect) 순간 손잡이를 원래 로컬 위치로 부드럽게 되돌린다.
/// 코루틴 대신 Update() 기반 상태 머신으로 처리하도록 리팩토링.
/// v74.0.3: WhenPointerEventRaised 이벤트를 사용해 Unselect 감지 :contentReference[oaicite:0]{index=0}
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class ParachuteReturnToOrigin : MonoBehaviour
{
    [Header("복귀 설정")]
    [Tooltip("복귀에 걸릴 시간 (초)")]
    [Range(0.05f, 1f)]
    public float returnDuration = 0.25f;

    // 원래 로컬 위치 저장
    private Vector3 _originLocalPos;
    private Grabbable _grab;

    // 리턴 상태용 내부 변수
    private bool   _isReturning;
    private float  _returnTimer;
    private Vector3 _returnStartPos;

    private void Awake()
    {
        _grab           = GetComponent<Grabbable>();
        _originLocalPos = transform.localPosition;

        // Unselect 이벤트 구독
        _grab.WhenPointerEventRaised += OnPointerEvent;
    }

    private void OnDestroy()
    {
        // 이벤트 해제
        if (_grab != null)
            _grab.WhenPointerEventRaised -= OnPointerEvent;
    }

    private void Update()
    {
        if (_isReturning)
        {
            ProcessReturning();
        }
    }

    /// <summary>
    /// PointerEvent 콜백: Unselect 되면 Return 시작
    /// </summary>
    private void OnPointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Unselect)
            BeginReturn();
    }

    /// <summary>
    /// Return 동작을 시작하도록 상태 초기화
    /// </summary>
    private void BeginReturn()
    {
        // 이미 리턴 중이라면 재시작
        _isReturning = true;
        _returnTimer = 0f;
        _returnStartPos = transform.localPosition;
    }

    /// <summary>
    /// Update()에서 매 프레임 호출되어, Lerp로 로컬 포지션을 원래 위치로 되돌림
    /// </summary>
    private void ProcessReturning()
    {
        // 타이머 증가
        _returnTimer += Time.deltaTime;

        // 보간 비율 계산
        float t = Mathf.Clamp01(_returnTimer / returnDuration);
        transform.localPosition = Vector3.Lerp(_returnStartPos, _originLocalPos, t);

        // 완료 시 상태 종료
        if (t >= 1f)
        {
            transform.localPosition = _originLocalPos;
            _isReturning = false;
        }
    }
}
