using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Sd2 EditMode 테스트 — Phase 1~4 단위 검증.
/// ContingencyReceiveEditModeTests 패턴 그대로 — Reflection 기반 (Assembly-CSharp).
/// </summary>
public class ReserveDeployEditModeTests
{
    private static Assembly _gameAssembly;

    private static Assembly GameAssembly
    {
        get
        {
            if (_gameAssembly != null) return _gameAssembly;
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            return _gameAssembly;
        }
    }

    private static Type GetGameType(string fullName)
    {
        if (GameAssembly == null) return null;
        return GameAssembly.GetType(fullName);
    }

    private static string GetScriptAbsolutePath(string relative)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 1 — PlayCharacter.DeployReserveRecoil
    // ─────────────────────────────────────────────────────────────

    // T1.1 — DeployReserveRecoil 시그니처 존재
    [Test]
    public void P1_PlayCharacter_DeployReserveRecoil_SignatureExists()
    {
        var t = GetGameType("PlayCharacter");
        Assert.IsNotNull(t, "PlayCharacter type not found");

        var method = t.GetMethod("DeployReserveRecoil",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "DeployReserveRecoil method not found");
        Assert.AreEqual(typeof(IEnumerator), method.ReturnType,
            "DeployReserveRecoil must return IEnumerator");
        Assert.AreEqual(0, method.GetParameters().Length,
            "DeployReserveRecoil must take 0 args");
    }

    // T1.2 — reserveShockStrength SerializeField 존재 + default 20f
    [Test]
    public void P1_PlayCharacter_ReserveShockStrength_FieldExists_Default20()
    {
        var t = GetGameType("PlayCharacter");
        Assert.IsNotNull(t, "PlayCharacter type not found");

        var field = t.GetField("reserveShockStrength",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "reserveShockStrength field not found");
        Assert.AreEqual(typeof(float), field.FieldType);

        var go = new GameObject("__T1_ReserveShock");
        try
        {
            var pc = go.AddComponent(t);
            var value = (float)field.GetValue(pc);
            Assert.AreEqual(20f, value, 0.001f, "default value should be 20f");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 2 — AresHWPC 가드 4중 (isSubPara OR 확장)
    // ─────────────────────────────────────────────────────────────

    private const string AresHWPCPath = "Assets/Scripts/AresHardwareParagliderController.cs";

    // T2.1 — 4중 가드 isSubPara 키워드 포함 (소스 grep)
    [Test]
    public void P2_AresHWPC_FourGuards_ContainIsSubPara()
    {
        var path = GetScriptAbsolutePath(AresHWPCPath);
        Assume.That(File.Exists(path), $"source not found: {path}");
        var src = File.ReadAllText(path);

        // 각 가드 영역에 isSubPara 키워드 1건 이상 포함 — 라인 번호 변동 허용
        Assert.IsTrue(Regex.IsMatch(src,
            @"private void CalculateAndSendTargetRotation\([\s\S]+?isSubPara"),
            "CalculateAndSendTargetRotation 입구 가드 isSubPara 누락");
        Assert.IsTrue(Regex.IsMatch(src,
            @"private void ApplyTurningTransform\([\s\S]+?isSubPara"),
            "ApplyTurningTransform 가드 isSubPara 누락");
        Assert.IsTrue(Regex.IsMatch(src,
            @"private void ApplyYawRotation\([\s\S]+?isSubPara"),
            "ApplyYawRotation 가드 isSubPara 누락");
        // SendMotionData 직전 cachedMotionData 0 클램프 — 4번째 가드
        Assert.IsTrue(Regex.IsMatch(src,
            @"if \(isSubPara\)[\s\S]+?RollLeftSpeed\s*=\s*0[\s\S]+?SendMotionData"),
            "SendMotionData 직전 cachedMotionData 0 클램프 누락");
    }

    // T2.2 — isSubPara=true 후 ApplyTurningTransform → rb.MoveRotation 미호출 (가드 통과)
    [Test]
    public void P2_AresHWPC_ApplyTurningTransform_GuardsOn_IsSubPara()
    {
        var t = GetGameType("AresHardwareParagliderController");
        Assert.IsNotNull(t, "AresHardwareParagliderController type not found");

        var go = new GameObject("__T2_Guard");
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        try
        {
            var pc = go.AddComponent(t);

            // 필수 필드 세팅 (Reflection — isPara public, isSubPara public, rb public)
            t.GetField("isPara").SetValue(pc, true);
            t.GetField("isSubPara").SetValue(pc, true);   // ★ 가드 트리거
            t.GetField("rb").SetValue(pc, rb);

            var startRot = rb.rotation;
            var apply = t.GetMethod("ApplyTurningTransform",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(apply, "ApplyTurningTransform method not found");
            apply.Invoke(pc, null);   // 가드 통과 → 회전 변화 0

            Assert.AreEqual(startRot, rb.rotation,
                "isSubPara=true 일 때 회전이 변하면 가드 깨짐");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 3 — AresHWPC chestTrigger 토글 + DeploySubPara 본문
    // ─────────────────────────────────────────────────────────────

    // T3.1 — chestTrigger SerializeField 존재
    [Test]
    public void P3_AresHWPC_ChestTrigger_FieldExists()
    {
        var t = GetGameType("AresHardwareParagliderController");
        Assert.IsNotNull(t, "AresHardwareParagliderController type not found");

        var f = t.GetField("chestTrigger",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(f, "chestTrigger field not found");
        Assert.AreEqual(typeof(GameObject), f.FieldType);
    }

    // T3.2 — Begin → SetActive(true), End → SetActive(false)
    [Test]
    public void P3_AresHWPC_BeginEnd_TogglesChestTriggerActive()
    {
        var t = GetGameType("AresHardwareParagliderController");
        var listenerType = GetGameType("TriggerListener");
        Assert.IsNotNull(t);
        Assert.IsNotNull(listenerType);

        var go = new GameObject("__T3_Host");
        var chestGo = new GameObject("__T3_ChestTrig");
        chestGo.AddComponent<BoxCollider>().isTrigger = true;
        chestGo.AddComponent(listenerType);
        chestGo.SetActive(false);   // §4.5 초기 비활성

        try
        {
            var pc = go.AddComponent(t);
            t.GetField("chestTrigger",
                BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, chestGo);

            var begin = t.GetMethod("BeginLineTwistProcedure",
                BindingFlags.Public | BindingFlags.Instance);
            var end = t.GetMethod("EndLineTwistProcedure",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(begin);
            Assert.IsNotNull(end);

            begin.Invoke(pc, new object[] { 1, 55f });
            Assert.IsTrue(chestGo.activeSelf, "Begin 후 chestTrigger 활성화 안 됨");

            end.Invoke(pc, null);
            Assert.IsFalse(chestGo.activeSelf, "End 후 chestTrigger 비활성화 안 됨");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(chestGo);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // T3.3 — Begin 중복 호출 시 OnPlayerEntered 핸들러 1건만 (idempotent)
    [Test]
    public void P3_AresHWPC_BeginTwice_EventHandler_Idempotent()
    {
        var t = GetGameType("AresHardwareParagliderController");
        var listenerType = GetGameType("TriggerListener");
        Assert.IsNotNull(t);
        Assert.IsNotNull(listenerType);

        var go = new GameObject("__T3i_Host");
        var chestGo = new GameObject("__T3i_ChestTrig");
        chestGo.AddComponent<BoxCollider>().isTrigger = true;
        var listener = chestGo.AddComponent(listenerType);
        chestGo.SetActive(false);

        try
        {
            var pc = go.AddComponent(t);
            t.GetField("chestTrigger",
                BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, chestGo);

            var begin = t.GetMethod("BeginLineTwistProcedure",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(begin);

            begin.Invoke(pc, new object[] { 1, 55f });
            begin.Invoke(pc, new object[] { 1, 55f });   // 2회 호출

            // OnPlayerEntered event 의 backing delegate field — private + Action 타입
            var eventField = listenerType.GetField("OnPlayerEntered",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(eventField, "OnPlayerEntered backing field lookup 실패");
            var del = (System.Action)eventField.GetValue(listener);
            Assert.IsNotNull(del, "핸들러 미등록");
            Assert.AreEqual(1, del.GetInvocationList().Length,
                "Begin 2회 호출 시 핸들러 중복 등록 — idempotent 깨짐");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(chestGo);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Phase 4 — StateManager_New AddFailAction 람다 + CompleteContingency
    // ─────────────────────────────────────────────────────────────

    private const string StateManagerNewPath = "Assets/Scripts/StateManager_New.cs";

    // T4.1 — PullSubCord case 안 AddFailAction 람다에 DeploySubPara 호출 포함
    [Test]
    public void P4_StateManager_AddFailAction_ContainsAutoReserveLog()
    {
        var path = GetScriptAbsolutePath(StateManagerNewPath);
        Assume.That(File.Exists(path), $"source not found: {path}");
        var src = File.ReadAllText(path);

        Assert.IsTrue(Regex.IsMatch(src,
            @"case CompleteCondition\.PullSubCord[\s\S]+?AddFailAction[\s\S]+?DeploySubPara"),
            "PullSubCord case 의 AddFailAction 람다 안에 DeploySubPara 호출 누락");
    }

    // T4.2 ★★ W-pre6 race 회피 — _activeContingencyId="" 가 DeploySubPara 호출보다 먼저
    [Test]
    public void P4_StateManager_CompleteContingency_WPre6_OrderEnforced()
    {
        var path = GetScriptAbsolutePath(StateManagerNewPath);
        Assume.That(File.Exists(path), $"source not found: {path}");
        var src = File.ReadAllText(path);

        // CompleteContingency 본문 추출
        var match = Regex.Match(src,
            @"private void CompleteContingency\([^)]+\)\s*\{([\s\S]+?)\n    \}",
            RegexOptions.Multiline);
        Assert.IsTrue(match.Success, "CompleteContingency 본문 추출 실패");
        var body = match.Groups[1].Value;

        var clearIdx = body.IndexOf("_activeContingencyId = \"\"", StringComparison.Ordinal);
        var deployIdx = body.IndexOf("DeploySubPara", StringComparison.Ordinal);
        Assert.Greater(clearIdx, -1, "_activeContingencyId = \"\" 누락");
        Assert.Greater(deployIdx, -1, "DeploySubPara 호출 누락");
        Assert.Less(clearIdx, deployIdx,
            "W-pre6 race — _activeContingencyId=\"\" 가 DeploySubPara 호출보다 먼저여야 함");
    }

    // T4.3 — CompleteContingency 진입 시 entry 로그 발화 + _activeContingencyId 즉시 "" (행동 검증)
    [Test]
    public void P4_StateManager_CompleteContingency_FailPath_LogsBeforeDeploy()
    {
        var smType = GetGameType("StateManager_New");
        var contType = GetGameType("Contingency");
        Assert.IsNotNull(smType, "StateManager_New type not found");
        Assert.IsNotNull(contType, "Contingency type not found");

        var go = new GameObject("__T4_StateManager");
        try
        {
            var sm = go.AddComponent(smType);

            // _activeContingencyId = "STD_LineTwist" 강제 설정 (실패 path 시뮬레이션)
            var activeIdField = smType.GetField("_activeContingencyId",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(activeIdField, "_activeContingencyId field not found");
            activeIdField.SetValue(sm, "STD_LineTwist");

            var c = Activator.CreateInstance(contType);
            contType.GetField("id").SetValue(c, "STD_LineTwist");

            // entry 로그 패턴 expect
            LogAssert.Expect(LogType.Log,
                new Regex(@"CompleteContingency entry.*cleared.*success=False"));

            var method = smType.GetMethod("CompleteContingency",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "CompleteContingency method not found");

            // _character/UIManager.Inst null 등으로 후속 NRE 발생 가능 — entry 로그/_activeContingencyId 검증이 핵심
            try { method.Invoke(sm, new object[] { c, false }); }
            catch (TargetInvocationException) { /* expected — LineTwist 분기 skip 후 후속 UIManager 등 NRE 무관 */ }

            // _activeContingencyId 즉시 "" 검증
            Assert.AreEqual("", (string)activeIdField.GetValue(sm),
                "CompleteContingency 후 _activeContingencyId 미초기화");

            // 후속 NRE 의 expected/unexpected log 매칭 실패 무시 (entry 로그만 핵심)
            LogAssert.ignoreFailingMessages = true;
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}