using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using WebSocketSharp;

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

    public AudioSource sfx;
    [SerializeField] private AudioClip[] audioClips;
    public VolumeCtrl volumeCtrl;
    public LightCtrl lightCtrl;
    public GameObject pullCordHand;
    public TriggerListener pullCordCol;

    void OnEnable()
    {
        // 씬 로드 이벤트 구독
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[WearingSet] 씬 로드됨: {scene.name}");
        var pChar = FindAnyObjectByType<PlayCharacter>();
        if (pChar != null)
        {
            var group = FindGroup(Item.Hook);
            group.triggerCol = pChar.hookTrigger;
            group.equippedObjs.Add(pChar.hookObj);
        }
        
        foreach (var g in groups)
        {
            if (g == null) continue;
            AttachTrigger(g);
        }
        //StartCoroutine(DelayedRefresh());
    }

    System.Collections.IEnumerator DelayedRefresh()
    {
        yield return null;
        RefreshAllGroups();
    }

    /// <summary>모든 그룹의 오브젝트를 재검색</summary>
    void RefreshAllGroups()
    {
        Scene targetScene = SceneLoadManager.Inst.currentScene;

        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            Debug.LogError("[WearingSet] currentScene이 유효하지 않습니다.");
            return;
        }

        GameObject[] rootObjects = targetScene.GetRootGameObjects();

        foreach (var g in groups)
        {
            if (g == null) continue;

            if (g.triggerCol == null)
                g.triggerCol = FindTriggerCollider(g.itemType, rootObjects);

            if (g.wearingObj == null)
                g.wearingObj = FindWearingObject(g.itemType, rootObjects);

            if (g.equippedObjs == null || g.equippedObjs.Count == 0)
                g.equippedObjs = FindEquippedObjects(g.itemType, rootObjects);
        }

        foreach (var g in groups)
        {
            if (g == null) continue;
            AttachTrigger(g);
        }

        Debug.Log("[WearingSet] 오브젝트 재검색 완료");
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
        // 여전히 null이면 경고 후 리턴
        if (g.triggerCol == null)
        {
            Debug.LogWarning($"[WearingSet] {g.itemType} 트리거를 찾을 수 없습니다. " +
                            $"'{g.itemType}_Trigger' 이름의 GameObject를 생성하거나 Inspector에서 수동 할당하세요.");
            return;
        }

        var tl = g.triggerCol.GetComponent<TriggerListener>() ??
                 g.triggerCol.gameObject.AddComponent<TriggerListener>();

        tl.OnPlayerEntered += () => Equip(g.itemType);
        tl.OnPlayerEntered += () =>
        {
            Debug.Log($"{g.itemType.ToString()} OnPlayerEntered로 UI에 성공신호를 보낸다.");
            UIManager.Inst.OnSuccessAction();
        };
    }

    /// <summary>씬의 루트 오브젝트들에서 재귀적으로 GameObject 검색</summary>
    GameObject FindInHierarchy(GameObject[] rootObjects, System.Func<GameObject, bool> predicate)
    {
        foreach (var root in rootObjects)
        {
            if (predicate(root))
                return root;

            var result = FindInChildren(root.transform, predicate);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>자식 오브젝트들에서 재귀적으로 검색</summary>
    GameObject FindInChildren(Transform parent, System.Func<GameObject, bool> predicate)
    {
        foreach (Transform child in parent)
        {
            if (predicate(child.gameObject))
                return child.gameObject;

            var result = FindInChildren(child, predicate);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>씬의 루트 오브젝트들에서 재귀적으로 여러 GameObject 검색</summary>
    List<GameObject> FindAllInHierarchy(GameObject[] rootObjects, System.Func<GameObject, bool> predicate)
    {
        List<GameObject> results = new List<GameObject>();
        foreach (var root in rootObjects)
        {
            if (predicate(root))
                results.Add(root);

            FindAllInChildren(root.transform, predicate, results);
        }
        return results;
    }

    /// <summary>자식 오브젝트들에서 재귀적으로 모두 검색</summary>
    void FindAllInChildren(Transform parent, System.Func<GameObject, bool> predicate, List<GameObject> results)
    {
        foreach (Transform child in parent)
        {
            if (predicate(child.gameObject))
                results.Add(child.gameObject);

            FindAllInChildren(child, predicate, results);
        }
    }

    /// <summary>
    /// 네이밍 컨벤션을 기반으로 트리거 콜라이더를 자동 검색
    /// 패턴: "{ItemType}_Trigger" 또는 자식 오브젝트에서 "{ItemType}"과 "Trigger" 포함
    /// </summary>
    Collider FindTriggerCollider(Item itemType, GameObject[] rootObjects)
    {
        string triggerName = $"{itemType}_Trigger";
        string itemName = itemType.ToString();

        // 패턴 1: 정확한 이름으로 검색
        GameObject triggerObj = FindInHierarchy(rootObjects, obj => obj.name == triggerName);
        if (triggerObj != null)
        {
            Collider col = triggerObj.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
                Debug.Log($"[WearingSet] {itemType} 트리거 자동 발견: {triggerName}");
                return col;
            }
        }

        // 패턴 2: WearingSet의 자식 오브젝트에서 검색
        foreach (Transform child in transform)
        {
            if (child.name.Contains(itemName) && child.name.Contains("Trigger"))
            {
                Collider col = child.GetComponent<Collider>();
                if (col != null)
                {
                    col.isTrigger = true;
                    Debug.Log($"[WearingSet] {itemType} 트리거 자동 발견 (자식): {child.name}");
                    return col;
                }
            }
        }

        // 패턴 3: 이름 패턴 매칭 (대소문자 무시)
        GameObject found = FindInHierarchy(rootObjects, obj =>
        {
            string name = obj.name.ToLower();
            string item = itemName.ToLower();
            return name.Contains(item) && name.Contains("trigger");
        });

        if (found != null)
        {
            Collider col = found.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
                Debug.Log($"[WearingSet] {itemType} 트리거 자동 발견 (패턴매칭): {found.name}");
                return col;
            }
        }

        return null;
    }

    /// <summary>
    /// 네이밍 컨벤션을 기반으로 착용 대기 오브젝트를 자동 검색
    /// 패턴: "{ItemType}_Wearing", "{ItemType}_Standby", "{ItemType}_Wait"
    /// </summary>
    GameObject FindWearingObject(Item itemType, GameObject[] rootObjects)
    {
        string itemName = itemType.ToString();

        // 패턴 1: 여러 네이밍 패턴 시도
        string[] patterns = {
            $"{itemType}_Wearing",
            $"{itemType}_Standby",
            $"{itemType}_Wait",
            $"{itemType}Wearing",
            $"{itemType}Standby"
        };

        foreach (string pattern in patterns)
        {
            GameObject obj = FindInHierarchy(rootObjects, o => o.name == pattern);
            if (obj != null)
            {
                Debug.Log($"[WearingSet] {itemType} 대기 오브젝트 자동 발견: {pattern}");
                return obj;
            }
        }

        // 패턴 2: WearingSet의 자식 오브젝트에서 검색
        foreach (Transform child in transform)
        {
            if (child.name.Contains(itemName) &&
                (child.name.Contains("Wearing") || child.name.Contains("Standby") || child.name.Contains("Wait")))
            {
                Debug.Log($"[WearingSet] {itemType} 대기 오브젝트 자동 발견 (자식): {child.name}");
                return child.gameObject;
            }
        }

        // 패턴 3: 이름 패턴 매칭 (대소문자 무시)
        GameObject found = FindInHierarchy(rootObjects, obj =>
        {
            string name = obj.name.ToLower();
            string item = itemName.ToLower();
            return name.Contains(item) &&
                   (name.Contains("wearing") || name.Contains("standby") || name.Contains("wait"));
        });

        if (found != null)
        {
            Debug.Log($"[WearingSet] {itemType} 대기 오브젝트 자동 발견 (패턴매칭): {found.name}");
            return found;
        }

        return null;
    }

    /// <summary>
    /// 네이밍 컨벤션을 기반으로 착용 시 활성화할 오브젝트들을 자동 검색
    /// 패턴: "{ItemType}_Equipped", "{ItemType}_Equipped_1", "{ItemType}_Wear"
    /// </summary>
    List<GameObject> FindEquippedObjects(Item itemType, GameObject[] rootObjects)
    {
        List<GameObject> foundObjects = new List<GameObject>();
        string itemName = itemType.ToString();

        // 패턴 1: 단일 오브젝트 검색
        string[] singlePatterns = {
            $"{itemType}_Equipped",
            $"{itemType}_Wear",
            $"{itemType}Equipped",
            $"{itemType}Wear"
        };

        foreach (string pattern in singlePatterns)
        {
            GameObject obj = FindInHierarchy(rootObjects, o => o.name == pattern);
            if (obj != null && !foundObjects.Contains(obj))
            {
                foundObjects.Add(obj);
                Debug.Log($"[WearingSet] {itemType} 착용 오브젝트 자동 발견: {pattern}");
            }
        }

        // 패턴 2: 번호가 붙은 여러 오브젝트 검색 (예: Helmet_Equipped_1, Helmet_Equipped_2)
        for (int i = 1; i <= 10; i++)
        {
            string[] numberedPatterns = {
                $"{itemType}_Equipped_{i}",
                $"{itemType}_Wear_{i}",
                $"{itemType}Equipped{i}",
                $"{itemType}Wear{i}"
            };

            foreach (string pattern in numberedPatterns)
            {
                GameObject obj = FindInHierarchy(rootObjects, o => o.name == pattern);
                if (obj != null && !foundObjects.Contains(obj))
                {
                    foundObjects.Add(obj);
                    Debug.Log($"[WearingSet] {itemType} 착용 오브젝트 자동 발견: {pattern}");
                }
            }
        }

        // 패턴 3: WearingSet의 자식 오브젝트에서 검색
        foreach (Transform child in transform)
        {
            if (child.name.Contains(itemName) &&
                (child.name.Contains("Equipped") || child.name.Contains("Wear")))
            {
                if (!foundObjects.Contains(child.gameObject))
                {
                    foundObjects.Add(child.gameObject);
                    Debug.Log($"[WearingSet] {itemType} 착용 오브젝트 자동 발견 (자식): {child.name}");
                }
            }
        }

        // 패턴 4: 이름 패턴 매칭 (대소문자 무시)
        if (foundObjects.Count == 0)
        {
            List<GameObject> patternMatches = FindAllInHierarchy(rootObjects, obj =>
            {
                string name = obj.name.ToLower();
                string item = itemName.ToLower();
                return name.Contains(item) &&
                       (name.Contains("equipped") || name.Contains("wear")) &&
                       !name.Contains("wearing") && !name.Contains("standby") && !name.Contains("trigger");
            });

            foreach (var obj in patternMatches)
            {
                if (!foundObjects.Contains(obj))
                {
                    foundObjects.Add(obj);
                    Debug.Log($"[WearingSet] {itemType} 착용 오브젝트 자동 발견 (패턴매칭): {obj.name}");
                }
            }
        }

        if (foundObjects.Count == 0)
        {
            Debug.LogWarning($"[WearingSet] {itemType} 착용 오브젝트를 찾을 수 없습니다. " +
                            $"'{itemType}_Equipped' 이름의 GameObject를 생성하거나 Inspector에서 수동 할당하세요.");
        }

        return foundObjects;
    }

    void Equip(Item itemType)
    {
        if (itemType == Item.None)
        {
            Debug.LogError("아이템 착용 절차가 아닌데 착용하는 매서드가 실패 이벤트에 등록되있음.");
            return;
        }
        var g = FindGroup(itemType);
        if (g == null) return;

        // 1) 착용 오브젝트 활성화
        foreach (var obj in g.equippedObjs)
            if (obj) obj.SetActive(true);

        // 2) 대기 오브젝트·콜라이더 GameObject 비활성화
        if (g.wearingObj)  g.wearingObj.SetActive(false);
        if (g.triggerCol)  g.triggerCol.enabled = false;

        switch (itemType)
        {
            case Item.Altimeter:
            {
                sfx.clip = audioClips[0];
                break;
            }
            case Item.Helmet:
            {
                sfx.clip = audioClips[1];
                volumeCtrl.ApplyPreset("Helmet", 0.5f);
                break;
            }
            case Item.Hook:
            {
                sfx.clip = audioClips[2];
                break;
            }
            case Item.Mask:
            {
                Debug.Log("마스크 착용 하는 소리 재생");
                break;
            }
        }
        
        sfx.Play();
        
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
