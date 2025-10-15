#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// MonoBehaviour용
// ─────────────────────────────────────────────────────────────
[CustomEditor(typeof(MonoBehaviour), true)]
[CanEditMultipleObjects]
public class ShowConstEditorMono : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        ShowConstDrawer.DrawConstants(targets);
    }
}

// ─────────────────────────────────────────────────────────────
// ScriptableObject용
// ─────────────────────────────────────────────────────────────
[CustomEditor(typeof(ScriptableObject), true)]
[CanEditMultipleObjects]
public class ShowConstEditorSo : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        ShowConstDrawer.DrawConstants(targets);
    }
}


// ─────────────────────────────────────────────────────────────
// 공통 로직 – 실제 상수 수집·출력부
// ─────────────────────────────────────────────────────────────
static class ShowConstDrawer
{
    const string DEFAULT_HEADER = "읽기 전용 상수";

    public static void DrawConstants(Object[] targets)
    {
        // ① 헤더별로 모으기
        var byHeader = new Dictionary<string, List<FieldInfo>>();

        foreach (var t in targets)
        {
            foreach (var f in t.GetType().GetFields(
                         BindingFlags.Static | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
            {
                if (!(f.IsLiteral || f.IsInitOnly)) continue;

                var attr = f.GetCustomAttribute<ShowConstAttribute>();
                if (attr == null) continue;

                string header = string.IsNullOrEmpty(attr.header)
                    ? DEFAULT_HEADER
                    : attr.header;

                if (!byHeader.TryGetValue(header, out var list))
                    byHeader[header] = list = new List<FieldInfo>();

                list.Add(f);
            }
        }

        if (byHeader.Count == 0) return;

        // ② 출력
        EditorGUILayout.Space();
        foreach (var kv in byHeader)
        {
            EditorGUILayout.LabelField(kv.Key, EditorStyles.boldLabel);

            foreach (var f in kv.Value.Distinct())
            {
                var attr = f.GetCustomAttribute<ShowConstAttribute>();

                string fieldLabel =
                    string.IsNullOrEmpty(attr.label)
                        ? ObjectNames.NicifyVariableName(f.Name)
                        : attr.label;

                var val = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);

                EditorGUILayout.LabelField(fieldLabel, val?.ToString() ?? "null");
            }

            EditorGUILayout.Space(4);
        }
    }
}
#endif