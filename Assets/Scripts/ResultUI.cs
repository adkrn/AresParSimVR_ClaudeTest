using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultUI : MonoBehaviour
{
    [Header("UI ������Ʈ ����")]
    public Transform procParent;
    public GameObject procEvalItem;
    public Transform valueParent;
    public GameObject valueEvalItem;

    [SerializeField] private TMP_Text totalScore;
    [SerializeField] private TMP_Text rankText;
    
    /// <summary>
    /// UI ��� ����
    /// </summary>
    public void Init()
    {
        // ���� UI ����
        foreach (Transform child in procParent)
            Destroy(child.gameObject);
        foreach (Transform child in valueParent)
            Destroy(child.gameObject);

        var total = 0;
        var totalMax = 0;
        
        foreach (var evResult in UIManager.Inst.evalRList)
        {
            var isProc = evResult.category is EvCategory.Procedure or EvCategory.Incident;
            var parent = isProc ? procParent : valueParent;
            var prefab = isProc ? procEvalItem : valueEvalItem;

            var go = Instantiate(prefab, parent);
            var itemUI = go.GetComponent<EvalItemUI>();
            if (itemUI != null)
                itemUI.Init(evResult);
            
            total += int.Parse(evResult.score);
            
            if (Enum.TryParse<EvName>(evResult.name, out var evName))
            {
                var eval = DataManager.Inst.GetEvaluation(evName);
                if (eval != null && int.TryParse(eval.maxScore, out int max))
                    totalMax += max;
            }
        }

        totalScore.text = total.ToString();
        
        // ��ũ ���
        string rank = "F";
        if (totalMax > 0)
        {
            float ratio = (float)total / totalMax * 100f;
            rank =  ratio >= 90f ? "A"
                : ratio >= 80f ? "B"
                : ratio >= 70f ? "C"
                : ratio >= 60f ? "D"
                : "F";
        }

        // ��ũ UI ǥ��
        rankText.text = rank;
    }
}

// ��� ������ ������
public class EvalResult
{
    public string name;
    public EvCategory category;
    public string result;
    public string score;
}
