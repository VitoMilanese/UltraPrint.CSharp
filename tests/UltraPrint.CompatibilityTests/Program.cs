using System.Data;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Security;
using UltraPrint.Legacy.Scripting;

var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TPMFAO19.ly");
if (!File.Exists(fixture)) throw new FileNotFoundException("Compatibility fixture not copied to output.", fixture);

var codec = new UltraPrint22115LayoutCodec();
var originalBytes = File.ReadAllBytes(fixture);
var layout = codec.Load(fixture);

AssertNearly(85, layout.WidthMm, 0.001, "layout width");
AssertNearly(54, layout.HeightMm, 0.001, "layout height");
AssertEqual(300, layout.Dpi, "layout dpi");
AssertEqual(11, layout.Fields.Count, "decoded field count");
AssertEqual(LayoutSide.Front, layout.Fields.Single(x => x.Name == "Immagine_1").Side, "front marker side");
AssertEqual(LayoutSide.Back, layout.Fields.Single(x => x.Name == "Immagine_11").Side, "back marker side");
AssertEqual(LegacyDataSourceKind.Access, LegacyRecordSourceFactory.DetectKind("Operatori.FFM"), "FFM uses Jet/Access compatibility path");
AssertEqual(LegacyDataSourceKind.DBase, LegacyRecordSourceFactory.DetectKind("archive.dbf"), "DBF detection");
AssertEqual(LegacyDataSourceKind.Excel, LegacyRecordSourceFactory.DetectKind("records.xls"), "Excel detection");

var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.CompatibilityTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var roundTrip = Path.Combine(temp, "roundtrip.ly");
    codec.Save(layout, roundTrip);
    var roundTripBytes = File.ReadAllBytes(roundTrip);
    AssertTrue(originalBytes.AsSpan().SequenceEqual(roundTripBytes), "unchanged .ly round-trip must be byte-identical");

    var duplicateLayout = codec.Load(roundTrip);
    var source = duplicateLayout.Fields.Single(x => x.Name == "%HOLDER_LAST_NAME%");
    var clone = codec.DuplicateField(duplicateLayout, source);
    AssertEqual(12, duplicateLayout.Fields.Count, "duplicate increments field count");
    AssertTrue(clone.LegacyRecordTemplate.Length == UltraPrint22115LayoutCodec.FieldRecordSize, "duplicate preserves full raw record template");

    var duplicatedFile = Path.Combine(temp, "duplicated.ly");
    codec.Save(duplicateLayout, duplicatedFile);
    var duplicatedReload = codec.Load(duplicatedFile);
    AssertEqual(12, duplicatedReload.Fields.Count, "duplicated layout reload count");
    var duplicatedReloaded = duplicatedReload.Fields.Single(x => x.Index == clone.Index);
    AssertEqual(clone.LegacyTypeCode, duplicatedReloaded.LegacyTypeCode, "duplicated field type survives reload");
    AssertNearly(clone.Xmm, duplicatedReloaded.Xmm, 0.02, "duplicated field X survives reload");
    AssertNearly(clone.Ymm, duplicatedReloaded.Ymm, 0.02, "duplicated field Y survives reload");
    AssertTrue(duplicatedReloaded.LegacyRecordTemplate.Length == UltraPrint22115LayoutCodec.FieldRecordSize, "duplicated raw record survives reload");

    var insertedLayout = codec.Load(roundTrip);
    var inserted = codec.CreateField(insertedLayout, LayoutFieldKind.Text, LayoutSide.Front, legacyTypeCode: 3);
    inserted.Text.Content = "Compatibility test";
    var insertedFile = Path.Combine(temp, "inserted.ly");
    codec.Save(insertedLayout, insertedFile);
    var insertedReload = codec.Load(insertedFile);
    AssertEqual(12, insertedReload.Fields.Count, "inserted field reload count");
    var insertedReloaded = insertedReload.Fields.Single(x => x.Name == inserted.Name);
    AssertEqual(LayoutSide.Front, insertedReloaded.Side, "new front field remains before back-side marker");
    AssertEqual("Compatibility test", insertedReloaded.Text.Content, "new text payload round-trip");

    AssertTrue(codec.DeleteField(insertedLayout, inserted), "delete returns true");
    var deletedFile = Path.Combine(temp, "deleted.ly");
    codec.Save(insertedLayout, deletedFile);
    var deletedReload = codec.Load(deletedFile);
    AssertEqual(11, deletedReload.Fields.Count, "deleted field does not reappear after save/reload");

    TestDelimitedDataSource(temp);
    TestRecordBinding(roundTrip, codec);
    TestManagedDataState(roundTrip, codec);
    TestOperatorDatabaseLocator(temp);
    TestScriptCompatibility(temp);
    ScriptMacroCompatibilityTests.Run(temp);
    ScriptProgramPlannerTests.Run();
    ScriptSubstitutionCompatibilityTests.Run(temp);
    ScriptInvocationCompatibilityTests.Run();

    Console.WriteLine("UltraPrint compatibility tests passed.");
}
finally
{
    try { Directory.Delete(temp, recursive: true); }
    catch { }
}

static void TestDelimitedDataSource(string temp)
{
    var csv = Path.Combine(temp, "records.csv");
    File.WriteAllText(csv, "LAST_NAME;FIRST_NAME;NOTE\r\nRossi;Mario;\"A;B\"\r\nBianchi;Anna;Test\r\n");
    using var source = LegacyRecordSourceFactory.Open(csv);
    AssertEqual(LegacyDataSourceKind.Csv, source.Kind, "CSV record source kind");
    var tableName = source.GetTableNames().Single();
    var table = source.OpenTable(tableName);
    AssertEqual(2, table.Rows.Count, "CSV row count");
    AssertEqual(3, table.Columns.Count, "CSV column count");
    AssertEqual("Rossi", Convert.ToString(table.Rows[0]["LAST_NAME"])!, "CSV first field");
    AssertEqual("A;B", Convert.ToString(table.Rows[0]["NOTE"])!, "CSV quoted delimiter");
}

static void TestRecordBinding(string roundTrip, UltraPrint22115LayoutCodec codec)
{
    var sourceLayout = codec.Load(roundTrip);
    var original = sourceLayout.Fields.Single(x => x.Name == "%HOLDER_LAST_NAME%");
    var originalContent = original.Text.Content;

    var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["HOLDER_LAST_NAME"] = "Rossi",
        ["FIRST"] = "Mario"
    };
    var bound = LegacyRecordBinder.CreateBoundLayout(sourceLayout, record);
    AssertEqual("Rossi", bound.Fields.Single(x => x.Index == original.Index).Text.Content, "placeholder record binding");
    AssertEqual(originalContent, original.Text.Content, "record binding does not mutate legacy template");

    var firstName = sourceLayout.Fields.Single(x => x.Name == "%HOLDER_FIRST_NAME%");
    var overrides = new Dictionary<int, string> { [firstName.Index] = "FIRST" };
    var overrideBound = LegacyRecordBinder.CreateBoundLayout(sourceLayout, record, overrides);
    AssertEqual("Mario", overrideBound.Fields.Single(x => x.Index == firstName.Index).Text.Content, "managed binding override");
}

static void TestManagedDataState(string roundTrip, UltraPrint22115LayoutCodec codec)
{
    var stateLayout = codec.Load(roundTrip);
    var field = stateLayout.Fields.Single(x => x.Name == "%HOLDER_LAST_NAME%");
    var state = new ManagedLayoutDataState(
        @"C:\Legacy\records.mdb",
        "SELECT * FROM Tessere",
        "Tessere",
        new Dictionary<int, string> { [field.Index] = "COGNOME" });

    ManagedBindingStore.SaveState(stateLayout, state);
    var loaded = ManagedBindingStore.LoadState(stateLayout);
    AssertEqual(state.DatabasePath!, loaded.DatabasePath!, "managed database path state");
    AssertEqual(state.Sql!, loaded.Sql!, "managed SQL state");
    AssertEqual(state.Table!, loaded.Table!, "managed table state");
    AssertEqual("COGNOME", loaded.Bindings[field.Index], "managed field binding state");
    AssertTrue(File.Exists(roundTrip + ".data.json"), "managed state uses non-destructive sidecar");
}

static void TestOperatorDatabaseLocator(string temp)
{
    var root = Path.Combine(temp, "operator-locator");
    var dbDirectory = Path.Combine(root, "Db");
    Directory.CreateDirectory(dbDirectory);

    var candidates = LegacyOperatorDatabaseLocator.Candidates(root);
    AssertEqual(Path.Combine(dbDirectory, "Operatori.FFM"), candidates[0], "operator DB first startup path");
    AssertEqual(Path.Combine(root, "Operatori.FFM"), candidates[1], "operator DB legacy root fallback");

    File.WriteAllText(candidates[1], "root");
    AssertEqual(candidates[1], LegacyOperatorDatabaseLocator.FindExisting(root)!, "operator DB root fallback discovery");

    File.WriteAllText(candidates[0], "db");
    AssertEqual(candidates[0], LegacyOperatorDatabaseLocator.FindExisting(root)!, "operator DB Db folder takes precedence");
}

static void TestScriptCompatibility(string temp)
{
    AssertEqual(20, LegacyScriptContract.ObjectNames.Count, "recovered AddObjects name count");
    AssertTrue(LegacyScriptContract.ObjectNames.Contains("Mainform"), "script Mainform object name");
    AssertTrue(LegacyScriptContract.ObjectNames.Contains("Carta"), "script Carta object name");
    AssertTrue(LegacyScriptContract.ObjectNames.Contains("ClipBoard"), "script ClipBoard object name");
    AssertTrue(LegacyScriptContract.LifecycleEvents.SequenceEqual(new[] { "OnLoad", "Load", "Main", "Unload" }),
        "recovered script lifecycle event names");

    // The native PreparaCodice search strings include leading spaces for these
    // statement-level replacements, so the fixture deliberately preserves one.
    var vb6 = "Private Sub Form_Unload(Cancel)\r\n" +
              "Dim Count as Integer\r\n" +
              " Unload Me\r\n" +
              "'Me.'Caption = \"Test\"\r\n" +
              "End Sub\r\n";
    var prepared = LegacyScriptCodePreprocessor.Prepare(vb6);
    AssertTrue(!prepared.Contains("Private ", StringComparison.OrdinalIgnoreCase), "PreparaCodice removes Private");
    AssertTrue(!prepared.Contains(" as Integer", StringComparison.OrdinalIgnoreCase), "PreparaCodice removes integer type clause");
    AssertTrue(prepared.Contains("Sub Form_Unload()", StringComparison.OrdinalIgnoreCase), "PreparaCodice normalizes Form_Unload");
    AssertTrue(prepared.Contains("Chiudimi", StringComparison.OrdinalIgnoreCase), "PreparaCodice rewrites native-spaced Unload Me");
    AssertTrue(prepared.Contains("Me.Caption", StringComparison.OrdinalIgnoreCase), "PreparaCodice restores commented Me member prefix");

    var root = Path.Combine(temp, "script-contract");
    var globalScriptDirectory = Path.Combine(root, "Script");
    var appScriptDirectory = Path.Combine(root, "App", "BadgeApp", "Script");
    Directory.CreateDirectory(globalScriptDirectory);
    Directory.CreateDirectory(appScriptDirectory);
    var globalScript = Path.Combine(globalScriptDirectory, "Form.VBS");
    var appScript = Path.Combine(appScriptDirectory, "Form.VBS");
    File.WriteAllText(globalScript, "Sub Load(): End Sub");
    File.WriteAllText(appScript, "Sub Main(): End Sub");

    var context = new LegacyScriptPathContext(root, "BadgeApp");
    var candidates = LegacyScriptPathResolver.GetCandidates(context, "Form");
    AssertEqual(globalScript, candidates[0], "global Script path candidate");
    AssertTrue(candidates.Contains(appScript, StringComparer.OrdinalIgnoreCase), "application-scoped Script path candidate");
    AssertEqual(globalScript, LegacyScriptPathResolver.FindFirstExisting(context, "Form")!, "global Script candidate precedence");
    var discovered = LegacyScriptPathResolver.Discover(context);
    AssertTrue(discovered.Contains(globalScript, StringComparer.OrdinalIgnoreCase), "global script discovery");
    AssertTrue(discovered.Contains(appScript, StringComparer.OrdinalIgnoreCase), "application-scoped script discovery");

    var recordingEngine = new RecordingScriptEngine();
    using (var session = new LegacyScriptSession(recordingEngine))
    {
        session.RegisterObject("Carta", new object());
        session.RegisterObject("Fn", new object());
        session.Load("Sub Load(): End Sub", "fixture.vbs", prepareLegacyCode: false);
        AssertTrue(recordingEngine.Calls.SequenceEqual(new[]
        {
            "Reset",
            "AddObject:Carta:True",
            "AddObject:Fn:True",
            "AddCode:fixture.vbs",
        }), "raw managed session preserves Reset/AddObjects/AddCode order");
        session.Invoke("Load");
        AssertEqual("Invoke:Load:0", recordingEngine.Calls[^1], "raw managed script event dispatch");
    }

    recordingEngine = new RecordingScriptEngine();
    using (var session = new LegacyScriptSession(recordingEngine))
    {
        session.RegisterObject("Carta", new object());
        var plan = session.LoadProgram(
            "x = 1\r\nPrivate Sub Load()\r\nx = 2\r\nEnd Sub\r\n",
            "fixture-program.vbs");
        AssertTrue(!plan.Cancelled, "native AddProg-compatible session completes");
        AssertEqual(1, plan.ExecuteStatements.Count, "native AddProg-compatible session executes pre-procedure line");
        AssertTrue(recordingEngine.Calls.SequenceEqual(new[]
        {
            "Reset",
            "AddObject:Carta:True",
            "ExecuteStatement:fixture-program.vbs:x = 1",
            "AddCode:fixture-program.vbs"
        }), "managed execution uses Reset -> AddObjects -> ExecuteStatement -> AddCode");
        session.Invoke("Load", 1, "A");
        AssertEqual("Invoke:Load:2", recordingEngine.Calls[^1], "managed Vbscript dispatch forwards optional arguments");
    }
}

static void AssertTrue(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Assertion failed: {name}");
}

static void AssertEqual<T>(T expected, T actual, string name) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}, got {actual}.");
}

static void AssertNearly(double expected, double actual, double tolerance, string name)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}, got {actual}.");
}

sealed class RecordingScriptEngine : ILegacyScriptEngine
{
    public List<string> Calls { get; } = new();
    public string Description => "Recording test engine";

    public void Reset() => Calls.Add("Reset");

    public void AddObject(string name, object value, bool addMembers = true) =>
        Calls.Add($"AddObject:{name}:{addMembers}");

    public void ExecuteStatement(string statement, string? sourcePath = null) =>
        Calls.Add($"ExecuteStatement:{sourcePath ?? "<none>"}:{statement.Trim()}");

    public void AddCode(string code, string? sourcePath = null) =>
        Calls.Add($"AddCode:{sourcePath ?? "<none>"}");

    public object? Invoke(string procedureName, params object?[] arguments)
    {
        Calls.Add($"Invoke:{procedureName}:{arguments.Length}");
        return null;
    }

    public void Dispose() => Calls.Add("Dispose");
}
