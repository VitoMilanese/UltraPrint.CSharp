using System.Data;
using System.Globalization;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Managed replacement for the recovered Record2Card / RecordToReport binding path.
/// Native DataField metadata embedded in each .ly field record wins, followed by an explicit
/// managed session override. As a compatibility fallback a field named %COLUMN% is also resolved
/// when the current record really contains COLUMN.
/// </summary>
public static class LegacyRecordBinder
{
    public static IReadOnlyDictionary<string, object?> Snapshot(DataRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in row.Table.Columns)
            result[column.ColumnName] = row.IsNull(column) ? null : row[column];
        return result;
    }

    public static IReadOnlyDictionary<string, object?> Snapshot(DataRowView rowView) => Snapshot(rowView.Row);

    public static CardLayout CreateBoundLayout(
        CardLayout source,
        IReadOnlyDictionary<string, object?> record,
        IReadOnlyDictionary<int, string>? bindingOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(record);

        var clone = new CardLayout
        {
            SourcePath = source.SourcePath,
            Name = source.Name,
            WidthMm = source.WidthMm,
            HeightMm = source.HeightMm,
            Dpi = source.Dpi,
            FrontBackground = source.FrontBackground,
            BackBackground = source.BackBackground,
            DatabasePath = source.DatabasePath,
            Sql = source.Sql,
            ScriptApplication = source.ScriptApplication,
            UseWin32ApiPrinting = source.UseWin32ApiPrinting
        };

        foreach (var original in source.Fields.OrderBy(x => x.Index))
        {
            var field = CloneField(original);
            var sessionBinding = bindingOverrides is not null && bindingOverrides.TryGetValue(original.Index, out var overrideValue)
                ? overrideValue
                : null;
            switch (field.Kind)
            {
                case LayoutFieldKind.Text:
                    if (TryResolveFieldValue(record, field, sessionBinding, out var textValue))
                        field.Text.Content = textValue;
                    break;

                case LayoutFieldKind.Image:
                    if (TryResolveFieldValue(record, field, sessionBinding, out var imageValue))
                    {
                        if (!string.IsNullOrWhiteSpace(imageValue))
                            field.Image.File = ResolvePhotoPath(source, imageValue, field.Image.DefaultExtension) ?? imageValue;
                        else
                            field.Image.File = string.Empty;
                    }
                    break;

                case LayoutFieldKind.Barcode:
                    if (TryResolveFieldValue(record, field, sessionBinding, out var barcodeValue))
                        field.LegacyPayload = barcodeValue;
                    break;
            }
            clone.Fields.Add(field);
        }

        return clone;
    }

    public static bool TryResolve(
        IReadOnlyDictionary<string, object?> record,
        string? explicitBinding,
        string? fieldName,
        out object? value)
    {
        foreach (var candidate in BindingCandidates(explicitBinding, fieldName))
        {
            if (TryGetCaseInsensitive(record, candidate, out value)) return true;
        }
        value = null;
        return false;
    }

    public static string ToDisplayString(object? value)
    {
        if (value is null || value is DBNull) return string.Empty;
        return value switch
        {
            DateTime date => date.ToString(CultureInfo.CurrentCulture),
            byte[] bytes => $"[{bytes.Length} bytes]",
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };
    }

    private static bool TryResolveFieldValue(
        IReadOnlyDictionary<string, object?> record,
        LayoutField field,
        string? sessionBinding,
        out string value)
    {
        // The Database / Records workspace override is an explicit current-session choice and
        // therefore intentionally wins over persisted legacy metadata.
        if (!string.IsNullOrWhiteSpace(sessionBinding))
            return LegacyNativeDataFieldSemantics.TryResolve(record, sessionBinding, out value);

        // frmCarta.Record2Card reads the fixed DataField String * 28 directly from the field UDT.
        // Reading from the preserved 264-byte raw template avoids inventing a new .ly model offset.
        var nativeDataField = LegacyNativeDataFieldSemantics.ReadDataField(field);
        if (!string.IsNullOrWhiteSpace(nativeDataField))
            return LegacyNativeDataFieldSemantics.TryResolve(record, nativeDataField, out value);

        // Older managed builds copied %COLUMN% field names into Text/Image.DatabaseField as a
        // convenience. Do not mistake that inferred value for a persisted native DataField.
        var modelBinding = field.Kind == LayoutFieldKind.Image
            ? field.Image.DatabaseField
            : field.Text.DatabaseField;
        if (!string.IsNullOrWhiteSpace(modelBinding) && !IsInferredPlaceholderBinding(field, modelBinding))
            return LegacyNativeDataFieldSemantics.TryResolve(record, modelBinding, out value);

        if (TryResolve(record, explicitBinding: null, field.Name, out var fallback))
        {
            value = ToDisplayString(fallback);
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsInferredPlaceholderBinding(LayoutField field, string binding) =>
        field.Name.Length >= 3 &&
        field.Name[0] == '%' && field.Name[^1] == '%' &&
        string.Equals(field.Name, binding, StringComparison.Ordinal);

    private static IEnumerable<string> BindingCandidates(string? explicitBinding, string? fieldName)
    {
        if (!string.IsNullOrWhiteSpace(explicitBinding))
        {
            yield return explicitBinding.Trim();
            var normalized = TrimPercent(explicitBinding);
            if (!string.Equals(normalized, explicitBinding.Trim(), StringComparison.Ordinal)) yield return normalized;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(fieldName) && fieldName.Length >= 3 && fieldName[0] == '%' && fieldName[^1] == '%')
            yield return TrimPercent(fieldName);
    }

    private static string TrimPercent(string value)
    {
        var result = value.Trim();
        while (result.Length > 0 && result[0] == '%') result = result[1..];
        while (result.Length > 0 && result[^1] == '%') result = result[..^1];
        return result.Trim();
    }

    private static bool TryGetCaseInsensitive(IReadOnlyDictionary<string, object?> record, string key, out object? value)
    {
        if (record.TryGetValue(key, out value)) return true;
        foreach (var pair in record)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }

    private static string? ResolvePhotoPath(CardLayout layout, string storedValue, string? defaultExtension)
    {
        var value = storedValue.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (value.Length == 0) return null;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(value)) && !string.IsNullOrWhiteSpace(defaultExtension))
        {
            var extension = defaultExtension.Trim();
            if (!extension.StartsWith('.')) extension = "." + extension;
            value += extension;
        }

        if (Path.IsPathRooted(value)) return FindCaseInsensitive(value);
        if (string.IsNullOrWhiteSpace(layout.SourcePath)) return null;

        var layoutDirectory = Path.GetDirectoryName(Path.GetFullPath(layout.SourcePath))!;
        var applicationDirectory = Directory.GetParent(layoutDirectory)?.FullName ?? layoutDirectory;
        var fileName = SafeFileName(value);
        var candidates = new List<string>
        {
            Path.Combine(layoutDirectory, value),
            Path.Combine(applicationDirectory, value),
            Path.Combine(applicationDirectory, "Foto", fileName),
            Path.Combine(layoutDirectory, "Foto", fileName),
            Path.Combine(layoutDirectory, fileName)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = FindCaseInsensitive(candidate);
            if (resolved is not null) return resolved;
        }
        return null;
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return path; }
    }

    private static string? FindCaseInsensitive(string candidate)
    {
        try
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            var directory = Path.GetDirectoryName(candidate);
            var fileName = Path.GetFileName(candidate);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || string.IsNullOrWhiteSpace(fileName)) return null;
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static LayoutField CloneField(LayoutField source)
    {
        var clone = new LayoutField
        {
            Index = source.Index,
            Name = source.Name,
            Kind = source.Kind,
            Side = source.Side,
            LegacyTypeCode = source.LegacyTypeCode,
            LegacyPayload = source.LegacyPayload,
            LegacyRecordTemplate = source.LegacyRecordTemplate.ToArray(),
            Xmm = source.Xmm,
            Ymm = source.Ymm,
            WidthMm = source.WidthMm,
            HeightMm = source.HeightMm,
            Level = source.Level
        };

        clone.Text.Content = source.Text.Content;
        clone.Text.DatabaseField = source.Text.DatabaseField;
        clone.Text.Fixed = source.Text.Fixed;
        clone.Text.FontName = source.Text.FontName;
        clone.Text.FontSize = source.Text.FontSize;
        clone.Text.Bold = source.Text.Bold;
        clone.Text.Italic = source.Text.Italic;
        clone.Text.Strikeout = source.Text.Strikeout;
        clone.Text.Alignment = source.Text.Alignment;

        clone.Appearance.Opaque = source.Appearance.Opaque;
        clone.Appearance.ForeColorOle = source.Appearance.ForeColorOle;
        clone.Appearance.BackColorOle = source.Appearance.BackColorOle;
        clone.Appearance.Border = source.Appearance.Border;
        clone.Appearance.BorderWidth = source.Appearance.BorderWidth;
        clone.Appearance.BorderColorOle = source.Appearance.BorderColorOle;
        clone.Appearance.RotationDegrees = source.Appearance.RotationDegrees;

        clone.Image.File = source.Image.File;
        clone.Image.DatabaseField = source.Image.DatabaseField;
        clone.Image.KeepOriginalSize = source.Image.KeepOriginalSize;
        clone.Image.KeepAspectRatio = source.Image.KeepAspectRatio;
        clone.Image.DefaultExtension = source.Image.DefaultExtension;

        clone.Table.Rows = source.Table.Rows;
        clone.Table.Columns = source.Table.Columns;
        clone.Table.Header = source.Table.Header;
        clone.Table.Grid = source.Table.Grid;
        clone.Table.Widths = source.Table.Widths;
        clone.Table.Sql = source.Table.Sql;
        return clone;
    }
}
