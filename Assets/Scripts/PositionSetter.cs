using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬-오브젝트 쌍을 Inspector에서 설정하기 위한 클래스
/// </summary>
[Serializable]
public class SceneObjectPair
{
    [Tooltip("씬 이름")]
    public string sceneName;

    [Tooltip("배치될 타겟 오브젝트 이름")]
    public string objectName;

    [Space(10)]
    [Tooltip("위치와 회전을 수동으로 설정")]
    public bool isPosSet;

    [Tooltip("로컬 위치")]
    public Vector3 pos;

    [Tooltip("로컬 회전 (Euler Angles)")]
    public Vector3 rot;
}

public class PositionSetter : MonoBehaviour
{
    [Header("씬별 배치 설정")]
    [Tooltip("각 씬에서 이 오브젝트가 배치될 타겟 오브젝트를 지정합니다.")]
    public List<SceneObjectPair> targetList = new List<SceneObjectPair>();

    // 런타임에서 빠른 검색을 위한 Dictionary (내부 사용)
    private Dictionary<string, SceneObjectPair> targetDic;

    public void Init(string currentSceneName, GameObject[] rootObjects)
    {
        // targetList를 Dictionary로 변환 (빠른 검색을 위해)
        BuildTargetDictionary();

        // targetDic이 null이거나 비어있으면 종료
        if (targetDic == null || targetDic.Count == 0)
        {
            Debug.LogWarning("[PositionSetter] targetList가 비어있습니다. Inspector에서 설정해주세요.");
            return;
        }

        // currentSceneName이 targetDic에 없으면 종료
        if (!targetDic.TryGetValue(currentSceneName, out var pair))
        {
            Debug.LogWarning($"[PositionSetter] targetDic에 씬 '{currentSceneName}'이 존재하지 않습니다.");
            return;
        }

        // targetDic에서 타겟 오브젝트 이름 가져오기
        if (string.IsNullOrEmpty(pair.objectName))
        {
            Debug.LogWarning($"[PositionSetter] 씬 '{currentSceneName}'에 대한 타겟 오브젝트 이름이 비어있습니다.");
            return;
        }
        
        // 1) Try find target in root objects
        GameObject targetObject = rootObjects
            .FirstOrDefault(obj => obj != null && obj.name == pair.objectName);

        // 2) If not found, search children of each root
        if (targetObject == null)
        {
            foreach (var root in rootObjects)
            {
                if (root == null) continue;

                targetObject = FindInChildren(root.transform, pair.objectName);
                if (targetObject != null)
                    break;
            }
        }

        // 타겟 오브젝트를 찾지 못한 경우
        if (targetObject == null)
        {
            Debug.LogError($"[PositionSetter] rootObjects에서 '{pair.objectName}' 오브젝트를 찾을 수 없습니다.");
            return;
        }

        // 현재 오브젝트를 타겟 오브젝트의 하위로 배치
        transform.SetParent(targetObject.transform, false);

        // isPosSet이 true면 위치와 회전 설정
        if (pair.isPosSet)
        {
            transform.localPosition = pair.pos;
            transform.localEulerAngles = pair.rot;
            Debug.Log($"[PositionSetter] '{gameObject.name}'을(를) '{pair.objectName}' 하위에 배치하고 위치/회전을 설정했습니다. (Pos: {pair.pos}, Rot: {pair.rot})");
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;
            Debug.Log($"[PositionSetter] '{gameObject.name}'을(를) '{pair.objectName}' 하위에 배치했습니다.");
        }
    }
    
    private GameObject FindInChildren(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child.gameObject;

            var found = FindInChildren(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// targetList를 Dictionary로 변환합니다.
    /// </summary>
    private void BuildTargetDictionary()
    {
        if (targetList == null || targetList.Count == 0)
        {
            targetDic = new Dictionary<string, SceneObjectPair>();
            return;
        }

        targetDic = new Dictionary<string, SceneObjectPair>();

        foreach (var pair in targetList)
        {
            // 빈 씬 이름이나 오브젝트 이름은 건너뛰기
            if (string.IsNullOrEmpty(pair.sceneName))
            {
                Debug.LogWarning("[PositionSetter] targetList에 씬 이름이 비어있는 항목이 있습니다. 건너뜁니다.");
                continue;
            }

            if (string.IsNullOrEmpty(pair.objectName))
            {
                Debug.LogWarning($"[PositionSetter] 씬 '{pair.sceneName}'의 오브젝트 이름이 비어있습니다. 건너뜁니다.");
                continue;
            }

            // 중복 키 체크
            if (targetDic.ContainsKey(pair.sceneName))
            {
                Debug.LogWarning($"[PositionSetter] 씬 '{pair.sceneName}'이 중복되었습니다. 첫 번째 항목만 사용됩니다.");
                continue;
            }

            targetDic.Add(pair.sceneName, pair);
        }

        Debug.Log($"[PositionSetter] {targetDic.Count}개의 씬-오브젝트 쌍을 로드했습니다.");
    }

    /// <summary>
    /// 배치되는 씬이 targetDic에 존재하는지 확인합니다.
    /// </summary>
    /// <param name="key">확인할 씬 이름</param>
    /// <returns>키 존재 여부</returns>
    public bool HasKey(string key)
    {
        if (targetDic == null)
            BuildTargetDictionary();

        return targetDic != null && targetDic.ContainsKey(key);
    }
}
