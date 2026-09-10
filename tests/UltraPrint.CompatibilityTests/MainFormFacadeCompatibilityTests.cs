using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class MainFormFacadeCompatibilityTests
{
    [ModuleInitializer]
    public static void RunAtModuleLoad()
    {
        TestFacadeForwardsProvenOperations();
        TestRecoveredAbiContract();
        TestRegistrationOrder();
    }

    private static void TestFacadeForwardsProvenOperations()
    {
        var host = new RecordingMainFormHost();
        var facade = new LegacyScriptMainFormFacade(host);

        facade.StampaRecord();
        facade.CaricaSfondo();
        facade.Termina();

        AssertEqual(1, host.PrintCalls, "Mainform.StampaRecord forwards once");
        AssertEqual(1, host.BackgroundReloadCalls, "Mainform.CaricaSfondo forwards once");
        AssertEqual(1, host.TerminateCalls, "Mainform.Termina forwards once");
    }

    private static void TestRecoveredAbiContract()
    {
        var methods = LegacyScriptMainFormContract.Methods.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        AssertEqual(14, methods.Count, "recovered MainForm public method ABI count");
        AssertEqual(0, methods["StampaRecord"].ExplicitArgumentCount, "StampaRecord args");
        AssertEqual(2, methods["SetButton"].ExplicitArgumentCount, "SetButton args");
        AssertEqual(1, methods["SetCoordinate"].ExplicitArgumentCount, "SetCoordinate args");
        AssertEqual(2, methods["PrinterEscape"].ExplicitArgumentCount, "PrinterEscape args");
        AssertEqual(LegacyScriptReturnKind.Value, methods["PuoFare"].ReturnKind, "PuoFare is a function");
        AssertEqual(1, methods["PuoFare"].ExplicitArgumentCount, "PuoFare args");
        AssertEqual(LegacyScriptReturnKind.Variant, methods["GestioneRecord"].ReturnKind, "GestioneRecord Variant return");
        AssertEqual(6, methods["GestioneCampo"].ExplicitArgumentCount, "GestioneCampo args");
        AssertEqual(3, methods.Values.Count(x => x.ManagedBehaviorExposed), "only strongly mapped Mainform methods exposed");
    }

    private static void TestRegistrationOrder()
    {
        var host = new RecordingMainFormHost();
        var engine = new MainFormRecordingScriptEngine();
        using var session = new LegacyScriptSession(engine);
        var facades = LegacyScriptCoreFacadeRegistration.Register(session, Path.GetTempPath(), mainFormHost: host);
        session.Load("Sub Load(): End Sub", "mainform.vbs", prepareLegacyCode: false);

        AssertTrue(facades.Mainform is not null, "core facade set exposes Mainform when host supplied");
        var expectedPrefix = new[]
        {
            "Reset",
            "AddObject:Me:True",
            "AddObject:Mainform:True",
            "AddObject:Fn:True",
            "AddObject:Funzioni:True",
            "AddObject:File:True",
            "AddObject:App:True"
        };
        AssertTrue(engine.Calls.Take(expectedPrefix.Length).SequenceEqual(expectedPrefix),
            "managed facade registration preserves native relative order including Mainform");
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

    private sealed class RecordingMainFormHost : ILegacyScriptMainFormHost
    {
        public int PrintCalls { get; private set; }
        public int BackgroundReloadCalls { get; private set; }
        public int TerminateCalls { get; private set; }

        public void PrintCurrentRecord() => PrintCalls++;
        public void ReloadBackground() => BackgroundReloadCalls++;
        public void TerminateApplication() => TerminateCalls++;
    }

    private sealed class MainFormRecordingScriptEngine : ILegacyScriptEngine
    {
        public List<string> Calls { get; } = new();
        public string Description => "Mainform facade recording engine";
        public void Reset() => Calls.Add("Reset");
        public void AddObject(string name, object value, bool addMembers = true) => Calls.Add($"AddObject:{name}:{addMembers}");
        public void ExecuteStatement(string statement, string? sourcePath = null) => Calls.Add("ExecuteStatement");
        public void AddCode(string code, string? sourcePath = null) => Calls.Add($"AddCode:{sourcePath}");
        public object? Invoke(string procedureName, params object?[] arguments) => null;
        public void Dispose() => Calls.Add("Dispose");
    }
}
