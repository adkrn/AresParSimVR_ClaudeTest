using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Player 전용 관절 데이터 송신 컴포넌트
/// 이름 기반 자동 매핑 + Reflection 기반 자동 데이터 수집 (송신 전용)
/// </summary>
public class jointMapper : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("공용 매핑 설정 (ScriptableObject)")]
    public JointMappingConfig config;
    
    [Tooltip("자동 매핑 실행 시점")]
    public bool autoMapOnStart = true;
    
    [Header("검색 설정")]
    [Tooltip("검색 시작점 (null이면 자신)")]
    public Transform searchRoot;

    [Header("디버그")] 
    [SerializeField] private bool isDebugMode = false;
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private List<MappingStatus> mappingStatusList = new List<MappingStatus>();
    
    // 자동 매핑된 Transform들
    private Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    
    // Reflection 캐시 (성능 최적화)
    private System.Type jointRotationType;
    private Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();
    
    [System.Serializable]
    public class MappingStatus
    {
        public string fieldName;
        public bool isFound;
        public string foundObjectName;
        public Vector3 currentRotation;
    }
    
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
    /// </summary>
    public void AutoMap()
    {
        if (!ValidateConfig())
            return;
            
        // 모든 하위 Transform 가져오기
        Transform[] allTransforms = searchRoot.GetComponentsInChildren<Transform>();
        
        if (showDebugLogs)
            Debug.Log($"[jointMapper] 자동 매핑 시작 - {allTransforms.Length}개 Transform 검색");
        
        // 매핑 상태 리스트 초기화
        mappingStatusList.Clear();
        joints.Clear();
        fieldCache.Clear();
        
        // 각 매핑에 대해 검색
        foreach (var map in config.mappings)
        {
            // 매핑 시도 (별도 메서드로 분리)
            var result = FindMappingTransform(map, allTransforms);
            
            // 매핑 성공 시 처리
            if (result.found)
            {
                joints[map.fieldName] = result.transform;
                
                // FieldInfo 캐싱
                var field = jointRotationType.GetField(map.fieldName);
                if (field != null)
                    fieldCache[map.fieldName] = field;
                
                if (showDebugLogs)
                    Debug.Log($"✅ [Player] 매핑 성공: {map.fieldName} → {result.transform.name}");
            }
            else if (showDebugLogs)
            {
                Debug.LogWarning($"❌ [Player] 매핑 실패: {map.fieldName} - 패턴: {string.Join(", ", map.patterns)}");
            }
            
            // 매핑 상태 기록
            mappingStatusList.Add(new MappingStatus
            {
                fieldName = map.fieldName,
                isFound = result.found,
                foundObjectName = result.found ? result.transform.name : "Not Found",
                currentRotation = Vector3.zero
            });
        }
        
        Debug.Log($"[jointMapper] 매핑 완료 - 성공: {joints.Count}/{config.mappings.Count}");
    }
    
    /// <summary>
    /// 특정 매핑에 대한 Transform 검색
    /// </summary>
    private (bool found, Transform transform) FindMappingTransform(JointMappingConfig.JointMap map, Transform[] allTransforms)
    {
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();
            
            // 패턴 매칭
            foreach (var pattern in map.patterns)
            {
                if (nameLower.Contains(pattern.ToLower()))
                {
                    return (true, t);  // 찾으면 즉시 반환
                }
            }
        }
        
        return (false, null);  // 못 찾은 경우
    }
    
    
    
    /// <summary>
    /// 관절 데이터 수집 (Reflection 자동화)
    /// </summary>
    public JointRotation CollectData()
    {
        var data = new JointRotation();
        data.type = "jointRotation";
        
        string simNo = WS_DB_Client.Instance.GetSimulatorNumber();
        data.simNo = int.TryParse(simNo, out var simNumber) ? simNumber : 99; // 기본값
        
        // if (showDebugLogs)
        // {
        //     Debug.Log($"[jointMapper] ========== 데이터 수집 시작 (SimNo: {data.simNo}) ==========");
        //     Debug.Log($"[jointMapper] 총 매핑 수: {config.mappings.Count}, 활성 관절 수: {joints.Count}");
        // }

        // int processedCount = 0;
        // int successCount = 0;
        
        // 각 매핑에 대해 데이터 수집
        foreach (var map in config.mappings)
        {
            //processedCount++;
            
            if (!joints.ContainsKey(map.fieldName))
            {
                // if (showDebugLogs)
                //     Debug.LogWarning($"[jointMapper] [{processedCount}/{config.mappings.Count}] 매핑되지 않은 필드: {map.fieldName}");
                continue;
            }
            
            Transform joint = joints[map.fieldName];
            if (joint == null)
            {
                // if (showDebugLogs)
                //     Debug.LogWarning($"[jointMapper] [{processedCount}/{config.mappings.Count}] Transform이 null: {map.fieldName}");
                continue;
            }
                
            // FieldInfo 가져오기 (캐시에서)
            if (!fieldCache.TryGetValue(map.fieldName, out FieldInfo field))
            {
                // if (showDebugLogs)
                //     Debug.LogWarning($"[jointMapper] [{processedCount}/{config.mappings.Count}] FieldInfo 캐시 없음: {map.fieldName}");
                continue;
            }
            
            // 로컬 회전값 가져오기 (정규화 없이 그대로 사용)
            Vector3 rotation = joint.localRotation.eulerAngles;
            
            // if (showDebugLogs)
            //     Debug.Log($"[jointMapper] [{processedCount}/{config.mappings.Count}] {map.fieldName}: " +
            //         $"회전값({rotation.x:F1}, {rotation.y:F1}, {rotation.z:F1}) | " +
            //         $"타입: {map.jointType} | Transform: {joint.name}");
            
            try
            {
                // 타입에 따라 변환 및 할당
                switch (map.jointType)
                {
                    case JointDataType.Vector3:
                        // Vector3S 필드 (x, y, z 회전)
                        Vector3S vector3Value = new Vector3S(
                            (short)(rotation.x),
                            (short)(rotation.y),
                            (short)(rotation.z)
                        );
                        field.SetValue(data, vector3Value);
                        
                        // if (showDebugLogs)
                        //     Debug.Log($"[jointMapper] ✅ {map.fieldName} Vector3S 설정: " +
                        //         $"X={vector3Value.x}, Y={vector3Value.y}, Z={vector3Value.z}");
                        //successCount++;
                        break;
                        
                    case JointDataType.Vector2:
                        // Vector2S 필드 (y, z 회전)
                        Vector2S vector2Value = new Vector2S(
                            (short)(rotation.y),
                            (short)(rotation.z)
                        );
                        field.SetValue(data, vector2Value);
                        
                        // if (showDebugLogs)
                        //     Debug.Log($"[jointMapper] ✅ {map.fieldName} Vector2S 설정: " +
                        //         $"Y={vector2Value.y}, Z={vector2Value.z}");
                        //successCount++;
                        break;
                        
                    case JointDataType.Short:
                        // short 필드 (z축만)
                        short shortValue = (short)(rotation.z);
                        field.SetValue(data, shortValue);
                        
                        // if (showDebugLogs)
                        //     Debug.Log($"[jointMapper] ✅ {map.fieldName} Short 설정: Z={shortValue}");
                        //successCount++;
                        break;
                        
                    default:
                        Debug.LogWarning($"[jointMapper] ❌ 알 수 없는 JointDataType: {map.jointType} for {map.fieldName}");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[jointMapper] ❌ 데이터 설정 실패 {map.fieldName}: {e.Message}");
                Debug.LogError($"[jointMapper] Field Type: {field.FieldType}, Trying to set: {map.jointType}");
            }
        }
        
        // if (showDebugLogs)
        // {
        //     Debug.Log($"[jointMapper] ========== 데이터 수집 완료 ==========");
        //     Debug.Log($"[jointMapper] 성공: {successCount}/{config.mappings.Count}, 실패: {config.mappings.Count - successCount}");
        //     
        //     // 최종 데이터 검증 로그
        //     try
        //     {
        //         Debug.Log($"[jointMapper] 최종 데이터 검증:");
        //         Debug.Log($"  - SimNo: {data.simNo}");
        //         Debug.Log($"  - Type: {data.type}");
        //         
        //         // 몇 가지 주요 필드 값 확인
        //         if (data.thighL != null)
        //             Debug.Log($"  - thighL: ({data.thighL.x}, {data.thighL.y}, {data.thighL.z})");
        //         if (data.thighR != null)
        //             Debug.Log($"  - thighR: ({data.thighR.x}, {data.thighR.y}, {data.thighR.z})");
        //         Debug.Log($"  - calfL: {data.calfL}");
        //         Debug.Log($"  - calfR: {data.calfR}");
        //         if (data.spine != null)
        //             Debug.Log($"  - spine: ({data.spine.x}, {data.spine.y}, {data.spine.z})");
        //         if (data.head != null)
        //             Debug.Log($"  - head: ({data.head.x}, {data.head.y}, {data.head.z})");
        //         Debug.Log($"[jointMapper] ====================================");
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"[jointMapper] 데이터 검증 중 오류: {e.Message}");
        //     }
        // }
        
        return data;
    }
    
    
    // 정규화 함수 제거 - 값을 그대로 전송하기 위해 사용하지 않음
    // /// <summary>
    // /// 각도 정규화 (-180 ~ 180)
    // /// </summary>
    // Vector3 NormalizeAngles(Vector3 angles)
    // {
    //     for (int i = 0; i < 3; i++)
    //     {
    //         while (angles[i] > 180f) angles[i] -= 360f;
    //         while (angles[i] < -180f) angles[i] += 360f;
    //     }
    //     return angles;
    // }
    
    /// <summary>
    /// 설정 검증
    /// </summary>
    bool ValidateConfig()
    {
        if (config == null)
        {
            Debug.LogError("[jointMapper] JointMappingConfig이 할당되지 않았습니다!");
            return false;
        }
        
        return config.ValidateMappings();
    }
    
    /// <summary>
    /// 특정 관절의 Transform 가져오기
    /// </summary>
    public Transform GetJoint(string fieldName)
    {
        return joints.TryGetValue(fieldName, out Transform joint) ? joint : null;
    }
    
    /// <summary>
    /// 매핑 상태 가져오기
    /// </summary>
    public Dictionary<string, Transform> GetAllJoints()
    {
        return new Dictionary<string, Transform>(joints);
    }
    
    /// <summary>
    /// 매핑 초기화
    /// </summary>
    public void ClearMappings()
    {
        joints.Clear();
        fieldCache.Clear();
        mappingStatusList.Clear();
    }
    
    // Editor에서 상태 업데이트 (디버그용)
    #if UNITY_EDITOR
    void Update()
    {
        if (Application.isPlaying && mappingStatusList != null)
        {
            foreach (var status in mappingStatusList)
            {
                if (joints.ContainsKey(status.fieldName))
                {
                    Transform joint = joints[status.fieldName];
                    if (joint != null)
                    {
                        status.currentRotation = joint.localRotation.eulerAngles;
                    }
                }
            }
        }
    }
    #endif
}