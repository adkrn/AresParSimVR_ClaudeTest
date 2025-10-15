using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using System.Collections;

public class StartManager : MonoBehaviour
{
    [Header("페이드 설정")]
    [SerializeField] private FadeController fadeCtrl;  // FadeController 참조
    [SerializeField] private float fadeDuration = 1.0f; // (FadeController에도 동일 설정)

    [Header("시작 메뉴 설정")]
    [SerializeField] private GameObject startMenu;
    [SerializeField] private float onStartMenuTime = 6.0f;
    [SerializeField] private ScenarioInfoUI infoUI;

    [Header("카메라")] 
    [SerializeField] private GameObject ovrCamera;
    [SerializeField] private Transform nextCameraParent;
    [SerializeField] private Transform oriCameraParent;

    [SerializeField] private string mainScene;

    private void Start()
    {
        StateManager.OnInit += Init;
        fadeCtrl.Init(FadeDir.In, SendThisState);
    }

    private void Init()
    {
        // 1. 카메라 위치 원래대로 초기화
        ovrCamera.transform.parent = oriCameraParent;
        ovrCamera.transform.localPosition = new Vector3(0, 0, 0);
        ovrCamera.transform.localEulerAngles = new Vector3(0, 0, 0);
        
        
    }

    private void SendThisState()
    {
        
    }

    private IEnumerator OnStartMenuCoroutine()
    {
        yield return new WaitForSeconds(onStartMenuTime);
        startMenu.SetActive(true);
    }
    
    [ProcedureHandler("MissionBrief")]
    public void StartFadeOut()
    {
        fadeCtrl.Init(FadeDir.Out, OpenMainScene);
    }

    [ProcedureHandler("MissionBrief")]
    private void ShowBriefing()
    {
        Debug.Log("Briefing");
        StateManager.InstructionUIShown += infoUI.InitUI;
        
        // 비디오 녹화
        //FFMPEGRecorder.Inst.StartRecording();
    }

    /// <summary>
    /// 일반 절차에서는 UI 확인 후 StartFadeOut에서 다음 절차로 넘어갈때 실행
    /// 스킵할떄는 바로 실행.
    /// </summary>
    [ProcedureHandler("MissionBrief", ExecMode.SkipOnly)]
    private void OpenMainScene()
    {
        var stateManager = FindAnyObjectByType<StateManager>();
        stateManager.OnSuccess();
        stateManager.OnProcedureComplete();
        
        SceneManager.LoadScene(mainScene);
    }
}
