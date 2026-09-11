using System.Globalization;
using System.Text;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Native frmCarta.Record2Card DataField contract recovered from UltraPrint 2.2.115.
/// The in-memory field UDT stores DataField at +0x19A as String * 28; VB6 binary
/// serialization places the same fixed string at byte 230 of the 264-byte .ly record.
/// </summary>
public static class LegacyNativeDataFieldSemantics
{
    public const int Record2CardNativeAddress = 0x553FA0;
    public const int NativeFieldRecordStrideBytes = 0x1D8;
    public const int NativeFieldTypeOffset = 0x28;
    public const int NativePayloadOffset = 0x3C;
    public const int NativeDataFieldOffset = 0x19A;
    public const int NativeFieldLevelOffset = 0x1D6;

    public const int SerializedFieldTypeOffset = 20;
    public const int SerializedPayloadOffset = 38;
    public const int SerializedPayloadLength = 124;
    public const int SerializedDataFieldOffset = 230;
    public const int SerializedDataFieldLength = 28;
    public const int SerializedFieldLevelOffset = 262;

    /// <summary>
    /// Reads the exact persisted DataField text without mutating or normalizing the raw record.
    /// The legacy fixed string is ANSI in the .ly file; Latin-1 gives a lossless byte-to-char map.
    /// </summary>
    public static string ReadDataField(LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var record = field.LegacyRecordTemplate;
        if (record.Length < SerializedDataFieldOffset + SerializedDataFieldLength)
            return string.Empty;

        return Encoding.Latin1
            .GetString(record, SerializedDataFieldOffset, SerializedDataFieldLength)
            .TrimEnd('\0', ' ')
            .Trim();
    }

    /// <summary>
    /// Resolves a non-empty native DataField against the supplied current record. Record2Card
    /// uses a direct Fields(DataField) lookup when no '[' is present. If '[' is present, it sends
    /// the whole fixed DataField string through the already recovered 0x00606910 bracket resolver.
    /// Missing/Null fields therefore resolve to empty text rather than retaining stale payload.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyDictionary<string, object?> record,
        string? dataField,
        out string resolved)
    {
        ArgumentNullException.ThrowIfNull(record);
        var expression = dataField?.Trim() ?? string.Empty;
        if (expression.Length == 0)
        {
            resolved = string.Empty;
            return false;
        }

        if (!expression.Contains(LegacyBracketRecordFieldSemantics.OpeningDelimiter, StringComparison.Ordinal))
        {
            resolved = TryGetCaseInsensitive(record, expression, out var raw)
                ? LegacyRecordBinder.ToDisplayString(raw)
                : string.Empty;
            return true;
        }

        resolved = ExpandBracketExpression(record, expression);
        return true;
    }

    public static string ExpandBracketExpression(
        IReadOnlyDictionary<string, object?> record,
        string expression)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(expression);

        var result = expression;
        while (true)
        {
            var open = result.IndexOf('[', StringComparison.Ordinal);
            if (open < 0) break;
            var close = result.IndexOf(']', open + 1);
            if (close < 0 || close == open + 1) break;

            var body = result.Substring(open + 1, close - open - 1);
            var parts = body.Split(',', LegacyBracketRecordFieldSemantics.MaximumParsedArguments,
                StringSplitOptions.None);
            var fieldToken = parts[0].Trim();
            if (fieldToken.Length == 0) break;

            var start = parts.Length > 1 ? ParseLegacyValToInt32(parts[1]) : 0;
            var length = parts.Length > 2 ? ParseLegacyValToInt32(parts[2]) : 0;
            var found = TryGetCaseInsensitive(record, fieldToken, out var raw);
            var fieldValue = found && raw is not null && raw is not DBNull
                ? LegacyRecordBinder.ToDisplayString(raw)
                : null;
            var replacement = LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(
                found,
                fieldValue,
                start,
                length);

            var previous = result;
            result = LegacyBracketRecordFieldSemantics.ReplaceResolvedTokenOccurrences(
                result,
                body,
                LegacyBracketRecordSourceKind.FrmDatabase,
                replacement);
            if (string.Equals(previous, result, StringComparison.Ordinal)) break;
        }

        return result;
    }

    private static int ParseLegacyValToInt32(string value)
    {
        var text = value.TrimStart();
        if (text.Length == 0) return 0;

        var sign = 1;
        var index = 0;
        if (text[index] is '+' or '-')
        {
            if (text[index] == '-') sign = -1;
            index++;
        }

        if (index + 2 <= text.Length && text[index] == '&' &&
            (text[index + 1] is 'H' or 'h'))
        {
            index += 2;
            var start = index;
            while (index < text.Length && Uri.IsHexDigit(text[index])) index++;
            if (index == start) return 0;
            return int.TryParse(text[start..index], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? checked(sign * hex)
                : 0;
        }

        var numericStart = index;
        var seenDigit = false;
        var seenDecimal = false;
        while (index < text.Length)
        {
            var ch = text[index];
            if (char.IsDigit(ch))
            {
                seenDigit = true;
                index++;
                continue;
            }
            if (ch == '.' && !seenDecimal)
            {
                seenDecimal = true;
                index++;
                continue;
            }
            break;
        }

        if (!seenDigit) return 0;
        var numeric = text[numericStart..index];
        if (!double.TryParse(numeric, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return 0;
        try
        {
            return checked(Convert.ToInt32(sign * number, CultureInfo.InvariantCulture));
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetCaseInsensitive(
        IReadOnlyDictionary<string, object?> record,
        string key,
        out object? value)
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
}
