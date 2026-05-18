using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoadManager : MonoBehaviour
{
    public static SceneLoadManager Inst { get; private set; }
    
    [SerializeField] private string lobbySceneName;
    [SerializeField] private string mainSceneName;
    [SerializeField] private Camera disCam;
    [SerializeField] private GameObject camConst;
    [SerializeField] private RenderTexture _texture;
    
    // 현재 활성화된 씬
    public Scene currentScene;

    [SerializeField] private Transform oriTransform;
    [SerializeField] private List<PositionSetter> psList;

    private FadeController fadeCtrl;

    private void Awake()
    {
        if (Inst == null)
        {
            Inst = this;
        }
    }

    private void Start()
    {
        Scene loadedScene = SceneManager.GetSceneByName(lobbySceneName);
        currentScene = loadedScene;
        
        Debug.Log("현재 설정된 씬 : " + currentScene.name);
        
        fadeCtrl = FindAnyObjectByType<FadeController>(FindObjectsInactive.Include);
        
        AttachPointInScene();
    }

    /// <summary>
    /// 현재 로드되어 있는 컨텐츠 씬을 언로드함
    /// </summary>
    private void UnLoadCurrentScene()
    {
        disCam.targetTexture = null;
        camConst.SetActive(false);
        ReturnPointObject();
        
        // 현재 켜져있는 씬을 언로드 한다.
        if (currentScene.isLoaded)
        {
            SceneManager.UnloadSceneAsync(currentScene);
        }
    }

    /// <summary>
    /// 로비 씬 불러오기
    /// </summary>
    public void LoadLobbyScene()
    {
        UnLoadCurrentScene();
        AsyncOperation op = SceneManager.LoadSceneAsync(lobbySceneName, LoadSceneMode.Additive);
        
        // 로드 완료 후 currentScene 갱신 및 ActiveScene 설정
        if (op != null)
        {
            op.completed += _ =>
            {
                Scene loadedScene = SceneManager.GetSceneByName(lobbySceneName);
                if (loadedScene.IsValid())
                {
                    currentScene = loadedScene;
                }
                else
                {
                    Debug.LogError($"[SceneLoadManager] 로비 씬({lobbySceneName})을 찾을 수 없습니다.");
                }

                AttachPointInScene();
                camConst.SetActive(true);
                disCam.targetTexture = _texture;
            };
        }
    }

    /// <summary>
    /// 메인 씬 불러오기
    /// </summary>
    public void LoadMainScene(Action completeEvent)
    {
        Debug.Log("LoadMainScene 실행");
        UnLoadCurrentScene();
        AsyncOperation op = SceneManager.LoadSceneAsync(mainSceneName, LoadSceneMode.Additive);
        
        // 로드 완료 후 currentScene 갱신 및 ActiveScene 설정
        if (op != null)
        {
            op.completed += _ =>
            {
                Scene loadedScene = SceneManager.GetSceneByName(mainSceneName);
                if (loadedScene.IsValid())
                {
                    currentScene = loadedScene;
                }
                else
                {
                    Debug.LogError($"[SceneLoadManager] 로비 씬({mainSceneName})을 찾을 수 없습니다.");
                }
                
                AttachPointInScene();
                completeEvent?.Invoke();
                camConst.SetActive(true);
                disCam.targetTexture = _texture;
            };
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void AttachPointInScene()
    {
        var roots = currentScene.GetRootGameObjects();

        foreach (var posSetter in psList)
        {
            if (posSetter.HasKey(currentScene.name))
            {
                posSetter.Init(currentScene.name, roots);
            }
        }
    }

    private void ReturnPointObject()
    {
        foreach (var posSetter in psList)
        {
            posSetter.transform.parent = oriTransform;
        }
    }
}
