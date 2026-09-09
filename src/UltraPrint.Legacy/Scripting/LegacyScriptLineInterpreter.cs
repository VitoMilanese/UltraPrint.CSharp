using System.Globalization;
using UltraPrint.Legacy.Configuration;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Context needed by the confirmed subset of native Funzioni.Interpretariga
/// (0x004B1DF0, vtable 0x840).
/// </summary>
public sealed record LegacyScriptInterpretationContext(
    string ApplicationDirectory,
    string ExecutableName,
    ILegacyScriptInteraction? Interaction = null,
    ILegacyScriptVariableStore? Variables = null)
{
    public string RootDirectory => Path.GetFullPath(ApplicationDirectory);
}

/// <summary>
/// UI-dependent operations directly observed in Interpretariga. Returning null or
/// an empty string means that the user cancelled, matching the native MainForm
/// cancellation path used by ?(), @GETFILE(), @DIRECTORY() and @COMPUTER().
/// </summary>
public interface ILegacyScriptInteraction
{
    string? Prompt(string prompt);
    string? SelectFile(string firstArgument, string secondArgument);
    string? SelectDirectory(string prompt);
    string? SelectComputer(string prompt);
}

/// <summary>
/// Boundary for Funzioni.GetVariabile / SetVariabile. The native implementation
/// uses two parallel Variant arrays and compares trimmed, upper-cased names.
/// </summary>
public interface ILegacyScriptVariableStore
{
    string? Get(string name);
    void Set(string name, string value);
}

public sealed record LegacyScriptInterpretationResult(
    string Text,
    bool Cancelled = false,
    bool RequiresInteraction = false,
    bool HasUnresolvedNativeMacros = false);

/// <summary>
/// Managed compatibility implementation of only the Interpretariga branches whose
/// semantics have been recovered strongly enough from UltraPrint 2.2.115 native code.
/// Unknown branches are preserved verbatim instead of being guessed.
/// </summary>
public static class LegacyScriptLineInterpreter
{
    private const string IniSection = "$";
    private const string NativeVariableDelimiter = "\u00B7";

    public static LegacyScriptInterpretationResult InterpretConfirmed(
        string line,
        LegacyScriptInterpretationContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ApplicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ExecutableName);

        // Native Interpretariga first calls SostituisciRiga, then processes these
        // macro families in the order below.
        var text = LegacyScriptSubstitutionTable.Apply(line, context.RootDirectory);
        text = InterpretVariableMacros(text, context);
        text = InterpretIniMacros(text, context);
        var requiresInteraction = false;

        var interactive = ReplaceInteractiveMacros(
            text,
            "?(",
            context.Interaction,
            body => context.Interaction!.Prompt(body),
            ref requiresInteraction);
        if (interactive.Cancelled) return Finish(interactive.Text, true, requiresInteraction);
        text = interactive.Text;

        interactive = ReplaceInteractiveMacros(
            text,
            "@GETFILE(",
            context.Interaction,
            body =>
            {
                var tokenizer = new LegacyWordTokenizer(body);
                var first = tokenizer.Next(",;");
                var second = tokenizer.Next(";,");
                return context.Interaction!.SelectFile(first, second);
            },
            ref requiresInteraction);
        if (interactive.Cancelled) return Finish(interactive.Text, true, requiresInteraction);
        text = interactive.Text;

        interactive = ReplaceInteractiveMacros(
            text,
            "@DIRECTORY(",
            context.Interaction,
            body => context.Interaction!.SelectDirectory(body),
            ref requiresInteraction);
        if (interactive.Cancelled) return Finish(interactive.Text, true, requiresInteraction);
        text = interactive.Text;

        interactive = ReplaceInteractiveMacros(
            text,
            "@COMPUTER(",
            context.Interaction,
            body => context.Interaction!.SelectComputer(body),
            ref requiresInteraction);
        if (interactive.Cancelled) return Finish(interactive.Text, true, requiresInteraction);
        text = interactive.Text;

        return Finish(text, false, requiresInteraction);
    }

    private static string InterpretVariableMacros(string line, LegacyScriptInterpretationContext context)
    {
        var text = line;
        var searchFrom = 0;
        while (TryFindMacro(text, "@(", searchFrom, out var macro))
        {
            if (context.Variables is null)
            {
                searchFrom = macro.EndExclusive;
                continue;
            }

            // Native GetVariabile returns Empty when the name is missing. Parola then
            // extracts the first token using ANSI byte 0xB7 (middle dot) as delimiter.
            var rawValue = context.Variables.Get(macro.Body) ?? string.Empty;
            var tokenizer = new LegacyWordTokenizer(rawValue);
            var value = tokenizer.Next(NativeVariableDelimiter).Trim();

            // The native code checks the right-most character for '+', removes every
            // '+' through Sostituisci, converts to I4, increments, substitutes the new
            // value and stores it back through SetVariabile.
            if (value.EndsWith('+'))
            {
                value = value.Replace("+", string.Empty, StringComparison.Ordinal);
                value = checked(ParseLegacyInteger(value) + 1).ToString(CultureInfo.InvariantCulture);
                context.Variables.Set(macro.Body, value);
            }

            text = ReplaceRange(text, macro.Start, macro.EndExclusive - macro.Start, value);
            searchFrom = macro.Start + value.Length;
        }

        return text;
    }

    private static string InterpretIniMacros(string line, LegacyScriptInterpretationContext context)
    {
        var text = line;
        var searchFrom = 0;
        while (TryFindMacro(text, "$(", searchFrom, out var macro))
        {
            var dot = macro.Body.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                // The native branch only enters its INI handling after finding '.'.
                searchFrom = macro.EndExclusive;
                continue;
            }

            var scope = macro.Body[..dot];
            var key = macro.Body[(dot + 1)..];
            var increment = key.EndsWith('+');
            if (increment) key = key[..^1];

            var iniPath = GetIniPath(context, scope);
            var ini = File.Exists(iniPath)
                ? LegacyIniDocument.Load(iniPath)
                : LegacyIniDocument.Parse(string.Empty);
            var value = ini.Get(IniSection, key, string.Empty) ?? string.Empty;

            if (increment)
            {
                value = checked(ParseLegacyInteger(value) + 1).ToString(CultureInfo.InvariantCulture);
                ini.Set(IniSection, key, value);
                ini.Save(iniPath);
            }

            text = ReplaceRange(text, macro.Start, macro.EndExclusive - macro.Start, value);
            searchFrom = macro.Start + value.Length;
        }

        return text;
    }

    public static string GetIniPath(LegacyScriptInterpretationContext context, string scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.RootDirectory;
        return scope.Length == 0
            ? Path.Combine(root, context.ExecutableName + ".Ini")
            : Path.Combine(root, "App", scope, scope + ".Ini");
    }

    private static int ParseLegacyInteger(string value)
    {
        // VB's numeric conversion honors the current locale. Try that first and
        // invariant culture second so recovered ANSI-era values remain usable.
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var result))
            return result;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return result;
        throw new FormatException($"Legacy numeric value '{value}' is not an Int32.");
    }

    private static LegacyScriptInterpretationResult ReplaceInteractiveMacros(
        string input,
        string prefix,
        ILegacyScriptInteraction? interaction,
        Func<string, string?> replacementFactory,
        ref bool requiresInteraction)
    {
        var text = input;
        var searchFrom = 0;
        while (TryFindMacro(text, prefix, searchFrom, out var macro))
        {
            if (interaction is null)
            {
                requiresInteraction = true;
                searchFrom = macro.EndExclusive;
                continue;
            }

            var replacement = replacementFactory(macro.Body);
            if (string.IsNullOrEmpty(replacement))
                return Finish(text, true, requiresInteraction);

            text = ReplaceRange(text, macro.Start, macro.EndExclusive - macro.Start, replacement);
            searchFrom = macro.Start + replacement.Length;
        }

        return Finish(text, false, requiresInteraction);
    }

    private static LegacyScriptInterpretationResult Finish(
        string text,
        bool cancelled,
        bool requiresInteraction) =>
        new(
            text,
            cancelled,
            requiresInteraction,
            HasUnresolvedNativeMacros(text));

    private static bool HasUnresolvedNativeMacros(string text) =>
        text.Contains("@(", StringComparison.OrdinalIgnoreCase);

    private static bool TryFindMacro(string text, string prefix, int startIndex, out MacroSpan macro)
    {
        var start = text.IndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            macro = default;
            return false;
        }

        var bodyStart = start + prefix.Length;
        var close = text.IndexOf(')', bodyStart);
        if (close < 0)
        {
            macro = default;
            return false;
        }

        macro = new MacroSpan(start, close + 1, text[bodyStart..close]);
        return true;
    }

    private static string ReplaceRange(string value, int start, int length, string replacement) =>
        value[..start] + replacement + value[(start + length)..];

    private readonly record struct MacroSpan(int Start, int EndExclusive, string Body);
}

/// <summary>
/// Stateful tokenizer matching the recovered Funzioni.Parola behavior: a non-empty
/// source resets the cursor, a word is read until any delimiter character is found,
/// and subsequent delimiter characters are skipped.
/// </summary>
internal sealed class LegacyWordTokenizer
{
    private readonly string _source;
    private int _position;

    public LegacyWordTokenizer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string Next(string delimiterCharacters)
    {
        ArgumentNullException.ThrowIfNull(delimiterCharacters);
        if (_position >= _source.Length) return string.Empty;

        var start = _position;
        while (_position < _source.Length &&
               delimiterCharacters.IndexOf(_source[_position]) < 0)
        {
            _position++;
        }

        var value = _source[start.._position];
        while (_position < _source.Length &&
               delimiterCharacters.IndexOf(_source[_position]) >= 0)
        {
            _position++;
        }
        return value;
    }
}
