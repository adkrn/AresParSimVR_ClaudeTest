/// <summary>
/// 비행기 명령 타입
///
/// 기존 문제:
/// - 이벤트 핸들러 중복 등록
/// - StateManager와 AirPlane의 강한 결합
/// - 상태 분기의 폭발
///
/// 해결책:
/// - 명령 기반 제어로 책임 분리
/// - StateManager: 명령 생성 및 절차 관리
/// - AirPlane: 명령 실행만 담당
/// </summary>
public enum AirplaneCommandType
{
    /// <summary>
    /// 특정 지점까지 이동 후 대기
    /// 용도: Point 절차에서 출발 지점까지 이동 후 교관 신호 대기
    /// </summary>
    MoveToAndWait,

    /// <summary>
    /// 특정 지점까지 이동 (대기 없음)
    /// 용도: Point 절차에서 목표 지점까지 이동하여 절차 완료
    /// </summary>
    MoveTo,

    /// <summary>
    /// 현재 위치에서 대기
    /// 용도: 교관 신호 대기가 필요한 경우
    /// </summary>
    WaitAtCurrent,

    /// <summary>
    /// 대기 해제하고 계속 진행
    /// 용도: 비행기 출발 명령
    /// </summary>
    Continue,

    /// <summary>
    /// 특정 지점으로 즉시 텔레포트
    /// 용도: 스킵 처리
    /// </summary>
    TeleportTo
}
