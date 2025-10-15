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
    [SerializeField] private List<MappingStatus> mappingStatusList = new List<MappingStatus>();
    
    // 자동 매핑된 Transform들
    private Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    
    // Reflection 캐시 (성능 최적화)
    private System.Type jointRotationType;
    private Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();
    
    // 최근 수신 데이터 (디버그용)
    private JointRotation lastReceivedData;
    private float lastReceiveTime;
    
    [System.Serializable]
    public class MappingStatus
    {
        public string fieldName;
        public bool isFound;
        public string foundObjectName;
        public Vector3 currentRotation;
        public Vector3 targetRotation;
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
            Debug.Log($"[OtherParticipant {participantSimNo}] 자동 매핑 시작 - {allTransforms.Length}개 Transform 검색");
        
        // 매핑 상태 리스트 초기화
        mappingStatusList.Clear();
        joints.Clear();
        fieldCache.Clear();
        
        int successCount = 0;
        
        // 각 매핑에 대해 검색
        foreach (var map in config.mappings)
        {
            bool found = false;
            Transform foundTransform = null;
            
            // 모든 Transform 검색
            foreach (var t in allTransforms)
            {
                string nameLower = t.name.ToLower();
                
                // 패턴 매칭
                foreach (var pattern in map.patterns)
                {
                    if (nameLower.Contains(pattern.ToLower()))
                    {
                        joints[map.fieldName] = t;
                        foundTransform = t;
                        found = true;
                        successCount++;
                        
                        // FieldInfo 캐싱
                        var field = jointRotationType.GetField(map.fieldName);
                        if (field != null)
                            fieldCache[map.fieldName] = field;
                        
                        if (showDebugLogs)
                            Debug.Log($"✅ [Participant {participantSimNo}] 매핑 성공: {map.fieldName} → {t.name}");
                        
                        goto NextMap;
                    }
                }
            }
            
            NextMap:
            
            // 매핑 상태 기록
            mappingStatusList.Add(new MappingStatus
            {
                fieldName = map.fieldName,
                isFound = found,
                foundObjectName = found ? foundTransform.name : "Not Found",
                currentRotation = Vector3.zero,
                targetRotation = Vector3.zero
            });
            
            if (!found && showDebugLogs)
            {
                Debug.LogWarning($"❌ [Participant {participantSimNo}] 매핑 실패: {map.fieldName} - 패턴: {string.Join(", ", map.patterns)}");
            }
        }
        
        Debug.Log($"[OtherParticipant {participantSimNo}] 매핑 완료 - 성공: {successCount}/{config.mappings.Count}");
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
                        
                    default:
                        Debug.LogWarning($"[OtherParticipant {participantSimNo}] 알 수 없는 JointDataType: {map.jointType} for {map.fieldName}");
                        continue;
                }
                
                // 매핑 상태 업데이트 (디버그용)
                var status = GetMappingStatus(map.fieldName);
                if (status != null)
                {
                    status.targetRotation = rotation;
                }
                
                // 회전 적용 (Euler angles로 직접 적용)
                if (useSmoothRotation)
                {
                    // 부드러운 보간은 Update에서 처리
                    // 여기서는 타겟만 설정
                    if (status != null)
                    {
                        status.targetRotation = rotation;
                    }
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
    
    void Update()
    {
        // 부드러운 회전 보간 처리
        if (useSmoothRotation && mappingStatusList != null)
        {
            foreach (var status in mappingStatusList)
            {
                if (!status.isFound) continue;
                
                if (joints.ContainsKey(status.fieldName))
                {
                    Transform joint = joints[status.fieldName];
                    if (joint != null)
                    {
                        // 현재 회전 업데이트
                        status.currentRotation = joint.localEulerAngles;
                        
                        // 타겟을 향해 보간 (Euler angles를 사용한 선형 보간)
                        Vector3 currentEuler = joint.localEulerAngles;
                        Vector3 targetEuler = status.targetRotation;
                        
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
        }
    }
    
    /// <summary>
    /// 설정 검증
    /// </summary>
    bool ValidateConfig()
    {
        if (config == null)
        {
            Debug.LogError($"[OtherParticipant {participantSimNo}] JointMappingConfig이 할당되지 않았습니다!");
            return false;
        }
        
        return config.ValidateMappings();
    }
    
    /// <summary>
    /// 매핑 상태 가져오기
    /// </summary>
    MappingStatus GetMappingStatus(string fieldName)
    {
        foreach (var status in mappingStatusList)
        {
            if (status.fieldName == fieldName)
                return status;
        }
        return null;
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
        
        if (showDebugLogs)
            Debug.Log($"[OtherParticipant {participantSimNo}] 매핑 초기화됨");
    }
    
    /// <summary>
    /// 매핑된 관절 개수 반환
    /// </summary>
    public int GetMappedJointCount()
    {
        return joints.Count;
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
    /// Inspector에서 상태 표시
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && joints.Count > 0)
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
    }
#endif
}