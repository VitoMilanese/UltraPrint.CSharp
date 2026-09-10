using System.Data;
using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Scripting;

internal static class SequenceScriptFacadeCompatibilityTests
{
    [ModuleInitializer]
    public static void RunAtModuleLoad()
    {
        TestLegacySeqRoundTrip();
        TestSequenceAliasIdentityAndForwarding();
        TestLegacyQbeMatching();
        TestRecoveredContracts();
    }

    private static void TestLegacySeqRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "UltraPrint.SequenceScriptTests", Guid.NewGuid().ToString("N"));
        var ly = Path.Combine(root, "ly");
        Directory.CreateDirectory(ly);
        try
        {
            var layout = new CardLayout
            {
                Name = "Badge",
                SourcePath = Path.Combine(ly, "Badge.ly"),
                WidthMm = 85,
                HeightMm = 54,
                Dpi = 300
            };
            var path = LegacySequenceIniStore.GetDefaultPath(layout, root);
            AssertEqual(Path.Combine(root, "ly", "Badge.Seq"), path, "native default .Seq path");

            File.WriteAllText(path,
                "[Sequenza]\r\n" +
                "CustomLegacyKey=KEEP\r\n" +
                "MargineDestro=17,5\r\n");

            var settings = new SequencePrintSettings
            {
                Rows = 3,
                Columns = 4,
                MarginLeftMm = 22,
                MarginTopMm = 8.25,
                HorizontalPitchMm = 87.5,
                VerticalPitchMm = 56.75,
                StartSlot = 2,
                Side = LayoutSide.Back,
                DrawCutMarks = true
            };
            LegacySequenceIniStore.Save(path, layout, settings);

            var ini = LegacyIniDocument.Load(path);
            AssertEqual("3", ini.Get("Sequenza", "Righe")!, "legacy .Seq Righe");
            AssertEqual("4", ini.Get("Sequenza", "Colonne")!, "legacy .Seq Colonne");
            AssertEqual("8,25", ini.Get("Sequenza", "MargineAlto")!, "legacy .Seq Italian decimal margin");
            AssertEqual("87,5", ini.Get("Sequenza", "PassoOrizzontale")!, "legacy .Seq horizontal pitch");
            AssertEqual("56,75", ini.Get("Sequenza", "PassoVerticale")!, "legacy .Seq vertical pitch");
            AssertEqual("KEEP", ini.Get("Sequenza", "CustomLegacyKey")!, "unknown legacy .Seq key preserved");
            AssertEqual("17,5", ini.Get("Sequenza", "MargineDestro")!, "unresolved right-margin key preserved");

            var baseline = new SequencePrintSettings
            {
                Rows = 1,
                Columns = 1,
                MarginLeftMm = 33,
                MarginTopMm = 1,
                HorizontalPitchMm = 85,
                VerticalPitchMm = 54,
                StartSlot = 0,
                Side = LayoutSide.Back,
                DrawCutMarks = true
            };
            var loaded = LegacySequenceIniStore.Load(path, layout, baseline);
            AssertEqual(3, loaded.Rows, "legacy .Seq loads Righe");
            AssertEqual(4, loaded.Columns, "legacy .Seq loads Colonne");
            AssertNearly(8.25, loaded.MarginTopMm, 0.0001, "legacy .Seq loads MargineAlto");
            AssertNearly(87.5, loaded.HorizontalPitchMm, 0.0001, "legacy .Seq loads horizontal pitch");
            AssertNearly(56.75, loaded.VerticalPitchMm, 0.0001, "legacy .Seq loads vertical pitch");
            AssertNearly(33, loaded.MarginLeftMm, 0.0001, "unresolved MargineDestro does not corrupt managed left margin");
            AssertEqual(LayoutSide.Back, loaded.Side, "unresolved legacy side controls preserve managed baseline");
            AssertTrue(loaded.DrawCutMarks, "unresolved legacy Taglio preserves managed baseline");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void TestSequenceAliasIdentityAndForwarding()
    {
        var sequenceHost = new RecordingSequenceHost { PickedRecord = 7 };
        var databaseHost = new RecordingDatabaseHost();
        var engine = new RecordingScriptEngine();
        using var session = new LegacyScriptSession(engine);
        var facades = LegacyScriptCoreFacadeRegistration.Register(
            session,
            Path.GetTempPath(),
            databaseHost: databaseHost,
            sequenceHost: sequenceHost);
        session.Load("Sub Load(): End Sub", "sequence.vbs", prepareLegacyCode: false);

        AssertTrue(facades.Sequence is not null, "Sequenza facade is created");
        AssertTrue(engine.Objects.TryGetValue("Sequenza", out var sequenza), "Sequenza is registered");
        AssertTrue(engine.Objects.TryGetValue("Stampa", out var stampa), "Stampa is registered");
        AssertTrue(ReferenceEquals(sequenza, stampa), "Sequenza and Stampa preserve native alias identity");
        AssertTrue(ReferenceEquals(sequenza, facades.Sequence), "registered Sequenza is returned facade");

        AssertEqual(7, Convert.ToInt32(facades.Sequence!.Pescarecord()), "Pescarecord forwards native one-based result");
        AssertEqual(1, sequenceHost.PickCalls, "Pescarecord forwards to live sequence host");

        facades.Sequence.ScriviSetup();
        AssertEqual(1, sequenceHost.SaveCalls, "ScriviSetup forwards to live sequence host");
        AssertTrue(sequenceHost.LastSavePath is null, "omitted ScriviSetup filename remains Missing/default");

        facades.Sequence.LeggiSetup("custom.Seq");
        AssertEqual(1, sequenceHost.LoadCalls, "LeggiSetup forwards to live sequence host");
        AssertEqual("custom.Seq", sequenceHost.LastLoadPath!, "LeggiSetup explicit filename forwarded");

        AssertRelativeOrder(engine.Calls, "AddObject:Db:True", "AddObject:Sequenza:True");
        AssertRelativeOrder(engine.Calls, "AddObject:Sequenza:True", "AddObject:Stampa:True");
        AssertRelativeOrder(engine.Calls, "AddObject:Stampa:True", "AddObject:frmDatabase:True");
    }

    private static void TestLegacyQbeMatching()
    {
        var records = new DataTable();
        records.Columns.Add("Name", typeof(string));
        records.Columns.Add("Age", typeof(int));
        records.Columns.Add("Enabled", typeof(bool));
        records.Rows.Add("Alfa", 18, true);
        records.Rows.Add("Beta", 27, false);
        records.Rows.Add("Gamma", 42, true);

        AssertEqual(2, LegacyQbeMatcher.FindFirst(records, "Name", ".*.", "et"), "QBE contains returns one-based row");
        AssertEqual(2, LegacyQbeMatcher.FindFirst(records, "Name", "..*", "TA"), "QBE text matching is case-insensitive");
        AssertEqual(2, LegacyQbeMatcher.FindFirst(records, "Age", ">=", "20"), "QBE numeric comparison");
        AssertEqual(2, LegacyQbeMatcher.FindFirst(records, "Age", "x--x", "20--30"), "QBE numeric range");
        AssertEqual(2, LegacyQbeMatcher.FindFirst(records, "Enabled", "Falso", string.Empty), "QBE false predicate");
        AssertEqual(0, LegacyQbeMatcher.FindFirst(records, "Name", "=", "Missing"), "QBE no match returns zero");
    }

    private static void TestRecoveredContracts()
    {
        var pesca = LegacyScriptSequenceContract.Methods.Single(x => x.Name == "Pescarecord");
        AssertEqual(0x005C3590, pesca.NativeAddress, "Pescarecord native address");
        AssertEqual(0, pesca.ExplicitArgumentCount, "Pescarecord hidden Variant result is not an explicit argument");
        AssertEqual(LegacyScriptReturnKind.Variant, pesca.ReturnKind, "Pescarecord returns Variant");
        AssertTrue(pesca.ManagedBehaviorExposed, "Pescarecord exposed after FindFirst/AbsolutePosition recovery");

        var posiziona = LegacyScriptSequenceContract.Methods.Single(x => x.Name == "PosizionaPagina");
        AssertEqual(1, posiziona.ExplicitArgumentCount, "PosizionaPagina one explicit Variant");
        AssertTrue(!posiziona.ManagedBehaviorExposed, "PosizionaPagina page/tab numbering remains unexposed");

        foreach (var name in new[] { "ScriviSetup", "LeggiSetup" })
        {
            var setup = LegacyScriptSequenceContract.Methods.Single(x => x.Name == name);
            AssertEqual(1, setup.ExplicitArgumentCount, $"{name} one Optional Variant");
            AssertEqual(1, setup.OptionalArgumentCount, $"{name} argument is optional");
            AssertTrue(setup.ManagedBehaviorExposed, $"{name} exposed after .Seq recovery");
        }
    }

    private static void AssertRelativeOrder(IReadOnlyList<string> calls, string first, string second)
    {
        var firstIndex = IndexOf(calls, first);
        var secondIndex = IndexOf(calls, second);
        AssertTrue(firstIndex >= 0 && secondIndex > firstIndex, $"{first} precedes {second}");
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], value, StringComparison.Ordinal)) return i;
        return -1;
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

    private static void AssertNearly(double expected, double actual, double tolerance, string name)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}, got {actual}.");
    }

    private sealed class RecordingSequenceHost : ILegacyScriptSequenceHost
    {
        public int PickCalls { get; private set; }
        public int PickedRecord { get; init; }
        public int SaveCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public string? LastSavePath { get; private set; }
        public string? LastLoadPath { get; private set; }

        public object PickRecord()
        {
            PickCalls++;
            return PickedRecord;
        }

        public void SaveSetup(string? fileName)
        {
            SaveCalls++;
            LastSavePath = fileName;
        }

        public void LoadSetup(string? fileName)
        {
            LoadCalls++;
            LastLoadPath = fileName;
        }
    }

    private sealed class RecordingDatabaseHost : ILegacyScriptDatabaseHost
    {
        public void RefreshTables() { }
        public void FindRecord() { }
    }

    private sealed class RecordingScriptEngine : ILegacyScriptEngine
    {
        public List<string> Calls { get; } = new();
        public Dictionary<string, object> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Description => "Sequence facade recording engine";
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
