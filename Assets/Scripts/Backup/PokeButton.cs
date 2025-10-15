using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// XR Poke 버튼(Hover·Poke 가능).
/// 시작 시 / ShowMenu 시점에 카메라 전방에 자동 배치한다.
/// </summary>
public class PokeButton : MonoBehaviour
{
    /* ---------- 인스펙터 설정 ---------- */

    [Header("UI References")]
    [Tooltip("월드 스페이스 캔버스(힌트 텍스트 포함)")]
    [SerializeField] GameObject hintCanvas;

    [SerializeField] TMP_Text hintLabel;
    [SerializeField] TMP_Text btnLabel;
    [SerializeField] PointableUnityEventWrapper buttonEventWrapper;

    [Header("Auto-Place Settings")]
    [Tooltip("카메라로부터의 전방 거리(m)")]
    [SerializeField] float appearDistance = 1.2f;

    [Tooltip("카메라 기준 세로 오프셋(+ 위, – 아래, m)")]
    [SerializeField] float verticalOffset = -0.15f;
    
    private CameraFrontPlacer _cameraFrontPlacer;

    /* ---------- 내부 ---------- */

    private void Start()
    {
        _cameraFrontPlacer = GetComponent<CameraFrontPlacer>();

        // 처음 Poke버튼 표시는 5초 후
        //StartCoroutine(ShowMenu());

        //StateManager_TimeLine.InstructionUIShown += SetMenuActive;
    }

    IEnumerator ShowMenu()
    {
        yield return new WaitForSeconds(5f);

        SetMenuActive(true, "Enter the Aircraft", "Enter");
    }

    public void DelayShowMenu(float delay, string newMessage = null, string btnText = "OK")
    {
        StartCoroutine(DelayActiveMenu(delay, newMessage, btnText));
    }

    IEnumerator DelayActiveMenu(float delay, string newMessage, string btnText)
    {
        yield return new WaitForSeconds(delay);
        
        SetMenuActive(true, newMessage, btnText);
    }
    
    public void SetMenuActive(bool active, string newMessage = null, string btnText = "OK")
    {
        _cameraFrontPlacer.Place();
        Debug.Log($"[PokeButton] SetMenuActive : {active}, {newMessage}");

        if (newMessage != null)  hintLabel.text = newMessage;
        if (btnText     != null) btnLabel.text  = btnText;

        hintCanvas.SetActive(active);
    }
    
    public void SetMenuActive(Procedure proc)
    {
        _cameraFrontPlacer?.Place();
    }
}