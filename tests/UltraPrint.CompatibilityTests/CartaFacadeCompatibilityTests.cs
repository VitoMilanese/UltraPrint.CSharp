using System.Globalization;
using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Scripting;

internal static class CartaFacadeCompatibilityTests
{
    [ModuleInitializer]
    public static void RunAtModuleLoad()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.CartaFacadeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            TestRecoveredTypeCatalog();
            TestCampoRoundTrip(temp);
            TestNuovoCampoAndRegistration(temp);
            TestRecoveredTypeRoundTrip(temp);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void TestRecoveredTypeCatalog()
    {
        AssertEqual(LayoutFieldKind.Rectangle, LegacyFieldTypeCatalog.KindForType(2), "type 2 Rettangolo");
        AssertEqual(LayoutFieldKind.Text, LegacyFieldTypeCatalog.KindForType(3), "type 3 Testo");
        AssertEqual(LayoutFieldKind.Barcode, LegacyFieldTypeCatalog.KindForType(4), "type 4 Barcode");
        AssertEqual(LayoutFieldKind.Image, LegacyFieldTypeCatalog.KindForType(5), "type 5 Immagine");
        AssertEqual(LayoutFieldKind.Image, LegacyFieldTypeCatalog.KindForType(6), "type 6 supplied photo/image field");
        AssertEqual(LayoutFieldKind.Hardware, LegacyFieldTypeCatalog.KindForType(7), "type 7 Twain");
        AssertEqual(LayoutFieldKind.Hardware, LegacyFieldTypeCatalog.KindForType(8), "type 8 Telecamera");
        AssertEqual(LayoutFieldKind.MagneticStripe, LegacyFieldTypeCatalog.KindForType(9), "type 9 BandaMagnetica");
        AssertEqual(LayoutFieldKind.Chip, LegacyFieldTypeCatalog.KindForType(10), "type 10 smart-card field");
        AssertEqual(LayoutFieldKind.Table, LegacyFieldTypeCatalog.KindForType(13), "type 13 Tabella");
        AssertEqual(LayoutFieldKind.Unknown, LegacyFieldTypeCatalog.KindForType(12), "unproven field type remains unknown");
    }

    private static void TestCampoRoundTrip(string temp)
    {
        var campo = Path.Combine(temp, "Campo.ini");
        var layout = CreateLayout();
        var field = layout.Fields[0];
        var host = new RecordingCartaHost(layout, campo);
        var carta = new LegacyScriptCartaFacade(host);

        carta.CampoToIni(1);
        var ini = LegacyIniDocument.Load(campo);
        var values = new CampoCurrentValueStore(ini);
        AssertEqual("Nome", values.Get("Generale", "Nome")!, "CampoToIni writes field name");
        AssertEqual("12,5", values.Get("Generale", "X")!, "CampoToIni writes Italian X");
        AssertEqual("-1", values.Get("Testo", "Grassetto")!, "CampoToIni writes VB Boolean");
        AssertEqual("&H80000008", values.Get("Aspetto", "Colore")!, "CampoToIni writes OLE system color");

        ini.Set("$", "Generale.X", "23,75");
        ini.Set("$", "Generale.Y", "8,25");
        ini.Set("$", "Testo.Contenuto", "Changed by Campo.ini");
        ini.Set("$", "Testo.Centrato", "-1");
        ini.Set("$", "Testo.Sinistra", "0");
        ini.Save(campo);

        carta.IniToCampo(1);
        AssertNearly(23.75, field.Xmm, 0.0001, "IniToCampo reads Italian X");
        AssertNearly(8.25, field.Ymm, 0.0001, "IniToCampo reads Italian Y");
        AssertEqual("Changed by Campo.ini", field.Text.Content, "IniToCampo reads text content");
        AssertEqual(TextAlignment.Center, field.Text.Alignment, "IniToCampo reads alignment flags");
        AssertEqual(field, host.SelectedField!, "IniToCampo selects the field");
        AssertTrue(host.ChangeNotifications > 0, "IniToCampo marks managed layout changed");
        AssertTrue(host.Redraws > 0, "IniToCampo redraws managed Carta");
    }

    private static void TestNuovoCampoAndRegistration(string temp)
    {
        var campo = Path.Combine(temp, "NuovoCampo.ini");
        var layout = CreateLayout();
        var host = new RecordingCartaHost(layout, campo);
        var carta = new LegacyScriptCartaFacade(host);

        carta.NuovoCampo(4);
        AssertEqual(2, layout.Fields.Count, "NuovoCampo adds a field");
        var barcode = host.SelectedField!;
        AssertEqual(4, barcode.LegacyTypeCode, "NuovoCampo preserves native type code");
        AssertEqual(LayoutFieldKind.Barcode, barcode.Kind, "NuovoCampo maps Barcode kind");
        AssertTrue(barcode.Name.StartsWith("Barcode_", StringComparison.Ordinal), "NuovoCampo uses native Barcode label");
        AssertEqual(2, LegacyFieldTypeCatalog.ToLegacyFieldNumber(barcode), "managed slot -> one-based Carta field number");
        AssertEqual(barcode, LegacyFieldTypeCatalog.FindByLegacyFieldNumber(layout, 2)!, "one-based Carta field lookup");

        var engine = new RecordingScriptEngine();
        using var session = new LegacyScriptSession(engine);
        var facades = LegacyScriptCoreFacadeRegistration.Register(session, temp, cartaHost: host);
        session.Load("Sub Load(): End Sub", "carta.vbs", prepareLegacyCode: false);

        AssertTrue(facades.Carta is not null, "core facade set exposes Carta when host supplied");
        var expectedPrefix = new[]
        {
            "Reset",
            "AddObject:Me:True",
            "AddObject:Carta:True",
            "AddObject:Fn:True",
            "AddObject:Funzioni:True",
            "AddObject:File:True",
            "AddObject:App:True"
        };
        AssertTrue(engine.Calls.Take(expectedPrefix.Length).SequenceEqual(expectedPrefix),
            "managed facade registration preserves native relative order including Carta");
    }

    private static void TestRecoveredTypeRoundTrip(string temp)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TPMFAO19.ly");
        if (!File.Exists(fixture))
            throw new FileNotFoundException("Carta type round-trip fixture not copied to output.", fixture);

        var codec = new UltraPrint22115LayoutCodec();
        var layout = codec.Load(fixture);
        var host = new RecordingCartaHost(layout, Path.Combine(temp, "roundtrip-Campo.ini"));
        var carta = new LegacyScriptCartaFacade(host, codec);
        carta.NuovoCampo(4);
        var created = host.SelectedField!;

        var saved = Path.Combine(temp, "carta-barcode.ly");
        codec.Save(layout, saved);
        var reloaded = codec.Load(saved);
        var roundTripped = reloaded.Fields.Single(field => field.Index == created.Index);
        AssertEqual(4, roundTripped.LegacyTypeCode, "Carta-created Barcode type code survives .ly save/reload");
        AssertEqual(LayoutFieldKind.Barcode, roundTripped.Kind, "Carta-created Barcode kind is decoded after .ly reload");
        AssertTrue(roundTripped.Name.StartsWith("Barcode_", StringComparison.Ordinal), "Carta-created Barcode name survives .ly reload");
    }

    private static CardLayout CreateLayout()
    {
        var layout = new CardLayout
        {
            Name = "CartaFacade",
            WidthMm = 85,
            HeightMm = 54,
            Dpi = 300
        };
        var field = new LayoutField
        {
            Index = 0,
            Name = "Nome",
            Kind = LayoutFieldKind.Text,
            Side = LayoutSide.Front,
            LegacyTypeCode = 3,
            Xmm = 12.5,
            Ymm = 7.5,
            WidthMm = 30,
            HeightMm = 5,
            Level = 1,
            Appearance = { ForeColorOle = unchecked((int)0x80000008) }
        };
        field.Text.Content = "Mario";
        field.Text.FontName = "Arial";
        field.Text.FontSize = 10;
        field.Text.Bold = true;
        layout.Fields.Add(field);
        return layout;
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

    private sealed class RecordingCartaHost : ILegacyScriptCartaHost
    {
        public RecordingCartaHost(CardLayout layout, string campoIniPath)
        {
            Layout = layout;
            CampoIniPath = campoIniPath;
        }

        public CardLayout? Layout { get; }
        public LayoutSide CurrentSide { get; set; } = LayoutSide.Front;
        public LayoutField? SelectedField { get; private set; }
        public string CampoIniPath { get; }
        public int ChangeNotifications { get; private set; }
        public int Redraws { get; private set; }

        public void SelectField(LayoutField field) => SelectedField = field;
        public void NotifyLayoutChanged() => ChangeNotifications++;
        public void Redraw() => Redraws++;
    }

    private sealed class RecordingScriptEngine : ILegacyScriptEngine
    {
        public List<string> Calls { get; } = new();
        public string Description => "Carta facade recording engine";
        public void Reset() => Calls.Add("Reset");
        public void AddObject(string name, object value, bool addMembers = true) => Calls.Add($"AddObject:{name}:{addMembers}");
        public void ExecuteStatement(string statement, string? sourcePath = null) => Calls.Add("ExecuteStatement");
        public void AddCode(string code, string? sourcePath = null) => Calls.Add($"AddCode:{sourcePath}");
        public object? Invoke(string procedureName, params object?[] arguments) => null;
        public void Dispose() => Calls.Add("Dispose");
    }
}
