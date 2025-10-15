using System;
using System.Collections;
using UnityEngine;

public enum AppType
{
    ControlManager,
    Simulator,
    ViewManager
}

public class MainManager : MonoBehaviour
{
    public AppType appType = AppType.ControlManager;
    
    public static MainManager Inst { get; private set; }


    /// <summary>
    /// Awake
    /// </summary>
    private void Awake()
    {
        if (Inst == null)
        {
            // 씬 전환 시 파괴되지 않도록 설정
            Inst = this;
            DontDestroyOnLoad(gameObject);
            //Init();
        }
        else
        {
            // 이미 인스턴스가 존재하면 중복된 오브젝트는 파괴
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    private void Init()
    {
        switch (appType)
        {
            case AppType.ControlManager:
                // 버튼을 누르면 네트워크 연결시도 UI
                break;
            case AppType.Simulator:
                // 화면을 약 2-3초 바라보면 네트워크 연결시도 UI
                break;
            case AppType.ViewManager:
                // 버튼을 누르면 네트워크 연결시도 UI
                break;
        }
    }

    /// <summary>
    /// 네트워크 연결 시도
    /// </summary>
    public void OnNetInit()
    {
        // 네트워크 초기화
        // NetworkManager.Inst.Init();
    }
    
    /// <summary>
    /// 로그인 UI 표시
    /// </summary>
    public void ShowLoginUI()
    {
        Debug.Log("로그인 UI 표시 또는 자동 로그인");
    }

    /// <summary>
    /// 지정된 팝업 타입에 따라 팝업을 표시하는 메서드
    /// 지정된 시간이 지나면 자동으로 꺼진다.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="title"></param>
    /// <param name="message"></param>
    /// <param name="popoutTime"></param>
    public void ShowPopup(PopupType type, string title, string message, float popoutTime = 2.0f)
    {
        return; //현재는 테스트...
        // popupType에 따라 어떤 팝업을 보여줄지 결정한다.
        GameObject popup = LoadPrefab(type.ToString());
        // 로드된 팝업프리팹에 타이틀과 메세지를 입력한다.
        popup.GetComponent<PopupInfo>().Init(title, message);
        
        // autoHideTime이 0일 경우 사용자가 꺼야만 팝업이 꺼진다.
        if (popoutTime > 0) StartCoroutine(Popout(popup, popoutTime));
    }

    /// <summary>
    /// 지정된 오브젝트를 Destroy 처리한다.
    /// </summary>
    /// <param name="popout"></param>
    private IEnumerator Popout(GameObject popout,float t, Action act = null)
    {
        yield return new WaitForSeconds(t);
        
        act(); // 지정된 오브젝트가 제거되면서 실행되어야 될 내용을 여기서 처리한다.
        Destroy(popout);
    }

    /// <summary>
    /// 리소스에서 프리팹을 호출하여 생성한다.
    /// </summary>
    /// <param name="prefabName"></param>
    /// <returns></returns>
    private GameObject LoadPrefab(string prefabName)
    {
        GameObject go = Resources.Load(prefabName) as GameObject;
        return go;
    }
}
