using System.Globalization;
using System.Runtime.InteropServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// UI-neutral bridge used by the COM-visible Carta facade. The original Carta object is
/// frmCarta itself; the managed replacement keeps the script contract in UltraPrint.Legacy
/// and delegates editor-specific selection/redraw/dirty-state behavior to the host.
/// </summary>
public interface ILegacyScriptCartaHost
{
    CardLayout? Layout { get; }
    LayoutSide CurrentSide { get; }
    LayoutField? SelectedField { get; }
    string CampoIniPath { get; }

    void SelectField(LayoutField field);
    void NotifyLayoutChanged();
    void Redraw();
}

/// <summary>
/// Recovered, script-callable subset of frmCarta.
///
/// Native signatures were recovered from the VB6 native methods:
/// NuovoCampo(Variant), CampoToIni(Variant), IniToCampo(Variant), SetupCampo(Variant),
/// DisegnaCampi(), AggiornaMisure() and AggiornaBottoni(). Methods whose argument
/// semantics are still unclear (SelectField, LabelToCampo, SetButton, AggiornaMarcatori,
/// FaiFoto and Record2Card) are intentionally not exposed yet.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptCartaFacade
{
    private readonly ILegacyScriptCartaHost _host;
    private readonly UltraPrint22115LayoutCodec _codec;

    public LegacyScriptCartaFacade(
        ILegacyScriptCartaHost host,
        UltraPrint22115LayoutCodec? codec = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _codec = codec ?? new UltraPrint22115LayoutCodec();
    }

    public void NuovoCampo(object Tipo)
    {
        var layout = RequireLayout();
        var typeCode = ConvertLegacyInt(Tipo, nameof(Tipo));
        if (typeCode is <= 0 or > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Tipo), typeCode, "Invalid legacy field type.");

        var side = _host.CurrentSide;
        if (side is not (LayoutSide.Front or LayoutSide.Back))
            side = _host.SelectedField?.Side is LayoutSide.Front or LayoutSide.Back
                ? _host.SelectedField.Side
                : LayoutSide.Front;

        var kind = LegacyFieldTypeCatalog.KindForType(typeCode);
        var field = _codec.CreateField(layout, kind, side, typeCode);
        field.Name = CreateNativeStyleName(layout, field, LegacyFieldTypeCatalog.NativeLabelForType(typeCode));

        _host.SelectField(field);
        _host.NotifyLayoutChanged();
        LegacyCampoCurrentFieldSynchronizer.Write(_host.CampoIniPath, field);
        _host.Redraw();
    }

    public void CampoToIni(object Campo)
    {
        var field = RequireField(Campo, nameof(Campo));
        LegacyCampoCurrentFieldSynchronizer.Write(_host.CampoIniPath, field);
    }

    public void IniToCampo(object Campo)
    {
        var field = RequireField(Campo, nameof(Campo));
        LegacyCampoCurrentFieldSynchronizer.Read(_host.CampoIniPath, field);
        _host.SelectField(field);
        _host.NotifyLayoutChanged();
        _host.Redraw();
    }

    public void SetupCampo(object Campo)
    {
        var field = RequireField(Campo, nameof(Campo));
        _host.SelectField(field);
        LegacyCampoCurrentFieldSynchronizer.Write(_host.CampoIniPath, field);
        _host.Redraw();
    }

    public void DisegnaCampi() => _host.Redraw();

    /// <summary>
    /// VB6 kept a separate label/control representation and copied measurements back and
    /// forth. The managed editor uses CardLayout as the single source of truth, so the
    /// functional equivalent is to refresh the rendered measurements.
    /// </summary>
    public void AggiornaMisure() => _host.Redraw();

    public void AggiornaBottoni() => _host.Redraw();

    private CardLayout RequireLayout() =>
        _host.Layout ?? throw new InvalidOperationException("Carta requires an open UltraPrint layout.");

    private LayoutField RequireField(object value, string parameterName)
    {
        var layout = RequireLayout();
        var legacyFieldNumber = ConvertLegacyInt(value, parameterName);
        return LegacyFieldTypeCatalog.FindByLegacyFieldNumber(layout, legacyFieldNumber)
               ?? throw new ArgumentOutOfRangeException(
                   parameterName,
                   legacyFieldNumber,
                   $"UltraPrint field {legacyFieldNumber} does not exist.");
    }

    private static int ConvertLegacyInt(object? value, string parameterName)
    {
        if (value is null or DBNull)
            throw new ArgumentNullException(parameterName);
        try
        {
            return Convert.ToInt32(value, CultureInfo.CurrentCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"'{value}' is not a valid legacy integer.", parameterName, ex);
        }
    }

    private static string CreateNativeStyleName(CardLayout layout, LayoutField field, string prefix)
    {
        // NuovoCampo starts with the recovered Italian type label and then works with the
        // current field number. Keep the legacy 20-byte name limit enforced by the .ly record.
        var fieldNumber = LegacyFieldTypeCatalog.ToLegacyFieldNumber(field);
        var stem = string.IsNullOrWhiteSpace(prefix) ? "Campo" : prefix.Trim();
        var candidate = $"{stem}_{fieldNumber}";
        if (candidate.Length > 20) candidate = candidate[..20];

        if (!layout.Fields.Any(other => !ReferenceEquals(other, field) &&
                                        string.Equals(other.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            return candidate;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var maxStem = Math.Max(1, 20 - suffixText.Length);
            var unique = (stem.Length > maxStem ? stem[..maxStem] : stem) + suffixText;
            if (!layout.Fields.Any(other => !ReferenceEquals(other, field) &&
                                            string.Equals(other.Name, unique, StringComparison.OrdinalIgnoreCase)))
                return unique;
        }

        return candidate;
    }
}

/// <summary>
/// Managed counterpart of the confirmed frmCarta.CampoToIni / IniToCampo current-field
/// exchange. Native code writes flattened keys into Campo.ini section [$]. Only keys that
/// have been correlated with the managed field model are changed; unrelated legacy settings
/// remain byte/order-preserved by LegacyIniDocument.
/// </summary>
public static class LegacyCampoCurrentFieldSynchronizer
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static void Write(string campoIniPath, LayoutField field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campoIniPath);
        ArgumentNullException.ThrowIfNull(field);

        var path = Path.GetFullPath(campoIniPath);
        var document = File.Exists(path)
            ? LegacyIniDocument.Load(path)
            : LegacyIniDocument.Parse(string.Empty);
        var store = new CampoCurrentValueStore(document);

        store.Set("Generale", "Y", FormatItalian(field.Ymm, "0.0######"));
        store.Set("Generale", "X", FormatItalian(field.Xmm, "0.0######"));
        store.Set("Generale", "Larghezza", FormatItalian(field.WidthMm, "0.0######"));
        store.Set("Generale", "Altezza", FormatItalian(field.HeightMm, "0.0######"));
        store.Set("Generale", "Livello", field.Level.ToString(CultureInfo.InvariantCulture));
        store.Set("Generale", "Nome", field.Name ?? string.Empty);

        store.Set("Testo", "Font", field.Text.FontName ?? string.Empty);
        store.Set("Testo", "Dimensione", FormatItalian(field.Text.FontSize, "0.######"));
        store.Set("Testo", "Grassetto", VbBool(field.Text.Bold));
        store.Set("Testo", "Corsivo", VbBool(field.Text.Italic));
        store.Set("Testo", "Barrato", VbBool(field.Text.Strikeout));
        store.Set("Testo", "Sinistra", VbBool(field.Text.Alignment == TextAlignment.Left));
        store.Set("Testo", "Destra", VbBool(field.Text.Alignment == TextAlignment.Right));
        store.Set("Testo", "Centrato", VbBool(field.Text.Alignment == TextAlignment.Center));
        store.Set("Testo", "Campo", field.Text.DatabaseField ?? string.Empty);
        store.Set("Testo", "Contenuto", field.Kind == LayoutFieldKind.Text
            ? field.Text.Content ?? string.Empty
            : field.LegacyPayload ?? string.Empty);
        store.Set("Testo", "Fisso", VbBool(field.Text.Fixed));

        store.Set("Immagine", "Campo", field.Image.DatabaseField ?? string.Empty);
        store.Set("Immagine", "Proporzioni", VbBool(field.Image.KeepAspectRatio));
        store.Set("Immagine", "Originali", VbBool(field.Image.KeepOriginalSize));
        store.Set("Immagine", "File", field.Image.File ?? string.Empty);
        store.Set("Immagine", "Estensione", field.Image.DefaultExtension ?? string.Empty);

        store.Set("Aspetto", "Opaco", VbBool(field.Appearance.Opaque));
        store.Set("Aspetto", "Colore", FormatOleColor(field.Appearance.ForeColorOle));
        store.Set("Aspetto", "Sfondo", FormatOleColor(field.Appearance.BackColorOle));
        store.Set("Aspetto", "Bordo", VbBool(field.Appearance.Border));
        store.Set("Aspetto", "Spessore", field.Appearance.BorderWidth.ToString(CultureInfo.InvariantCulture));
        store.Set("Aspetto", "Rotazione", field.Appearance.RotationDegrees.ToString(CultureInfo.InvariantCulture));
        store.Set("Aspetto", "ColoreBordo", FormatOleColor(field.Appearance.BorderColorOle));
        store.Set("Aspetto", "Originali", VbBool(field.Image.KeepOriginalSize));
        store.Set("Aspetto", "Proporzioni", VbBool(field.Image.KeepAspectRatio));

        store.Set("Tabella", "Righe", field.Table.Rows.ToString(CultureInfo.InvariantCulture));
        store.Set("Tabella", "Colonne", field.Table.Columns.ToString(CultureInfo.InvariantCulture));
        store.Set("Tabella", "Intestazione", VbBool(field.Table.Header));
        store.Set("Tabella", "Griglia", VbBool(field.Table.Grid));
        store.Set("Tabella", "Sql", field.Table.Sql ?? string.Empty);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        document.Save(path);
    }

    public static void Read(string campoIniPath, LayoutField field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campoIniPath);
        ArgumentNullException.ThrowIfNull(field);
        var path = Path.GetFullPath(campoIniPath);
        if (!File.Exists(path)) return;

        var document = LegacyIniDocument.Load(path);
        var store = new CampoCurrentValueStore(document);

        field.Ymm = store.GetItalianDouble("Generale", "Y") ?? field.Ymm;
        field.Xmm = store.GetItalianDouble("Generale", "X") ?? field.Xmm;
        field.WidthMm = store.GetItalianDouble("Generale", "Larghezza") ?? field.WidthMm;
        field.HeightMm = store.GetItalianDouble("Generale", "Altezza") ?? field.HeightMm;
        field.Level = store.GetInteger("Generale", "Livello") ?? field.Level;
        field.Name = store.Get("Generale", "Nome", field.Name)?.TrimEnd() ?? field.Name;

        field.Text.FontName = store.Get("Testo", "Font", field.Text.FontName)?.TrimEnd() ?? field.Text.FontName;
        field.Text.FontSize = store.GetItalianDouble("Testo", "Dimensione") ?? field.Text.FontSize;
        field.Text.Bold = store.GetVbBoolean("Testo", "Grassetto") ?? field.Text.Bold;
        field.Text.Italic = store.GetVbBoolean("Testo", "Corsivo") ?? field.Text.Italic;
        field.Text.Strikeout = store.GetVbBoolean("Testo", "Barrato") ?? field.Text.Strikeout;
        field.Text.Fixed = store.GetVbBoolean("Testo", "Fisso") ?? field.Text.Fixed;
        field.Text.DatabaseField = store.Get("Testo", "Campo", field.Text.DatabaseField)?.TrimEnd() ?? field.Text.DatabaseField;
        if (field.Kind == LayoutFieldKind.Text)
        {
            field.Text.Content = store.Get("Testo", "Contenuto", field.Text.Content)?.TrimEnd() ?? field.Text.Content;
            field.LegacyPayload = field.Text.Content;
        }

        var left = store.GetVbBoolean("Testo", "Sinistra");
        var right = store.GetVbBoolean("Testo", "Destra");
        var centered = store.GetVbBoolean("Testo", "Centrato");
        if (centered == true) field.Text.Alignment = TextAlignment.Center;
        else if (right == true) field.Text.Alignment = TextAlignment.Right;
        else if (left == true) field.Text.Alignment = TextAlignment.Left;

        field.Image.DatabaseField = store.Get("Immagine", "Campo", field.Image.DatabaseField)?.TrimEnd() ?? field.Image.DatabaseField;
        field.Image.KeepAspectRatio = store.GetVbBoolean("Immagine", "Proporzioni") ?? field.Image.KeepAspectRatio;
        field.Image.KeepOriginalSize = store.GetVbBoolean("Immagine", "Originali") ?? field.Image.KeepOriginalSize;
        field.Image.DefaultExtension = store.Get("Immagine", "Estensione", field.Image.DefaultExtension)?.TrimEnd() ?? field.Image.DefaultExtension;
        if (field.Kind == LayoutFieldKind.Image)
        {
            field.Image.File = store.Get("Immagine", "File", field.Image.File)?.TrimEnd() ?? field.Image.File;
            field.LegacyPayload = field.Image.File;
        }

        field.Appearance.Opaque = store.GetVbBoolean("Aspetto", "Opaco") ?? field.Appearance.Opaque;
        field.Appearance.ForeColorOle = ParseOleColor(store.Get("Aspetto", "Colore"), field.Appearance.ForeColorOle);
        field.Appearance.BackColorOle = ParseOleColor(store.Get("Aspetto", "Sfondo"), field.Appearance.BackColorOle);
        field.Appearance.Border = store.GetVbBoolean("Aspetto", "Bordo") ?? field.Appearance.Border;
        field.Appearance.BorderWidth = store.GetInteger("Aspetto", "Spessore") ?? field.Appearance.BorderWidth;
        field.Appearance.RotationDegrees = store.GetInteger("Aspetto", "Rotazione") ?? field.Appearance.RotationDegrees;
        field.Appearance.BorderColorOle = ParseOleColor(store.Get("Aspetto", "ColoreBordo"), field.Appearance.BorderColorOle);

        field.Table.Rows = store.GetInteger("Tabella", "Righe") ?? field.Table.Rows;
        field.Table.Columns = store.GetInteger("Tabella", "Colonne") ?? field.Table.Columns;
        field.Table.Header = store.GetVbBoolean("Tabella", "Intestazione") ?? field.Table.Header;
        field.Table.Grid = store.GetVbBoolean("Tabella", "Griglia") ?? field.Table.Grid;
        field.Table.Sql = store.Get("Tabella", "Sql", field.Table.Sql)?.TrimEnd() ?? field.Table.Sql;
    }

    private static string VbBool(bool value) => value ? "-1" : "0";

    private static string FormatItalian(double value, string format) => value.ToString(format, Italian);

    private static string FormatOleColor(int value) => "&H" + unchecked((uint)value).ToString("X", CultureInfo.InvariantCulture);

    private static int ParseOleColor(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var value = raw.Trim();
        if (value.StartsWith("&H", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return unchecked((int)hex);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
