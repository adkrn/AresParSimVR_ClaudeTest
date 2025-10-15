using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 관절 매핑 설정을 공용으로 관리하는 ScriptableObject
/// 프로젝트 전체에서 재사용 가능한 매핑 테이블
/// </summary>
[CreateAssetMenu(fileName = "JointMappingConfig", menuName = "Config/JointMapping", order = 1)]
public class JointMappingConfig : ScriptableObject
{
    [System.Serializable]
    public class JointMap
    {
        [Tooltip("JointRotation 구조체의 필드명")]
        public string fieldName;
        
        [Tooltip("GameObject 이름에서 검색할 패턴들 (대소문자 구분 안함)")]
        public string[] patterns;
        
        [Tooltip("데이터 타입 : Vector3 (xyz 회전), Vector2(yz 회전) Short (z축만)")]
        public JointDataType jointType;
        
        public JointMap(string field, string[] searchPatterns, JointDataType type)
        {
            fieldName = field;
            patterns = searchPatterns;
            jointType = type;
        }
    }
    
    [Header("관절 매핑 테이블")]
    [Tooltip("각 관절의 매핑 정보를 정의합니다")]
    public List<JointMap> mappings = new List<JointMap>();
    
    /// <summary>
    /// 기본 매핑 테이블 초기화
    /// Inspector에서 Reset 버튼을 누르면 실행됨
    /// </summary>
    void Reset()
    {
        mappings = new List<JointMap>
        {
            // 다리 관절
            new JointMap("thighL", new[] { "thigh_l", "thighl", "leftthigh", "l_thigh", "leg_upper_l", "leftupperleg" }, JointDataType.Vector3),
            new JointMap("calfL", new[] { "calf_l", "calfl", "leftcalf", "l_calf", "leg_lower_l", "leftlowerleg", "shin_l" }, JointDataType.Short),
            new JointMap("thighR", new[] { "thigh_r", "thighr", "rightthigh", "r_thigh", "leg_upper_r", "rightupperleg" }, JointDataType.Vector3),
            new JointMap("calfR", new[] { "calf_r", "calfr", "rightcalf", "r_calf", "leg_lower_r", "rightlowerleg", "shin_r" }, JointDataType.Short),
            
            // 몸통 관절
            new JointMap("spine", new[] { "spine", "spine1", "spine_01", "back", "torso_lower", "lowerback" }, JointDataType.Vector3),
            new JointMap("chest", new[] { "chest", "spine2", "spine_02", "torso_middle", "middleback" }, JointDataType.Vector3),
            new JointMap("upperChest", new[] { "upperchest", "upper_chest", "spine3", "spine_03", "torso_upper", "upperback" }, JointDataType.Vector3),
            
            // 팔 관절 - 왼쪽
            new JointMap("clavicleL", new[] { "clavicle_l", "claviclel", "leftclavicle", "l_shoulder", "shoulder_l", "leftshoulder" }, JointDataType.Vector2),
            new JointMap("upperArmL", new[] { "upperarm_l", "arm_upper_l", "leftupperarm", "l_arm", "l_upperarm", "leftarm" }, JointDataType.Vector3),
            new JointMap("forearmL", new[] { "forearm_l", "arm_lower_l", "leftforearm", "l_forearm", "leftlowerarm", "elbow_l" }, JointDataType.Short),
            
            // 팔 관절 - 오른쪽
            new JointMap("clavicleR", new[] { "clavicle_r", "clavicler", "rightclavicle", "r_shoulder", "shoulder_r", "rightshoulder" }, JointDataType.Vector2),
            new JointMap("upperArmR", new[] { "upperarm_r", "arm_upper_r", "rightupperarm", "r_arm", "r_upperarm", "rightarm" }, JointDataType.Vector3),
            new JointMap("forearmR", new[] { "forearm_r", "arm_lower_r", "rightforearm", "r_forearm", "rightlowerarm", "elbow_r" }, JointDataType.Short),
            
            // 머리 관절
            new JointMap("neck", new[] { "neck", "neck1", "neck_01", "neck_lower", "necklower" }, JointDataType.Vector3),
            new JointMap("head", new[] { "head", "head1", "head_01", "skull", "headtop" }, JointDataType.Vector3)
        };
    }
    
    /// <summary>
    /// 특정 필드명에 대한 매핑 정보 가져오기
    /// </summary>
    public JointMap GetMapping(string fieldName)
    {
        return mappings.Find(m => m.fieldName == fieldName);
    }
    
    /// <summary>
    /// 매핑 테이블이 유효한지 검증
    /// </summary>
    public bool ValidateMappings()
    {
        if (mappings == null || mappings.Count == 0)
        {
            Debug.LogError("[JointMappingConfig] 매핑 테이블이 비어있습니다!");
            return false;
        }
        
        foreach (var map in mappings)
        {
            if (string.IsNullOrEmpty(map.fieldName))
            {
                Debug.LogError("[JointMappingConfig] 필드명이 비어있는 매핑이 있습니다!");
                return false;
            }
            
            if (map.patterns == null || map.patterns.Length == 0)
            {
                Debug.LogError($"[JointMappingConfig] {map.fieldName}의 검색 패턴이 비어있습니다!");
                return false;
            }
        }
        
        return true;
    }
}

public enum JointDataType
{
    Vector3,
    Vector2,
    Short
}