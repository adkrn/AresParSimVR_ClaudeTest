using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;

[CustomEditor(typeof(Transform))]
public class TransformInspectorExtended : Editor
{
    private bool showGlobal = false;
    private static bool isScaleLock = true; // 스케일 락 기본값: 켜짐

    // 기본 Transform Inspector 리플렉션용
    private Editor defaultEditor;
    private MethodInfo doPositionFieldMethod;
    private MethodInfo doRotationFieldMethod;
    private MethodInfo doScaleFieldMethod; // 필요하면 사용할 수 있지만, 여기서는 커스텀 스케일 필드를 직접 구현

    private void OnEnable()
    {
        // UnityEditor.TransformInspector 타입(내부)을 Reflection으로 가져옴
        Type transformInspectorType = typeof(Transform).Assembly.GetType("UnityEditor.TransformInspector");
        if (transformInspectorType != null)
        {
            // 내부 Inspector 인스턴스를 만든다
            defaultEditor = Editor.CreateEditor(target, transformInspectorType);

            // 내부 메서드를 Reflection으로 얻는다 (포지션, 로테이션, 스케일)
            doPositionFieldMethod = transformInspectorType.GetMethod(
                "DoPositionField",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            doRotationFieldMethod = transformInspectorType.GetMethod(
                "DoRotationField",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            doScaleFieldMethod = transformInspectorType.GetMethod(
                "DoScaleField",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        Transform t = (Transform)target;

        // ─────────────────────────────────────────────────────────────────
        // 1) Position (기본 Inspector 방식)
        // ─────────────────────────────────────────────────────────────────
        if (doPositionFieldMethod != null)
        {
            doPositionFieldMethod.Invoke(defaultEditor, null);
        }
        else
        {
            // 만약 Reflection 실패 시 대비
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("m_LocalPosition"), 
                new GUIContent("Position")
            );
        }

        // ─────────────────────────────────────────────────────────────────
        // 2) Rotation (기본 Inspector 방식)
        // ─────────────────────────────────────────────────────────────────
        if (doRotationFieldMethod != null)
        {
            doRotationFieldMethod.Invoke(defaultEditor, null);
        }
        else
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("m_LocalRotation"), 
                new GUIContent("Rotation")
            );
        }

        // ─────────────────────────────────────────────────────────────────
        // 3) Scale (커스텀 구현 + 스케일 락)
        //    - 원본과 같은 정렬을 위해 직접 한 줄에 Label + Lock Icon + X/Y/Z 배치
        // ─────────────────────────────────────────────────────────────────
        {
            // 한 줄 높이의 Rect를 가져온다
            Rect lineRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);

            // "Scale" 라벨 Rect
            Rect labelRect = new Rect(
                lineRect.x,
                lineRect.y,
                EditorGUIUtility.labelWidth,
                lineRect.height
            );
            // 남은 영역 (Label 뒤쪽)
            Rect fieldRect = new Rect(
                labelRect.xMax,
                lineRect.y,
                lineRect.width - labelRect.width,
                lineRect.height
            );

            // 왼쪽에 "Scale" 라벨
            EditorGUI.LabelField(labelRect, "Scale");

            // fieldRect 영역 안에서 ( Lock Icon ) + ( X ) + ( Y ) + ( Z ) 를 배치
            float iconWidth = 25f;
            float eachWidth = (fieldRect.width - iconWidth+20f) / 3f;

            // Lock 아이콘 Rect
            Rect lockRect = new Rect(fieldRect.x-25, fieldRect.y, iconWidth, fieldRect.height);
            // X 필드 Rect
            Rect xRect = new Rect(lockRect.xMax+1f, fieldRect.y, eachWidth-1f, fieldRect.height);
            // Y 필드 Rect
            Rect yRect = new Rect(xRect.xMax+4f, fieldRect.y, eachWidth-1f, fieldRect.height);
            // Z 필드 Rect
            Rect zRect = new Rect(yRect.xMax+4f, fieldRect.y, eachWidth-1f, fieldRect.height);

            // 스케일 락 아이콘 로드 (버전에 따라 다른 아이콘명을 시도)
            // LockIcon / LockIcon-On / InspectorLock 등
            GUIContent lockIcon = EditorGUIUtility.IconContent(isScaleLock ? "LockIcon-On" : "LockIcon");
            if (lockIcon != null) lockIcon.tooltip = "Lock Scale (Uniform)";
            
            // (1) 아이콘 배경색 설정
            //    - OFF일 때 회색, ON일 때 청록
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = isScaleLock ? Color.cyan : Color.gray;
            
            // 아이콘 버튼
            if (GUI.Button(lockRect, lockIcon))
            {
                // 막 켤 때는 X값으로 통일
                if (!isScaleLock)
                {
                    float unified = t.localScale.x;
                    t.localScale = new Vector3(unified, unified, unified);
                }
                isScaleLock = !isScaleLock;
            }

            // 버튼 그린 후 배경색 복원
            GUI.backgroundColor = oldColor;

            // (2) X/Y/Z 필드 표시
            //     - 어느 축을 수정해도 X/Y/Z 레이블이 뜨도록 하려면
            //       각 FloatField에 작은 labelWidth 할당
            EditorGUI.BeginChangeCheck();

            float oldLabelWidth = EditorGUIUtility.labelWidth;
            // 축 레이블 "X" / "Y" / "Z"만 표시하기 위해 labelWidth를 줄임
            EditorGUIUtility.labelWidth = 14f; 

            Vector3 scale = t.localScale;

            if (isScaleLock)
            {
                // 락이 켜져 있으면 X/Y/Z를 동시에 표시하되 값은 모두 동일해야 함
                float uniform = scale.x; // X값을 기준
                // X
                float newX = EditorGUI.FloatField(xRect, new GUIContent("X"), uniform);
                // Y
                float newY = EditorGUI.FloatField(yRect, new GUIContent("Y"), uniform);
                // Z
                float newZ = EditorGUI.FloatField(zRect, new GUIContent("Z"), uniform);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Change Scale (Uniform)");

                    // 어느 한 축이라도 달라졌다면 그 값으로 통일
                    float finalVal = uniform;
                    if (!Mathf.Approximately(newX, uniform))      finalVal = newX;
                    else if (!Mathf.Approximately(newY, uniform)) finalVal = newY;
                    else if (!Mathf.Approximately(newZ, uniform)) finalVal = newZ;

                    t.localScale = new Vector3(finalVal, finalVal, finalVal);
                    EditorUtility.SetDirty(t);
                }
            }
            else
            {
                // 락이 꺼져 있으면 X/Y/Z 각각 독립
                float newX = EditorGUI.FloatField(xRect, new GUIContent("X"), scale.x);
                float newY = EditorGUI.FloatField(yRect, new GUIContent("Y"), scale.y);
                float newZ = EditorGUI.FloatField(zRect, new GUIContent("Z"), scale.z);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Change Scale (Non-Uniform)");
                    t.localScale = new Vector3(newX, newY, newZ);
                    EditorUtility.SetDirty(t);
                }
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        // ─────────────────────────────────────────────────────────────────
        // 글로벌 표시 토글 버튼
        // ─────────────────────────────────────────────────────────────────
        if (GUILayout.Button(showGlobal ? "글로벌 인포 끄기" : "글로벌 인포 확인"))
        {
            showGlobal = !showGlobal;
        }
        
        if (showGlobal)
        {
            // 글로벌 포지션 (옵션)
            EditorGUI.BeginChangeCheck();
            Vector3 newGlobalPos = EditorGUILayout.Vector3Field("Global Position", t.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Global Position");
                t.position = newGlobalPos;
                EditorUtility.SetDirty(t);
            }
        
            // 글로벌 로테이션 (옵션)
            EditorGUI.BeginChangeCheck();
            Vector3 newGlobalRot = EditorGUILayout.Vector3Field("Global Rotation", t.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Global Rotation");
                t.eulerAngles = newGlobalRot;
                EditorUtility.SetDirty(t);
            }
            
            // 글로벌 스케일 (옵션)
            EditorGUILayout.Vector3Field("Global Scale(Read Only)", t.lossyScale);
            // EditorGUILayout.LabelField("Global Scale", t.lossyScale.ToString());
        }
        
        serializedObject.ApplyModifiedProperties();
        SceneView.RepaintAll();
    }
}
