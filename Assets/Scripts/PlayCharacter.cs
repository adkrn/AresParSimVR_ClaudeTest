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
    [SerializeField] private GameObject airPlaneGround;
    
    [SerializeField] private StateManager_New _stateManagerInspector;
    [SerializeField] private CameraController _cameraInspector;
    [SerializeField] private Transform jumpPos;
    [SerializeField] private LightCtrl lightCtrl;
    [SerializeField] private VolumeCtrl volumeCtrl;
    [SerializeField] private GameObject followCloud;

    [Header("고리 오브젝트, wearingSet에 메인 씬 활성화 후 연결할것들")] 
    public Collider hookTrigger;
    public GameObject hookObj;
    
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
    
    [SerializeField] private SimpleGroundingControl groundingControl;

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
    private bool isLanding = false;

    /// <summary>메인 낙하산 전개 여부 — 중복 Deploy 가드용 (D6).
    /// Deploy() / DeploySubPara() / TotalMalfunction MainParaOff 분기 진입 시 true.
    /// "메인 또는 reserve 중 하나는 활성" 의 의미로 해석.</summary>
    public bool isParaDeployed { get; private set; }

    private Action _onEndAltitudeReached;

    private void Start()
    {
        // 자유낙하 항력계수 계산
        dragCoefficient = CalculateDragCoefficient(rho: rho, Cd: Cd, area: A, mass: m);

        // Inspector에서 할당된 값 사용, 없으면 FindAnyObjectByType 사용
        _stateManager = _stateManagerInspector ? _stateManagerInspector : FindAnyObjectByType<StateManager_New>();
        _camera = _cameraInspector ? _cameraInspector : FindAnyObjectByType<CameraController>();
        lightCtrl = FindAnyObjectByType<LightCtrl>();
        volumeCtrl = FindAnyObjectByType<VolumeCtrl>();
        var wSet = FindAnyObjectByType<WearingSet>();
        sfx = sfx ? sfx : wSet.sfx;
        pullCordCol = wSet.pullCordCol;
        pullCordHand = wSet.pullCordHand;
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
        // Unity fake-null 대응: `?.`는 Inspector 미할당 SerializeField 의 UnassignedReferenceException 을 catch 못 함.
        if (reserveParachuteVisual != null) reserveParachuteVisual.SetActive(false);   // Sd2c-mini v3 — 예비낙하산도 동기 비활성
        Debug.Log("[PlayCharacter] 낙하산 다시 감추기 완료");

        // 5. 전개 가드 초기화 — 재훈련 시작 시 isParaDeployed 잔재 해제 (D6)
        isParaDeployed = false;
        _onEndAltitudeReached = null;
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
        transform.position = jumpPos.position;
        transform.parent = sceneRoot;
        //transform.eulerAngles = new Vector3(0, 180, 0);
        cloud.transform.parent = sceneRoot;
        
        paraCtrl.JumpStart();
        StartFreeFallPose();
        
        startFallPos = transform.position;
        startFallTime = Time.time;

        _stateManager.isJump = true;
        JumpSoundEffect();
        if (groundingControl == null) groundingControl = FindAnyObjectByType<SimpleGroundingControl>();
        airPlaneGround.layer = 0;
        airPlaneGround.SetActive(false);
        groundingControl.DisableImmediate();

        // 점프 했을때 ParaCtrl의 중력 켜주기
        //paraCtrl.rb.useGravity = true;
        _updateAction += HeightCheck;
        //_updateAction += EmergencyO2Mask;
        //_fallDistance = GetFreeFallDistance;
        _updateAction += () =>
        {
            ParticipantManager.Inst.SetMonitoringDataPlayerFlag(paraCtrl.isPara, paraCtrl.isSubPara, isLanding);
        };
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
    
    private void JumpSoundEffect()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>JumpSoundEffect 실행");
        sfx.clip = audioClips[0];
        sfx.Play();
        StartCoroutine(NextSound(1, true));
    }

    /// <summary>
    /// 낙하산 줄 당기기
    /// </summary>
    public void AddPullCordTrigger()
    {
        // 교육생이 낙하산 줄을 당길때 낙하산이 펼쳐지는 액션 활성화
        if (pullCordCol != null)
        {
            pullCordHand.SetActive(true);
            pullCordCol.gameObject.SetActive(true);
            pullCordCol.OnPlayerEntered += Deploy;
        }
        
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
    /// 고도 도달 시 절차 완료 신호만 발생 (Deploy 미실행) — STANDARD FreeFall(EndAltitude) 전용 (D3).
    /// HAHO/HALO 의 AddPullCordTrigger 와 분리 (D8).
    /// </summary>
    public void AddEndAltitudeTrigger(float alt, Action onReached)
    {
        Debug.Log($"<color=yellow>[PlayCharacter]</color>AddEndAltitudeTrigger 등록: {alt}m 이하 도달 시 절차 완료 신호 (Deploy 미실행)");
        _alt = alt;
        _onEndAltitudeReached = onReached;
        _updateAction += EndAltitudeCheck;
    }

    private void EndAltitudeCheck()
    {
        if (transform.position.y > _alt) return;
        _updateAction -= EndAltitudeCheck;

        Debug.Log($"<color=yellow>[PlayCharacter]</color>고도 {_alt}m 이하 도달 → EndAltitude 절차 완료 신호");
        var cb = _onEndAltitudeReached;
        _onEndAltitudeReached = null;
        cb?.Invoke();
    }

    /// <summary>
    /// 메인 의도적 미전개 마킹 — TotalMalfunction action=MainParaOff 케이스 (D6).
    /// Deploy 시각/물리 효과 없이 가드만 활성 (Landing 자동 Deploy 비활성, 중복 Deploy 차단).
    /// reserve 전개는 ApplyContingencyHardware → 옵션 C 가 처리.
    /// </summary>
    public void MarkParaDeployedSuppressed()
    {
        Debug.Log("[PlayCharacter] MarkParaDeployedSuppressed — 메인 미전개 의도. isParaDeployed=true (가드용)");
        isParaDeployed = true;
    }

    /// <summary>
    /// 낙하산 펼치기
    /// </summary>
    public void Deploy()
    {
        // D6 — 중복 Deploy 가드 (StateManager Landing 자동 가드 / AltCheck / 손당김 등 중복 호출 방어)
        if (isParaDeployed)
        {
            Debug.Log("[PlayCharacter] Deploy — 이미 전개됨(isParaDeployed=true), 중복 호출 skip");
            return;
        }
        isParaDeployed = true;

        // 낙하산줄 숨기기
        if (pullCordCol != null)
        {
            pullCordCol.OnPlayerEntered -= Deploy;
            pullCordHand.SetActive(false);
            pullCordCol.gameObject.SetActive(false);
        }
        _updateAction -= AltCheck;
        _updateAction += SetVignetteZero;
        EndFreeFallPose();
        
        Debug.Log("<color=yellow>[PlayCharacter]</color>Deploy 실행");
        // 감춘 다리 다시 보이기
        //jumperSet.ShowHideSet(2, true);
        
        // 낙하산 펼치는 소리 재생
        sfx.clip = audioClips[2];
        sfx.loop = false;
        sfx.Play();

        // 낙하산 컨트롤러 활성화
        paraCtrl.ParaDeploy();
        followCloud.SetActive(false);
        
        // 카메라 노이즈를 낙하산 전용으로 변경
        _camera.OnParaNoiseCam();
        
        // 캐릭터 낙하산 펼칠때 흔들리는 애니메이션 실행
        // _fallDistance = GetParachuteFallDistance;
        if (animator == null) animator = FindAnyObjectByType<OVRManager>().GetComponent<Animator>();
        animator.Play("ParaOpen", -1, 0f);
        
        // 낙하산 펼침 애니메이션 실행
        if (jumperParachute == null)
        {
            jumperParachute = FindAnyObjectByType<CustomConstraint>(FindObjectsInactive.Include).gameObject;
        }
        jumperParachute.SetActive(true);
        
        // GetComponent 캐싱 적용
        if (_paraAnimCache == null)
            _paraAnimCache = jumperParachute.GetComponent<Animator>();
        
        _paraAnimCache?.SetTrigger("deploy");
        AnimSequenceCheck.StartTrigger(_paraAnimCache, "deploy", EndDeployParachute);
        
        StartCoroutine(NextSound(3, true));
    }

    [Header("━━━ Reserve Recoil (Sd2) ━━━")]
    [Tooltip("예비낙하산 전개 시 위로 충격 (m/s VelocityChange). 권장 18~22f")]
    [SerializeField] private float reserveShockStrength = 20f;

    [Header("━━━ Reserve Visual (Sd2c-mini v2) ━━━")]
    [Tooltip("예비낙하산 전개 시 비활성화할 메인 낙하산 메쉬 GO (씬 간 참조 → 자동 lookup name 사용)")]
    [SerializeField] private GameObject mainParachuteVisual;
    [SerializeField] private string mainParachuteVisualName = "MC-4";

    [Tooltip("예비낙하산 전개 시 활성화할 예비낙하산 메쉬 GO (씬 간 참조 → 자동 lookup name 사용)")]
    [SerializeField] private GameObject reserveParachuteVisual;
    [SerializeField] private string reserveParachuteVisualName = "Reserve";

    // Sd2-stage — 1차 메인 컷어웨이 visual (메인 메쉬 OFF만, 예비는 2차 대기)
    public void CutawayMainPara()
    {
        if (mainParachuteVisual == null)
            mainParachuteVisual = FindGameObjectByNameInScenes(mainParachuteVisualName);

        if (mainParachuteVisual != null)
        {
            mainParachuteVisual.SetActive(false);
            Debug.Log($"[PlayCharacter] CutawayMainPara — mainParachuteVisual({mainParachuteVisual.name}) 비활성");
        }
        else
        {
            Debug.LogWarning($"[PlayCharacter] CutawayMainPara — mainParachuteVisual('{mainParachuteVisualName}') 미발견 — skip");
        }
    }

    // 산줄꼬임 절차 — 예비낙하산 visual 콜백 (S4a, D14 옵션 D — visual 변화 X)
    public void DeploySubPara()
    {
        Debug.Log("[PlayCharacter] DeploySubPara 호출 — S4a 범위 (mesh swap 은 S4c)");

        // D6 — reserve 전개도 "전개 활성" 으로 마킹 (Landing 자동 Deploy 가드, 중복 Deploy 차단)
        isParaDeployed = true;

        // Sd2c-mini v2 — 메인 메쉬 OFF (idempotent — CutawayMainPara 가 이미 호출되었어도 안전)
        if (mainParachuteVisual == null)
            mainParachuteVisual = FindGameObjectByNameInScenes(mainParachuteVisualName);
        if (reserveParachuteVisual == null)
            reserveParachuteVisual = FindGameObjectByNameInScenes(reserveParachuteVisualName);

        if (mainParachuteVisual != null)
        {
            mainParachuteVisual.SetActive(false);
            Debug.Log($"[PlayCharacter] mainParachuteVisual({mainParachuteVisual.name}) 비활성");
        }
        else
        {
            Debug.LogWarning($"[PlayCharacter] mainParachuteVisual('{mainParachuteVisualName}') 미발견 — 메인 메쉬 비활성화 skip");
        }

        if (reserveParachuteVisual != null)
        {
            reserveParachuteVisual.SetActive(true);
            Debug.Log($"[PlayCharacter] reserveParachuteVisual({reserveParachuteVisual.name}) 활성");
        }
        else
        {
            Debug.LogWarning($"[PlayCharacter] reserveParachuteVisual('{reserveParachuteVisualName}') 미발견 — 예비 메쉬 활성화 skip");
        }

        // Sd2c-mini v3 — 자세 애니 재발화 (일반 낙하산 ParaDeploy:302 패턴 차용)
        //   설계서 D-pre5=C(자세 변경 X) 와는 충돌하나 사용자 결정으로 시각 효과 보강
        if (animator != null)
        {
            animator.Play("ParaOpen", -1, 0f);
            Debug.Log("[PlayCharacter] ParaOpen 재실행 — 예비낙하산 전개 자세 효과");
        }

        // S4c 진입 후 보강 (본 작업 외):
        //   _paraAnimCache?.SetTrigger("subDeploy");  // GQ-5000 Open 애니
    }

    /// <summary>
    /// inactive 포함 — Resources.FindObjectsOfTypeAll 로 씬 인스턴스만 필터.
    /// AresHWPC.FindReservePullTargetInScenes 와 동일 패턴 (씬 간 참조 우회).
    /// </summary>
    private GameObject FindGameObjectByNameInScenes(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;

        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in all)
        {
            var go = t.gameObject;
            if (go.name == targetName
                && go.scene.IsValid()
                && go.hideFlags == HideFlags.None)
            {
                Debug.Log($"[PlayCharacter] {targetName} 자동 lookup: (scene={go.scene.name})");
                return go;
            }
        }
        Debug.LogWarning($"[PlayCharacter] {targetName} 자동 lookup 실패 — 씬에 GO 없거나 hideFlags 비정상");
        return null;
    }

    // 예비낙하산 전개 시 위 충격 — Sd2 D-pre5=C (자세 변경 X, AddForce 만)
    public IEnumerator DeployReserveRecoil()
    {
        if (paraCtrl == null || paraCtrl.rb == null)
        {
            Debug.LogWarning("[PlayCharacter] DeployReserveRecoil — paraCtrl/rb null, skip");
            yield break;
        }
        paraCtrl.rb.AddForce(Vector3.up * reserveShockStrength, ForceMode.VelocityChange);
        Debug.Log($"[PlayCharacter] DeployReserveRecoil — AddForce up*{reserveShockStrength}");
        yield break;
    }

    // 산줄꼬임 진입 콜백 — broken_05 trigger 발화 (S4a, T11, D5='broken'/D6=broken_05)
    public void OnLineTwistEnter()
    {
        if (_paraAnimCache == null && jumperParachute != null)
            _paraAnimCache = jumperParachute.GetComponent<Animator>();

        // D5 확정값(S4a-0 검사): Para_M4_Controller broken trigger + M4_Idle→broken_05 transition
        _paraAnimCache?.SetTrigger("broken");
        Debug.Log("[PlayCharacter] OnLineTwistEnter — broken trigger 발화 (→ broken_05 state)");
    }

    private void EndDeployParachute()
    {
        Debug.Log("<color=yellow>[PlayCharacter]</color>EndDeployParachute 실행");
        //jumperSet.ShowHideSet(1, true);
        //paraCtrl.GetComponent<HardwareGrabSync>().SetInitialPositions();
        if (AresHardwareService.Inst.UseHardware)
        {
            _stateManager.leftFollower.OnGrabBegin();
            _stateManager.rightFollower.OnGrabBegin();
        }

        // Deploy 애니메이션 종료는 HAHO/HALO FreeFall(PullCord)의 절차 완료 트리거.
        // STANDARD Landing 자동 Deploy 가드 등 다른 경로에서 Deploy가 호출된 경우 stale OnProcedureComplete가 다음 절차를 잘못 완료시키는 문제 방지.
        if (_stateManager.IsDeployProcedureCompleter())
        {
            _stateManager.OnSuccess();
            _stateManager.OnProcedureComplete();
        }
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

    private void HeightCheck()
    {
        if (transform.position.y is < 5000 and >= 2000)
        {
            lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f));
        }
        else if (transform.position.y is < 2000 and > 1000)
        {
            volumeCtrl.ApplyPreset("2000");
            lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f));
        }
        if (transform.position.y > _alt) return;
        lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f));
        _updateAction -= HeightCheck;
    }

    private void SetVignetteZero()
    {
        // if (transform.position.y < 1000 && transform.position.y >= 995)
        // {
        //     //lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f), 0.0001f, 0.01f);
        // }
        if (transform.position.y > 500) return;
        volumeCtrl.ApplyPreset("500");
        _updateAction -= SetVignetteZero;
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
        if (groundingControl == null) groundingControl = FindAnyObjectByType<SimpleGroundingControl>();
        groundingControl.SetGroundingEnabled(true);
        isLanding = true;
        
        lightCtrl.SetFog(new Color(0.773f,0.839f,0.896f,0.8f), 0.0001f);
        
        // 낙하산 컨트롤러 초기화
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        paraCtrl.isJumpStart = false;
        paraCtrl.isPara = false;
        
        // 낙하산 비활성화
        jumperParachute.SetActive(false);
        if (reserveParachuteVisual != null) reserveParachuteVisual.SetActive(false);   // Sd2c-mini v3 — 강하완료 시 예비낙하산도 동기 비활성
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
            _updateAction = null;
            ParticipantManager.Inst.SetMonitoringDataPlayerFlag(paraCtrl.isPara, paraCtrl.isSubPara, isLanding);
        }));
        sfx.clip = audioClips[4];
        sfx.loop = false;
        sfx.Play();
        Debug.Log("땅에 도착했습니다.");
    }

    private void OnTree()
    {
        Debug.Log("나무에 충돌했습니다.");
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        jumperParachute.SetActive(false);
        if (reserveParachuteVisual != null) reserveParachuteVisual.SetActive(false);   // Sd2c-mini v3 — 나무 충돌 시 예비낙하산도 동기 비활성

        Vector3 position = transform.position;
        transform.position = position;
    }

    private void OnRock()
    {
        Debug.Log("돌에 충돌했습니다.");
        paraCtrl.rb.useGravity = false;
        paraCtrl.rb.isKinematic = true;
        jumperParachute.SetActive(false);
        if (reserveParachuteVisual != null) reserveParachuteVisual.SetActive(false);   // Sd2c-mini v3 — 돌 충돌 시 예비낙하산도 동기 비활성

        Vector3 position = transform.position;
        transform.position = position;
    }
    
    [Header("FreeFall Transform Control")]
    [SerializeField] private float freeFallPitchTarget = 75f; // 목표 pitch 각도
    [SerializeField] private float pitchRotationSpeed = 30f; // 회전 속도 (도/초)
    private bool isFreeFalling = false;

    public void StartFreeFallPose()
    {
        Debug.Log("[PlayCharacter] FreeFall 자세 시작 - Transform 방식");
        isFreeFalling = true;
        _updateAction += UpdateFreeFallPitch;
    }

    public void EndFreeFallPose()
    {
        Debug.Log("[PlayCharacter] FreeFall 자세 종료");
        isFreeFalling = false;
        _updateAction -= UpdateFreeFallPitch;
        
        // 복원 시작
        StartCoroutine(RestoreToUprightPose());
    }

    private void UpdateFreeFallPitch()
    {
        if (!isFreeFalling) return;

        // 현재 pitch 각도
        float currentPitch = transform.eulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;

        // 목표 각도로 부드럽게 회전
        float targetPitch = freeFallPitchTarget;
        float newPitch = Mathf.MoveTowards(currentPitch, targetPitch, pitchRotationSpeed * Time.deltaTime);     

        // Y축(Yaw)는 유지, Z축(Roll)은 0으로
        transform.rotation = Quaternion.Euler(newPitch, transform.eulerAngles.y, 0f);
    }

  // private IEnumerator RestorePitchWithDeployShock()
  // {
  //     float elapsed = 0f;
  //     float phaseDuration = 0.3f; // 각 단계별 시간
  //
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     // Phase 1: 줄 당김 → 몸이 위로 당겨짐 (0.0~0.3초)
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     float startPitch = transform.eulerAngles.x;
  //     if (startPitch > 180f) startPitch -= 360f;
  //
  //     // 일시적으로 더 앞으로 숙여짐 (줄이 당겨지는 효과)
  //     float overshootPitch = startPitch + 15f;
  //
  //     while (elapsed < phaseDuration)
  //     {
  //         elapsed += Time.deltaTime;
  //         float t = elapsed / phaseDuration;
  //
  //         float currentPitch = Mathf.Lerp(startPitch, overshootPitch, t);
  //         transform.rotation = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);
  //
  //         yield return null;
  //     }
  //
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     // Phase 2: 낙하산 펼침 → 급격히 위로 (0.3~0.8초)
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     elapsed = 0f;
  //     phaseDuration = 0.5f;
  //
  //     // 목표: 약간 뒤로 젖혀짐 (감속 충격)
  //     float overshootBackward = -10f;
  //
  //     while (elapsed < phaseDuration)
  //     {
  //         elapsed += Time.deltaTime;
  //         float t = elapsed / phaseDuration;
  //
  //         // EaseOutElastic: 튕기는 효과
  //         float elasticT = 1f + Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - 0.075f) * (2f * Mathf.PI) /
  // 0.3f);
  //
  //         float currentPitch = Mathf.Lerp(overshootPitch, overshootBackward, elasticT);
  //         transform.rotation = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);
  //
  //         // 충격 효과 (위로 힘)
  //         if (t < 0.1f && paraCtrl.rb != null)
  //         {
  //             paraCtrl.rb.AddForce(Vector3.up * 30f, ForceMode.Force);
  //         }
  //
  //         yield return null;
  //     }
  //
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     // Phase 3: 안정화 → 직립 (0.8~2.0초)
  //     // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  //     elapsed = 0f;
  //     phaseDuration = 1.2f;
  //
  //     while (elapsed < phaseDuration)
  //     {
  //         elapsed += Time.deltaTime;
  //         float t = elapsed / phaseDuration;
  //
  //         // SmoothStep: 부드러운 감속
  //         float smoothT = t * t * (3f - 2f * t);
  //
  //         float currentPitch = Mathf.Lerp(overshootBackward, 0f, smoothT);
  //         transform.rotation = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);
  //
  //         yield return null;
  //     }
  //
  //     // 최종 정렬
  //     transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
  //
  //     Debug.Log("[PlayCharacter] 낙하산 전개 복원 완료 - 3단계 시뮬레이션");
  // }
  
  [SerializeField] private float deployRestoreDuration = 1.2f;  // 복원 시간 (초)
  [SerializeField] private float deployShockStrength = 20f;     // 충격 강도 (위로 힘)
  
  /// <summary>
  /// 직립 자세로 복원 (간단 버전)
  /// 핵심: 단순 Slerp만 사용, 복잡한 Phase 없음
  /// </summary>
  private IEnumerator RestoreToUprightPose()
  {
      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      // 1. 시작 상태 저장
      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      Quaternion startRotation = transform.rotation;

      // 현재 Yaw 보존 (방향은 유지)
      float currentYaw = transform.eulerAngles.y;

      // 목표: 직립 자세 (Pitch = 0)
      Quaternion targetRotation = Quaternion.Euler(0f, currentYaw, 0f);
      
      // 위로 순간 힘 (감속 충격 시뮬레이션)
      paraCtrl.rb.AddForce(Vector3.up * deployShockStrength, ForceMode.VelocityChange);
      Debug.Log($"[PlayCharacter] 낙하산 전개 충격 적용: {deployShockStrength}N");

      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      // 3. 부드러운 복원 (간단 버전)
      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      float elapsed = 0f;

      while (elapsed < deployRestoreDuration)
      {
          elapsed += Time.deltaTime;
          float t = elapsed / deployRestoreDuration;

          // Smoothstep 보간 (자연스러운 감속)
          float smoothT = t * t * (3f - 2f * t);

          // Quaternion Slerp (안정적인 회전)
          transform.rotation = Quaternion.Slerp(startRotation, targetRotation, smoothT);

          yield return null;
      }

      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      // 4. 최종 정렬
      // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
      transform.rotation = targetRotation;

      Debug.Log("[PlayCharacter] 낙하산 전개 자세 복원 완료");
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
