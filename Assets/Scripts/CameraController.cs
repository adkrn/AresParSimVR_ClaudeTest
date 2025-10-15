using System;
using System.Collections;
using UnityEngine;

public enum CamPos
{
    InAirPlane,
    Exit,
    Character
}

public class CameraController : MonoBehaviour
{
    [Header("카메라 위치 포인트")]
    [SerializeField] Transform[] camPoints;   // 씬에 배치된 뷰 포인트
    [SerializeField] Transform rigRoot;       // XR Rig 또는 JumperSet 부모
    [SerializeField] private ConditionalGageUI _gageUI;
    public FadeController fadeCtrl;
    
    [SerializeField] private Vector3 mainCamPosition;
    [SerializeField] private float camPosNoise;
    
    int _index;
    private Action _updateAction;
    private StateManager_New _stateManager;
    
    void Start()
    {
        _stateManager = FindAnyObjectByType<StateManager_New>();
        Init();
    }

    private void Init()
    {
        camPosNoise = 0;
        _updateAction = CamNoiseAction;
        Debug.Log("[CameraController] 카메라 노이즈 초기화");
        
        fadeCtrl.Init(FadeDir.In, null);
        Debug.Log("[CameraController] 카메라 초기 위치로 설정");
    }

    [ProcedureHandler("oxygen_check", ExecMode.Always)]
    private void MoveInPlane()
    {
        Debug.Log("[oxygen_check] 비행기 안으로 이동");
        StateManager.InstructionUIShown += _gageUI.InitUI;
        MoveToPoint(CamPos.InAirPlane);
        fadeCtrl.Init(FadeDir.In, null);
    }

    private void Update()
    {
        _updateAction?.Invoke();
    }

    [ProcedureHandler("door_check")]
    public void DoorCheck()
    {
        Debug.Log("[door_check] DoorCheck");
        _gageUI.ActiveTrigger(3.0f);
        _gageUI.onComplete = _stateManager.OnProcedureComplete;
    }

    /// <summary>
    /// 점프자리로 이동
    /// </summary>
    [ProcedureHandler("door_approach",ExecMode.Always)]
    public void MoveJumpArea()
    {
        Debug.Log("[door_approach] 뛰어내릴곳으로 이동");
        MoveToPoint(CamPos.Exit);
    }

    /// <summary>
    /// 낙하 상태일때 카메라 노이즈 실행.
    /// </summary>
    [ProcedureHandler("jump", ExecMode.Always)]
    public void OnJumpNoiseCam()
    {
        Debug.Log("점프시작 카메라 캐릭터 위치로 이동한다.");
        MoveToPoint(CamPos.Character);
        _updateAction = JumpNoiseAction;
    }
    
    /// <summary>
    /// 낙하산을 펼쳤을때 카메라 노이즈 실행
    /// </summary>
    [ProcedureHandler("parachute_deploy", ExecMode.Always)]
    public void OnParaNoiseCam()
    {
        _updateAction = ParachuteNoiseAction;
    }

    public void OnGround()
    {
        _updateAction -= ParachuteNoiseAction;
    }

    /// <summary>
    /// 일반 캠 노이즈
    /// </summary>
    private void CamNoiseAction()
    {
        //Debug.Log("일반캠노이즈");
        camPosNoise = Mathf.PerlinNoise(-1, Time.time * 7.0f);
        transform.localPosition = mainCamPosition + new Vector3(0, camPosNoise / 8.0f, 0);
    }
    
    private void JumpNoiseAction()
    {
        //Debug.Log("점프캠노이즈");
        camPosNoise = Mathf.PerlinNoise(-1, Time.time * 10.0f);
        transform.localPosition = mainCamPosition + new Vector3(0, camPosNoise / 8.0f, 0);
        transform.localPosition = mainCamPosition + new Vector3(camPosNoise, camPosNoise, camPosNoise);
        transform.localEulerAngles = new Vector3(camPosNoise * 1.3f, camPosNoise * 0.3f, camPosNoise * 0.6f);
    }
    
    private void ParachuteNoiseAction()
    {
        //Debug.Log("낙하산캠노이즈");
        camPosNoise = Mathf.PerlinNoise(-1, Time.time / 2.0f);
        transform.localPosition = mainCamPosition + new Vector3(camPosNoise, camPosNoise, camPosNoise);
        transform.localEulerAngles = new Vector3(camPosNoise * 1, camPosNoise * 2, camPosNoise * 1);
    }

    public void MoveToPoint(CamPos next)
    {
        _index = Mathf.Clamp((int)next, 0, camPoints.Length - 1);
        rigRoot.SetParent(camPoints[_index]);
        rigRoot.localPosition = Vector3.zero;
        rigRoot.localRotation = Quaternion.identity;
    }
}