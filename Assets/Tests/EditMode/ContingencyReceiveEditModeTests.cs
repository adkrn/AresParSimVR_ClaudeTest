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
/// Phase 1 EditMode 테스트.
/// Assets/Scripts/ 에 main asmdef 가 없어 Assembly-CSharp 직접 참조 불가 → reflection 기반.
/// </summary>
public class ContingencyReceiveEditModeTests
{
    private const string CONTINGENCY_CSV_RELATIVE = "Assets/StreamingAssets/Csvs/CD_Contingency.csv";
    private const int EXPECTED_ROW_COUNT = 30;
    private const string KNOWN_ID = "STD_CableBreak";

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

    private static string GetCsvAbsolutePath()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, CONTINGENCY_CSV_RELATIVE));
    }

    private static List<string[]> ReadContingencyRows(out string[] header)
    {
        var path = GetCsvAbsolutePath();
        Assume.That(File.Exists(path), $"CSV not found: {path}");

        var raw = File.ReadAllText(path);
        var lineSplit = new Regex(@"\r\n|\n\r|\n|\r");
        var fieldSplit = new Regex(@",(?=(?:[^""]*""[^""]*"")*(?![^""]*""))");

        var lines = lineSplit.Split(raw).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        header = fieldSplit.Split(lines[0]).Select(s => s.Trim('"')).ToArray();

        var rows = new List<string[]>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = fieldSplit.Split(lines[i]).Select(s => s.Trim('"')).ToArray();
            rows.Add(fields);
        }
        return rows;
    }

    private static int IndexOfHeader(string[] header, string name)
    {
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] == name) return i;
        }
        return -1;
    }

    // -------------------------------------------------------------------
    // 1. Contingency CSV — 30 rows parse successfully
    // -------------------------------------------------------------------
    [Test]
    public void Contingency_CSV_30Rows_AllParseSuccessfully()
    {
        var rows = ReadContingencyRows(out var header);
        Assert.IsNotNull(rows, "rows null");
        Assert.IsNotNull(header, "header null");
        Assert.AreEqual(EXPECTED_ROW_COUNT, rows.Count, "Expected 30 contingency rows");

        var idIdx = IndexOfHeader(header, "id");
        Assert.GreaterOrEqual(idIdx, 0, "header missing 'id'");
        foreach (var r in rows)
        {
            Assert.IsTrue(r.Length >= header.Length, "row column count < header length");
            Assert.IsFalse(string.IsNullOrEmpty(r[idIdx]), "row has empty id");
        }
    }

    // -------------------------------------------------------------------
    // 2. Contingency CSV — every row's completeCondition == SubParaOn
    // -------------------------------------------------------------------
    [Test]
    public void Contingency_CSV_AllRows_CompleteCondition_Equals_SubParaOn()
    {
        var rows = ReadContingencyRows(out var header);
        var idx = IndexOfHeader(header, "completeCondition");
        Assert.GreaterOrEqual(idx, 0, "header missing 'completeCondition'");

        foreach (var r in rows)
        {
            Assert.AreEqual("SubParaOn", r[idx], $"row id={r[0]} has unexpected completeCondition='{r[idx]}'");
        }
    }

    // -------------------------------------------------------------------
    // 3. Contingency CSV — every row's duration parses to int 10
    // -------------------------------------------------------------------
    [Test]
    public void Contingency_CSV_AllRows_Duration_ParseToInt_Equals_10()
    {
        var rows = ReadContingencyRows(out var header);
        var idx = IndexOfHeader(header, "duration");
        Assert.GreaterOrEqual(idx, 0, "header missing 'duration'");

        foreach (var r in rows)
        {
            Assert.IsTrue(int.TryParse(r[idx], out var v), $"row id={r[0]} duration='{r[idx]}' not int");
            Assert.AreEqual(10, v, $"row id={r[0]} duration={v} != 10");
        }
    }

    // -------------------------------------------------------------------
    // 4. DataManager.GetContingency — known id returns instance
    // -------------------------------------------------------------------
    [Test]
    public void DataManager_GetContingency_KnownId_ReturnsInstance()
    {
        var dm = GetDataManagerInst();
        var contingencyType = GetGameType("Contingency");
        Assert.IsNotNull(contingencyType, "Contingency type not found");

        var list = BuildContingencyListWithId(contingencyType, KNOWN_ID);
        SetContingencysField(dm, list);

        var method = dm.GetType().GetMethod("GetContingency", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "GetContingency method not found");

        var result = method.Invoke(dm, new object[] { KNOWN_ID });
        Assert.IsNotNull(result, "Expected non-null Contingency for known id");

        var idField = contingencyType.GetField("id");
        Assert.AreEqual(KNOWN_ID, (string)idField.GetValue(result));
    }

    // -------------------------------------------------------------------
    // 5. DataManager.GetContingency — unknown id returns null
    // -------------------------------------------------------------------
    [Test]
    public void DataManager_GetContingency_UnknownId_ReturnsNull()
    {
        var dm = GetDataManagerInst();
        var contingencyType = GetGameType("Contingency");
        var list = BuildContingencyListWithId(contingencyType, KNOWN_ID);
        SetContingencysField(dm, list);

        var method = dm.GetType().GetMethod("GetContingency", BindingFlags.Public | BindingFlags.Instance);
        var result = method.Invoke(dm, new object[] { "NON_EXISTENT_ID" });
        Assert.IsNull(result);
    }

    // -------------------------------------------------------------------
    // 6. DataManager.GetContingency — null/empty id returns null
    // -------------------------------------------------------------------
    [Test]
    public void DataManager_GetContingency_NullOrEmptyId_ReturnsNull()
    {
        var dm = GetDataManagerInst();
        var contingencyType = GetGameType("Contingency");
        var list = BuildContingencyListWithId(contingencyType, KNOWN_ID);
        SetContingencysField(dm, list);

        var method = dm.GetType().GetMethod("GetContingency", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method.Invoke(dm, new object[] { null }));
        Assert.IsNull(method.Invoke(dm, new object[] { "" }));
    }

    // -------------------------------------------------------------------
    // 7. StateManager_New.ReceiveContingency(null) — no side effect, warns
    // -------------------------------------------------------------------
    [Test]
    public void StateManager_ReceiveContingency_Null_NoSideEffect_LogsWarning()
    {
        var smType = GetGameType("StateManager_New");
        Assert.IsNotNull(smType, "StateManager_New type not found");

        var go = new GameObject("__TestStateManager_New");
        try
        {
            var sm = go.AddComponent(smType);
            Assert.IsNotNull(sm);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[StateManager\] ReceiveContingency: null"));

            var receive = smType.GetMethod("ReceiveContingency", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(receive, "ReceiveContingency method not found");
            receive.Invoke(sm, new object[] { null });

            var idField = smType.GetField("_activeContingencyId", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(idField, "_activeContingencyId field not found");
            Assert.AreEqual("", (string)idField.GetValue(sm));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // -------------------------------------------------------------------
    // 8. ContingencyCompleteCondition enum has None and SubParaOn
    // -------------------------------------------------------------------
    [Test]
    public void ContingencyCompleteCondition_Enum_HasMembers_None_And_SubParaOn()
    {
        var enumType = GetGameType("ContingencyCompleteCondition");
        Assert.IsNotNull(enumType, "ContingencyCompleteCondition type not found");
        Assert.IsTrue(enumType.IsEnum, "ContingencyCompleteCondition is not an enum");

        var names = Enum.GetNames(enumType);
        CollectionAssert.Contains(names, "None");
        CollectionAssert.Contains(names, "SubParaOn");
    }

    // -------------------------------------------------------------------
    // Phase 3 영역 — 시그니처만 정의, Inconclusive 처리
    // -------------------------------------------------------------------
    [Test]
    public void StateManager_ReceiveContingency_ActiveSlotOccupied_IgnoresAndLogsWarning()
    {
        Assert.Inconclusive("Phase 3 영역");
    }

    [Test]
    public void StateManager_ReceiveContingency_SkipPending_QueuesNotApplies()
    {
        Assert.Inconclusive("Phase 3 영역");
    }

    [Test]
    public void StateManager_WaitForContingencyDuration_Expiry_CallsCompleteContingency()
    {
        Assert.Inconclusive("Phase 3 영역");
    }

    [Test]
    public void StateManager_ReceiveContingency_SkipPending_QueueExpiresAfterTimeout()
    {
        Assert.Inconclusive("Phase 3 영역");
    }

    // ================== Helpers ==================

    private static object GetDataManagerInst()
    {
        var dmType = GetGameType("DataManager");
        Assert.IsNotNull(dmType, "DataManager type not found");
        var instProp = dmType.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(instProp, "DataManager.Inst property not found");
        var inst = instProp.GetValue(null);
        Assert.IsNotNull(inst, "DataManager.Inst is null");
        return inst;
    }

    private static object BuildContingencyListWithId(Type contingencyType, string id)
    {
        var listType = typeof(List<>).MakeGenericType(contingencyType);
        var list = Activator.CreateInstance(listType);

        var entry = Activator.CreateInstance(contingencyType);
        contingencyType.GetField("id").SetValue(entry, id);

        var addMethod = listType.GetMethod("Add");
        addMethod.Invoke(list, new[] { entry });
        return list;
    }

    private static void SetContingencysField(object dataManagerInst, object listValue)
    {
        var f = dataManagerInst.GetType().GetField("contingencys", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(f, "contingencys field not found");
        f.SetValue(dataManagerInst, listValue);
    }
}
