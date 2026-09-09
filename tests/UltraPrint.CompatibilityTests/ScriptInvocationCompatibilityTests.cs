using UltraPrint.Legacy.Scripting;

internal static class ScriptInvocationCompatibilityTests
{
    public static void Run()
    {
        TestProcedureNameNormalization();
        TestDirectRunNameAndArguments();
        TestMissingArgumentTruncation();
        TestNoCodeSentinel();
        TestDeferredUnloadAtOutermostDispatch();
        TestVariableTable();
        TestCoreFacades();
    }

    private static void TestProcedureNameNormalization()
    {
        AssertEqual("Load", LegacyScriptInvocation.BuildProcedureName(string.Empty, "Load"),
            "Vbscript empty prefix uses event name");
        AssertEqual("Field_Click", LegacyScriptInvocation.BuildProcedureName("Field", "Click"),
            "Vbscript prefix and event use underscore separator");
        AssertEqual("P_12", LegacyScriptInvocation.BuildProcedureName("12", "Click"),
            "Vbscript positive numeric prefix uses P_ prefix");
        AssertEqual("A*_Load", LegacyScriptInvocation.BuildProcedureName("A X", "Load"),
            "Vbscript removes spaces and converts uppercase X to literal star passed to Run");
        AssertEqual("Ax_Load", LegacyScriptInvocation.BuildProcedureName("Ax", "Load"),
            "Vbscript X replacement remains case-sensitive");
        AssertEqual(
            LegacyScriptInvocation.BuildProcedureName("Field", "Click"),
            LegacyScriptInvocation.BuildProcedurePattern("Field", "Click"),
            "legacy BuildProcedurePattern alias preserves source compatibility");

        LegacyScriptInvocation.ValidateOptionalArguments(new object?[LegacyScriptInvocation.MaximumOptionalArguments]);
        AssertThrows<ArgumentOutOfRangeException>(
            () => LegacyScriptInvocation.ValidateOptionalArguments(
                new object?[LegacyScriptInvocation.MaximumOptionalArguments + 1]),
            "Vbscript rejects more than seven optional arguments");
    }

    private static void TestDirectRunNameAndArguments()
    {
        var engine = new RecordingDispatchEngine { HasProceduresValue = true };
        using var session = new LegacyScriptSession(engine);
        session.Load("Sub Load(): End Sub", "fixture.vbs", prepareLegacyCode: false);
        engine.Calls.Clear();

        session.InvokeLegacy("A X", "Load", 1, "two");

        AssertTrue(engine.Calls.SequenceEqual(new[] { "Invoke:A*_Load:2" }),
            "normalized star name is handed directly to engine Run boundary without local wildcard resolution");
        AssertEqual(0, session.DispatchDepth, "dispatch depth returns to zero after direct invocation");
    }

    private static void TestMissingArgumentTruncation()
    {
        var supplied = LegacyScriptInvocation.GetSuppliedOptionalArguments(
            new object?[] { 1, LegacyScriptMissing.Value, 3 });
        AssertEqual(1, supplied.Length, "first Missing truncates Vbscript optional argument list");
        AssertEqual(1, Convert.ToInt32(supplied[0]), "argument before first Missing is preserved");

        supplied = LegacyScriptInvocation.GetSuppliedOptionalArguments(
            new object?[] { LegacyScriptMissing.Value, 2 });
        AssertEqual(0, supplied.Length, "Missing in first optional slot produces zero Run arguments");

        supplied = LegacyScriptInvocation.GetSuppliedOptionalArguments(
            new object?[] { null, 2 });
        AssertEqual(2, supplied.Length, "Null is a supplied Variant and is not the VB Missing sentinel");

        var engine = new RecordingDispatchEngine { HasProceduresValue = true };
        using var session = new LegacyScriptSession(engine);
        session.Load("Sub Load(): End Sub", "missing.vbs", prepareLegacyCode: false);
        engine.Calls.Clear();
        session.InvokeLegacy(string.Empty, "Load", 1, LegacyScriptMissing.Value, 3);
        AssertTrue(engine.Calls.SequenceEqual(new[] { "Invoke:Load:1" }),
            "session dispatch mirrors native rtcIsMissing cascade before ScriptControl.Run");
    }

    private static void TestNoCodeSentinel()
    {
        var engine = new RecordingDispatchEngine { HasProceduresValue = false };
        using var session = new LegacyScriptSession(engine);
        session.Load("x = 1", "no-procedures.vbs", prepareLegacyCode: false);
        engine.Calls.Clear();

        var result = session.InvokeLegacy(string.Empty, "Load");

        AssertEqual(LegacyScriptInvocation.NoCodeSentinel, (string)result!,
            "native Vbscript NO CODE sentinel is returned when the first module has no procedures");
        AssertEqual(0, engine.Calls.Count, "NO CODE path does not invoke ScriptControl.Run");
        AssertTrue(session.IsLoaded, "NO CODE probe alone does not reset the script engine");
        AssertEqual(0, session.DispatchDepth, "NO CODE path still balances native dispatch depth");
    }

    private static void TestDeferredUnloadAtOutermostDispatch()
    {
        var engine = new RecordingDispatchEngine { HasProceduresValue = true };
        using var session = new LegacyScriptSession(engine);
        session.Load("Sub Load(): End Sub\r\nSub Form_Unload(): End Sub", "unload.vbs", prepareLegacyCode: false);
        engine.Calls.Clear();

        engine.OnInvoke = procedureName =>
        {
            if (procedureName == "Load")
            {
                AssertEqual(1, session.DispatchDepth, "outer Vbscript dispatch depth");
                session.RequestUnload();
            }
            else if (procedureName == "Form_Unload")
            {
                AssertEqual(2, session.DispatchDepth, "recursive Form_Unload dispatch depth");
                session.RequestUnload();
            }
        };

        session.InvokeLegacy(string.Empty, "Load");

        AssertTrue(engine.Calls.SequenceEqual(new[]
        {
            "Invoke:Load:0",
            "Invoke:Form_Unload:0",
            "Reset"
        }), "Chiudimi unload is deferred until outermost dispatch, then Form_Unload runs before Reset");
        AssertEqual(0, session.DispatchDepth, "unload path balances nested dispatch depth");
        AssertTrue(!session.UnloadRequested, "pending Chiudimi flag is cleared after Reset");
        AssertTrue(!session.IsLoaded, "native unload Reset leaves no script code loaded");
        AssertThrows<InvalidOperationException>(
            () => session.InvokeLegacy(string.Empty, "Load"),
            "dispatch after unload requires code to be loaded again");
    }

    private static void TestVariableTable()
    {
        var variables = new LegacyScriptVariableTable();
        AssertEqual(0, variables.Count, "legacy variable table starts empty");
        AssertTrue(variables.GetVariabile("Missing") is null, "GetVariabile missing name returns Empty/null");
        AssertTrue(!variables.SetVariabile("Missing", 5), "SetVariabile does not create missing native slots");
        AssertEqual(0, variables.Count, "SetVariabile missing name leaves table unchanged");

        variables.Define(" counter ", 41);
        AssertEqual(1, variables.Count, "host definition allocates one legacy variable slot");
        AssertEqual(41, Convert.ToInt32(variables.GetVariabile("COUNTER")), "variable lookup is trim/case normalized");
        AssertTrue(variables.IncVariabile("counter"), "IncVariabile finds existing normalized name");
        AssertEqual(42, Convert.ToInt32(variables.GetVariabile(" counter ")), "IncVariabile performs Variant + 1");
        AssertTrue(variables.SetVariabile("Counter", "99"), "SetVariabile updates existing slot");
        AssertEqual("99", Convert.ToString(variables.GetVariabile("COUNTER"))!, "SetVariabile preserves assigned Variant value");
        AssertTrue(!variables.IncVariabile("NotThere"), "IncVariabile ignores missing names");
    }

    private static void TestCoreFacades()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.ScriptFacades", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var app = new LegacyScriptAppFacade(temp, "UltraPrint");
            AssertEqual(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), app.Path,
                "App.Path facade uses legacy application root");
            AssertEqual("UltraPrint", app.EXEName, "App.EXEName facade");

            var file = new LegacyScriptFileFacade();
            var ini = Path.Combine(temp, "fixture.ini");
            AssertEqual("fallback", file.GetIni("Setup", "Name", "fallback", ini),
                "File.GetIni returns default for missing file");
            file.WriteIni("Setup", "Name", "UltraPrint", ini);
            AssertEqual("UltraPrint", file.GetIni("setup", "name", "fallback", ini),
                "File.GetIni/WriteIni preserve case-insensitive INI lookup");

            AssertEqual("gz", file.SoloExt(@"C:\cards\archive.tar.gz"), "File.SoloExt returns text after final dot");
            AssertEqual(string.Empty, file.SoloExt(@"C:\cards\plain"), "File.SoloExt returns empty without dot");
            AssertEqual("archive.tar.gz", file.SoloNomeFile(@"C:\cards\archive.tar.gz"),
                "File.SoloNomeFile default preserves extension");
            AssertEqual("archive", file.SoloNomeFile(@"C:\cards\archive.tar.gz", false),
                "File.SoloNomeFile false removes from first basename dot");
            AssertEqual(@"C:\cards\", file.SoloPath(@"C:\cards\archive.tar.gz"),
                "File.SoloPath preserves trailing backslash");

            var existing = Path.Combine(temp, "exists.txt");
            File.WriteAllText(existing, "ok");
            AssertTrue(file.Esiste(existing), "File.Esiste finds ordinary file");
            AssertTrue(file.Esiste(Path.Combine(temp, "*.txt")), "File.Esiste preserves Dir-style wildcard use");
            AssertTrue(!file.Esiste(Path.Combine(temp, "*.missing")), "File.Esiste returns false for unmatched wildcard");

            var variables = new LegacyScriptVariableTable();
            variables.Define("Count", 1);
            var funzioni = new LegacyScriptFunctionsFacade(variables);
            AssertEqual(1, Convert.ToInt32(funzioni.GetVariabile(" count ")), "Funzioni.GetVariabile facade");
            funzioni.IncVariabile("COUNT");
            AssertEqual(2, Convert.ToInt32(funzioni.GetVariabile("Count")), "Funzioni.IncVariabile facade");
            funzioni.SetVariabile("Count", 7);
            AssertEqual(7, Convert.ToInt32(funzioni.GetVariabile("Count")), "Funzioni.SetVariabile facade");
            AssertEqual("aXX", funzioni.Sostituisci("abb", "b", "X"), "Funzioni.Sostituisci binary replace-all facade");
            AssertEqual("Ab", funzioni.Sostituisci("Ab", "a", "x"), "Funzioni.Sostituisci remains case-sensitive");

            var unloadRequested = false;
            var host = new LegacyScriptHostFacade(() => unloadRequested = true);
            host.Chiudimi();
            AssertTrue(unloadRequested, "Me.Chiudimi facade reaches managed unload request boundary");

            var engine = new RecordingDispatchEngine { HasProceduresValue = true };
            using var session = new LegacyScriptSession(engine);
            var registered = LegacyScriptCoreFacadeRegistration.Register(session, temp);
            registered.Variables.Define("Injected", 5);
            session.Load("Sub Load(): End Sub", "facades.vbs", prepareLegacyCode: false);
            AssertTrue(engine.Calls.SequenceEqual(new[]
            {
                "Reset",
                "AddObject:Me:True",
                "AddObject:Fn:True",
                "AddObject:Funzioni:True",
                "AddObject:File:True",
                "AddObject:App:True",
                "AddCode"
            }), "proven managed facades are injected before code load in deterministic native-relative order");
            AssertTrue(ReferenceEquals(engine.RegisteredObjects["Fn"], engine.RegisteredObjects["Funzioni"]),
                "Fn and Funzioni preserve native singleton alias identity");
            AssertTrue(ReferenceEquals(engine.RegisteredObjects["Funzioni"], registered.Funzioni),
                "registered Funzioni facade is the same object exposed to ScriptControl");
            AssertEqual(5, Convert.ToInt32(registered.Funzioni.GetVariabile("Injected")),
                "registered Funzioni and Interpretariga share the same variable table");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void AssertThrows<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Assertion failed: {name}. Expected {typeof(TException).Name}.");
    }

    private static void AssertTrue(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Assertion failed: {name}");
    }

    private static void AssertEqual<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}, got {actual}.");
    }

    private sealed class RecordingDispatchEngine : ILegacyScriptEngine, ILegacyScriptProcedureProbe
    {
        public List<string> Calls { get; } = new();
        public Dictionary<string, object> RegisteredObjects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Description => "Recording dispatch engine";
        public bool HasProceduresValue { get; init; }
        public Action<string>? OnInvoke { get; set; }

        public bool HasProcedures() => HasProceduresValue;

        public void Reset() => Calls.Add("Reset");

        public void AddObject(string name, object value, bool addMembers = true)
        {
            RegisteredObjects[name] = value;
            Calls.Add($"AddObject:{name}:{addMembers}");
        }

        public void ExecuteStatement(string statement, string? sourcePath = null) =>
            Calls.Add("ExecuteStatement");

        public void AddCode(string code, string? sourcePath = null) =>
            Calls.Add("AddCode");

        public object? Invoke(string procedureName, params object?[] arguments)
        {
            Calls.Add($"Invoke:{procedureName}:{arguments.Length}");
            OnInvoke?.Invoke(procedureName);
            return null;
        }

        public void Dispose() { }
    }
}
