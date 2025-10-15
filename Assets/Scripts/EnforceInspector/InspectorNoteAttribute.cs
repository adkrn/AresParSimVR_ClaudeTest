using UnityEngine;

public enum NoteType { Info, Warning, Error }

public sealed class InspectorNoteAttribute : PropertyAttribute
{
    public readonly string   message;
    public readonly NoteType type;

    public InspectorNoteAttribute(string message,
        NoteType type = NoteType.Info)
    {
        this.message = message;
        this.type    = type;
    }
}