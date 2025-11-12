using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Player 전용 관절 데이터 송신 컴포넌트
/// Body + Finger 관절 데이터 자동 매핑 및 수집
/// </summary>
public class jointMapper : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("Body 관절 매핑 설정")]
    public JointMappingConfig config;

    [Tooltip("Finger 관절 매핑 설정")]
    public FingerMappingConfig fingerConfig;

    [Tooltip("자동 매핑 실행 시점")]
    public bool autoMapOnStart = true;

    [Header("검색 설정")]
    [Tooltip("검색 시작점 (null이면 자신)")]
    public Transform searchRoot;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    // ========== 핵심 데이터 ==========

    // 자동 매핑된 Transform들
    private Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    private Dictionary<string, Transform> fingerJoints = new Dictionary<string, Transform>();

    // Reflection 캐시 (성능 최적화)
    private Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

    // 손가락 초기 회전값 저장 (범위 체크용)
    private Dictionary<string, float> fingerInitialRotations = new Dictionary<string, float>();

    // ========== 초기화 ==========

    void Awake()
    {
        if (searchRoot == null)
            searchRoot = transform;
    }

    void Start()
    {
        if (autoMapOnStart)
        {
            AutoMap();
            CaptureInitialRotations();
        }
    }

    // ========== 자동 매핑 ==========

    /// <summary>
    /// 자동 매핑 실행 - Body와 Finger 관절 모두 검색
    /// </summary>
    public void AutoMap()
    {
        if (!ValidateConfig())
            return;

        Transform[] allTransforms = searchRoot.GetComponentsInChildren<Transform>();

        if (showDebugLogs)
            Debug.Log($"[jointMapper] 자동 매핑 시작 - {allTransforms.Length}개 Transform 검색");

        // 초기화
        joints.Clear();
        fingerJoints.Clear();
        fieldCache.Clear();

        int bodySuccessCount = 0;
        int fingerSuccessCount = 0;

        // Body 관절 매핑
        if (config != null && config.mappings != null)
        {
            bodySuccessCount = MapBodyJointsFromConfig(config.mappings, allTransforms);
        }

        // Finger 관절 매핑
        if (fingerConfig != null && fingerConfig.mappings != null)
        {
            fingerSuccessCount = MapFingerJointsFromConfig(fingerConfig.mappings, allTransforms);
        }

        Debug.Log($"[jointMapper] 매핑 완료 - Body: {bodySuccessCount}/{config?.mappings.Count ?? 0}, Finger: {fingerSuccessCount}/{fingerConfig?.mappings.Count ?? 0}");
    }

    /// <summary>
    /// Body 관절 매핑 수행
    /// </summary>
    private int MapBodyJointsFromConfig(
        List<JointMappingConfig.JointMap> mappings,
        Transform[] allTransforms)
    {
        int successCount = 0;

        foreach (var map in mappings)
        {
            var result = FindMappingTransform(map, allTransforms);

            if (result.found)
            {
                joints[map.fieldName] = result.transform;

                var field = typeof(JointRotation).GetField(map.fieldName);
                if (field != null)
                    fieldCache[map.fieldName] = field;

                successCount++;

                if (showDebugLogs)
                    Debug.Log($"✅ [Body] 매핑 성공: {map.fieldName} → {result.transform.name}");
            }
            else if (showDebugLogs)
            {
                Debug.LogWarning($"❌ [Body] 매핑 실패: {map.fieldName}");
            }
        }

        return successCount;
    }

    /// <summary>
    /// Finger 관절 매핑 수행
    /// </summary>
    private int MapFingerJointsFromConfig(
        List<FingerMappingConfig.FingerMap> mappings,
        Transform[] allTransforms)
    {
        int successCount = 0;

        foreach (var map in mappings)
        {
            var result = FindFingerMappingTransform(map, allTransforms);

            if (result.found)
            {
                fingerJoints[map.fieldName] = result.transform;

                var field = typeof(FingerRotation).GetField(map.fieldName);
                if (field != null)
                    fieldCache[map.fieldName] = field;

                successCount++;

                if (showDebugLogs)
                    Debug.Log($"✅ [Finger] 매핑 성공: {map.fieldName} → {result.transform.name}");
            }
            else if (showDebugLogs)
            {
                Debug.LogWarning($"❌ [Finger] 매핑 실패: {map.fieldName}");
            }
        }

        return successCount;
    }

    /// <summary>
    /// 특정 매핑에 대한 Transform 검색
    /// </summary>
    private (bool found, Transform transform) FindMappingTransform(
        JointMappingConfig.JointMap map,
        Transform[] allTransforms)
    {
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();

            foreach (var pattern in map.patterns)
            {
                if (nameLower.Contains(pattern.ToLower()))
                {
                    return (true, t);
                }
            }
        }

        return (false, null);
    }
    
    private (bool found, Transform transform) FindFingerMappingTransform(
        FingerMappingConfig.FingerMap map,
        Transform[] allTransforms)
    {
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();

            foreach (var pattern in map.patterns)
            {
                if (nameLower.Contains(pattern.ToLower()))
                {
                    return (true, t);
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// 손가락 초기 회전값 캡처 (범위 체크 기준값)
    /// </summary>
    private void CaptureInitialRotations()
    {
        fingerInitialRotations.Clear();

        foreach (var kvp in fingerJoints)
        {
            string fieldName = kvp.Key;
            Transform joint = kvp.Value;

            if (joint != null)
            {
                float initialZ = joint.localRotation.eulerAngles.z;
                fingerInitialRotations[fieldName] = initialZ;

                if (showDebugLogs)
                {
                    Debug.Log($"[jointMapper] {fieldName} 초기값 저장: {initialZ:F1}°");
                }
            }
        }

        Debug.Log($"[jointMapper] 손가락 초기 회전값 캡처 완료: {fingerInitialRotations.Count}개");
    }

    /// <summary>
    /// 초기값 재설정 (보정 기능)
    /// </summary>
    [ContextMenu("Reset Finger Initial Rotations")]
    public void ResetInitialRotations()
    {
        CaptureInitialRotations();
        Debug.Log("[jointMapper] 손가락 초기 회전값 재설정 완료");
    }

    // ========== 데이터 수집 (송신용) ==========

    /// <summary>
    /// Body 관절 데이터 수집
    /// </summary>
    public JointRotation CollectData()
    {
        var data = new JointRotation();
        data.type = "jointRotation";

        string simNo = WS_DB_Client.Instance.GetSimulatorNumber();
        data.simNo = int.TryParse(simNo, out var simNumber) ? simNumber : 99;

        foreach (var map in config.mappings)
        {
            if (!joints.ContainsKey(map.fieldName))
                continue;

            Transform joint = joints[map.fieldName];
            if (joint == null)
                continue;

            if (!fieldCache.TryGetValue(map.fieldName, out FieldInfo field))
                continue;

            Vector3 rotation = joint.localRotation.eulerAngles;

            try
            {
                switch (map.jointType)
                {
                    case JointDataType.Vector3:
                        Vector3S vector3Value = new Vector3S(
                            (short)(rotation.x),
                            (short)(rotation.y),
                            (short)(rotation.z)
                        );
                        field.SetValue(data, vector3Value);
                        break;

                    case JointDataType.Vector2:
                        Vector2S vector2Value = new Vector2S(
                            (short)(rotation.y),
                            (short)(rotation.z)
                        );
                        field.SetValue(data, vector2Value);
                        break;

                    case JointDataType.Short:
                        short shortValue = (short)(rotation.z);
                        field.SetValue(data, shortValue);
                        break;

                    case JointDataType.ShortY:
                        short shortYValue = (short)(rotation.y);
                        field.SetValue(data, shortYValue);
                        break;

                    default:
                        Debug.LogWarning($"[jointMapper] 알 수 없는 JointDataType: {map.jointType}");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[jointMapper] 데이터 설정 실패 {map.fieldName}: {e.Message}");
            }
        }

        return data;
    }

    /// <summary>
    /// Finger 관절 데이터 수집
    /// </summary>
    public FingerRotation CollectFingerData()
    {
        var data = new FingerRotation();
        data.type = "fingerRotation";

        string simNo = WS_DB_Client.Instance.GetSimulatorNumber();
        data.simNo = int.TryParse(simNo, out var simNumber) ? simNumber : 99;

        if (fingerConfig == null || fingerConfig.mappings == null)
            return data;

        foreach (var map in fingerConfig.mappings)
        {
            if (!fingerJoints.ContainsKey(map.fieldName))
                continue;

            Transform joint = fingerJoints[map.fieldName];
            if (joint == null)
                continue;

            if (!fieldCache.TryGetValue(map.fieldName, out FieldInfo field))
                continue;

            Vector3 rotation = joint.localRotation.eulerAngles;

            try
            {
                // 초기값 가져오기
                if (!fingerInitialRotations.TryGetValue(map.fieldName, out float initialRotation))
                {
                    // 초기값이 없으면 현재값을 초기값으로 설정
                    initialRotation = rotation.z;
                    fingerInitialRotations[map.fieldName] = initialRotation;

                    if (showDebugLogs)
                    {
                        Debug.Log($"[jointMapper] {map.fieldName} 초기값 자동 설정: {initialRotation:F1}°");
                    }
                }

                // 델타 계산
                float delta = Mathf.DeltaAngle(initialRotation, rotation.z);

                // 범위 가져오기
                var mapping = fingerConfig.GetMapping(map.fieldName);
                if (mapping == null || !mapping.enableRangeCheck)
                {
                    // 범위 체크 비활성화면 그대로 전송
                    short shortValue = (short)(rotation.z);
                    field.SetValue(data, shortValue);
                    continue;
                }

                // 델타를 범위로 클램핑
                float clampedDelta = Mathf.Clamp(delta, mapping.minRotation, mapping.maxRotation);

                // 클램핑된 델타를 절대값으로 변환 (0-360 범위)
                float clampedAbsolute = initialRotation + clampedDelta;
                clampedAbsolute = Mathf.Repeat(clampedAbsolute, 360f);

                short finalValue = (short)(clampedAbsolute);

                // 범위 체크 및 로그
                if (delta != clampedDelta)
                {
                    // 범위 밖이어서 클램핑됨
                    if (showDebugLogs)
                    {
                        Debug.LogWarning($"[jointMapper] {map.fieldName} 범위 밖 → 클램핑: " +
                                       $"초기={initialRotation:F1}°, 현재={rotation.z:F1}°, " +
                                       $"델타={delta:F1}° → 클램핑={clampedDelta:F1}° → 전송={finalValue}");
                    }
                }
                else
                {
                    // 범위 내 정상 전송
                    if (showDebugLogs)
                    {
                        Debug.Log($"[jointMapper] ✅ {map.fieldName} 전송: " +
                                $"초기={initialRotation:F1}°, 현재={rotation.z:F1}°, 델타={delta:F1}° → {finalValue}");
                    }
                }

                // 값 설정
                field.SetValue(data, finalValue);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[jointMapper] Finger 데이터 설정 실패 {map.fieldName}: {e.Message}");
            }
        }

        return data;
    }

    // ========== 유틸리티 ==========

    /// <summary>
    /// 설정 검증
    /// </summary>
    bool ValidateConfig()
    {
        bool hasValidConfig = false;

        if (config != null && config.ValidateMappings())
        {
            hasValidConfig = true;
        }
        else
        {
            Debug.LogWarning("[jointMapper] JointMappingConfig이 할당되지 않았거나 검증 실패");
        }

        if (fingerConfig != null && fingerConfig.ValidateMappings())
        {
            hasValidConfig = true;
        }
        else
        {
            Debug.LogWarning("[jointMapper] FingerMappingConfig이 할당되지 않았거나 검증 실패");
        }

        if (!hasValidConfig)
        {
            Debug.LogError("[jointMapper] Body 또는 Finger Config 중 하나 이상이 필요합니다!");
        }

        return hasValidConfig;
    }

    /// <summary>
    /// 특정 관절의 Transform 가져오기 (Body 또는 Finger)
    /// </summary>
    public Transform GetJoint(string fieldName)
    {
        if (joints.TryGetValue(fieldName, out Transform joint))
            return joint;

        if (fingerJoints.TryGetValue(fieldName, out Transform fingerJoint))
            return fingerJoint;

        return null;
    }

    /// <summary>
    /// 모든 Body 관절 가져오기
    /// </summary>
    public Dictionary<string, Transform> GetAllJoints()
    {
        return new Dictionary<string, Transform>(joints);
    }

    /// <summary>
    /// 모든 Finger 관절 가져오기
    /// </summary>
    public Dictionary<string, Transform> GetAllFingerJoints()
    {
        return new Dictionary<string, Transform>(fingerJoints);
    }

    /// <summary>
    /// 매핑 초기화
    /// </summary>
    public void ClearMappings()
    {
        joints.Clear();
        fingerJoints.Clear();
        fieldCache.Clear();
    }
}
