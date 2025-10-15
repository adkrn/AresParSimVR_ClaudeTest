using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SampleData : MonoBehaviour
{
    [Header("Auto‑generated sample lists (read‑only)")]
    public List<TimeLine> timeLine;
    public List<Procedure> procedures;
    public List<Instruction> instructions;

#if UNITY_EDITOR
    private void OnEnable()
    {
        // 에디터 & 런타임 모두 호출: 에디터 모드(Edit)에서만 자동 채움
        if (!Application.isPlaying)
        {
            // 변경 사항이 씬에 저장되도록 플래그 설정
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}




