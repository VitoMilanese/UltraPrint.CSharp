using System.Data;
using System.Globalization;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Operators displayed by UltraPrint 2.2.115 FormQBE. The legacy form turns these choices into
/// DAO FindFirst criteria. The managed matcher preserves the same visible operator tokens while
/// evaluating loaded DataTable rows so it also works for CSV/text and modern ACE-backed sources.
/// </summary>
public static class LegacyQbeMatcher
{
    public static IReadOnlyList<LegacyQbeOperator> Operators { get; } =
    [
        new("*..", "*.. Che inizia per.."),
        new(".*.", ".*. Che contiene .."),
        new("..*", "..* Che finisce per.."),
        new("=", "=   Che uguale a"),
        new("<>", "<>  Che diverso da"),
        new("*-*", "*-* Compreso Tra"),
        new(">", ">   Maggiore di"),
        new(">=", ">=  Maggiore o uguale"),
        new("<", "<   Minore di"),
        new("<=", "<=  Minore o uguale"),
        new("x--x", "x--x Compreso cifra-cifra"),
        new("Vero", "Vero  Che Vero"),
        new("Falso", "Falso Che Falso")
    ];

    /// <summary>
    /// Returns the legacy one-based absolute position of the first matching row, or zero when no
    /// row matches. Native Pescarecord returns DAO Recordset.AbsolutePosition + 1 after FindFirst.
    /// </summary>
    public static int FindFirst(DataTable records, string fieldName, string operatorToken, string? operand)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorToken);
        if (!records.Columns.Contains(fieldName))
            throw new ArgumentException($"Unknown QBE field '{fieldName}'.", nameof(fieldName));

        var column = records.Columns[fieldName]!;
        for (var index = 0; index < records.Rows.Count; index++)
        {
            if (Matches(records.Rows[index][column], column.DataType, operatorToken, operand))
                return index + 1;
        }
        return 0;
    }

    private static bool Matches(object? raw, Type dataType, string token, string? operand)
    {
        var value = raw is null || raw is DBNull ? null : raw;
        operand ??= string.Empty;

        if (string.Equals(token, "Vero", StringComparison.OrdinalIgnoreCase))
            return TryBoolean(value, out var trueValue) && trueValue;
        if (string.Equals(token, "Falso", StringComparison.OrdinalIgnoreCase))
            return TryBoolean(value, out var falseValue) && !falseValue;

        if (token is "*-*" or "x--x")
        {
            if (!TrySplitRange(operand, out var lower, out var upper)) return false;
            return Compare(value, dataType, lower) >= 0 && Compare(value, dataType, upper) <= 0;
        }

        var text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        if (token == "*..") return text.StartsWith(operand, StringComparison.CurrentCultureIgnoreCase);
        if (token == ".*.") return text.Contains(operand, StringComparison.CurrentCultureIgnoreCase);
        if (token == "..*") return text.EndsWith(operand, StringComparison.CurrentCultureIgnoreCase);

        var comparison = Compare(value, dataType, operand);
        return token switch
        {
            "=" => comparison == 0,
            "<>" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            _ => false
        };
    }

    private static int Compare(object? value, Type dataType, string operand)
    {
        if (value is null)
            return string.IsNullOrEmpty(operand) ? 0 : -1;

        var targetType = Nullable.GetUnderlyingType(dataType) ?? dataType;
        if (targetType == typeof(DateTime) || value is DateTime)
        {
            if (TryDate(value, out var leftDate) && TryDate(operand, out var rightDate))
                return leftDate.CompareTo(rightDate);
        }

        if (IsNumericType(targetType) || IsNumericValue(value))
        {
            if (TryDecimal(value, out var leftNumber) && TryDecimal(operand, out var rightNumber))
                return leftNumber.CompareTo(rightNumber);
        }

        if (targetType == typeof(bool) || value is bool)
        {
            if (TryBoolean(value, out var leftBoolean) && TryBoolean(operand, out var rightBoolean))
                return leftBoolean.CompareTo(rightBoolean);
        }

        var leftText = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        return string.Compare(leftText, operand, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool TrySplitRange(string text, out string lower, out string upper)
    {
        var delimiter = text.IndexOf("--", StringComparison.Ordinal);
        if (delimiter < 0)
        {
            lower = string.Empty;
            upper = string.Empty;
            return false;
        }
        lower = text[..delimiter].Trim();
        upper = text[(delimiter + 2)..].Trim();
        return lower.Length > 0 && upper.Length > 0;
    }

    private static bool TryBoolean(object? value, out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim();
        if (bool.TryParse(text, out result)) return true;
        if (string.Equals(text, "Vero", StringComparison.OrdinalIgnoreCase) || text == "1" || text == "-1")
        {
            result = true;
            return true;
        }
        if (string.Equals(text, "Falso", StringComparison.OrdinalIgnoreCase) || text == "0")
        {
            result = false;
            return true;
        }
        result = false;
        return false;
    }

    private static bool TryDecimal(object? value, out decimal result)
    {
        if (value is null)
        {
            result = 0;
            return false;
        }
        try
        {
            if (value is IConvertible && value is not string)
            {
                result = Convert.ToDecimal(value, CultureInfo.CurrentCulture);
                return true;
            }
        }
        catch { }

        var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out result) ||
               decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out result) ||
               decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryDate(object? value, out DateTime result)
    {
        if (value is DateTime date)
        {
            result = date;
            return true;
        }
        var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(text, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out result) ||
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static bool IsNumericValue(object value) => IsNumericType(value.GetType());

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or
            TypeCode.Double or TypeCode.Decimal;
    }
}

public sealed record LegacyQbeOperator(string Token, string Label)
{
    public override string ToString() => Label;
}
