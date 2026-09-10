using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class DatabaseScriptFacadeCompatibilityTests
{
    [ModuleInitializer]
    public static void RunAtModuleLoad()
    {
        TestNativeDaoFieldTypeNames();
        TestDatabaseAliasIdentityAndForwarding();
        TestRecoveredContracts();
    }

    private static void TestNativeDaoFieldTypeNames()
    {
        var expected = new Dictionary<int, string>
        {
            [1] = "YESNO",
            [2] = "BYTE",
            [3] = "INTEGER",
            [4] = "LONG",
            [5] = "CURRENCY",
            [6] = "SINGLE",
            [7] = "DOUBLE",
            [8] = "DATE",
            [10] = "TEXT",
            [11] = "LONGBINARY",
            [12] = "MEMO",
            [16] = "AUTOINCRFIELD"
        };

        foreach (var pair in expected)
            AssertEqual(pair.Value, LegacyDaoFieldTypeCatalog.NameForType(pair.Key), $"DAO type {pair.Key}");
        AssertEqual(string.Empty, LegacyDaoFieldTypeCatalog.NameForType(9), "unknown DAO type stays empty");
    }

    private static void TestDatabaseAliasIdentityAndForwarding()
    {
        var host = new RecordingDatabaseHost();
        var engine = new RecordingScriptEngine();
        using var session = new LegacyScriptSession(engine);
        var facades = LegacyScriptCoreFacadeRegistration.Register(
            session,
            Path.GetTempPath(),
            databaseHost: host);
        session.Load("Sub Load(): End Sub", "database.vbs", prepareLegacyCode: false);

        AssertTrue(facades.Database is not null, "Db facade is created");
        AssertTrue(facades.Tabella is not null, "Tabella facade is created");
        AssertTrue(engine.Objects.TryGetValue("Db", out var db), "Db is registered");
        AssertTrue(engine.Objects.TryGetValue("frmDatabase", out var frmDatabase), "frmDatabase is registered");
        AssertTrue(ReferenceEquals(db, frmDatabase), "Db and frmDatabase preserve native alias identity");
        AssertTrue(ReferenceEquals(db, facades.Database), "registered Db is the returned facade");
        AssertTrue(engine.Objects.TryGetValue("Tabella", out var tabella) && ReferenceEquals(tabella, facades.Tabella),
            "Tabella is the returned separate facade");

        facades.Database!.RiempiTabelle();
        AssertEqual(1, host.RefreshTableCalls, "RiempiTabelle forwards to shared database host");
        AssertEqual("TEXT", facades.Database.NometipoCampo(10), "NometipoCampo forwards exact native mapping");
        AssertEqual("DATE", facades.Database.NometipoCampo((short)8), "NometipoCampo accepts VB-style integer variants");
        AssertEqual(string.Empty, facades.Database.NometipoCampo("not-a-number"), "NometipoCampo invalid input stays empty");

        facades.Tabella!.Trovarecord();
        AssertEqual(1, host.FindRecordCalls, "Tabella.Trovarecord uses the same shared host");

        AssertRelativeOrder(engine.Calls, "AddObject:Db:True", "AddObject:frmDatabase:True");
        AssertRelativeOrder(engine.Calls, "AddObject:frmDatabase:True", "AddObject:Tabella:True");
        AssertRelativeOrder(engine.Calls, "AddObject:Tabella:True", "AddObject:Fn:True");
    }

    private static void TestRecoveredContracts()
    {
        var riempi = LegacyScriptDatabaseContract.FrmDatabaseMethods.Single(x => x.Name == "RiempiTabelle");
        AssertEqual(0x00568260, riempi.NativeAddress, "RiempiTabelle native address");
        AssertEqual(0, riempi.ExplicitArgumentCount!.Value, "RiempiTabelle zero arguments");
        AssertTrue(riempi.ManagedBehaviorExposed, "RiempiTabelle exposed only after behavior recovery");

        var tipo = LegacyScriptDatabaseContract.FrmDatabaseMethods.Single(x => x.Name == "NometipoCampo");
        AssertEqual(0x0056E4A0, tipo.NativeAddress, "NometipoCampo native address");
        AssertEqual(1, tipo.ExplicitArgumentCount!.Value, "NometipoCampo one argument");

        var trova = LegacyScriptDatabaseContract.TabellaMethods.Single(x => x.Name == "Trovarecord");
        AssertEqual(0x00523850, trova.NativeAddress, "Trovarecord native address");
        AssertEqual(0, trova.ExplicitArgumentCount!.Value, "Trovarecord zero arguments");
        AssertTrue(trova.ManagedBehaviorExposed, "Trovarecord exposed");

        var carica = LegacyScriptDatabaseContract.TabellaMethods.Single(x => x.Name == "Carica");
        AssertTrue(carica.ExplicitArgumentCount is null, "Carica ambiguous source signature remains unguessed");
        AssertTrue(!carica.ManagedBehaviorExposed, "Carica is not exposed as a fake method");
    }

    private static void AssertRelativeOrder(IReadOnlyList<string> calls, string first, string second)
    {
        var firstIndex = calls.IndexOf(first);
        var secondIndex = calls.IndexOf(second);
        AssertTrue(firstIndex >= 0 && secondIndex > firstIndex, $"{first} precedes {second}");
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

    private sealed class RecordingDatabaseHost : ILegacyScriptDatabaseHost
    {
        public int RefreshTableCalls { get; private set; }
        public int FindRecordCalls { get; private set; }
        public void RefreshTables() => RefreshTableCalls++;
        public void FindRecord() => FindRecordCalls++;
    }

    private sealed class RecordingScriptEngine : ILegacyScriptEngine
    {
        public List<string> Calls { get; } = new();
        public Dictionary<string, object> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Description => "Database facade recording engine";
        public void Reset() => Calls.Add("Reset");
        public void AddObject(string name, object value, bool addMembers = true)
        {
            Calls.Add($"AddObject:{name}:{addMembers}");
            Objects[name] = value;
        }
        public void ExecuteStatement(string statement, string? sourcePath = null) => Calls.Add("ExecuteStatement");
        public void AddCode(string code, string? sourcePath = null) => Calls.Add($"AddCode:{sourcePath}");
        public object? Invoke(string procedureName, params object?[] arguments) => null;
        public void Dispose() => Calls.Add("Dispose");
    }
}

internal static class DatabaseScriptFacadeListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }
}
