using System;

/// <summary>
/// 비행기 명령 클래스
///
/// 설계 원칙:
/// - 명령 패턴: 실행할 작업을 객체로 캡슐화
/// - 완료 콜백: 명령 완료 시 StateManager에 알림
/// - 단순성: 디버깅 필드 최소화 (필수 필드만 유지)
///
/// 기존 방식 (이벤트):
///   StateManager → 이벤트 핸들러 등록 → AirPlane 이동 → 이벤트 발생 → StateManager 처리
///   문제: 이벤트 중복 등록, 해제 타이밍 복잡, 상태 추적 어려움
///
/// 새로운 방식 (명령):
///   StateManager → 명령 생성 → AirPlane 큐에 추가 → 순차 실행 → 완료 콜백
///   장점: 중복 없음, 자동 정리, 명령 흐름 명확
/// </summary>
[Serializable]
public class AirplaneCommand
{
    /// <summary>
    /// 명령 타입 (MoveToAndWait, MoveTo, Continue 등)
    /// </summary>
    public AirplaneCommandType type;

    /// <summary>
    /// 목표 route 인덱스
    /// Continue 명령의 경우 -1 (목표 없음)
    /// </summary>
    public int targetRouteIndex;

    /// <summary>
    /// 명령 완료 콜백
    /// success: 명령 성공 여부
    ///
    /// 사용 예:
    /// - true: 정상 완료 → StateManager가 절차 완료 처리
    /// - false: 실패 (현재는 사용 안 함, 향후 확장 가능)
    /// </summary>
    public Action<bool> onComplete;

    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="type">명령 타입</param>
    /// <param name="targetRouteIndex">목표 route 인덱스 (Continue는 -1)</param>
    /// <param name="onComplete">완료 콜백 (옵션)</param>
    public AirplaneCommand(
        AirplaneCommandType type,
        int targetRouteIndex,
        Action<bool> onComplete = null)
    {
        this.type = type;
        this.targetRouteIndex = targetRouteIndex;
        this.onComplete = onComplete;
    }

    public override string ToString()
    {
        return $"[AirplaneCommand] {type} to Route#{targetRouteIndex}";
    }
}
