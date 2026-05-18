using Oculus.Interaction.HandGrab;
using UnityEngine;

public class RiserGrabInteractor : HandGrabInteractor
{
    [Header("Out of View Settings")]
    [SerializeField] private bool maintainGrabOutOfView = true;
    [SerializeField] private bool showDebugLog = false;

    protected override bool ComputeShouldUnselect()
    {
        if (!maintainGrabOutOfView)
            return base.ComputeShouldUnselect();

        // 시야 밖이면 unselect 하지 않음
        if (!Hand.IsTrackedDataValid)
        {
            if (showDebugLog)
                Debug.Log("[TrackingTolerant] Hand out of view - maintaining grab");
            return false;
        }

        // 시야 안에서는 기본 동작
        return base.ComputeShouldUnselect();
    }
}
