using UnityEngine;
using Oculus.Movement.Effects.Deprecated;

/// <summary>
/// Meta Movement SDK의 TwoBoneIK를 활용한 Foot IK 컨트롤러
/// 지면에 레이캐스트를 쏴서 발을 자연스럽게 배치
/// </summary>
public class FootIKController : MonoBehaviour
{
    [Header("=== IK 설정 ===")]
    [SerializeField] private bool enableIK = true;
    [SerializeField] [Range(0f, 1f)] private float ikWeight = 1f;
    [Tooltip("IK 업데이트 간격 (초) - 성능 최적화")]
    [SerializeField] private float ikUpdateInterval = 0.1f;

    [Header("=== 본 구조 (High_Jump_char_006) ===")]
    [Tooltip("Bip001 L Thigh")]
    [SerializeField] private Transform leftUpperLeg;
    [Tooltip("Bip001 L Calf")]
    [SerializeField] private Transform leftLowerLeg;
    [Tooltip("Bip001 L Foot")]
    [SerializeField] private Transform leftFoot;

    [Tooltip("Bip001 R Thigh")]
    [SerializeField] private Transform rightUpperLeg;
    [Tooltip("Bip001 R Calf")]
    [SerializeField] private Transform rightLowerLeg;
    [Tooltip("Bip001 R Foot")]
    [SerializeField] private Transform rightFoot;

    [Header("=== IK 타겟 (Rig 하위) ===")]
    [Tooltip("Rig/LeftFootTarget")]
    [SerializeField] private Transform leftFootTarget;
    [Tooltip("Rig/RightFootTarget")]
    [SerializeField] private Transform rightFootTarget;

    [Header("=== 무릎 타겟 (Pole) ===")]
    [Tooltip("무릎이 향할 방향 - 없으면 자동 생성")]
    [SerializeField] private Transform leftKneeTarget;
    [SerializeField] private Transform rightKneeTarget;

    [Header("=== 레이캐스트 설정 ===")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("레이캐스트 시작 높이 (발 위에서)")]
    [SerializeField] private float rayStartHeight = 1f;
    [Tooltip("레이캐스트 최대 거리")]
    [SerializeField] private float rayDistance = 2f;
    [Tooltip("발과 지면 사이 간격")]
    [SerializeField] private float footOffset = 0.05f;

    [Header("=== IK 컴포넌트 ===")]
    [SerializeField] private TwoBoneIK leftLegIK;
    [SerializeField] private TwoBoneIK rightLegIK;

    [Header("=== 골반 높이 조정 ===")]
    [SerializeField] private bool adjustHipHeight = true;
    [Tooltip("Bip001 Pelvis")]
    [SerializeField] private Transform pelvis;
    [Tooltip("골반과 발 사이 기본 높이")]
    [SerializeField] private float defaultHipHeight = 1.0f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugRays = true;
    [SerializeField] private bool showDebugLogs = false;

    // 내부 변수
    private float lastIKUpdate = 0f;
    private Vector3 initialPelvisPosition;

    private void Start()
    {
        // 초기 골반 위치 저장
        if (pelvis)
            initialPelvisPosition = pelvis.localPosition;

        // 무릎 타겟 자동 생성
        CreateKneeTargets();

        // TwoBoneIK 컴포넌트 설정
        SetupTwoBoneIK();

        if (showDebugLogs)
            Debug.Log("[FootIK] 초기화 완료");
    }

    private void LateUpdate()
    {
        if (!enableIK) return;

        // 성능 최적화: 일정 간격으로만 업데이트
        if (Time.time - lastIKUpdate < ikUpdateInterval)
            return;

        lastIKUpdate = Time.time;

        // 발 IK 업데이트
        Vector3 leftFootPos = UpdateFootIK(leftFoot, leftFootTarget, Color.red);
        Vector3 rightFootPos = UpdateFootIK(rightFoot, rightFootTarget, Color.blue);

        // 골반 높이 조정
        if (adjustHipHeight && pelvis)
            AdjustHipHeight(leftFootPos, rightFootPos);
    }

    /// <summary>
    /// 발 IK 업데이트 - 지면 검색 및 타겟 위치 설정
    /// </summary>
    private Vector3 UpdateFootIK(Transform footBone, Transform footTarget, Color debugColor)
    {
        if (!footBone || !footTarget)
            return Vector3.zero;

        // 레이캐스트 시작 위치 (발 위에서)
        Vector3 rayStart = footBone.position + Vector3.up * rayStartHeight;

        // 디버그 레이 표시
        if (showDebugRays)
            Debug.DrawRay(rayStart, Vector3.down * rayDistance, debugColor);

        // 지면 검색
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
            rayDistance, groundLayer))
        {
            // 발 타겟 위치 업데이트 (부드럽게)
            Vector3 targetPos = hit.point + Vector3.up * footOffset;
            footTarget.position = Vector3.Lerp(
                footTarget.position,
                targetPos,
                ikWeight * Time.deltaTime * 10f
            );

            // 발 회전 업데이트 (지면 법선에 맞춤)
            Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, hit.normal)
                * footBone.rotation;
            
            footTarget.rotation = Quaternion.Slerp(
                footTarget.rotation,
                targetRot,
                ikWeight * Time.deltaTime * 10f
            );

            if (showDebugLogs)
                Debug.Log($"[FootIK] {footBone.name} 지면 감지: {hit.point}");

            return targetPos;
        }

        return footTarget.position;
    }

    /// <summary>
    /// 골반 높이 조정 - 양발 평균 높이에 맞춤
    /// </summary>
    private void AdjustHipHeight(Vector3 leftFootPos, Vector3 rightFootPos)
    {
        // // 양발의 평균 높이 계산
        // float avgFootHeight = (leftFootPos.y + rightFootPos.y) / 2f;
        //
        // // 골반 목표 높이
        // Vector3 targetPelvisPos = pelvis.position;
        // targetPelvisPos.y = avgFootHeight + defaultHipHeight;
        //
        // // 부드럽게 이동
        // pelvis.position = Vector3.Lerp(
        //     pelvis.position,
        //     targetPelvisPos,
        //     Time.deltaTime * 5f
        // );
        var lp = pelvis.localPosition;
        lp.y = ( (leftFootPos.y + rightFootPos.y) * 0.5f ) + defaultHipHeight;  // 높이 기준은 캐릭터 local로 해석
        pelvis.localPosition = Vector3.Lerp(pelvis.localPosition, lp, Time.deltaTime * 5f);
    }

    /// <summary>
    /// 무릎 타겟 자동 생성
    /// </summary>
    private void CreateKneeTargets()
    {
        if (!leftKneeTarget && leftLowerLeg)
        {
            GameObject go  = new GameObject("LeftKneeTarget");
            go.transform.SetParent(transform, worldPositionStays:false);
            // 허벅지 정면 방향으로 0.4~0.6m
            go.transform.position = leftLowerLeg.position + leftUpperLeg.forward * 0.5f; 
            go.transform.rotation = Quaternion.LookRotation(leftUpperLeg.forward, leftUpperLeg.up);
            leftKneeTarget = go.transform;

            if (showDebugLogs)
                Debug.Log("[FootIK] 왼쪽 무릎 타겟 자동 생성");
        }

        if (!rightKneeTarget && rightLowerLeg)
        {
            var go = new GameObject("RightKneeTarget");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = rightLowerLeg.position + rightUpperLeg.forward * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(rightUpperLeg.forward, rightUpperLeg.up);
            rightKneeTarget = go.transform;

            if (showDebugLogs)
                Debug.Log("[FootIK] 오른쪽 무릎 타겟 자동 생성");
        }
    }

    /// <summary>
    /// TwoBoneIK 컴포넌트 설정
    /// </summary>
    private void SetupTwoBoneIK()
    {
        // 왼쪽 다리 IK
        if (!leftLegIK)
        {
            GameObject leftIKObj = new GameObject("LeftLegIK");
            leftIKObj.transform.SetParent(transform);
            leftLegIK = leftIKObj.AddComponent<TwoBoneIK>();

            if (showDebugLogs)
                Debug.Log("[FootIK] 왼쪽 다리 TwoBoneIK 컴포넌트 생성");
        }

        // 왼쪽 IK 설정
        if (leftLegIK)
        {
            // Reflection으로 private 필드 설정
            var type = typeof(TwoBoneIK);
            type.GetField("_upperTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(leftLegIK, leftUpperLeg);
            type.GetField("_middleTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(leftLegIK, leftLowerLeg);
            type.GetField("_lowerTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(leftLegIK, leftFoot);
            type.GetField("_targetTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(leftLegIK, leftFootTarget);
            type.GetField("_poleTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(leftLegIK, leftKneeTarget);
        }

        // 오른쪽 다리 IK
        if (!rightLegIK)
        {
            GameObject rightIKObj = new GameObject("RightLegIK");
            rightIKObj.transform.SetParent(transform);
            rightLegIK = rightIKObj.AddComponent<TwoBoneIK>();

            if (showDebugLogs)
                Debug.Log("[FootIK] 오른쪽 다리 TwoBoneIK 컴포넌트 생성");
        }

        // 오른쪽 IK 설정
        if (rightLegIK)
        {
            var type = typeof(TwoBoneIK);
            type.GetField("_upperTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(rightLegIK, rightUpperLeg);
            type.GetField("_middleTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(rightLegIK, rightLowerLeg);
            type.GetField("_lowerTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(rightLegIK, rightFoot);
            type.GetField("_targetTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(rightLegIK, rightFootTarget);
            type.GetField("_poleTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(rightLegIK, rightKneeTarget);
        }
    }

    /// <summary>
    /// IK 활성화/비활성화
    /// </summary>
    public void SetIKEnabled(bool enabled)
    {
        enableIK = enabled;

        if (leftLegIK) leftLegIK.enabled = enabled;
        if (rightLegIK) rightLegIK.enabled = enabled;

        if (showDebugLogs)
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
    /// Ground Layer 설정
    /// </summary>
    public void SetGroundLayer(LayerMask layer)
    {
        groundLayer = layer;
    }
}