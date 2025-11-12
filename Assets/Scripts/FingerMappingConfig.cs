using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


/// <summary>
/// 손가락 관절 매핑 설정을 공용으로 관리하는 ScriptableObject
/// 프로젝트 전체에서 재사용 가능한 매핑 테이블
/// </summary>
[CreateAssetMenu(fileName = "FingerMappingConfig", menuName = "Config/FingerMapping", order = 2)]
public class FingerMappingConfig : ScriptableObject
{
    [Serializable]
    public class FingerMap
    {
        public string fieldName;

        public string[] patterns;

        [Header("회전값 범위 설정")]
        [Tooltip("회전값 최소 범위 (degree)")]
        public float minRotation = -180f;

        [Tooltip("회전값 최대 범위 (degree)")]
        public float maxRotation = 180f;

        [Tooltip("범위 체크 활성화")]
        public bool enableRangeCheck = true;

        [Header("하이어라키 재귀 설정")]
        [Tooltip("하위 관절에도 재귀적으로 회전 적용 (false면 루트만 적용)")]
        public bool enableHierarchyRecursion = true;

        public FingerMap(string field, string[] searchPatterns, float min = -180f, float max = 180f, bool enableCheck = true, bool enableRecursion = true)
        {
            fieldName = field;
            patterns = searchPatterns;
            minRotation = min;
            maxRotation = max;
            enableRangeCheck = enableCheck;
            enableHierarchyRecursion = enableRecursion;
        }
    }
    
    [Header("관절 매핑 테이블")]
    [Tooltip("손가락의 매핑 정보를 정의합니다")]
    public List<FingerMap> mappings = new List<FingerMap>();
    
    /// <summary>
    /// 기본 매핑 테이블 초기화
    /// Inspector에서 Reset 버튼을 누르면 실행됨
    /// </summary>
    void Reset()
    {
        mappings = new List<FingerMap>
        {
            // 왼손 손가락 (델타 기반 해부학적 가동 범위)
            // 엄지 (Thumb) - IP 관절: 반대 -30°, 구부림 +80°, 재귀 비활성화
            new FingerMap("fgL0", new[] { "L Finger0" },
                min: -30f, max: 80f, enableCheck: true, enableRecursion: false),

            // 검지 (Index) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgL1", new[] { "L Finger1" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 중지 (Middle) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgL2", new[] { "L Finger2" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 약지 (Ring) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgL3", new[] { "L Finger3" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 소지 (Pinky) - PIP 관절: 반대 -30°, 구부림 +90°, 재귀 활성화
            new FingerMap("fgL4", new[] { "L Finger4" },
                min: -30f, max: 90f, enableCheck: true, enableRecursion: true),

            // 오른손 손가락 (델타 기반 해부학적 가동 범위)
            // 엄지 (Thumb) - IP 관절: 반대 -30°, 구부림 +80°, 재귀 비활성화
            new FingerMap("fgR0", new[] { "R Finger0" },
                min: -30f, max: 80f, enableCheck: true, enableRecursion: false),

            // 검지 (Index) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgR1", new[] { "R Finger1" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 중지 (Middle) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgR2", new[] { "R Finger2" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 약지 (Ring) - PIP 관절: 반대 -30°, 구부림 +100°, 재귀 활성화
            new FingerMap("fgR3", new[] { "R Finger3" },
                min: -30f, max: 100f, enableCheck: true, enableRecursion: true),

            // 소지 (Pinky) - PIP 관절: 반대 -30°, 구부림 +90°, 재귀 활성화
            new FingerMap("fgR4", new[] { "R Finger4" },
                min: -30f, max: 90f, enableCheck: true, enableRecursion: true),
        };
    }
    
    /// <summary>
    /// 특정 필드명에 대한 매핑 정보 가져오기
    /// </summary>
    public FingerMap GetMapping(string fieldName)
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
            Debug.LogError("[FingerMappingConfig] 매핑 테이블이 비어있습니다!");
            return false;
        }

        foreach (var map in mappings)
        {
            if (string.IsNullOrEmpty(map.fieldName))
            {
                Debug.LogError("[FingerMappingConfig] 필드명이 비어있는 매핑이 있습니다!");
                return false;
            }

            if (map.patterns == null || map.patterns.Length == 0)
            {
                Debug.LogError($"[FingerMappingConfig] {map.fieldName}의 검색 패턴이 비어있습니다!");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 회전값이 유효한 범위 내에 있는지 체크 (절대값 기준)
    /// </summary>
    /// <param name="fieldName">필드명</param>
    /// <param name="rotation">체크할 회전값</param>
    /// <returns>범위 내면 true, 범위 밖이면 false</returns>
    public bool IsRotationInRange(string fieldName, float rotation)
    {
        var map = GetMapping(fieldName);
        if (map == null)
        {
            Debug.LogWarning($"[FingerMappingConfig] 매핑을 찾을 수 없음: {fieldName}");
            return false;
        }

        // 범위 체크가 비활성화되어 있으면 항상 true
        if (!map.enableRangeCheck)
            return true;

        bool inRange = rotation >= map.minRotation && rotation <= map.maxRotation;

        if (!inRange && Application.isEditor)
        {
            Debug.LogWarning($"[FingerMappingConfig] {fieldName} 범위 밖: {rotation:F1}° (범위: {map.minRotation}~{map.maxRotation})");
        }

        return inRange;
    }

    /// <summary>
    /// 회전값이 유효한 범위 내에 있는지 체크 (초기값 기준 델타)
    /// </summary>
    /// <param name="fieldName">필드명</param>
    /// <param name="initialRotation">초기 회전값</param>
    /// <param name="currentRotation">현재 회전값</param>
    /// <returns>범위 내면 true, 범위 밖이면 false</returns>
    public bool IsRotationInRange(string fieldName, float initialRotation, float currentRotation)
    {
        var map = GetMapping(fieldName);
        if (map == null)
        {
            Debug.LogWarning($"[FingerMappingConfig] 매핑을 찾을 수 없음: {fieldName}");
            return false;
        }

        // 범위 체크가 비활성화되어 있으면 항상 true
        if (!map.enableRangeCheck)
            return true;

        // 초기값 대비 델타 계산 (Mathf.DeltaAngle은 -180~180 범위로 최단 각도 반환)
        float delta = Mathf.DeltaAngle(initialRotation, currentRotation);

        // 델타 기준 min/max 범위 체크 (비대칭)
        bool inRange = delta >= map.minRotation && delta <= map.maxRotation;

        if (!inRange && Application.isEditor)
        {
            Debug.LogWarning($"[FingerMappingConfig] {fieldName} 범위 밖: " +
                           $"초기={initialRotation:F1}°, 현재={currentRotation:F1}°, " +
                           $"델타={delta:F1}° (범위: {map.minRotation}~{map.maxRotation})");
        }

        return inRange;
    }

    /// <summary>
    /// 하이어라키 재귀가 활성화되어 있는지 확인
    /// </summary>
    /// <param name="fieldName">필드명</param>
    /// <returns>재귀 활성화 시 true</returns>
    public bool IsHierarchyRecursionEnabled(string fieldName)
    {
        var map = GetMapping(fieldName);
        return map != null && map.enableHierarchyRecursion;
    }
}
