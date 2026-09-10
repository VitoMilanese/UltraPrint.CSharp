using System.Globalization;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Partial, conservative compatibility layer for the legacy Sequenza *.Seq files.
/// Native ScriviSetup/LeggiSetup enumerate form controls and persist them in the
/// [Sequenza] INI section. Only control names with an unambiguous managed equivalent
/// are mapped here; unknown keys (and still-unresolved controls such as MargineDestro)
/// are preserved verbatim on save.
/// </summary>
public static class LegacySequenceIniStore
{
    public const string SectionName = "Sequenza";

    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public static IReadOnlyList<string> ManagedKeys { get; } =
    [
        "Righe",
        "Colonne",
        "MargineAlto",
        "PassoOrizzontale",
        "PassoVerticale"
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
        result.MarginTopMm = ReadDouble(ini, "MargineAlto", result.MarginTopMm, 0, 1000);
        result.HorizontalPitchMm = ReadDouble(ini, "PassoOrizzontale", result.HorizontalPitchMm, 0, 1000);
        result.VerticalPitchMm = ReadDouble(ini, "PassoVerticale", result.VerticalPitchMm, 0, 1000);

        // The native form has MargineDestro, Fronte/Retro, orientation, PaginaSingola,
        // Taglio and other controls. Their exact relationship to the managed placement
        // model is not yet sufficiently decoded, so the baseline values are retained.
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
        ini.Set(SectionName, "MargineAlto", FormatNumber(settings.MarginTopMm));
        ini.Set(SectionName, "PassoOrizzontale", FormatNumber(settings.HorizontalPitchMm));
        ini.Set(SectionName, "PassoVerticale", FormatNumber(settings.VerticalPitchMm));
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
