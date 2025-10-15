using Oculus.Interaction;
using UnityEngine;
using Oculus.Interaction.Input;      // Handedness enum

/// <summary>
/// Grabbable 한 손잡이를 로컬 Y축으로만 슬라이드하도록 제한
/// </summary>
[RequireComponent(typeof(Grabbable))]
[DisallowMultipleComponent]
public class ParachuteLinearTransformer : OneGrabTranslateTransformer
{
    [Tooltip("위쪽 한계(로컬 Y)")]
    public float upperLimit = 0.0f;     // 원위치
    [Tooltip("아래쪽 한계(로컬 Y)")]
    public float lowerLimit = -0.25f;   // ↓ 25 cm

    // OneGrabTranslateTransformer.Initialize() 는 Grabbable 쪽에서
    // 자동 호출되므로 여기서는 제약값만 준비해 두면 됩니다.
    private void Awake()
    {
        // FloatConstraint는 'Constrain' 플래그와 'Value' 두 필드만 존재
        FloatConstraint minY = new FloatConstraint { Constrain = true, Value = lowerLimit };
        FloatConstraint maxY = new FloatConstraint { Constrain = true, Value = upperLimit };

        // 나머지 축은 Constrain=false 그대로 두면 자유 이동→이 클래스에서 잠급니다.
        OneGrabTranslateConstraints c = new OneGrabTranslateConstraints
        {
            ConstraintsAreRelative = true,   // 로컬 원점 기준
            MinX = new FloatConstraint(),    // Constrain = false
            MaxX = new FloatConstraint(),
            MinZ = new FloatConstraint(),
            MaxZ = new FloatConstraint(),
            MinY = minY,
            MaxY = maxY
        };

        Constraints = c;   // ← OneGrabTranslateTransformer 프로퍼티에 전달
    }
}