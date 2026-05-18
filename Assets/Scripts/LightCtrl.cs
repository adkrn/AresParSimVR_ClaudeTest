using UnityEngine;

public class LightCtrl : MonoBehaviour
{
    [SerializeField] private float lerpSpeed = 2f;

    private Color targetColor;
    private float targetDensity;

    private void Start()
    {
        RenderSettings.fog = true;
        targetColor = RenderSettings.fogColor;
        targetDensity = RenderSettings.fogDensity;
    }

    /// <summary>
    /// Fog 색상과 밀도 설정
    /// </summary>
    /// <param name="color">목표 Fog 색상</param>
    /// <param name="density">목표 Fog 밀도</param>
    /// <param name="customSpeed">전환 속도 (기본 2f)</param>
    public void SetFog(Color color, float density = 0.0004f, float customSpeed = 2f)
    {
        targetColor = color;
        targetDensity = density;
        lerpSpeed = customSpeed;
    }

    private void Update()
    {
        RenderSettings.fogColor = Color.Lerp(
            RenderSettings.fogColor,
            targetColor,
            Time.deltaTime * lerpSpeed
        );

        RenderSettings.fogDensity = Mathf.Lerp(
            RenderSettings.fogDensity,
            targetDensity,
            Time.deltaTime * lerpSpeed
        );
    }
}
