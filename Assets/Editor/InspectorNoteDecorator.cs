#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(InspectorNoteAttribute))]
public class InspectorNoteDecorator : DecoratorDrawer
{
    static MessageType ToEditorType(NoteType t) =>
        t switch
        {
            NoteType.Info    => MessageType.Info,
            NoteType.Warning => MessageType.Warning,
            NoteType.Error   => MessageType.Error,
            _                => MessageType.None
        };

    // DecoratorDrawer 는 ‘대상 필드’가 없으므로 attribute 캐싱만 필요
    InspectorNoteAttribute Attr => (InspectorNoteAttribute)attribute;

    public override float GetHeight()          // HelpBox 높이
    {
        float width  = EditorGUIUtility.currentViewWidth;
        return EditorStyles.helpBox.CalcHeight(
            new GUIContent(Attr.message), width) + 2f;
    }

    public override void OnGUI(Rect position)  // HelpBox 그리기
    {
        EditorGUI.HelpBox(position, Attr.message, ToEditorType(Attr.type));
    }
}
#endif