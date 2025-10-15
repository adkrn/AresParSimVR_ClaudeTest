using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]                 // ← 인스펙터에 노출될 래퍼
public class GameObjectGroup
{
    public List<GameObject> objects = new();   // 내부 리스트
}

public class JumperSet : MonoBehaviour
{
    [SerializeField] List<GameObject> headsetObjects = new();
    [SerializeField] public List<GameObjectGroup> objectsSet;
    [SerializeField] private int firstHideSetIndex = 0;
    [SerializeField] private GameObject gpsHidePart;
    [SerializeField] private Transform gpsRotPart;
    [SerializeField] private Transform initParent;

    private void Start()
    {
        StateManager.OnInit += () =>
        {
            Init();
        };
        
        Init();
    }

    private void Init(bool isPlayer = true)
    {
        transform.parent = initParent;
        transform.localPosition = new Vector3(73.1f, -25.39f, -755f);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        Debug.Log("[jumperSet] 점퍼셋 위치 초기화");
        
        ShowHideSet(index: firstHideSetIndex, isShow: false);
        CloseGps(isOpen: false);
        Debug.Log("[jumperSet] 착용할 헬멧 초기화");
    }

    /// <summary>
    /// 지정된 셋을 보이거나 감추도록 처리
    /// </summary>
    /// <param name="index"></param>
    /// <param name="isShow"></param>
    public void ShowHideSet(int index, bool isShow)
    {
        return;
        objectsSet[index].objects.ForEach(x => x.SetActive(isShow));
    }

    public void HelmetShowHide(bool isShow)
    {
        objectsSet[0].objects.ForEach(x => x.SetActive(isShow));
    }

    /// <summary>
    /// 배쪽의 GPS 장치 열고 닫기
    /// </summary>
    /// <param name="isOpen"></param>
    private void CloseGps(bool isOpen)
    {
        gpsHidePart.SetActive(isOpen);
        var xAngle = isOpen ? 0 : 270;
        gpsRotPart.localEulerAngles = new Vector3(xAngle, 0, 0);
    }
}