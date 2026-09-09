using Microsoft.VisualBasic;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Explicit marker for a VB6 Optional Variant that was not supplied. The recovered
/// Funzioni.Vbscript checks rtcIsMissing from left to right and stops passing
/// arguments at the first Missing value.
/// </summary>
public static class LegacyScriptMissing
{
    public static object Value => Type.Missing;

    public static bool IsMissing(object? value) => ReferenceEquals(value, Type.Missing);
}

/// <summary>
/// Recovered invocation contract for Funzioni.Vbscript (0x004B7D00, vtable 0x84C).
/// Native code accepts a prefix, an event/base name and up to seven optional
/// Variant arguments. The normalized string is passed directly to ScriptControl.Run;
/// there is no separate UltraPrint procedure/wildcard lookup loop in Vbscript.
/// </summary>
public static class LegacyScriptInvocation
{
    public const int MaximumOptionalArguments = 7;
    public const string NoCodeSentinel = "NO CODE";

    public static string BuildProcedureName(string? prefix, string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        prefix ??= string.Empty;
        var procedureName = prefix.Length == 0
            ? eventName
            : prefix + "_" + eventName;

        // Native Vbscript calls VB Val(prefix) and, when the result is positive,
        // replaces the composite name with P_<prefix>.
        if (LegacyVb6ValueParser.Val(prefix) > 0)
            procedureName = "P_" + prefix;

        // These are two direct calls to the recovered case-sensitive Sostituisci
        // helper. X is therefore deliberately not treated as lowercase x. Any '*'
        // produced here remains literal input to ScriptControl.Run.
        procedureName = procedureName.Replace(" ", string.Empty, StringComparison.Ordinal);
        procedureName = procedureName.Replace("X", "*", StringComparison.Ordinal);
        return procedureName;
    }

    // Retained for source compatibility with the first recovery implementation.
    // The native binary proves this is a Run procedure name, not a locally-resolved pattern.
    public static string BuildProcedurePattern(string? prefix, string eventName) =>
        BuildProcedureName(prefix, eventName);

    public static void ValidateOptionalArguments(IReadOnlyCollection<object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > MaximumOptionalArguments)
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                $"UltraPrint Vbscript accepts at most {MaximumOptionalArguments} optional arguments.");
    }

    /// <summary>
    /// Mirrors the native rtcIsMissing cascade. Once one optional Variant is
    /// Missing, that argument and every argument to its right are omitted from
    /// ScriptControl.Run even if a later slot contains a value.
    /// </summary>
    public static object?[] GetSuppliedOptionalArguments(IReadOnlyList<object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ValidateOptionalArguments(arguments);

        var suppliedCount = 0;
        while (suppliedCount < arguments.Count && !LegacyScriptMissing.IsMissing(arguments[suppliedCount]))
            suppliedCount++;

        if (suppliedCount == 0) return Array.Empty<object?>();
        var result = new object?[suppliedCount];
        for (var i = 0; i < suppliedCount; i++) result[i] = arguments[i];
        return result;
    }
}

/// <summary>
/// Small boundary around the VB runtime conversions that the recovered native code
/// uses directly. Keeping this isolated makes it explicit where VB6 coercion
/// semantics are part of the compatibility contract.
/// </summary>
internal static class LegacyVb6ValueParser
{
    public static double Val(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        try { return Conversion.Val(value); }
        catch { return 0; }
    }

    public static string Chr(double value)
    {
        // VB converts the numeric Variant before producing the ANSI character.
        // Convert.ToInt32 uses the same midpoint-to-even rounding used by VB's
        // integer coercion for the uncommon case of a non-integral replacement.
        return Strings.Chr(Convert.ToInt32(value)).ToString();
    }
}
