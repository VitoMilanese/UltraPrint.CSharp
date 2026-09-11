using System.Globalization;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Pure result of resolving one matched legacy +(CounterName) token. It records the
/// observable counter mutation and replacement text without reading or writing Contatori.dat.
/// </summary>
public readonly record struct LegacyCounterTokenResolution(
    bool Matched,
    int? UpdatedValue,
    string Replacement,
    bool PersistsUpdatedCounterTable);

/// <summary>
/// Recovered native semantics behind the +(CounterName) expansion used by UltraPrint 2.2.115.
/// The native resolver is helper 0x005F5A90 and operates on the same counter table edited by
/// frmContatori. This model intentionally performs no file I/O; it captures the proven record,
/// increment, formatting, replacement, and persistence contract for regression testing.
/// </summary>
public static class LegacyCounterTokenSemantics
{
    public const int ResolverNativeAddress = 0x5F5A90;
    public const int LoadCounterTableNativeAddress = 0x5F5E70;
    public const int SaveCounterTableNativeAddress = 0x5F60C0;
    public const int PlusTokenExpansionNativeAddress = 0x6079D0;
    public const int ReplaceAllNativeAddress = 0x4A36F0;
    public const int LegacyZeriNativeAddress = 0x4A1D40;

    public const string PersistenceFileName = "Contatori.dat";
    public const string OpeningDelimiter = "+(";
    public const string ClosingDelimiter = ")";

    public const int FirstCounterIndex = 1;
    public const int LastCounterIndex = 99;
    public const int CounterRecordStrideBytes = 32;
    public const int CounterNameOffset = 0x00;
    public const int CounterNameFixedLengthCharacters = 10;
    public const int DigitsOffset = 0x14;
    public const int ZeroPaddingBooleanOffset = 0x16;
    public const int CurrentValueOffset = 0x1C;

    public const int DefaultDigits = 6;
    public const bool DefaultZeroPadding = true;
    public const int SmartDriverIncrement = 1;

    /// <summary>
    /// Native 0x005F5A90 initializes the return Variant to empty and leaves the table
    /// untouched when no trimmed 10-character counter name matches the token body.
    /// </summary>
    public static LegacyCounterTokenResolution NoMatch =>
        new(
            Matched: false,
            UpdatedValue: null,
            Replacement: string.Empty,
            PersistsUpdatedCounterTable: false);

    /// <summary>
    /// Models the already-matched record branch. Native code adds the supplied increment
    /// to the current Long before formatting/substitution, writes the Long back to +0x1C,
    /// and immediately calls the same save helper used by frmContatori.Ok.
    /// </summary>
    public static LegacyCounterTokenResolution ResolveMatchedCounter(
        int currentValue,
        int increment,
        int digits,
        bool zeroPadding)
    {
        var updatedValue = checked(currentValue + increment);
        var replacement = updatedValue.ToString(CultureInfo.InvariantCulture);
        if (zeroPadding)
            replacement = FormatWithLegacyZeri(replacement, digits);

        return new(
            Matched: true,
            UpdatedValue: updatedValue,
            Replacement: replacement,
            PersistsUpdatedCounterTable: true);
    }

    /// <summary>
    /// Reproduces Funzioni.Zeri structurally: Right(String(width, "0") + Trim(value), width).
    /// In particular, values wider than the selected width are truncated on the left just
    /// as in the VB6 routine rather than being allowed to grow beyond the configured digits.
    /// </summary>
    public static string FormatWithLegacyZeri(string value, int width)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        if (width == 0) return string.Empty;

        var combined = new string('0', width) + value.Trim();
        return combined[^width..];
    }

    /// <summary>Builds the exact token syntax consumed by native helper 0x006079D0.</summary>
    public static string BuildToken(string counterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(counterName);
        return OpeningDelimiter + counterName + ClosingDelimiter;
    }

    /// <summary>
    /// Funzioni.Sostituisci (0x004A36F0) loops until no binary InStr match remains. The
    /// counter resolver is called once before that replacement call, so all identical exact
    /// token occurrences in the current text receive one resolved value from one increment.
    /// </summary>
    public static string ReplaceAllResolvedTokenOccurrences(
        string source,
        string counterName,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacement);
        var token = BuildToken(counterName);
        return source.Replace(token, replacement, StringComparison.Ordinal);
    }
}
