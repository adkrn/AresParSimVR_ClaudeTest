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

    [Header("구름")] 
    public GameObject clouds;
    public GameObject stopCloud;
    [SerializeField] private Transform sceneRoot;

    private StateManager _stateManager;
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
    }

    private void Init()
    {
        // 비행기 도착 고도 가져와서 설정
        // 비행기 이륙 후 도착 고도에서 낙하를 시작함.
        jumpHeight = DataManager.Inst.scenario.endOperationalAltidute;
        
        // Route 포인트가 설정될 때까지 임시 위치 (화면 밖)
        // SetRoutePoints가 호출되면 0번 포인트로 이동함
        transform.position = new Vector3(0, jumpHeight, -5000);  // 더 멀리 배치
        Debug.Log($"[AirPlane] 비행기 임시 위치 설정: {transform.position}");

        // 문 여는 애니메이션 실행
        door01.localEulerAngles = Vector3.zero;
        door02.localEulerAngles = Vector3.zero;
        _doorOpen01Vector = new Vector3(doorOpen01_Angle, 0, 0);
        _doorOpen02Vector = new Vector3(doorOpen02_Angle, 0, 0);
        _doorEachOpenTime = doorOpenTime - _doorOpenInterval;
        Debug.Log("[AirPlane] 문 초기화 완료");
        DoorOpen();
        Debug.Log("[AirPlane] 문열기");
        
        isInitialized = true;
        isWaitingForSignal = true;
    }

    private void Update()
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
            // isWaitingForSignal = true;
            Debug.Log($"[AirPlane] {reachedIndex}번 포인트 도달 완료.");
            
            // 다음 포인트 인덱스는 증가시키지만 이동은 하지 않음
            currentRouteIndex = targetRouteIndex;
            targetRouteIndex++;
            
            if (currentRouteIndex >= routePoints.Count)
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
    
    /// <summary>
    /// 특정 포인트로 즉시 이동 (강제 이동)
    /// </summary>
    /// <param name="targetIndex">목표 포인트 인덱스</param>
    public void MoveToPointImmediately(int targetIndex)
    {
        if (routePoints == null || targetIndex >= routePoints.Count || targetIndex < 0)
        {
            Debug.LogError($"[AirPlane] 잘못된 포인트 인덱스: {targetIndex}");
            return;
        }
        //isWaitingForSignal = true;
        
        // 목표 포인트로 즉시 이동
        Transform targetPoint = routePoints[targetIndex];
        Vector3 targetPos = new Vector3(targetPoint.position.x, jumpHeight, targetPoint.position.z);
        transform.position = targetPos;

        Debug.Log($"[AirPlane] {targetIndex}번 포인트({targetPoint.name})로 즉시 이동 완료");
        
        // 현재 위치 다음 목표 지점으로 업데이트
        currentRouteIndex = targetIndex;
        targetRouteIndex = currentRouteIndex + 1;
        
        // 포인트 도달 이벤트 발생
        OnRoutePointReached?.Invoke(targetIndex);
    }
    
    public void DoorOpen()
    {
        audioDoorOpen.Play();
        _updateAction += DoorOpenAction;
    }
    
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
        if (_lerpTime02 >= 1.0f)
        {
            door01.localEulerAngles = _doorOpen01Vector;
            door02.localEulerAngles = _doorOpen02Vector;
            _lerpTime01 = 0.0f;
            _lerpTime02 = 0.0f;
            audioDoorOpen.Stop();
            _updateAction -= DoorOpenAction;
            DoorOpenCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 문여는 애니메이션 스킵
    /// </summary>
    // private void DoorOpenSkip()
    // {
    //     Debug.Log("문 열리는 애니메이션 스킵처리");
    //     door01.localEulerAngles = _doorOpen01Vector;
    //     door02.localEulerAngles = _doorOpen02Vector;
    //     _lerpTime01 = 0.0f;
    //     _lerpTime02 = 0.0f;
    //     audioDoorOpen.Stop();
    //     _updateAction -= DoorOpenAction;
    //     //DoorOpenCompleted?.Invoke();
    // }
    
    private void CloudsHide()
    {
        stopCloud.transform.parent = sceneRoot;
        clouds.SetActive(false);
    }
}
