using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 다른 참가자 전용 관절 데이터 수신 컴포넌트
/// 이름 기반 자동 매핑 + Reflection 기반 자동 데이터 적용 (수신 전용)
/// </summary>
public class OtherParticipantAvatar : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("공용 매핑 설정 (ScriptableObject)")]
    public JointMappingConfig config;
    public FingerMappingConfig fingerConfig;
    
    [Tooltip("각도를 short에서 복원할 때 사용할 스케일 팩터")]
    [Range(0f, 1000f)]
    public float scale = 100f;
    
    [Header("참가자 정보")]
    [Tooltip("이 아바타가 표현할 참가자의 시뮬레이터 번호")]
    public int participantSimNo;
    
    [Header("보간 설정")]
    [Tooltip("부드러운 회전 보간 사용 여부")]
    public bool useSmoothRotation = true;
    
    [Tooltip("회전 보간 속도")]
    [Range(1f, 20f)]
    public float smoothSpeed = 10f;
    
    [Header("자동 매핑")]
    [Tooltip("자동 매핑 실행 시점")]
    public bool autoMapOnStart = true;
    
    [Tooltip("검색 시작점 (null이면 자신)")]
    public Transform searchRoot;
    
    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    [Header("낙하산 오브젝트")]
    [Tooltip("메인 낙하산 오브젝트")]
    public GameObject mainParachute;

    [Tooltip("예비 낙하산 오브젝트")]
    public GameObject subParachute;

    // 자동 매핑된 Transform들
    private Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    private Dictionary<string, Transform> fingerJoints = new Dictionary<string, Transform>();

    // 부드러운 회전을 위한 타겟 회전값 저장
    private Dictionary<Transform, Vector3> targetRotations = new Dictionary<Transform, Vector3>();

    // 손가락 초기 z 회전값 저장
    private Dictionary<Transform, float> fingerInitialZRotations = new Dictionary<Transform, float>();

    // Reflection 캐시 (성능 최적화)
    private System.Type jointRotationType;
    private Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

    // 최근 수신 데이터 (디버그용)
    private JointRotation lastReceivedData;
    private float lastReceiveTime;

    private FingerRotation lastFingerReceivedData;
    private float lastFingerReceiveTime;
    
    void Awake()
    {
        // Reflection 타입 캐싱
        jointRotationType = typeof(JointRotation);
        
        // 검색 루트 설정
        if (searchRoot == null)
            searchRoot = transform;
    }
    
    void Start()
    {
        if (autoMapOnStart)
        {
            AutoMap();
        }
    }
    
    /// <summary>
    /// 자동 매핑 실행 - 하위 오브젝트에서 이름 패턴 매칭
    /// Body 관절과 Finger 관절을 모두 자동으로 검색
    /// </summary>
    public void AutoMap()
    {
        if (!ValidateConfig())
            return;

        // 모든 하위 Transform 가져오기
        Transform[] allTransforms = searchRoot.GetComponentsInChildren<Transform>();

        if (showDebugLogs)
            Debug.Log($"[OtherParticipant {participantSimNo}] 자동 매핑 시작 - {allTransforms.Length}개 Transform 검색");

        // 초기화
        joints.Clear();
        fingerJoints.Clear();
        fieldCache.Clear();
        targetRotations.Clear();

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

        if (config?.mappings != null)
            Debug.Log(
                $"[OtherParticipant {participantSimNo}] 매핑 완료 - Body: {bodySuccessCount}/{config?.mappings.Count ?? 0}, Finger: {fingerSuccessCount}/{fingerConfig?.mappings.Count ?? 0}");
    }

    /// <summary>
    /// Body 관절 매핑 수행
    /// </summary>
    /// <param name="mappings">Body 매핑 설정 리스트</param>
    /// <param name="allTransforms">검색할 Transform 배열</param>
    /// <returns>성공한 매핑 개수</returns>
    private int MapBodyJointsFromConfig(
        List<JointMappingConfig.JointMap> mappings,
        Transform[] allTransforms)
    {
        int successCount = 0;

        foreach (var map in mappings)
        {
            // 패턴 매칭 시도
            var result = FindTransformByPatterns(allTransforms, map.patterns);

            if (result.found)
            {
                // Body joints에 매핑 저장
                joints[map.fieldName] = result.transform;
                successCount++;

                // FieldInfo 캐싱 (JointRotation 타입)
                var field = typeof(JointRotation).GetField(map.fieldName);
                if (field != null)
                    fieldCache[map.fieldName] = field;

                // 타겟 회전값 초기화 (부드러운 회전용)
                targetRotations[result.transform] = result.transform.localEulerAngles;

                if (showDebugLogs)
                    Debug.Log($"✅ [Body] 매핑 성공: {map.fieldName} → {result.transform.name}");
            }
            else
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"❌ [Body] 매핑 실패: {map.fieldName} - 패턴: {string.Join(", ", map.patterns)}");
                }
            }
        }

        return successCount;
    }

    /// <summary>
    /// Finger 관절 매핑 수행
    /// </summary>
    /// <param name="mappings">Finger 매핑 설정 리스트</param>
    /// <param name="allTransforms">검색할 Transform 배열</param>
    /// <returns>성공한 매핑 개수</returns>
    private int MapFingerJointsFromConfig(
        List<FingerMappingConfig.FingerMap> mappings,
        Transform[] allTransforms)
    {
        int successCount = 0;

        foreach (var map in mappings)
        {
            // 패턴 매칭 시도
            var result = FindTransformByPatterns(allTransforms, map.patterns);

            if (result.found)
            {
                // Finger joints에 매핑 저장
                fingerJoints[map.fieldName] = result.transform;
                successCount++;

                // FieldInfo 캐싱 (FingerRotation 타입)
                var field = typeof(FingerRotation).GetField(map.fieldName);
                if (field != null)
                    fieldCache[map.fieldName] = field;

                // 타겟 회전값 초기화 (부드러운 회전용)
                targetRotations[result.transform] = result.transform.localEulerAngles;

                // 초기값 캐싱 (fieldName 전달)
                CacheFingerHierarchyInitialRotations(result.transform, map.fieldName);

                if (showDebugLogs)
                    Debug.Log($"✅ [Finger] 매핑 성공: {map.fieldName} → {result.transform.name}");
            }
            else
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"❌ [Finger] 매핑 실패: {map.fieldName} - 패턴: {string.Join(", ", map.patterns)}");
                }
            }
        }

        return successCount;
    }

    /// <summary>
    /// 패턴 리스트로 Transform 찾기 (조기 반환으로 goto 대체)
    /// </summary>
    /// <param name="transforms">검색할 Transform 배열</param>
    /// <param name="patterns">매칭할 패턴 리스트</param>
    /// <returns>(찾았는지 여부, 찾은 Transform)</returns>
    private (bool found, Transform transform) FindTransformByPatterns(
        Transform[] transforms,
        string[] patterns)
    {
        foreach (var t in transforms)
        {
            string nameLower = t.name.ToLower();

            foreach (var pattern in patterns)
            {
                if (nameLower.Contains(pattern.ToLower()))
                {
                    return (true, t);  // 조기 반환으로 goto 대체
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Finger 관절인지 확인 (Bip001 + Finger + L/R 패턴)
    /// </summary>
    private bool IsFingerBone(Transform t)
    {
        string name = t.name;
        return name.StartsWith("Bip001") &&
               name.Contains("Finger") &&
               (name.Contains(" L ") || name.Contains(" R "));
    }

    /// <summary>
    /// Finger 관절 하이어라키의 초기 z 회전값을 재귀적으로 캐싱
    /// </summary>
    /// <param name="fingerRoot">손가락 루트 Transform</param>
    /// <param name="fieldName">필드명 (재귀 제어용)</param>
    private void CacheFingerHierarchyInitialRotations(Transform fingerRoot, string fieldName)
    {
        // 루트 관절 초기값 캐싱
        fingerInitialZRotations[fingerRoot] = fingerRoot.localEulerAngles.z;

        // 하이어라키 재귀가 비활성화되어 있으면 중단
        bool enableRecursion = fingerConfig != null && fingerConfig.IsHierarchyRecursionEnabled(fieldName);

        if (!enableRecursion)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[OtherParticipant {participantSimNo}] {fieldName} 초기값 캐싱 재귀 스킵 - 루트만 캐싱");
            }
            return; // 재귀 중단
        }

        // 재귀 활성화된 손가락만 하위 관절 처리
        for (int i = 0; i < fingerRoot.childCount; i++)
        {
            Transform child = fingerRoot.GetChild(i);
            if (IsFingerBone(child))
                CacheFingerHierarchyInitialRotations(child, fieldName);
        }
    }

    /// <summary>
    /// Finger 관절 전체 하이어라키에 z 회전 델타 적용
    /// </summary>
    /// <param name="fingerRoot">손가락 루트 Transform</param>
    /// <param name="zDelta">적용할 z축 회전 델타</param>
    /// <param name="fieldName">필드명 (재귀 제어용)</param>
    private void ApplyFingerRotationToHierarchy(Transform fingerRoot, float zDelta, string fieldName)
    {
        // 초기값 가져오기
        if (!fingerInitialZRotations.TryGetValue(fingerRoot, out float initialZ))
        {
            initialZ = fingerRoot.localEulerAngles.z;
            fingerInitialZRotations[fingerRoot] = initialZ;
        }

        // 새 회전값 계산
        Vector3 newRotation = new Vector3(
            fingerRoot.localEulerAngles.x,
            fingerRoot.localEulerAngles.y,
            initialZ + zDelta
        );

        // 보간 또는 즉시 적용 (루트 관절)
        if (useSmoothRotation)
            targetRotations[fingerRoot] = newRotation;
        else
            fingerRoot.localEulerAngles = newRotation;

        // 하이어라키 재귀가 비활성화되어 있으면 중단
        bool enableRecursion = fingerConfig != null && fingerConfig.IsHierarchyRecursionEnabled(fieldName);

        if (!enableRecursion)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[OtherParticipant {participantSimNo}] {fieldName} 재귀 스킵 - 루트만 적용");
            }
            return; // 재귀 중단
        }

        // 재귀 활성화된 손가락만 하위 관절 처리
        for (int i = 0; i < fingerRoot.childCount; i++)
        {
            Transform child = fingerRoot.GetChild(i);
            if (IsFingerBone(child))
            {
                ApplyFingerRotationToHierarchy(child, zDelta, fieldName);
            }
        }
    }

    /// <summary>
    /// 관절 데이터 적용 (수신 전용 - Reflection 자동화)
    /// 백그라운드 스레드에서 호출될 수 있으므로 메인 스레드로 디스패치
    /// </summary>
    public void ApplyData(JointRotation data)
    {
        // 자신의 시뮬레이터 번호와 일치하는 데이터만 처리
        if (data.simNo != participantSimNo)
            return;
        
        // 메인 스레드로 실행 위임
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            ApplyDataOnMainThread(data);
        });
    }
    
    /// <summary>
    /// 메인 스레드에서 실제 데이터 적용
    /// </summary>
    private void ApplyDataOnMainThread(JointRotation data)
    {
        lastReceivedData = data;
        lastReceiveTime = Time.time;  // 이제 메인 스레드에서 안전하게 호출
        
        if (config == null || joints.Count == 0)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[OtherParticipant {participantSimNo}] 매핑이 준비되지 않음 - config: {config != null}, joints: {joints.Count}");
            return;
        }
        
        // 각 매핑에 대해 데이터 적용
        foreach (var map in config.mappings)
        {
            if (!joints.ContainsKey(map.fieldName))
                continue;
                
            Transform joint = joints[map.fieldName];
            if (joint == null)
                continue;
                
            // FieldInfo 가져오기 (캐시에서)
            if (!fieldCache.TryGetValue(map.fieldName, out FieldInfo field))
                continue;
            
            Vector3 rotation;
            
            try
            {
                // 타입에 따라 역변환
                switch (map.jointType)
                {
                    case JointDataType.Vector3:
                        // Vector3S에서 변환 (x, y, z 회전)
                        var vector3Value = (Vector3S)field.GetValue(data);
                        rotation = new Vector3(
                            vector3Value.x,
                            vector3Value.y,
                            vector3Value.z
                        );
                        break;
                        
                    case JointDataType.Vector2:
                        // Vector2S에서 변환 (y, z 회전, x는 현재값 유지)
                        var vector2Value = (Vector2S)field.GetValue(data);
                        Vector3 currentEulerV2 = joint.localRotation.eulerAngles;
                        rotation = new Vector3(
                            currentEulerV2.x,  // X축은 현재값 유지
                            vector2Value.y,
                            vector2Value.z
                        );
                        break;
                        
                    case JointDataType.Short:
                        // short에서 변환 (z축만, x,y는 현재값 유지)
                        short shortValue = (short)field.GetValue(data);
                        Vector3 currentEulerShort = joint.localRotation.eulerAngles;
                        rotation = new Vector3(
                            currentEulerShort.x,  // X축 현재값 유지
                            currentEulerShort.y,  // Y축 현재값 유지
                            shortValue
                        );
                        break;

                    case JointDataType.ShortY:
                        // short에서 변환 (y축만, x,z는 현재값 유지)
                        short shortYValue = (short)field.GetValue(data);
                        Vector3 currentEulerShortY = joint.localRotation.eulerAngles;
                        rotation = new Vector3(
                            currentEulerShortY.x,  // X축 현재값 유지
                            shortYValue,           // Y축만 업데이트
                            currentEulerShortY.z   // Z축 현재값 유지
                        );
                        break;

                    default:
                        Debug.LogWarning($"[OtherParticipant {participantSimNo}] 알 수 없는 JointDataType: {map.jointType} for {map.fieldName}");
                        continue;
                }

                // 회전 적용 (Euler angles로 직접 적용)
                if (useSmoothRotation)
                {
                    // 부드러운 보간은 Update에서 처리
                    // 여기서는 타겟만 설정
                    targetRotations[joint] = rotation;
                }
                else
                {
                    // 즉시 적용
                    joint.localEulerAngles = rotation;
                }
            }
            catch (System.Exception e)
            {
                if (showDebugLogs)
                    Debug.LogError($"[OtherParticipant {participantSimNo}] 데이터 적용 실패 {map.fieldName}: {e.Message}");
            }
        }
        
        //if (showDebugLogs)
            //Debug.Log($"[OtherParticipant {participantSimNo}] 데이터 적용 완료");
    }
    
    /// <summary>
    /// 손가락 관절 데이터 적용
    /// </summary>
    /// <param name="data"></param>
    public void ApplyFingerData(FingerRotation data)
    {
        // 자신의 시뮬레이터 번호와 일치하는 데이터만 처리
        if (data.simNo != participantSimNo) return;
        
        // 메인 스레드로 실행 위임
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            ApplyFingerDataOnMainThread(data);
        });
    }

    /// <summary>
    /// 
    /// </summary>
    private void ApplyFingerDataOnMainThread(FingerRotation data)
    {
        lastFingerReceivedData = data;
        lastFingerReceiveTime = Time.time;  // 이제 메인 스레드에서 안전하게 호출
        
        if (fingerConfig == null || fingerJoints.Count == 0)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[OtherParticipant {participantSimNo}] 손가락 매핑이 준비되지 않음 - config: {fingerConfig != null}, joints: {fingerJoints.Count}");
            return;
        }
        
        // 각 매핑에 대해 데이터 적용
        foreach (var map in fingerConfig.mappings)
        {
            if (!fingerJoints.ContainsKey(map.fieldName))
                continue;
                
            Transform joint = fingerJoints[map.fieldName];
            if (joint == null)
                continue;
                
            // FieldInfo 가져오기 (캐시에서)
            if (!fieldCache.TryGetValue(map.fieldName, out FieldInfo field))
                continue;

            try
            {
                short shortValue = (short)field.GetValue(data);
                float zDelta = shortValue;

                // 하이어라키 전체에 z 회전 델타 적용 (fieldName 전달)
                ApplyFingerRotationToHierarchy(joint, zDelta, map.fieldName);
            }
            catch (System.Exception e)
            {
                if (showDebugLogs)
                    Debug.LogError($"[OtherParticipant {participantSimNo}] 손가락 데이터 적용 실패 {map.fieldName}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 낙하산 상태 업데이트
    /// </summary>
    public void UpdateParachuteState(bool isPara, bool isSubPara)
    {
        if (mainParachute != null)
            mainParachute.SetActive(isPara && !isSubPara);

        if (subParachute != null)
            subParachute.SetActive(isSubPara);
    }

    void Update()
    {
        // 부드러운 회전 보간 처리
        if (useSmoothRotation && targetRotations != null)
        {
            // targetRotations의 Transform 목록을 복사하여 반복
            // (Dictionary 반복 중 수정 방지)
            var transforms = new List<Transform>(targetRotations.Keys);

            foreach (var joint in transforms)
            {
                if (joint == null)
                    continue;

                if (!targetRotations.TryGetValue(joint, out Vector3 targetEuler))
                    continue;

                // 타겟을 향해 보간 (Euler angles를 사용한 선형 보간)
                Vector3 currentEuler = joint.localEulerAngles;

                // 각도 차이를 최소화하기 위한 정규화
                for (int i = 0; i < 3; i++)
                {
                    float delta = Mathf.DeltaAngle(currentEuler[i], targetEuler[i]);
                    targetEuler[i] = currentEuler[i] + delta;
                }

                // 선형 보간 적용
                joint.localEulerAngles = Vector3.Lerp(
                    currentEuler,
                    targetEuler,
                    Time.deltaTime * smoothSpeed
                );
            }
        }
    }
    
    /// <summary>
    /// 설정 검증 - Body와 Finger 설정 모두 확인
    /// </summary>
    bool ValidateConfig()
    {
        bool hasValidConfig = false;

        // Body 설정 검증
        if (config != null)
        {
            if (config.ValidateMappings())
            {
                hasValidConfig = true;
            }
            else
            {
                Debug.LogWarning($"[OtherParticipant {participantSimNo}] JointMappingConfig 검증 실패");
            }
        }
        else
        {
            Debug.LogWarning($"[OtherParticipant {participantSimNo}] JointMappingConfig이 할당되지 않았습니다.");
        }

        // Finger 설정 검증
        if (fingerConfig != null)
        {
            if (fingerConfig.ValidateMappings())
            {
                hasValidConfig = true;
            }
            else
            {
                Debug.LogWarning($"[OtherParticipant {participantSimNo}] FingerMappingConfig 검증 실패");
            }
        }
        else
        {
            Debug.LogWarning($"[OtherParticipant {participantSimNo}] FingerMappingConfig이 할당되지 않았습니다.");
        }

        if (!hasValidConfig)
        {
            Debug.LogError($"[OtherParticipant {participantSimNo}] Body 또는 Finger Config 중 하나 이상이 필요합니다!");
        }

        return hasValidConfig;
    }

    /// <summary>
    /// 참가자 시뮬레이터 번호 설정
    /// </summary>
    public void SetParticipantSimNo(int simNo)
    {
        participantSimNo = simNo;
        if (showDebugLogs)
            Debug.Log($"[OtherParticipant] 시뮬레이터 번호 설정: {simNo}");
    }
    
    /// <summary>
    /// 특정 관절의 Transform 가져오기 (Body 또는 Finger)
    /// Body joints를 먼저 검색하고, 없으면 Finger joints 검색
    /// </summary>
    public Transform GetJoint(string fieldName)
    {
        // Body joints 검색
        if (joints.TryGetValue(fieldName, out Transform joint))
            return joint;

        // Finger joints 검색
        if (fingerJoints.TryGetValue(fieldName, out Transform fingerJoint))
            return fingerJoint;

        return null;
    }

    /// <summary>
    /// Finger 관절의 Transform 가져오기
    /// </summary>
    public Transform GetFingerJoint(string fieldName)
    {
        return fingerJoints.TryGetValue(fieldName, out Transform joint) ? joint : null;
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
    /// Body와 Finger 관절을 모두 합쳐서 가져오기
    /// </summary>
    public Dictionary<string, Transform> GetAllJointsIncludingFingers()
    {
        var allJoints = new Dictionary<string, Transform>(joints);

        // Finger joints 추가
        foreach (var kvp in fingerJoints)
        {
            if (!allJoints.ContainsKey(kvp.Key))
            {
                allJoints[kvp.Key] = kvp.Value;
            }
        }

        return allJoints;
    }
    
    /// <summary>
    /// 매핑 초기화 (Body와 Finger 모두)
    /// </summary>
    public void ClearMappings()
    {
        joints.Clear();
        fingerJoints.Clear();
        fieldCache.Clear();
        targetRotations.Clear();
        fingerInitialZRotations.Clear();

        if (showDebugLogs)
            Debug.Log($"[OtherParticipant {participantSimNo}] 매핑 초기화됨 (Body & Finger)");
    }
    
    /// <summary>
    /// 매핑된 관절 개수 반환 (Body + Finger 합계)
    /// </summary>
    public int GetMappedJointCount()
    {
        return joints.Count + fingerJoints.Count;
    }

    /// <summary>
    /// Body 관절 개수 반환
    /// </summary>
    public int GetMappedBodyJointCount()
    {
        return joints.Count;
    }

    /// <summary>
    /// Finger 관절 개수 반환
    /// </summary>
    public int GetMappedFingerJointCount()
    {
        return fingerJoints.Count;
    }
    
    /// <summary>
    /// 마지막 수신 시간 확인 (연결 상태 체크용)
    /// </summary>
    public float GetTimeSinceLastReceive()
    {
        return Time.time - lastReceiveTime;
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Inspector에서 상태 표시 (Body는 청록색, Finger는 노란색)
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        // Body joints 표시 (청록색)
        if (joints.Count > 0)
        {
            Gizmos.color = Color.cyan;
            foreach (var joint in joints.Values)
            {
                if (joint != null)
                {
                    Gizmos.DrawWireSphere(joint.position, 0.02f);
                }
            }
        }

        // Finger joints 표시 (노란색)
        if (fingerJoints.Count > 0)
        {
            Gizmos.color = Color.yellow;
            foreach (var fingerJoint in fingerJoints.Values)
            {
                if (fingerJoint != null)
                {
                    Gizmos.DrawWireSphere(fingerJoint.position, 0.015f);
                }
            }
        }
    }
#endif
}