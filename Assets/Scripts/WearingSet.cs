using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 하나의 착용 세트를 구성하는 데이터
/// </summary>
[Serializable]
public class WearingGroup
{
    public Item itemType;                  // 예: Helmet, Compass, Hook, Mask …

    [Header("착용할 오브젝트")]
    public GameObject wearingObj;          // 씬 시작 시 비활성 → ShowStandby에서 활성

    [Header("착용 트리거 콜라이더")]
    public Collider   triggerCol;          // isTrigger=true 필요 (자동 설정)

    [Header("착용 시 활성화할 오브젝트")]
    public List<GameObject> equippedObjs;  // 헬멧 안쪽 오브젝트 등 여러 개도 가능
}

/// <summary>
/// 씬별 착용 세트를 통합 관리.
/// 기존 JumperSet 기능 보강 및 클래스 이름 변경
/// </summary>
public class WearingSet : MonoBehaviour
{
    [SerializeField] private List<WearingGroup> groups;
    
    void Awake()
    {
        // 모든 트리거에 TriggerListener 부착 & 이벤트 연결
        foreach (var g in groups)
            AttachTrigger(g);
    }
    
    /// <summary>착용 전, 손에 들린 오브젝트와 트리거 표시</summary>
    public void ShowStandby(Item itemType)
    {
        var g = FindGroup(itemType);
        if (g == null) return;

        if (g.wearingObj)  g.wearingObj.SetActive(true);
        if (g.triggerCol)  g.triggerCol.gameObject.SetActive(true);
    }
    
    /// <summary>외부에서 강제 착용(타임아웃 등)</summary>
    public void ForceEquip(Item itemType) => Equip(itemType);
    
    void AttachTrigger(WearingGroup g)
    {
        if (g.triggerCol == null) return;

        var tl = g.triggerCol.GetComponent<TriggerListener>() ??
                 g.triggerCol.gameObject.AddComponent<TriggerListener>();

        tl.OnPlayerEntered += () => Equip(g.itemType);
        tl.OnPlayerEntered += () =>
        {
            Debug.Log($"{g.itemType.ToString()} OnPlayerEntered로 UI에 성공신호를 보낸다.");
            UIManager.Inst.OnSuccessAction();
        };
    }

    void Equip(Item itemType)
    {
        var g = FindGroup(itemType);
        if (g == null) return;

        // 1) 착용 오브젝트 활성화
        foreach (var obj in g.equippedObjs)
            if (obj) obj.SetActive(true);

        // 2) 대기 오브젝트·콜라이더 비활성화
        if (g.wearingObj)  g.wearingObj.SetActive(false);
        if (g.triggerCol)  g.triggerCol.enabled = false;
        
        Debug.Log($"[WearingSet] {itemType} 착용 완료");
    }

    WearingGroup FindGroup(Item itemType)
    {
        foreach (var g in groups)
            if (g.itemType == itemType) return g;

        Debug.LogWarning($"[WearingSet] {itemType} 그룹을 찾지 못함");
        return null;
    }
}
