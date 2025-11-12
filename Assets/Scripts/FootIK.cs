using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [Header("=== IK 설정 ===")]
    [SerializeField] private bool enableIK = true;
    [SerializeField] [Range(0f, 1f)] private float ikWeight = 1f;

    [Header("=== 골반 조정 ===")]
    [SerializeField] private bool adjustPelvis = true;
    [SerializeField] private float pelvisOffset = 0f;
    [SerializeField] [Range(0f, 1f)] private float pelvisSpeed = 0.5f;

    [Header("=== 레이캐스트 설정 ===")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float rayStartHeight = 1.0f;
    [SerializeField] private float rayDistance = 1.5f;
    [SerializeField] private float footOffset = 0.05f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugRays = false;

    private Animator animator;

    // 발 IK 데이터
    private Vector3 leftFootPosition;
    private Vector3 rightFootPosition;
    private Quaternion leftFootRotation;
    private Quaternion rightFootRotation;
    private float leftFootIkWeight;
    private float rightFootIkWeight;

    // 골반 조정
    private float pelvisHeightOffset;

    private void Start()
    {
        animator = GetComponent<Animator>();

        if (!animator)
        {
            Debug.LogError("[FootIK] Animator 컴포넌트가 없습니다!");
            enabled = false;
            return;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("[FootIK] Humanoid 리그가 아닙니다!");
            enabled = false;
            return;
        }

        Debug.Log("[FootIK] 초기화 완료 (Velog 방법)");
    }

    private void Update()
    {
        if (!enableIK) return;

        // Step 1: 발 위치와 회전 계산 (레이캐스트)
        SolveFeetPositions();
    }

    /// <summary>
    /// Step 1: 레이캐스트로 발의 목표 위치와 회전 계산
    /// </summary>
    private void SolveFeetPositions()
    {
        // 왼발 계산
        SolveFootPosition(
            AvatarIKGoal.LeftFoot,
            out leftFootPosition,
            out leftFootRotation,
            out leftFootIkWeight
        );

        // 오른발 계산
        SolveFootPosition(
            AvatarIKGoal.RightFoot,
            out rightFootPosition,
            out rightFootRotation,
            out rightFootIkWeight
        );

        // Step 2: 골반 높이 조정 (발 위치 기반)
        if (adjustPelvis)
        {
            MovePelvisHeight();
        }
    }

    /// <summary>
    /// 개별 발의 위치와 회전 계산
    /// </summary>
    private void SolveFootPosition(
        AvatarIKGoal foot,
        out Vector3 position,
        out Quaternion rotation,
        out float ikWeight)
    {
        // 현재 발 위치 가져오기
        Transform footBone = animator.GetBoneTransform(
            foot == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot
        );
        position = footBone.position;
        rotation = footBone.rotation;
        ikWeight = 0f;

        // 레이캐스트 시작 위치
        Vector3 rayStart = position + Vector3.up * rayStartHeight;

        // 디버그 레이
        if (showDebugRays)
        {
            Color rayColor = foot == AvatarIKGoal.LeftFoot ? Color.red : Color.blue;
            Debug.DrawRay(rayStart, Vector3.down * rayDistance, rayColor);
        }

        // 지면 검색
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
            rayDistance, groundLayer))
        {
            // 목표 위치 계산 (지면 + 오프셋)
            position = hit.point + Vector3.up * footOffset;

            // 목표 회전 계산 (지면 법선)
            rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, hit.normal),
                hit.normal
            );

            // IK 가중치 설정 (지면 감지 시)
            ikWeight = 1f;
        }
    }

    /// <summary>
    /// Step 2: 골반 높이 조정
    /// 블로그 핵심: 발 위치 변화에 따라 골반도 이동
    /// </summary>
    private void MovePelvisHeight()
    {
        // 왼발과 오른발 중 더 낮은 위치 찾기
        float leftOffsetY = leftFootPosition.y - transform.position.y;
        float rightOffsetY = rightFootPosition.y - transform.position.y;

        // 더 낮은 발 기준으로 골반 조정
        float targetPelvisOffset = Mathf.Min(leftOffsetY, rightOffsetY);

        // 부드럽게 전환 (선형보간)
        pelvisHeightOffset = Mathf.Lerp(
            pelvisHeightOffset,
            targetPelvisOffset + pelvisOffset,
            pelvisSpeed
        );
    }

    /// <summary>
    /// Step 3: 계산된 IK 값을 Animator에 적용
    /// Unity가 OnAnimatorIK 호출 시 자동 실행
    /// </summary>
    private void OnAnimatorIK(int layerIndex)
    {
        if (!animator || !enableIK) return;

        // 골반 높이 적용
        if (adjustPelvis)
        {
            ApplyPelvisHeight();
        }

        // 왼발 IK 적용
        MoveFeetToIkPoint(
            AvatarIKGoal.LeftFoot,
            leftFootPosition,
            leftFootRotation,
            leftFootIkWeight
        );

        // 오른발 IK 적용
        MoveFeetToIkPoint(
            AvatarIKGoal.RightFoot,
            rightFootPosition,
            rightFootRotation,
            rightFootIkWeight
        );
    }

    /// <summary>
    /// 골반 높이를 Animator에 적용
    /// </summary>
    private void ApplyPelvisHeight()
    {
        // Body Position (골반) 조정
        Vector3 bodyPosition = animator.bodyPosition;
        bodyPosition.y += pelvisHeightOffset;
        animator.bodyPosition = bodyPosition;
    }

    /// <summary>
    /// 발 IK를 Animator에 적용
    /// </summary>
    private void MoveFeetToIkPoint(
        AvatarIKGoal foot,
        Vector3 position,
        Quaternion rotation,
        float weight)
    {
        // IK 가중치 설정
        float finalWeight = weight * ikWeight;
        animator.SetIKPositionWeight(foot, finalWeight);
        animator.SetIKRotationWeight(foot, finalWeight);

        // IK 위치와 회전 적용
        if (finalWeight > 0)
        {
            animator.SetIKPosition(foot, position);
            animator.SetIKRotation(foot, rotation);
        }
    }

    /// <summary>
    /// IK 활성화/비활성화
    /// </summary>
    public void SetIKEnabled(bool enabled)
    {
        enableIK = enabled;
        
        if (!enabled)
        {
            pelvisHeightOffset = 0f;
            Debug.Log("[FootIK] IK 비활성화 - pelvisHeightOffset 리셋");
        }
        
        Debug.Log($"[FootIK] IK {(enabled ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// IK 가중치 설정
    /// </summary>
    public void SetIKWeight(float weight)
    {
        ikWeight = Mathf.Clamp01(weight);
    }

    /// <summary>
    /// 골반 조정 활성화/비활성화
    /// </summary>
    public void SetPelvisAdjustment(bool adjust)
    {
        adjustPelvis = adjust;
    }
}