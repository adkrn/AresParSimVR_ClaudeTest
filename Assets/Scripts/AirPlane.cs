using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class AirPlane : MonoBehaviour
{
    [Header("비행기 운행 관련")]
    public Transform center;
    public float rotSpeed = -1.5f;
    [SerializeField] float jumpHeight   = 1000f;
    public Transform[] propellers;
    private float propRotSpeed = 720.0f;
    [SerializeField] private string mainSceneName;
    
    [Header("문 관련")]
    [SerializeField] private AudioSource audioDoorOpen;
    [SerializeField] private Transform door01;
    [SerializeField] private Transform door02;
    [SerializeField] private float doorOpen01_Angle = -22.0f;
    [SerializeField] private float doorOpen02_Angle = -24.0f;
    [SerializeField] private LayerMask airPlaneLayer;
    public event Action DoorOpenCompleted;
    public event Action<int> OnRoutePointReached;  // Route 포인트 도달 이벤트
    public static event Action OnAirPlaneReady;  // 비행기 초기화 완료 이벤트
    private float doorOpenTime = 10.0f;
    private float _doorEachOpenTime;
    private float _doorOpenInterval = 2.0f;
    private float _doorOpenSpendTime = 0.0f;
    private float _lerpTime01 = 0;
    private float _lerpTime02 = 0;
    private Vector3 _doorOpen01Vector;
    private Vector3 _doorOpen02Vector;
    private bool doorOpenEndCalled = false;  // 문 열림 효과 중복 호출 방지

    [Header("구름")] 
    public GameObject clouds;
    public GameObject stopCloud;
    [SerializeField] private Transform sceneRoot;
    
    private Action _updateAction = null;
    
    [Header("Route 이동 관련")]
    [SerializeField] private float moveSpeed = 100f;
    [SerializeField] private float rotateSpeed = 2f;
    [SerializeField] private float reachDistance = 50f;
    private List<Transform> routePoints;
    private int currentRouteIndex = 0;
    private int targetRouteIndex = 1;
    private bool isFollowingRoute = false;
    private bool isInitialized = false;
    private bool isWaitingForSignal = false;  // 교관 신호 대기 상태

    // ============================================================
    // 명령 큐 시스템 (리팩토링)
    // ============================================================
    // 기존 문제:
    // 1. 이벤트 핸들러 중복 등록 (OnRoutePointReached에 여러 핸들러 등록)
    // 2. 이벤트 해제 타이밍 복잡 (언제 해제해야 하는지 불명확)
    // 3. 상태 분기 폭발 (비행기 위치, 대기 모드, 스킵 등 조합)
    //
    // 해결책: 명령 큐 패턴
    // - StateManager가 명령 생성 → AirPlane이 순차 실행
    // - 각 명령은 완료 시 콜백 호출 → StateManager가 다음 행동 결정
    // - 이벤트 중복 없음, 자동 정리, 명령 흐름 명확
    // ============================================================

    /// <summary>
    /// 명령 큐: FIFO(First In First Out) 순서로 명령 실행
    /// </summary>
    private Queue<AirplaneCommand> commandQueue = new Queue<AirplaneCommand>();

    /// <summary>
    /// 현재 실행 중인 명령
    /// </summary>
    private AirplaneCommand currentCommand = null;

    /// <summary>
    /// 명령 실행 중 여부
    /// </summary>
    private bool isExecutingCommand = false;

    private void Start()
    {
        // SetRoutePoints가 호출되지 않은 경우에만 Init 호출
        if (!isInitialized)
        {
            Init();
        }
        
        // 기본 원형 회전 대신 route 이동을 위해 주석처리
        // _updateAction += RotAround;
        _updateAction += PropellerAction;
        Transform plane = transform.GetChild(0);
        _updateAction += () =>
        {
            // 교관 화면에는 비행기 그래픽의 실제 회전(Slerp 보간으로 지연됨)이 아니라
            // 현재 진행 방향(다음 routePoint를 향하는 방향)을 보냄.
            // 곡선 경로(Bravo 등)에서 보간 지연으로 비행기 머리가 진행 방향과 어긋나는 문제 해결.
            Vector3 dir;
            if (isFollowingRoute && routePoints != null
                && targetRouteIndex >= 0 && targetRouteIndex < routePoints.Count)
            {
                Vector3 targetPos = new Vector3(
                    routePoints[targetRouteIndex].position.x,
                    jumpHeight,
                    routePoints[targetRouteIndex].position.z);
                dir = targetPos - transform.position;
            }
            else
            {
                // Route 미진행 시 비행기 forward 사용 (fallback)
                dir = plane.forward;
            }

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;

            ParticipantManager.Inst.SetMonitoringDataPlanePos(transform.position, yaw);
        };
    }

    private void Init()
    {
        // 비행기 도착 고도 가져와서 설정
        // 비행기 이륙 후 도착 고도에서 낙하를 시작함.
        jumpHeight = DataManager.Inst.scenario.endOperationalAltidute;
        
        // 노트 -> m/s 변환값
        //float knotToMps = 1852f / 3600f;
        //moveSpeed = DataManager.Inst.scenario.allowedSpeedKnots * knotToMps;
        moveSpeed = 25;
        
        // Route 포인트가 설정될 때까지 임시 위치 (화면 밖)
        // SetRoutePoints가 호출되면 0번 포인트로 이동함
        transform.position = new Vector3(0, jumpHeight, -5000);  // 더 멀리 배치
        Debug.Log($"[AirPlane] 비행기 임시 위치 설정: {transform.position}");

        // 문 상태 초기화 (닫힌 상태로 시작). DoorOpen / OpenDoorImmediately 트리거는 StateManager가 OnAirPlaneReady에서 결정.
        door01.localEulerAngles = Vector3.zero;
        door02.localEulerAngles = Vector3.zero;
        _doorOpen01Vector = new Vector3(doorOpen01_Angle, 0, 0);
        _doorOpen02Vector = new Vector3(doorOpen02_Angle, 0, 0);
        _doorEachOpenTime = doorOpenTime - _doorOpenInterval;
        _doorOpenSpendTime = 0f;
        _lerpTime01 = 0f;
        _lerpTime02 = 0f;
        doorOpenCalled = false;
        doorOpenEndCalled = false;
        Debug.Log("[AirPlane] 문 초기화 완료 (닫힌 상태)");

        isInitialized = true;
        isWaitingForSignal = true;
    }

    private void FixedUpdate()
    {
        _updateAction?.Invoke();
    }

    /// <summary>
    /// 비행기 운행 매서드
    /// </summary>
    private void RotAround()
    {
        // 중심을 기준으로 회전
        transform.RotateAround(center.position, Vector3.up, rotSpeed * Time.deltaTime);
        
        // 자식 오브젝트(비행기)가 항상 회전 방향을 향하게
        Transform plane = transform.GetChild(0);
        Vector3 dir = (center.position - plane.position).normalized;
        Vector3 forward = Vector3.Cross(Vector3.up, dir);
        plane.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
    
    /// <summary>
    /// Route 포인트 설정
    /// </summary>
    public void SetRoutePoints(List<Transform> points)
    {
        // 초기화가 안 되어 있으면 먼저 초기화
        if (!isInitialized)
        {
            Init();
        }
        
        routePoints = points;
        
        if (routePoints is not { Count: > 0 }) return;
        
        isFollowingRoute = true;
            
        // 첫 번째(0번) 포인트로 비행기 위치 즉시 설정
        Vector3 beforePos = transform.position;
        Vector3 startPos = new Vector3(routePoints[0].position.x, jumpHeight, routePoints[0].position.z);
        transform.position = startPos;
        //Debug.Log($"[AirPlane] 위치 설정 - 이전: {beforePos}, 목표(0번): {startPos}, 실제: {transform.position}");
            
        // 비행기 기수를 다음 포인트(1번) 방향으로 설정
        if (routePoints.Count > 1)
        {
            Vector3 nextPos = new Vector3(routePoints[1].position.x, jumpHeight, routePoints[1].position.z);
            Vector3 direction = (nextPos - startPos).normalized;
            if (direction != Vector3.zero)
            {
                Transform plane = transform.GetChild(0);
                plane.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
            
        // Route 이동 시작
        _updateAction += FollowRoute;
            
        Debug.Log($"[AirPlane] 0번 포인트({routePoints[0].name})에서 출발. 총 {routePoints.Count}개 포인트");
            
        // 비행기 초기화 완료 이벤트 발생
        OnAirPlaneReady?.Invoke();
        Debug.Log("[AirPlane] 초기화 완료 이벤트 발생");
    }
    
    /// <summary>
    /// 현재 route 인덱스 반환
    /// </summary>
    public int GetCurrentRouteIndex()
    {
        return currentRouteIndex;
    }
    
    /// <summary>
    /// 특정 route 포인트에 위치해 있는지 확인
    /// </summary>
    public bool IsAtRoutePoint(int index)
    {
        if (routePoints == null || index < 0 || index >= routePoints.Count) 
            return false;
            
        Vector3 targetPos = new Vector3(routePoints[index].position.x, jumpHeight, routePoints[index].position.z);
        float distance = Vector3.Distance(transform.position, targetPos);
        
        Debug.Log($"[AirPlane] Route {index}번 포인트까지 거리: {distance} (도달 기준: {reachDistance})");
        return distance < reachDistance;
    }
    
    /// <summary>
    /// Route 포인트를 따라 이동
    /// </summary>
    private void FollowRoute()
    {
        if (!isFollowingRoute || routePoints == null || currentRouteIndex >= routePoints.Count)
            return;
            
        // 신호 대기 중이면 이동하지 않음
        if (isWaitingForSignal)
            return;
            
        Transform targetPoint = routePoints[targetRouteIndex];
        Vector3 targetPos = new Vector3(targetPoint.position.x, jumpHeight, targetPoint.position.z);
        
        // 첫 프레임 디버깅
        if (Time.frameCount % 60 == 0)  // 1초마다 로그
        {
            //Debug.Log($"[AirPlane] 현재 위치: {transform.position}, 목표({currentRouteIndex}번): {targetPos}");
        }
        
        // 목표 지점까지의 방향
        Vector3 direction = (targetPos - transform.position).normalized;
        
        // 비행기 이동
        transform.position += direction * moveSpeed * Time.deltaTime;
        
        // 비행기 회전 (자식 오브젝트가 이동 방향을 바라보도록)
        Transform plane = transform.GetChild(0);
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            plane.rotation = Quaternion.Slerp(plane.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }
        
        // 목표 지점 도착 체크
        var distance = Vector3.Distance(transform.position, targetPos);
        if (distance < reachDistance)
        {
            // 현재 도달한 포인트 인덱스를 이벤트로 먼저 전달
            int reachedIndex = targetRouteIndex;
            Debug.Log($"[AirPlane] Route 포인트 {reachedIndex}번({routePoints[reachedIndex].name})에 도착");
            
            // 이벤트 구독자 확인
            OnRoutePointReached?.Invoke(reachedIndex);

            // 대기 상태로 전환하지 않음 - StateManager가 결정하도록 함
            Debug.Log($"[AirPlane] {reachedIndex}번 포인트 도달 완료.");
            
            // 다음 포인트 인덱스는 증가시키지만 이동은 하지 않음
            currentRouteIndex = targetRouteIndex;
            targetRouteIndex++;
            
            if (targetRouteIndex >= routePoints.Count)
            {
                // 모든 포인트 완료 - 다시 0번부터 설정 (하지만 이동은 대기)
                currentRouteIndex = 0;
                targetRouteIndex = 1;
                Debug.Log($"[AirPlane] 모든 route 포인트 완료. 다음 신호 대기 중...");
            }
        }
    }

    /// <summary>
    /// 프로펠러 돌아가는 애니메이션
    /// </summary>
    private void PropellerAction()
    {
        foreach (var propeller in propellers)
        {
            propeller.localEulerAngles += new Vector3(0, 0, propRotSpeed * Time.deltaTime);
        }
    }
    
    /// <summary>
    /// 교관 신호를 받아 다음 포인트로 이동 재개
    /// </summary>
    public void ResumeMovement()
    {
        if (isWaitingForSignal)
        {
            isWaitingForSignal = false;
            Debug.Log($"[AirPlane] 이동 재개. 다음 포인트({currentRouteIndex}번)로 이동 시작");
        }
    }
    
    /// <summary>
    /// 대기 모드 설정
    /// </summary>
    public void SetWaitingMode(bool waiting)
    {
        isWaitingForSignal = waiting;
        Debug.Log($"[AirPlane] 대기 모드 설정: {waiting}");
    }

    public bool GetWaitingMode()
    {
        return isWaitingForSignal;
    }

    // ============================================================
    // 명령 큐 시스템 메서드들
    // ============================================================

    /// <summary>
    /// 명령 큐 + 등록된 OnRoutePointReached 핸들러를 모두 클리어.
    /// 스킵 발생 시 이전 절차의 stale 명령/핸들러가 새 절차를 오염시키는 걸 방지.
    /// </summary>
    public void ClearCommandQueue()
    {
        int queuedCount = commandQueue.Count;
        commandQueue.Clear();
        currentCommand = null;
        isExecutingCommand = false;

        // 람다로 등록된 stale 핸들러 모두 끊어냄
        OnRoutePointReached = null;

        Debug.Log($"[AirPlane] 명령 큐/이벤트 핸들러 클리어 (제거된 명령: {queuedCount}개)");
    }

    /// <summary>
    /// 명령 추가 (외부에서 호출 - StateManager가 사용)
    ///
    /// 기존 방식:
    ///   StateManager가 직접 이벤트 핸들러 등록
    ///   → 중복 등록 가능, 해제 타이밍 복잡
    ///
    /// 새로운 방식:
    ///   StateManager가 명령 객체 생성 → EnqueueCommand로 전달
    ///   → 큐에서 순차 실행, 자동 정리
    /// </summary>
    /// <param name="command">실행할 명령</param>
    public void EnqueueCommand(AirplaneCommand command)
    {
        Debug.Log($"[AirPlane] 명령 추가: {command}");
        commandQueue.Enqueue(command);

        // 현재 실행 중인 명령이 없으면 즉시 실행
        if (!isExecutingCommand)
        {
            ProcessNextCommand();
        }
    }

    /// <summary>
    /// 큐에서 다음 명령을 꺼내 실행
    ///
    /// 실행 흐름:
    /// 1. 큐에서 명령 하나 꺼내기
    /// 2. 명령 타입에 따라 실행 (switch문)
    /// 3. 완료되면 CompleteCurrentCommand 호출
    /// 4. CompleteCurrentCommand에서 다시 ProcessNextCommand 호출 (재귀)
    /// 5. 큐가 빌 때까지 반복
    /// </summary>
    private void ProcessNextCommand()
    {
        if (commandQueue.Count == 0)
        {
            isExecutingCommand = false;
            currentCommand = null;
            Debug.Log("[AirPlane] 모든 명령 완료. 대기 상태.");
            return;
        }

        currentCommand = commandQueue.Dequeue();
        isExecutingCommand = true;

        Debug.Log($"[AirPlane] 명령 실행 시작: {currentCommand}");

        ExecuteCommand(currentCommand);
    }

    /// <summary>
    /// 명령 타입에 따라 실행
    ///
    /// 각 명령 타입별 동작:
    /// - MoveToAndWait: 목표까지 이동 후 대기 모드 전환
    /// - MoveTo: 목표까지 이동 (대기 없음)
    /// - WaitAtCurrent: 현재 위치에서 대기
    /// - Continue: 대기 해제
    /// - TeleportTo: 즉시 텔레포트
    /// </summary>
    /// <param name="command">실행할 명령</param>
    private void ExecuteCommand(AirplaneCommand command)
    {
        switch (command.type)
        {
            case AirplaneCommandType.MoveToAndWait:
                ExecuteMoveToAndWait(command);
                break;

            case AirplaneCommandType.MoveTo:
                ExecuteMoveTo(command);
                break;

            case AirplaneCommandType.WaitAtCurrent:
                // 현재 위치에서 즉시 대기
                SetWaitingMode(true);
                CompleteCurrentCommand(true);
                break;

            case AirplaneCommandType.Continue:
                // 대기 해제
                SetWaitingMode(false);
                CompleteCurrentCommand(true);
                break;

            case AirplaneCommandType.TeleportTo:
                // 즉시 텔레포트
                MoveToPointImmediately(command.targetRouteIndex);
                CompleteCurrentCommand(true);
                break;

            default:
                Debug.LogError($"[AirPlane] 알 수 없는 명령 타입: {command.type}");
                CompleteCurrentCommand(false);
                break;
        }
    }

    /// <summary>
    /// MoveToAndWait 명령 실행: 목표까지 이동 후 대기
    ///
    /// 기존 방식과 차이점:
    /// - 기존: StateManager가 이벤트 핸들러 직접 등록
    /// - 신규: 일회성 핸들러를 명령 내부에서 자동 등록/해제
    ///
    /// 사용 사례:
    /// - Point 절차에서 출발 지점까지 이동 후 교관 신호 대기
    /// </summary>
    /// <param name="command">명령 객체</param>
    private void ExecuteMoveToAndWait(AirplaneCommand command)
    {
        int currentIdx = GetCurrentRouteIndex();

        // 이미 목표 지점 이상에 있으면 즉시 완료
        if (currentIdx >= command.targetRouteIndex)
        {
            Debug.Log($"[AirPlane] 이미 목표 지점({command.targetRouteIndex})에 도달. 대기 모드로 전환.");
            SetWaitingMode(true);
            CompleteCurrentCommand(true);
            return;
        }

        // 목표 지점까지 이동 시작
        SetWaitingMode(false);

        // 일회성 이벤트 핸들러 등록
        // 핵심: 명령 완료 시 자동으로 해제됨 (람다 내부에서 -= handler)
        Action<int> handler = null;
        handler = (routeIdx) =>
        {
            if (routeIdx >= command.targetRouteIndex)
            {
                // 목표 도달: 대기 모드 전환
                SetWaitingMode(true);
                OnRoutePointReached -= handler;  // 자동 해제 (기존 문제 해결!)
                CompleteCurrentCommand(true);
            }
        };

        OnRoutePointReached += handler;
    }

    /// <summary>
    /// MoveTo 명령 실행: 목표까지 이동 (대기 없음)
    ///
    /// 사용 사례:
    /// - Point 절차에서 목표 지점까지 이동하여 절차 완료
    /// - 출발 지점과 목표 지점 사이에 있을 때
    /// </summary>
    /// <param name="command">명령 객체</param>
    private void ExecuteMoveTo(AirplaneCommand command)
    {
        int currentIdx = GetCurrentRouteIndex();

        // 이미 목표 지점 이상에 있으면 즉시 완료
        if (currentIdx >= command.targetRouteIndex)
        {
            Debug.Log($"[AirPlane] 이미 목표 지점({command.targetRouteIndex}) 도달. 계속 진행.");
            CompleteCurrentCommand(true);
            return;
        }

        // 목표 지점까지 이동 시작
        SetWaitingMode(false);

        // 일회성 이벤트 핸들러 등록
        Action<int> handler = null;
        handler = (routeIdx) =>
        {
            if (routeIdx >= command.targetRouteIndex)
            {
                // 목표 도달: 계속 진행 (대기 안 함)
                OnRoutePointReached -= handler;  // 자동 해제
                CompleteCurrentCommand(true);
            }
        };

        OnRoutePointReached += handler;
    }

    /// <summary>
    /// 현재 명령 완료 처리
    ///
    /// 실행 흐름:
    /// 1. 완료 콜백 호출 (StateManager에 알림)
    /// 2. 다음 명령 처리 (ProcessNextCommand 재귀 호출)
    ///
    /// 기존 방식과 차이점:
    /// - 기존: 이벤트 발생 → StateManager가 수동으로 다음 행동 결정
    /// - 신규: 콜백 호출 → 자동으로 다음 명령 처리
    /// </summary>
    /// <param name="success">명령 성공 여부</param>
    private void CompleteCurrentCommand(bool success)
    {
        if (currentCommand == null)
        {
            Debug.LogWarning("[AirPlane] CompleteCurrentCommand 호출되었지만 currentCommand가 null입니다.");
            return;
        }

        Debug.Log($"[AirPlane] 명령 완료: {currentCommand} | 성공: {success}");

        // 완료 콜백 호출 (StateManager에 알림)
        currentCommand.onComplete?.Invoke(success);

        // 다음 명령 처리
        ProcessNextCommand();
    }

    // ============================================================
    // 기존 메서드들 (유지)
    // ============================================================

    /// <summary>
    /// 특정 포인트로 즉시 이동 (강제 이동)
    /// </summary>
    /// <param name="targetIndex">목표 포인트 인덱스</param>
    public void MoveToPointImmediately(int targetIndex)
    {
        if (routePoints == null || targetIndex < 0 || targetIndex >= routePoints.Count)
        {
            Debug.LogError($"[AirPlane] 잘못된 포인트 인덱스: {targetIndex} (routePoints.Count={routePoints?.Count ?? 0})");
            return;
        }

        // 동일 인덱스 텔레포트는 no-op으로 허용 (스킵 시 동일 offset 절차 연속에서 발생)
        if (targetIndex == currentRouteIndex)
        {
            Debug.Log($"[AirPlane] {targetIndex}번 포인트에 이미 위치. 이벤트만 재발화.");
            OnRoutePointReached?.Invoke(targetIndex);
            return;
        }

        // 역방향 텔레포트는 명시적 경고로 처리하되 진행은 허용 (스킵 설계상 발생 가능)
        if (targetIndex < currentRouteIndex)
        {
            Debug.LogWarning($"[AirPlane] 역방향 텔레포트 감지: {currentRouteIndex} → {targetIndex}");
        }

        // 목표 포인트로 즉시 이동
        Transform targetPoint = routePoints[targetIndex];
        Vector3 targetPos = new Vector3(targetPoint.position.x, jumpHeight, targetPoint.position.z);
        transform.position = targetPos;

        Debug.Log($"[AirPlane] {targetIndex}번 포인트({targetPoint.name})로 즉시 이동 완료");

        // 현재 위치 다음 목표 지점으로 업데이트
        currentRouteIndex = targetIndex;
        targetRouteIndex = currentRouteIndex + 1;

        // 텔레포트도 자연 도달과 동일하게 OnRoutePointReached 발화 (BUG-3 수정)
        // → MoveTo/MoveToAndWait 핸들러가 텔레포트 후에도 정상 완료 가능
        OnRoutePointReached?.Invoke(targetIndex);
    }
    
    public void DoorOpen()
    {
        audioDoorOpen.Play();
        doorOpenEndCalled = false;  // 플래그 초기화

        // 문 열리기 시작: 눈부신 효과 (postExposure 4로 빠르게 전환)
        StateManager_New.Inst.volumeCtrl.ApplyPreset("OpenDoorStart", 0.1f);

        _updateAction += DoorOpenAction;
    }

    private bool doorOpenCalled = false;
    
    /// <summary>
    /// 문 열기
    /// </summary>
    private void DoorOpenAction()
    {
        _doorOpenSpendTime += (Time.deltaTime * 1.1f);
        _lerpTime01 = _doorOpenSpendTime / _doorEachOpenTime;
        _lerpTime02 = (_doorOpenSpendTime - _doorOpenInterval) / _doorEachOpenTime;
        door01.localEulerAngles = Vector3.Lerp(Vector3.zero, _doorOpen01Vector, _lerpTime01);
        door02.localEulerAngles = Vector3.Lerp(Vector3.zero, _doorOpen02Vector, _lerpTime02);

        // 문이 조금 열렸을 때 한 번만 서서히 복귀 효과 (postExposure 0.5로)
        if (!doorOpenCalled && _lerpTime01 >= 0.2f)
        {
            StateManager_New.Inst.volumeCtrl.ApplyPreset("OpenDoor", 1f);
            Debug.Log("OpenDoor ApplyPreset");
            doorOpenCalled = true;
        }
        
        if (!doorOpenEndCalled && _lerpTime01 >= 0.4f)
        {
            StateManager_New.Inst.volumeCtrl.ApplyPreset("OpenDoorEnd", 3f);  // 8초에 걸쳐 복귀
            Debug.Log("OpenDoorEnd ApplyPreset");
            doorOpenEndCalled = true;
        }

        if (_lerpTime02 >= 1.0f)
        {
            door01.localEulerAngles = _doorOpen01Vector;
            door02.localEulerAngles = _doorOpen02Vector;
            _lerpTime01 = 0.0f;
            _lerpTime02 = 0.0f;
            doorOpenCalled = false;
            doorOpenEndCalled = false;  // 플래그 리셋
            audioDoorOpen.Stop();
            _updateAction -= DoorOpenAction;
            DoorOpenCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 문을 즉시 열린 상태로 셋업. 스킵 흐름에서 호출되며 문 회전/오디오/햇빛 페이드를 모두 생략.
    /// 정상 흐름의 DoorOpen()이 호출되지 않은 시점에서도 안전하게 호출 가능 (idempotent).
    /// </summary>
    public void OpenDoorImmediately()
    {
        Debug.Log("[AirPlane] 문 즉시 열림 (스킵)");

        // 진행 중이던 DoorOpenAction 안전 해제
        _updateAction -= DoorOpenAction;

        // 문 각도 최종 상태로 강제
        door01.localEulerAngles = _doorOpen01Vector;
        door02.localEulerAngles = _doorOpen02Vector;

        // 누적/플래그 리셋
        _doorOpenSpendTime = 0f;
        _lerpTime01 = 0f;
        _lerpTime02 = 0f;
        doorOpenCalled = false;
        doorOpenEndCalled = false;

        if (audioDoorOpen != null && audioDoorOpen.isPlaying) audioDoorOpen.Stop();

        // 햇빛 페이드를 OpenDoorEnd 최종 상태로 즉시 복귀
        if (StateManager_New.Inst != null && StateManager_New.Inst.volumeCtrl != null)
        {
            StateManager_New.Inst.volumeCtrl.ApplyPreset("OpenDoorEnd", 0f);
        }
    }
    
    private void CloudsHide()
    {
        stopCloud.transform.parent = sceneRoot;
        clouds.SetActive(false);
    }
}
