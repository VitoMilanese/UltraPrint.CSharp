using System.Text;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Scripting;

internal static class ScriptMacroCompatibilityTests
{
    public static void Run(string temp)
    {
        TestVariableMacros(temp);
        TestIniMacros(temp);
        TestInteractiveMacros(temp);
        TestCancellation(temp);
        TestConservativeFallback(temp);
    }

    private static void TestVariableMacros(string temp)
    {
        var variables = new RecordingVariableStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "  Mario ·ignored metadata",
            ["Counter"] = "41+"
        });
        var context = new LegacyScriptInterpretationContext(
            Path.Combine(temp, "interpretariga-variables"),
            "UltraPrint",
            Variables: variables);
        Directory.CreateDirectory(context.RootDirectory);

        var result = LegacyScriptLineInterpreter.InterpretConfirmed(
            "A=@(name); B=@(Counter); C=@(Unknown)",
            context);
        AssertEqual("A=Mario; B=42; C=", result.Text, "@() resolves GetVariabile values and missing variables");
        AssertTrue(!result.HasUnresolvedNativeMacros, "@() is resolved when a variable store is supplied");
        AssertEqual("42", variables.Get("Counter")!, "@() plus stores the incremented value through SetVariabile");

        var second = LegacyScriptLineInterpreter.InterpretConfirmed("N=@(Counter)", context);
        AssertEqual("N=42", second.Text, "@() increment marker is consumed after SetVariabile writes the numeric value");
    }

    private static void TestIniMacros(string temp)
    {
        var root = Path.Combine(temp, "interpretariga-ini");
        Directory.CreateDirectory(root);
        var rootIni = Path.Combine(root, "UltraPrint.Ini");
        File.WriteAllBytes(
            rootIni,
            Encoding.Latin1.GetBytes("[$]\r\nName=Root value\r\nCounter=41\r\n"));

        var scopedDirectory = Path.Combine(root, "App", "BadgeApp");
        Directory.CreateDirectory(scopedDirectory);
        var scopedIni = Path.Combine(scopedDirectory, "BadgeApp.Ini");
        File.WriteAllBytes(
            scopedIni,
            Encoding.Latin1.GetBytes("[$]\r\nGreeting=Ciao\r\n"));

        var context = new LegacyScriptInterpretationContext(root, "UltraPrint");
        AssertEqual(rootIni, LegacyScriptLineInterpreter.GetIniPath(context, string.Empty), "root $() INI path");
        AssertEqual(scopedIni, LegacyScriptLineInterpreter.GetIniPath(context, "BadgeApp"), "scoped $() INI path");

        var read = LegacyScriptLineInterpreter.InterpretConfirmed(
            "A=$(.Name); B=$(BadgeApp.Greeting)",
            context);
        AssertEqual("A=Root value; B=Ciao", read.Text, "$() reads root and application INI values");
        AssertTrue(!read.Cancelled, "$() does not cancel");
        AssertTrue(!read.RequiresInteraction, "$() is deterministic");

        var increment = LegacyScriptLineInterpreter.InterpretConfirmed("N=$(.Counter+)", context);
        AssertEqual("N=42", increment.Text, "$() plus returns incremented value");
        var persisted = LegacyIniDocument.Load(rootIni);
        AssertEqual("42", persisted.Get("$", "Counter")!, "$() plus persists incremented value");

        var secondIncrement = LegacyScriptLineInterpreter.InterpretConfirmed("N=$(.Counter+)", context);
        AssertEqual("N=43", secondIncrement.Text, "$() plus increments persisted value again");

        var malformed = LegacyScriptLineInterpreter.InterpretConfirmed("X=$(NoDot)", context);
        AssertEqual("X=$(NoDot)", malformed.Text, "$() without scope/key dot remains untouched");
    }

    private static void TestInteractiveMacros(string temp)
    {
        var interaction = new RecordingLegacyScriptInteraction
        {
            PromptResult = "Mario",
            FileResult = @"C:\Cards\photo.jpg",
            DirectoryResult = @"C:\Cards",
            ComputerResult = "PRINTSERVER"
        };
        var context = new LegacyScriptInterpretationContext(
            Path.Combine(temp, "interpretariga-interactive"),
            "UltraPrint",
            interaction);
        Directory.CreateDirectory(context.RootDirectory);

        var result = LegacyScriptLineInterpreter.InterpretConfirmed(
            "U=?(Operator);F=@GETFILE(Image files;Choose image);D=@DIRECTORY(Choose folder);C=@COMPUTER(Choose computer)",
            context);

        AssertEqual(
            @"U=Mario;F=C:\Cards\photo.jpg;D=C:\Cards;C=PRINTSERVER",
            result.Text,
            "interactive Interpretariga macros are substituted in native order");
        AssertTrue(!result.Cancelled, "successful interactions do not cancel");
        AssertTrue(!result.RequiresInteraction, "provided interaction resolves interactive macros");
        AssertEqual("Operator", interaction.LastPrompt!, "?() passes exact prompt body");
        AssertEqual("Image files", interaction.LastFileFirstArgument!, "@GETFILE first Parola token");
        AssertEqual("Choose image", interaction.LastFileSecondArgument!, "@GETFILE second Parola token");
        AssertEqual("Choose folder", interaction.LastDirectoryPrompt!, "@DIRECTORY passes exact prompt body");
        AssertEqual("Choose computer", interaction.LastComputerPrompt!, "@COMPUTER passes exact prompt body");
    }

    private static void TestCancellation(string temp)
    {
        var interaction = new RecordingLegacyScriptInteraction { PromptResult = string.Empty };
        var context = new LegacyScriptInterpretationContext(
            Path.Combine(temp, "interpretariga-cancel"),
            "UltraPrint",
            interaction);
        Directory.CreateDirectory(context.RootDirectory);

        var result = LegacyScriptLineInterpreter.InterpretConfirmed("Before ?(Cancel me) After", context);
        AssertTrue(result.Cancelled, "empty interactive result follows native cancellation path");
        AssertEqual("Before ?(Cancel me) After", result.Text, "cancelled macro is not replaced");
    }

    private static void TestConservativeFallback(string temp)
    {
        var context = new LegacyScriptInterpretationContext(
            Path.Combine(temp, "interpretariga-fallback"),
            "UltraPrint");
        Directory.CreateDirectory(context.RootDirectory);

        var interactive = LegacyScriptLineInterpreter.InterpretConfirmed("X=@DIRECTORY(Pick)", context);
        AssertTrue(interactive.RequiresInteraction, "interactive macro is reported when no UI adapter is supplied");
        AssertEqual("X=@DIRECTORY(Pick)", interactive.Text, "interactive macro remains untouched without an adapter");

        var unresolved = LegacyScriptLineInterpreter.InterpretConfirmed("X=@(Counter)", context);
        AssertTrue(unresolved.HasUnresolvedNativeMacros, "@() requires the recovered variable-store boundary");
        AssertEqual("X=@(Counter)", unresolved.Text, "@() remains untouched without a variable store");
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

    private sealed class RecordingLegacyScriptInteraction : ILegacyScriptInteraction
    {
        public string? PromptResult { get; init; }
        public string? FileResult { get; init; }
        public string? DirectoryResult { get; init; }
        public string? ComputerResult { get; init; }

        public string? LastPrompt { get; private set; }
        public string? LastFileFirstArgument { get; private set; }
        public string? LastFileSecondArgument { get; private set; }
        public string? LastDirectoryPrompt { get; private set; }
        public string? LastComputerPrompt { get; private set; }

        public string? Prompt(string prompt)
        {
            LastPrompt = prompt;
            return PromptResult;
        }

        public string? SelectFile(string firstArgument, string secondArgument)
        {
            LastFileFirstArgument = firstArgument;
            LastFileSecondArgument = secondArgument;
            return FileResult;
        }

        public string? SelectDirectory(string prompt)
        {
            LastDirectoryPrompt = prompt;
            return DirectoryResult;
        }

        public string? SelectComputer(string prompt)
        {
            LastComputerPrompt = prompt;
            return ComputerResult;
        }
    }

    private sealed class RecordingVariableStore : ILegacyScriptVariableStore
    {
        private readonly Dictionary<string, string> _values;

        public RecordingVariableStore(Dictionary<string, string> values)
        {
            _values = values;
        }

        public string? Get(string name) =>
            _values.TryGetValue(name.Trim(), out var value) ? value : null;

        public void Set(string name, string value)
        {
            if (_values.ContainsKey(name.Trim()))
                _values[name.Trim()] = value;
        }
    }
}
