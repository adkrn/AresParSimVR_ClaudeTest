
/***************************************************************
 *  ProcedureActionHub.cs  ―  “stepName ⇄ 메서드” 자동 매핑 허브
 * -------------------------------------------------------------
 *  • StateManager 는 절차 진입 시
 *        actionHub.TryInvoke(this, stepName, skipBatch)
 *    단 한 줄로 핸들러를 호출합니다.
 *
 *  • 매핑 규칙
 *      ① [ProcedureHandler("key")] Attribute가 붙은 메서드만 수집
 *      ② 매개변수 X, void 리턴만 대상
 *      ③ key 는 대/소문자 무시
 *
 *  • 실행 분기
 *      skipBatch == false  →   일반 절차 실행용 메서드(기본)
 *      skipBatch == true   →   스킵-배치 전용 메서드만 실행
 *                              (Attribute 에 skipBatchOnly=true)
 *
 *  • 장점
 *      – 새 절차를 데이터에 추가 → 동일 이름(또는 Attribute) 메서드만
 *        작성하면 로직 자동 연결
 *      – TL 스킵 / 타임아웃 실패 등에서도 “정리용 핸들러”를 쉽게 태깅
 ****************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
///  ProcedureActionHub  ─ Attribute 기반 자동 매핑 (씬 전체 + 비활성 포함 고정)
///  [ProcedureHandler("key", isCoroutine?)] Attribute 가 붙은 메서드만 수집.
///  런타임 호출 O(1) 델리게이트.
/// </summary>
public class ProcedureActionHub : MonoBehaviour
{
    /// <summary>
    ///  Attribute 파싱 결과를 담아 두는 구조체
    /// </summary>
    private struct ProcEntry
    {
        public Delegate del;
        public bool     isCo;
        // 절차 수행 결과 매서드 여부
        // (스킵하거나 수행 실패시 다음 절차 진행을 위해 수행 결과 매서드는 실행되야함)
        public ExecMode mode;
    }
    
    /// <summary>
    /// stepKey → 매핑된 메서드 리스트
    /// </summary>
    private readonly Dictionary<string, List<ProcEntry>> _map = new();
    
    private void Awake()
    {
        // 씬의 모든 MonoBehaviour(비활성 포함) 가져오기
        var targets = SceneManager.GetActiveScene()
                                  .GetRootGameObjects()
                                  .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true));

        // 2. Attribute 가 달린 메서드만 매핑
        foreach (var beh in targets)
        {
            if (beh == null) continue;
            
            var methods = beh.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo mi in methods)
            {
                // Attribute 없는 메서드는 건너뜀
                var attrs = mi.GetCustomAttributes<ProcedureHandlerAttribute>(true).ToArray();
                if (attrs.Length == 0) continue;
                if (mi.GetParameters().Length != 0)
                {
                    Debug.LogWarning($"[ActionHub] {beh.GetType().Name}.{mi.Name}() " + "→ 매개변수/리턴이 있어 스킵");
                    continue;
                }

                foreach (var a in attrs)
                    AddMapping(a.Key, beh, mi, a);
            }
        }

        Debug.Log($"[ProcedureActionHub] Cached {_map.Sum(kv => kv.Value.Count)} actions " +
                  $"for {_map.Count} keys  |  Scope = WholeScene");
    }

    // 매핑 등록
    private void AddMapping(string rawKey, MonoBehaviour beh, MethodInfo mi, ProcedureHandlerAttribute a)
    {
        string key = rawKey.ToLowerInvariant();
        if (!_map.TryGetValue(key, out var list))
            _map[key] = list = new List<ProcEntry>();

        Delegate d = a.IsCo
            ? Delegate.CreateDelegate(typeof(Func<IEnumerator>), beh, mi)
            : Delegate.CreateDelegate(typeof(Action),          beh, mi);

        list.Add(new ProcEntry {
            del      = d,
            isCo     = a.IsCo,
            mode = a.Mode
        });

        if (Debug.isDebugBuild)
            Debug.Log($"[ActionHub] {(a.IsCo ? "[Co]" : "   ")} map \"{key}\" ← {beh.GetType().Name}.{mi.Name}()");
    }

    /* ──────────────────────────────────────────────
     *  TryInvoke : StateManager 가 호출
     * ──────────────────────────────────────────────
     *  • runner     : 코루틴 실행 주체
     *  • stepKey    : Procedure.stepName
     *  • skipBatch  : TL Skip / TimeOut 실패 등 “자동 완료” 상황
     *                → true 일 때 forSkip==true 인 핸들러만 실행
     * ────────────────────────────────────────────── */
    public bool TryInvoke(MonoBehaviour runner, string stepName, bool skipBatch = false, bool isFail = false)
    {
        if (string.IsNullOrEmpty(stepName)) return false;
        if (!_map.TryGetValue(stepName.ToLowerInvariant(), out var list))
        {
            Debug.LogWarning($"[ActionHub] No mapped action for <{stepName}>");
            return false;
        }

        foreach (var e in list)
        {
            if ( (skipBatch && e.mode == ExecMode.Normal) || (isFail && e.mode == ExecMode.Always) ||
                 (!skipBatch && e.mode == ExecMode.SkipOnly) || (!isFail && e.mode == ExecMode.Fail))
                continue; 
            
            if (e.isCo)
                runner.StartCoroutine((System.Collections.IEnumerator)e.del.DynamicInvoke());
            else
                e.del.DynamicInvoke();
        }
        return true;
    }
}

/* ──────────────────────────────────────────────
 *  ProcedureHandler Attribute 정의
 * ────────────────────────────────────────────── */
public enum ExecMode { Normal, SkipOnly, Always, Fail }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class ProcedureHandlerAttribute : Attribute
{
    public string   Key       { get; }
    public ExecMode Mode      { get; }
    public bool     IsCo      { get; }

    public ProcedureHandlerAttribute(
        string   key,
        ExecMode mode    = ExecMode.Normal,
        bool     isCo    = false)
    {
        Key  = key.ToLowerInvariant();
        Mode = mode;
        IsCo = isCo;
    }
}
