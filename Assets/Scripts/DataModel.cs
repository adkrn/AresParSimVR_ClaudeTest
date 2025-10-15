using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 타임라인
/// </summary>
[Serializable]
public class TimeLine
{
    public string jumpType;
    public string timelineID;
    public string name;
    public string nameKey;
    public string description;
    public string autoProceed;
}

/// <summary>
/// 절차
/// </summary>
[Serializable]
public class Procedure
{
    public string jumpType;
    public string id;
    public string parentTimelineId;
    public string stepName;
    public string stepNameKey;
    public string description;
    public string instructionId;
    public string requireLeader;
    public string triggerCondition;
    public CompleteCondition completeCondition;
    public FailCondition failCondition;
    public string duration;
    public string timeLimit;
    public Item item;
    public string autoActiveAltitude;
    public string requiredAltitude;
    public string altitudeTriggerType;
    public string isReaction;
    public string evaluationId;
}

/// <summary>
/// 지시
/// </summary>
[Serializable]
public class Instruction
{
    public string jumpType;
    public string id;
    public string name;
    public string contentTxtKey;
    public MediaType mediaType;
    public string mediaContent;
    public string voiceContent;
    public string displayDuration;
    public string timeLimit;
    public string targetGroup;
    public string hudVisible;
    public string iconName;
}


[Serializable]
public class Scenario
{
    public string id;
    public string name;
    public string jumpType;
    public string aircraftId;
    public string parachuteId;
    public string mapId;
    public string routeId;
    public string weatherId;
    public string dropSystemId;
    public string markerId;
    public string deploymentType;
    public string scheduledDateTime;
}

[Serializable]
public class FogData
{
    public string id;
    public FogDensity type;
}

[Serializable]
public class Evaluation
{
    public string id;
    public string jumpType;
    public string name;
    public string nameKey;
    public string descriptionKey;
    public EvCategory category;
    public string procedureId;
    public string minScore;
    public string maxScore;
    public ScoreRuleType scoreRuleType;
    public string minValueRange;
    public string avgValueRange;
    public string maxValueRange;
}

[Serializable]
public class Route
{
    public string routeId;
    public JumpType jumpType;
    public string mapId;
    public string routeGroup;
    public string routeName;
    public string pointName;
    public float pointX;
    public float pointZ;
    public string isDropPoint;
    public string mapLocationId;
    public int skipAircraftPosition;
    public bool isCompletePoint = false;
}

[Serializable]
public class Contingency
{
    public string id;
    public string jumpType;
    public string timelineId;
    public string procedureId;
    public string stepName;
    public string stepNameKey;
    public string description;
    public string leftCtrLine;
    public string rightCtrLine;
    public string dropSpeed;
    public string action;
    
}

/// <summary>
/// 절차 진행 완료를 위한 조건
/// </summary>
public enum TriggerCondition
{
    None,
    Time,
    Alt,
    True
}

/// <summary>
/// 절차 진행 완료 조건
/// </summary>
public enum CompleteCondition
{
    None,
    Time,
    Animation,
    Point,
    Item,
    SitDown,
    Stand,
    SceneLoading,
    Fall,
    PullCord,
    Landing
}

/// <summary>
/// 절차 실패 조건
/// </summary>
public enum FailCondition
{
    None,
    Time,
    Altitude
}

/// <summary>
/// 아이템 타입
/// </summary>
public enum Item
{
    Altimeter,
    Helmet,
    Hook,
    Mask
}

/// <summary>
/// UI에 표시할 미디어 타입
/// </summary>
public enum MediaType
{
    Prefab,
    Text
}

/// <summary>
/// 평가에서 쓰이는 카테고리 타입
/// </summary>
public enum EvCategory
{
    Procedure,
    Value,
    Incident,
    LandingType,
    Manual
}

/// <summary>
/// 모든 평가의 이름
/// </summary>
public enum EvName
{
    FallTime,
    TotalDistance,
    AltimeterOn,
    HelmetOn,
    OxyMask,
    SitDownComplete,
    StandUpComplete,
    HookUpComplete,
    GoJumpComplete,
    DeployAltitude,
    DeployTargetDistance,
    EventComplete,
    LandingType,
    TargetDistance,
    FlareComplete,
    LandingSpeed
}

/// <summary>
/// 시뮬레이터 정보
/// </summary>
[Serializable]
public class SimInfo
{
    public string simNo;
    public string clientId;
}

