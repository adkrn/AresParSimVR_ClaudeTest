using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class PlayCharacter : MonoBehaviour
{
    [Header("이동/물리 파라미터")]
    [SerializeField] float rotSpeed     = 720f;
    [SerializeField] float maxFallSpeed = 85f;
    [SerializeField] float parachuteMax = 5f;
    
    public Animator animator;
    public AudioSource sfx;
    [SerializeField] private JumperSet jumperSet;
    public GameObject jumperParachute;
    [SerializeField] private GameObject o2Mask;
    [SerializeField] private ConditionalGageUI gageUI;
    [SerializeField] private PokeButton pokeBtn;
    [SerializeField] private AudioClip[] audioClips;
    [SerializeField] private Transform sceneRoot;
    [SerializeField] private Transform initParent;
    [SerializeField] public AresHardwareParagliderController paraCtrl;
    [SerializeField] private Transform landingPoint;
    [SerializeField] private Transform goalPoint;
    [SerializeField] private GameObject cloud;
    
    [SerializeField] private StateManager_New _stateManagerInspector;
    [SerializeField] private CameraController _cameraInspector;
    
    [Header("현재 낙하속도")]
    [SerializeField] private float currentSpeed = 0f; 
    [Header("자유낙하항력계수: 기본 0.005")]
    [SerializeField] private float dragCoefficient = 0.005f;
    [Header("낙하산 전개 시 사용할 항력 계수 (크게 설정): 기본 1.0")]
    [SerializeField] float parachuteDragCoefficient = 1.0f;
    [Header("낙하산 전개 후 최대 속도")] 
    [SerializeField] float parachuteMaxSpeed = 5f; // 낙하산 전개 후 최대 속도

    [Header("낙하산 관련")] 
    [SerializeField] private GameObject pullCordHand;
    [SerializeField] private TriggerListener pullCordCol;
    
    [ShowConst("중력 및 항력 설정")]
    private const float Gravity = 9.80665f; // 중력 가속도
    [ShowConst("속도 제한: 자유낙학 기본 60m/s(200), 85m/s(300), 낙하산 전개 5m/s", label: "자유낙하 시 사용할 항력 계수 (0.5 * rho * Cd * A / m)")]
    private const float fallingMaxSpeed = 85f; // 자유낙하 초당 최대 속도(200km/h -> 약60, 300km/h -> 약85)
    [ShowConst("공기밀도: 기본 1.225kg/m³")]
    private const float rho = 1.225f; // 공기 밀도 (kg/m³)
    [ShowConst("인체항력계수: 기본 1.2")] 
    private const float Cd = 1.2f; // 인체 항력 계수
    [ShowConst("투영면적: 기본 0.7m²")] 
    private const float A = 0.7f; // 투영 면적 (m²)
    [ShowConst("장비착용무게: 기본 100kg")] 
    private const float m = 100f; // 질량 (kg)
    
    // 충돌 감지시 호출될 이벤트 정의
    public event Action OnGroundCollision;
    public event Action OnEndDeploy;
    public Action _updateAction;

    private Func<float> _fallDistance;
    private StateManager_New _stateManager;
    private CameraController _camera;
    
    // 컴포넌트 캐싱
    private Animator _paraAnimCache;
    
    private float startFallTime;
    private Vector3 startFallPos;

    private void Start()
    {
        // 자유낙하 항력계수 계산
        dragCoefficient = CalculateDragCoefficient(rho: rho, Cd: Cd, area: A, mass: m);

        // Inspector에서 할당된 값 사용, 없으면 FindAnyObjectByType 사용
        _stateManager = _stateManagerInspector ? _stateManagerInspector : FindAnyObjectByType<StateManager_New>();
        _camera = _cameraInspector ? _cameraInspector : FindAnyObjectByType<CameraController>();
        
        StateManager.OnInit += Init;
    }
    
    private void Init()
    {
        // 1. 업데이트 액션 초기화
        _updateAction = null;
        
        // 2. 캐릭터 위치 초기화
        transform.parent = initParent;
        transform.localPosition = new Vector3(0, 0, 0);
        Debug.Log("[PlayCharacter] 캐릭터 위치 초기화");
        
        // 3. 물리 초기화
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = false;
        Debug.Log("[PlayCharacter] 물리 설정 초기화");
        
        // 4. 낙하산 다시 감추기
        jumperParachute.SetActive(false);
        Debug.Log("[PlayCharacter] 낙하산 다시 감추기 완료");
    }

    private void Update()
    {
        // 조건부 Update 패턴 적용
        if (_updateAction != null)
            _updateAction.Invoke();
    }
    
    /// <summary>
    /// 앉기
    /// 임시로 앉는 애니메이션 후 성공처리를 위해 코루틴으로 딜레이 주고 성공처리
    /// </summary>
    public void SitDown()
    {
        Debug.Log("[PlayCharacter] 교육생이 앉았다.");

        StartCoroutine(TempDelayAni());
    }

    /// <summary>
    /// 일어서기
    /// 임시로 일어서는 애니메이션 대신 코루틴으로 딜레이 주고 성공처리
    /// </summary>
    public void Stand()
    {
        Debug.Log("[PlayCharacter] 교육생이 일어섰다.");
        
        StartCoroutine(TempDelayAni());
    }

    public void SkipStand()
    {
        Debug.Log("[PlayCharacter] 교육생이 일어섰다.");
    }
    
    public void SkipSitDown()
    {
        Debug.Log("[PlayCharacter] 교육생이 일어섰다.");
    }
    
    public IEnumerator TempDelayAni()
    {
        yield return new WaitForSeconds(1.0f);
        UIManager.Inst.OnSuccessAction();
    }
    
    public void Jump()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>Jump 실행");
        // 낙하시 다리 감추기
        //jumperSet.ShowHideSet(2, false);
        
        // 베이스 캠프 위로 이동.
        transform.parent = sceneRoot;
        transform.position = new Vector3(landingPoint.position.x, transform.position.y, landingPoint.position.z);
        transform.eulerAngles = new Vector3(0, 180, 0);
        cloud.transform.parent = sceneRoot;
        
        paraCtrl.JumpStart();
        
        startFallPos = transform.position;
        startFallTime = Time.time;
        
        // 점프 했을때 ParaCtrl의 중력 켜주기
        // paraCtrl.rb.useGravity = true;
        //_updateAction += FallTick;
        //_updateAction += EmergencyO2Mask;
        //_fallDistance = GetFreeFallDistance;
    }
    
    private void FallTick()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>FallTick 실행");
        transform.position += new Vector3(0, _fallDistance(), 0);
    }
    
    // private void EmergencyO2Mask()
    // {
    //     if(transform.position.y <= 800)
    //     {
    //         o2Mask.gameObject.SetActive(true);
    //         gageUI.InitUI("산소마스크 이상감지","ui_ico_mask_white",
    //             () => o2Mask.SetActive(false), 3f);
    //         _updateAction -= EmergencyO2Mask;
    //     }
    // }

    [ProcedureHandler("jump")]
    private void JumpSoundEffect()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>JumpSoundEffect 실행");
        sfx.clip = audioClips[0];
        sfx.Play();
        StartCoroutine(NextSound(1, true));
        
        _stateManager.OnSuccess();
        _stateManager.OnProcedureComplete();
    }

    /// <summary>
    /// 낙하산 줄 당기기
    /// </summary>
    public void AddPullCordTrigger()
    {
        // 교육생이 낙하산 줄을 당길때 낙하산이 펼쳐지는 액션 활성화
        pullCordHand.SetActive(true);
        pullCordCol.gameObject.SetActive(true);
        pullCordCol.OnPlayerEntered += Deploy;
        
        // 설정된 고도에 도달했을때 자동으로 펼쳐지는 트리거 활성화
        AltTrigger(DataManager.Inst.scenario.autoActiveAltitude);
    }

    private float _alt;

    /// <summary>
    /// 고도 조건 완료 이벤트
    /// </summary>
    public void AltTrigger(float alt)
    {
        Debug.Log($"<color=yellow>[PlayCharacter]</color>AltTrigger 등록: {alt}m 이하 도달 시 자동 낙하산 전개");
        _alt = alt;
        _updateAction += AltCheck;
    }

    private void AltCheck()
    {
        // Debug.Log("<color=yellow>[PlayCharacter]</color>AltCheck 실행");
        if (transform.position.y > _alt) return;
        
        _updateAction -= AltCheck;
        Debug.Log($"<color=yellow>[PlayCharacter]</color>고도 {_alt} 이하 도달 → 자동 낙하산 전개");

        Deploy();
    }

    /// <summary>
    /// 낙하산 펼치기
    /// </summary>
    [ProcedureHandler("parachute_deploy", ExecMode.Always)]
    public void Deploy()
    {
        // 낙하산줄 숨기기
        pullCordCol.OnPlayerEntered -= Deploy;
        pullCordHand.SetActive(false);
        pullCordCol.gameObject.SetActive(false);
        _updateAction -= AltCheck;
        
        Debug.Log("<color=yellow>[PlayCharacter]</color>Deploy 실행");
        // 감춘 다리 다시 보이기
        //jumperSet.ShowHideSet(2, true);
        
        // 낙하산 펼치는 소리 재생
        sfx.clip = audioClips[2];
        sfx.loop = false;
        sfx.Play();

        // 낙하산 컨트롤러 활성화
        paraCtrl.ParaDeploy();
        
        // 카메라 노이즈를 낙하산 전용으로 변경
        _camera.OnParaNoiseCam();
        
        // 캐릭터 낙하산 펼칠때 흔들리는 애니메이션 실행
        // _fallDistance = GetParachuteFallDistance;
        animator.Play("ParaOpen", -1, 0f);
        
        // 낙하산 펼침 애니메이션 실행
        jumperParachute.SetActive(true);
        
        // GetComponent 캐싱 적용
        if (_paraAnimCache == null)
            _paraAnimCache = jumperParachute.GetComponent<Animator>();
        
        _paraAnimCache?.SetTrigger("deploy");
        AnimSequenceCheck.StartTrigger(_paraAnimCache, "deploy", EndDeployParachute);
        
        StartCoroutine(NextSound(3, true));
    }

    [ProcedureHandler("oxygen_check")]
    private void CheckOxygen()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>CheckOxygen 게이지 3초 채우기");
        gageUI.ActiveTrigger(3f);
    }

    private void EndDeployParachute()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>EndDeployParachute 실행");
        //jumperSet.ShowHideSet(1, true);
        
        _stateManager.OnSuccess();
        _stateManager.OnProcedureComplete();
    }
    
    IEnumerator NextSound(int clipIndex, bool isLoop = false)
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>NextSound 실행");
        var clipLength = sfx.clip.length;
        yield return new WaitForSeconds(clipLength);

        sfx.clip = audioClips[clipIndex];
        sfx.loop = isLoop;
        sfx.Play();
    }
    
    /// <summary>
    /// 항력 가속도(drag)를 고려한 자유낙하 동안의 이동거리 (m)
    /// </summary>
    private float GetFreeFallDistance()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>GetFreeFallDistance 실행");
        // 항력으로 인한 감속 가속도: a_drag = dragCoefficient * v^2
        float dragAcc = dragCoefficient * currentSpeed * currentSpeed;
        // 순가속도 = 중력 - 항력
        float acceleration = Gravity - dragAcc;
        // 속도 업데이트
        currentSpeed += acceleration * Time.deltaTime;
        // 최대 속도 제한
        currentSpeed = Mathf.Clamp(currentSpeed, 0f, fallingMaxSpeed);
        // 델타타임 동안 이동한 거리 반환
        return (currentSpeed * Time.deltaTime) * -1;
    }

    /// <summary>
    /// 낙하산 전개 시(높은 항력) 이동거리 계산 (m)
    /// </summary>
    private float GetParachuteFallDistance()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>GetParachuteFallDistance 실행");
        // 낙하산 전개 시 더 큰 항력 계수 적용
        float dragAcc = parachuteDragCoefficient * currentSpeed * currentSpeed;
        float acceleration = Gravity - dragAcc;
        currentSpeed += acceleration * Time.deltaTime;
        // 낙하산 전개 후에는 훨씬 낮은 최대 속도로 제한
        currentSpeed = Mathf.Clamp(currentSpeed, 0f, parachuteMaxSpeed);
        return (currentSpeed * Time.deltaTime) * -1;
    }

    // [ProcedureHandler("flare")]
    // private void Flare()
    // {
    //     Debug.Log("Flare 절차 실행");
    //     gageUI.onComplete = _stateManager.OnProcedureComplete;
    // }

    private void OnCollisionEnter(Collision collision)
    {
        // 디버그 로그 조건부 컴파일
        #if UNITY_EDITOR
        Debug.Log($"<color=yellow>[PlayCharacter]</color>충돌된 오브젝트 이름과 태그: {collision.gameObject.name}, {collision.gameObject.tag}");
        #endif
        
        // 오브젝트에 충돌할때 각 태그에 맞게 처리
        switch (collision.gameObject.tag)
        {
            case "Ground":
            {
                OnGround();
                SetFallDetailData(collision.gameObject.tag);
                break;
            }
            case "Tree":
            {
                OnTree();
                SetFallDetailData(collision.gameObject.tag);
                break;
            }
            case "OnRock":
            {
                OnRock();
                SetFallDetailData(collision.gameObject.tag);
                break;
            }
        }
    }

    /// <summary>
    /// 절차와 상관없는 낙하 정보들을 결과UI에 표시한다.
    /// </summary>
    public void SetFallDetailData(string collisionTag)
    {
        // 낙하거리, 낙하시간 정보를 결과화면에 추가 
        var fallDuration = Time.time - startFallTime;
        var fallDistance = Vector3.Distance(startFallPos, transform.position);

        fallDuration = Mathf.Max(0f, fallDuration);
        fallDistance = Mathf.Max(0f, fallDistance);

        UIManager.Inst.AddResult(EvName.FallTime, Mathf.RoundToInt(fallDuration).ToString());
        UIManager.Inst.AddResult(EvName.TotalDistance, Mathf.RoundToInt(fallDistance).ToString());
        
        // 착지 정보 결과화면에 추가
        UIManager.Inst.AddResult(EvName.LandingType, collisionTag);
        
        // 낙하속도 결과화면에 추가
        //UIManager.Inst.AddResult(EvName.LandingSpeed, Mathf.RoundToInt(paraCtrl.impactSpeed).ToString());
        
        // 플레어 성공여부 결과화면에 추가
        // var isFlare = paraCtrl.brakeMultiplier < 0.3f ? "성공" : "실패";
        // UIManager.Inst.AddResult(EvName.FlareComplete, isFlare);
        
        // 목표지점과 거리 측정해서 평가 결과 추가
        var targetDistance = Vector3.Distance(transform.position, goalPoint.position);
        UIManager.Inst.AddResult(EvName.TargetDistance, Mathf.RoundToInt(targetDistance).ToString());
    }

    private void OnGround()
    {
        Debug.Log($"땅과 충돌했습니다");
        
        // 낙하산 컨트롤러 초기화
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        paraCtrl.isJumpStart = false;
        
        // 낙하산 비활성화
        jumperParachute.SetActive(false);
        //_fallDistance = () => 0;
        
        // 캐릭터 위치를 땅에 떨어진 위치에 고정
        Vector3 position = transform.position;
        transform.position = position;
        
        // 카메라 노이즈 액션 끄기
        _camera.OnGround();
        
        // 땅에 떨어질때 애니메이션 실행
        AresHardwareService.Inst.SetEvent(AresEvent.Landing);
        animator.Play("Landing");
        // 땅에 떨어질때 실행할 매서드 있으면 실행.
        AnimSequenceCheck.StartTrigger(animator, "Landing", (() =>
        {
            // AresHardwareService.Inst.SetEvent(AresEvent.Landed);
            OnGroundCollision?.Invoke();
        }));
        Debug.Log("땅에 도착했습니다.");
    }

    private void OnTree()
    {
        Debug.Log("나무에 충돌했습니다.");
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        jumperParachute.SetActive(false);
        
        Vector3 position = transform.position;
        transform.position = position;
    }

    private void OnRock()
    {
        Debug.Log("돌에 충돌했습니다.");
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        jumperParachute.SetActive(false);
        
        Vector3 position = transform.position;
        transform.position = position;
    }
    
    /// <summary>
    /// 자유낙하시 항력계수 계산 메서드
    /// </summary>
    /// <param name="rho">공기밀도</param>
    /// <param name="Cd">인체항력계수</param>
    /// <param name="area">투영면적</param>
    /// <param name="mass">몸무게</param>
    /// <returns></returns>
    public float CalculateDragCoefficient(float rho, float Cd, float area, float mass)
    {
        return 0.5f * rho * Cd * area / mass;
    }

    #region Public Getters for ParticipantManager
    /// <summary>
    /// 현재 낙하 속도를 반환합니다
    /// </summary>
    public float GetCurrentSpeed()
    {
        return currentSpeed;
    }

    /// <summary>
    /// 수직 낙하 속도를 반환합니다 (양수 값)
    /// </summary>
    public float GetFallSpeed()
    {
        return Mathf.Abs(currentSpeed);
    }

    /// <summary>
    /// 낙하산 전개 여부를 반환합니다
    /// </summary>
    public bool IsParachuteDeployed()
    {
        // 낙하산 오브젝트가 활성화되어 있으면 전개된 것으로 판단
        return jumperParachute != null && jumperParachute.activeSelf;
    }

    /// <summary>
    /// 현재 고도를 반환합니다
    /// </summary>
    public float GetAltitude()
    {
        return transform.position.y;
    }
    #endregion
}
