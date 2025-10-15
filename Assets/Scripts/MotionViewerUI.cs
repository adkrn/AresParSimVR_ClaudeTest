using System.Collections.Generic;
using TMPro;   // TextMeshPro 패키지 사용 (Dropdown/Text 등)
using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEditor.PackageManager;
using UnityEditor;
using System.Data;
using Newtonsoft.Json;
using Unity.VisualScripting;

public class MotionViewerUI : MonoBehaviour
{
    [Header("UI 컴포넌트 연결")]
    public TMP_Dropdown roleDropdown;        // 0: VR, 1: Viewer
    public TMP_Dropdown networkModeDropdown; // 0: Public, 1: Closed (폐쇄망) → 아직 실제 로직에 사용 안 함
    public TMP_Dropdown roomDropdown;        // 서버에서 받은 방 목록. 첫 옵션은 “-- Select a Room --”
    public TMP_InputField manualRoomInput;   // 직접 입력할 수 있는 방 ID InputField
    public TMP_Dropdown clientDropdown;

    public Button connectButton;  
    public Button joinRoomButton;             // Viewer 모드일 때만 표시

    public Button getClientListButton;
    public Button ShowStartButton;

    
    private Action _updateAction;
    
    private void Start()
    {
        RoleDropDownInit();
        NetworkModeDownInit();
        roomDropdownInit();
        // 초기 UI 상태
        roleDropdown.onValueChanged.AddListener(OnRoleChanged);
        connectButton.onClick.AddListener(OnConnectClicked);
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
        getClientListButton.onClick.AddListener(OnGetClientListClicked);
        ShowStartButton.onClick.AddListener(OnShowStart);
        clientDropdown.gameObject.SetActive(true);

        // 처음엔 Viewer 전용 UI 숨김
        roomDropdown.gameObject.SetActive(false);
        manualRoomInput.gameObject.SetActive(false);
        joinRoomButton.gameObject.SetActive(false);

        Debug.Log("<color=green>[MotionViewerUI]</color> roomDropdown에 할당된 UI 객체 이름: "+ roomDropdown.name);
        // WebSocketManager에서 방 목록 응답받으면 Dropdown을 채움
        WebSocketManager.Inst.OnRoomListReceived += rooms =>
        {
            PopulateRoomDropdown(rooms);
        };

        WebSocketManager.Inst.OnClientListReceived += clients =>
        {
            ClientDropdown(clients);
        };
    }

    private void Update()
    {
        _updateAction?.Invoke();
    }

    private void RoleDropDownInit()
    {
        roleDropdown.options = new List<TMP_Dropdown.OptionData>()
        {
            new TMP_Dropdown.OptionData("vr"),
            new TMP_Dropdown.OptionData("viewer")
        };
    }
    private void NetworkModeDownInit()
    {
        networkModeDropdown.options = new List<TMP_Dropdown.OptionData>()
        {
            new TMP_Dropdown.OptionData("public"),
            new TMP_Dropdown.OptionData("closed")
        };
    }

    private void roomDropdownInit()
    {
        roomDropdown.ClearOptions();
    }
    
    private void OnRoleChanged(int idx)
    {
        var roleText = roleDropdown.options[idx].text;
        WebSocketManager.Inst.role = roleText;
        bool isViewer = roleText == "viewer";
        roomDropdown.gameObject.SetActive(isViewer);
        manualRoomInput.gameObject.SetActive(isViewer);
        joinRoomButton.gameObject.SetActive(isViewer);

        if (isViewer)
        {
            Debug.Log("<color=green>[MotionViewerUI]</color>Viewer 모드이므로 방정보 요청");
            WebSocketManager.Inst.RequestRoomList();
        }
    }

    private void OnConnectClicked()
    {
        Debug.Log("<color=green>[MotionViewerUI]</color> OnConnectClicked 실행");
        // 1) Role 설정
        WebSocketManager.Inst.role = roleDropdown.options[roleDropdown.value].text;
        WebSocketManager.Inst.isClosedNetwork = networkModeDropdown.value == 1; // 0: Public, 1: Closed
        // 2) Viewer 모드인 경우, roomDropdown/수동 입력 중 아무 것도 선택되지 않으면 roomId를 빈 문자열로 세팅
        if (WebSocketManager.Inst.role == "viewer")
        {
            if (roomDropdown.options.Count > 0)
            {
                WebSocketManager.Inst.roomId = roomDropdown.options[roomDropdown.value].text;
            }
        }
        else
        {
            WebSocketManager.Inst.roomId = "";
        }

        // 3) WebRTCManager 시작 플래그: Update()에서 webSocketManager.Init() 호출
        Debug.Log("<color=green><color=green>[MotionViewerUI]</color></color> 로그인 성공");
        WebRTCManager.Inst.Init();
        // WebRTCManager.Inst.isRtc = true;
    }

    private void OnJoinRoomClicked()
    {
        // Viewer 모드일 때만 눌러야 함
        string selectedRoom = manualRoomInput.text.Trim();
        if (string.IsNullOrEmpty(selectedRoom))
        {
            // Dropdown에서 선택
            if (roomDropdown.options.Count > 0)
            {
                selectedRoom = roomDropdown.options[roomDropdown.value].text;
            }
        }

        if (string.IsNullOrEmpty(selectedRoom))
        {
            Debug.LogWarning("<color=green>[MotionViewerUI]</color> 방을 선택하거나 입력하세요.");
            return;
        }

        // 1) WebSocketManager에 Viewer 정보 세팅
        WebSocketManager.Inst.role   = "viewer";
        WebSocketManager.Inst.roomId = selectedRoom;

        // 2) register(JoinRoom) → 서버에 Viewer로 방 등록 시도
        WebSocketManager.Inst.JoinRoom(selectedRoom);

        // // 3) 로컬 스트림(화면 공유) 셋업
        WebRTCManager.Inst.SetupLocalStreamWith3DAndCanvas();
    }

    private void OnGetClientListClicked()
    {
        var getClient = new Dictionary<string, object>()
        {
            ["type"] = "client-list",
        };
        string json = JsonConvert.SerializeObject(getClient);

        WebSocketManager.Inst.ws.Send(json);
    }

    private void OnShowStart()
    {
        string selectClient = clientDropdown.options[clientDropdown.value].text;
        WebRTCManager.Inst.StartCallAsVR(selectClient);
    }


    private void PopulateRoomDropdown(List<string> rooms)
    {
        Action actionOnce = null;
        actionOnce += () =>
        {
            roomDropdown.ClearOptions();
            List<string> opts = new List<string>();
            opts.Add("-- Select a Room --");
            opts.AddRange(rooms);
            roomDropdown.AddOptions(opts);
            Debug.Log("<color=green>[MotionViewerUI]</color> Dropdown에 방 목록 채움: " + string.Join(", ", rooms));
            _updateAction -= actionOnce;
        };
        _updateAction += actionOnce;
    }

    private void ClientDropdown(List<string> clients)
    {
        //Action actionOnce = null;
        //actionOnce += () =>
        //{
        //    clientDropdown.ClearOptions();
        //    List<string> opts = new List<string>();
        //    opts.AddRange(clients);
        //    Debug.Log("<color=green>[MotionViewerUI]</color> Dropdown에 client 목록 채움: " + string.Join(", ", clients));
        //    _updateAction -= actionOnce;
        //};

        clientDropdown.ClearOptions();
        clientDropdown.options.Add(new TMP_Dropdown.OptionData("-- Select a Client --"));
        foreach (var item in clients)
        {
            if (item.Contains("000000") || item.Contains(WebSocketManager.Inst.clientId))
                continue;

            clientDropdown.options.Add(new TMP_Dropdown.OptionData(item));
        }

        //_updateAction += actionOnce;
    }
}