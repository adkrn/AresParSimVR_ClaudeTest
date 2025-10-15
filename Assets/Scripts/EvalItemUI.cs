using TMPro;
using UnityEngine;

public class EvalItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public TMP_Text nameText;
    public GameObject successIcon;
    public GameObject failIcon;
    public TMP_Text valueText;
    public TMP_Text scoreText;

    public void Init(EvalResult evResult)
    {
        if (nameText != null)
            nameText.text = evResult.name;

        if (successIcon != null)
            successIcon.SetActive(evResult.result == "성공");

        if (failIcon != null)
            failIcon.SetActive(evResult.result == "실패");

        if (valueText != null)
            valueText.text = evResult.result;

        if (scoreText != null)
            scoreText.text = evResult.score;
    }
}