// Runtime/ShowConstAttribute.cs
using UnityEngine;

/// <summary>
/// const(또는 static readonly) 필드를 Inspector 에 표시하기 위한 태그
/// </summary>
public sealed class ShowConstAttribute : PropertyAttribute
{
    /// <summary>섹션 제목(생략 시 기본 헤더 사용)</summary>
    public readonly string header;

    /// <summary>해당 필드에 표시할 라벨(생략 시 NicifyVariableName 사용)</summary>
    public readonly string label;

    /// <param name="header">묶음 제목(옵션)</param>
    /// <param name="label">필드 표시 이름(옵션)</param>
    public ShowConstAttribute(string header = null, string label = null)
    {
        this.header = header;
        this.label  = label;
    }
}
