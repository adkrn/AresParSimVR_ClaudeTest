using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static class EnumUtils
{
    private static readonly Dictionary<string, Type> _cache = new();

    /// <summary>
    /// <paramref name="enumName"/>에 해당하는 열거형의 <see cref="Type"/>을 반환합니다.
    /// 대소문자를 구분하지 않고 검색합니다.  
    /// </summary>
    /// <param name="enumName">찾을 열거형 이름</param>
    /// <returns>찾은 열거형의 <see cref="Type"/>; 없으면 <c>null</c></returns>
    public static Type GetEnumType(string enumName)
    {
        if (_cache.TryGetValue(enumName, out var t))
            return t;

        // 현재 AppDomain에 로드된 모든 어셈블리를 순회하며
        //    • 열거형인지(IsEnum)  
        //    • 이름이 enumName과 대소문자 무시하고 같은지
        //    조건을 만족하는 첫 번째 타입을 찾는다.
        t = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(tp => tp.IsEnum &&
                                  tp.Name.Equals(enumName,
                                      StringComparison.OrdinalIgnoreCase));
        
        _cache[enumName] = t;
        
        return t;
    }
    
    /// <summary>
    /// text가 TEnum의 멤버 이름과 일치하면 true, enum 값을 out 파라미터에 담아 반환
    /// </summary>
    public static bool TryGetEnumValue<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum            // enum 제약
    {
        // ignoreCase: true  → 대·소문자 무시
        return Enum.TryParse(text, ignoreCase: true, out value);
    }
}

public enum FadeDir
{
    In,
    Out
}

public enum Standard
{
    Briefing,
    GearUp,
    Boarding,
    StandBy,
    ExitJump,
    FreeFall,
    InAir,
    JumpComplete
}

public enum Haho
{
    Briefing,
    GearUp,
    Boarding,
    StandBy,
    ExitJump,
    FreeFall,
    InAir,
    JumpComplete
}

public enum Halo
{
    Briefing,
    GearUp,
    Boarding,
    StandBy,
    ExitJump,
    FreeFall,
    InAir,
    JumpComplete
}
