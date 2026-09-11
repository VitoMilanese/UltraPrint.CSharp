using System.Globalization;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Conservative compatibility layer for the legacy Sequenza *.Seq files.
/// Native ScriviSetup/LeggiSetup enumerate form controls and persist them in the
/// [Sequenza] INI section. Only control names with a proven managed equivalent are
/// mapped here; unknown keys are preserved verbatim on save.
/// </summary>
public static class LegacySequenceIniStore
{
    public const string SectionName = "Sequenza";

    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public static IReadOnlyList<string> ManagedKeys { get; } =
    [
        "Righe",
        "Colonne",
        "MargineDestro",
        "MargineAlto",
        "PassoOrizzontale",
        "PassoVerticale",
        "OffsetRetroX",
        "OffsetRetroY",
        "Orizzontale",
        "Verticale",
        "Taglio",
        "cboDimensioni",
        "FoglioPortrait",
        "FoglioLandscape",
        "RetroaSpecchio",
        "SoloFronte",
        "FronteRetro",
        "SoloRetro",
        "PaginaSingola"
    ];

    /// <summary>
    /// Native ScriviSetup/LeggiSetup build App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"
    /// when their optional filename argument is Missing.
    /// </summary>
    public static string GetDefaultPath(CardLayout layout, string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var caption = layout.Name?.Trim();
        if (string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(layout.SourcePath))
            caption = Path.GetFileNameWithoutExtension(layout.SourcePath);
        if (string.IsNullOrWhiteSpace(caption)) caption = "Layout";

        return Path.Combine(Path.GetFullPath(applicationDirectory), "ly", caption + ".Seq");
    }

    public static SequencePrintSettings Load(
        string path,
        CardLayout layout,
        SequencePrintSettings? baseline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(layout);

        var result = (baseline ?? ManagedSequenceStore.CreateDefault(layout)).Clone();
        if (!File.Exists(path)) return result;

        var ini = LegacyIniDocument.Load(path);
        result.Rows = ReadInt(ini, "Righe", result.Rows, 1, 100);
        result.Columns = ReadInt(ini, "Colonne", result.Columns, 1, 100);

        // Native StampaPagina adds MargineDestro to MSFlexGrid.CellLeft. The
        // misleading control name therefore represents a left-origin X offset.
        result.MarginLeftMm = ReadDouble(ini, "MargineDestro", result.MarginLeftMm, 0, 1000);
        result.MarginTopMm = ReadDouble(ini, "MargineAlto", result.MarginTopMm, 0, 1000);

        // Passo* is the inter-card gap. Card width/height is supplied separately by
        // MSFlexGrid.CellLeft/CellTop and must not be folded into these values.
        result.HorizontalPitchMm = ReadDouble(ini, "PassoOrizzontale", result.HorizontalPitchMm, 0, 1000);
        result.VerticalPitchMm = ReadDouble(ini, "PassoVerticale", result.VerticalPitchMm, 0, 1000);

        // OffsetRetro* is added after normal slot geometry and is allowed to move
        // back output in either direction.
        result.BackOffsetXmm = ReadDouble(ini, "OffsetRetroX", result.BackOffsetXmm, -10000, 10000);
        result.BackOffsetYmm = ReadDouble(ini, "OffsetRetroY", result.BackOffsetYmm, -10000, 10000);

        var horizontal = ReadOptionalCheckValue(ini, "Orizzontale");
        var vertical = ReadOptionalCheckValue(ini, "Verticale");
        if (horizontal == true && vertical != true)
            result.FillDirection = SequenceFillDirection.Horizontal;
        else if (vertical == true && horizontal != true)
            result.FillDirection = SequenceFillDirection.Vertical;

        // Taglio is not a crop-mark toggle. Native cmdImposta uses it to change
        // record numbering from page-major to slot-major-across-pages ordering.
        result.CutStack = ReadCheckValue(ini, "Taglio", result.CutStack);

        // cboDimensioni is a ComboBox. Native generic setup persists the control's
        // Text, while Form_Load populates its available strings from Campo.ini [Formati].
        var paperFormat = ini.Get(SectionName, "cboDimensioni");
        if (!string.IsNullOrWhiteSpace(paperFormat))
            result.PaperFormatText = paperFormat;

        var portrait = ReadOptionalCheckValue(ini, "FoglioPortrait");
        var landscape = ReadOptionalCheckValue(ini, "FoglioLandscape");
        if (portrait == true && landscape != true)
            result.PaperOrientation = SequencePaperOrientation.Portrait;
        else if (landscape == true && portrait != true)
            result.PaperOrientation = SequencePaperOrientation.Landscape;

        result.MirrorBack = ReadCheckValue(ini, "RetroaSpecchio", result.MirrorBack);

        // These three OptionButtons are the native output-mode selector. Fronte and
        // Retro themselves are the current phase/view toggles used while printing.
        var soloFront = ReadOptionalCheckValue(ini, "SoloFronte");
        var frontBack = ReadOptionalCheckValue(ini, "FronteRetro");
        var soloBack = ReadOptionalCheckValue(ini, "SoloRetro");
        if (soloFront == true && frontBack != true && soloBack != true)
            result.Side = LayoutSide.Front;
        else if (frontBack == true && soloFront != true && soloBack != true)
            result.Side = LayoutSide.Unknown;
        else if (soloBack == true && soloFront != true && frontBack != true)
            result.Side = LayoutSide.Back;

        result.SinglePageMode = ReadCheckValue(ini, "PaginaSingola", result.SinglePageMode);

        // Fronte/Retro remain transient native phase controls. Managed crop marks
        // are deliberately sidecar-only because no legacy crop-mark control exists.
        var capacity = Math.Max(1, result.Rows * result.Columns);
        if (result.StartSlot >= capacity) result.StartSlot = capacity - 1;
        result.Validate(layout.WidthMm, layout.HeightMm);
        return result;
    }

    public static void Save(string path, CardLayout layout, SequencePrintSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(layout.WidthMm, layout.HeightMm);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var ini = File.Exists(fullPath)
            ? LegacyIniDocument.Load(fullPath)
            : LegacyIniDocument.Parse(string.Empty);

        ini.Set(SectionName, "Righe", settings.Rows.ToString(CultureInfo.InvariantCulture));
        ini.Set(SectionName, "Colonne", settings.Columns.ToString(CultureInfo.InvariantCulture));
        ini.Set(SectionName, "MargineDestro", FormatNumber(settings.MarginLeftMm));
        ini.Set(SectionName, "MargineAlto", FormatNumber(settings.MarginTopMm));
        ini.Set(SectionName, "PassoOrizzontale", FormatNumber(settings.HorizontalPitchMm));
        ini.Set(SectionName, "PassoVerticale", FormatNumber(settings.VerticalPitchMm));
        ini.Set(SectionName, "OffsetRetroX", FormatNumber(settings.BackOffsetXmm));
        ini.Set(SectionName, "OffsetRetroY", FormatNumber(settings.BackOffsetYmm));
        ini.Set(SectionName, "Orizzontale", settings.FillDirection == SequenceFillDirection.Horizontal ? "1" : "0");
        ini.Set(SectionName, "Verticale", settings.FillDirection == SequenceFillDirection.Vertical ? "1" : "0");
        ini.Set(SectionName, "Taglio", settings.CutStack ? "1" : "0");
        if (!string.IsNullOrWhiteSpace(settings.PaperFormatText))
            ini.Set(SectionName, "cboDimensioni", settings.PaperFormatText);
        ini.Set(SectionName, "FoglioPortrait", settings.PaperOrientation == SequencePaperOrientation.Portrait ? "1" : "0");
        ini.Set(SectionName, "FoglioLandscape", settings.PaperOrientation == SequencePaperOrientation.Landscape ? "1" : "0");
        ini.Set(SectionName, "RetroaSpecchio", settings.MirrorBack ? "1" : "0");
        ini.Set(SectionName, "SoloFronte", settings.Side == LayoutSide.Front ? "1" : "0");
        ini.Set(SectionName, "FronteRetro", settings.Side == LayoutSide.Unknown ? "1" : "0");
        ini.Set(SectionName, "SoloRetro", settings.Side == LayoutSide.Back ? "1" : "0");
        ini.Set(SectionName, "PaginaSingola", settings.SinglePageMode ? "1" : "0");
        ini.Save(fullPath);
    }

    private static int ReadInt(LegacyIniDocument ini, string key, int fallback, int min, int max)
    {
        var raw = ini.Get(SectionName, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return fallback;
        return value < min || value > max ? fallback : value;
    }

    private static bool ReadCheckValue(LegacyIniDocument ini, string key, bool fallback) =>
        ReadOptionalCheckValue(ini, key) ?? fallback;

    private static bool? ReadOptionalCheckValue(LegacyIniDocument ini, string key)
    {
        var raw = ini.Get(SectionName, key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value != 0;
        if (bool.TryParse(raw, out var boolean)) return boolean;
        if (string.Equals(raw, "Vero", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "Si", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(raw, "Falso", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "No", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static double ReadDouble(
        LegacyIniDocument ini,
        string key,
        double fallback,
        double min,
        double max)
    {
        var raw = ini.Get(SectionName, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        raw = raw.Trim();

        if (!double.TryParse(raw, NumberStyles.Float, ItalianCulture, out var value) &&
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return fallback;
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
            return fallback;
        return value;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.################", ItalianCulture);
}
