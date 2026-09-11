using System.Text;
using UltraPrint.Legacy.Scripting;

internal static class ScriptSubstitutionCompatibilityTests
{
    public static void Run(string temp)
    {
        TestSequentialAnsiSubstitutions(temp);
        TestSubstitutionPrecedesInterpretarigaMacros(temp);
    }

    private static void TestSequentialAnsiSubstitutions(string temp)
    {
        var root = Path.Combine(temp, "sostituz-basic");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(
            Path.Combine(root, LegacyScriptSubstitutionTable.FileName),
            Encoding.Latin1.GetBytes(
                "<cr>,13\r\n" +
                "<tab>,9\r\n" +
                "HELLO, Ciao \r\n" +
                "ZERO,0\r\n" +
                "EMPTY,\r\n"));

        var result = LegacyScriptSubstitutionTable.Apply("HELLO<cr>X<tab>ZERO EMPTY hello", root);
        AssertEqual("Ciao\rX\t0 EMPTY hello", result,
            "SOSTITUZ applies literal/numeric rules sequentially and case-sensitively");

        var rules = LegacyScriptSubstitutionTable.ReadRules(root);
        AssertEqual(5, rules.Count, "SOSTITUZ non-empty row count");
        AssertEqual("\r", rules[0].Replacement, "SOSTITUZ numeric 13 becomes CR");
        AssertEqual("\t", rules[1].Replacement, "SOSTITUZ numeric 9 becomes TAB");
        AssertEqual("Ciao", rules[2].Replacement, "SOSTITUZ replacement token is trimmed");
        AssertEqual("0", rules[3].Replacement, "SOSTITUZ Val zero remains literal text");
        AssertEqual(string.Empty, rules[4].Replacement, "SOSTITUZ preserves empty replacement row");
    }

    private static void TestSubstitutionPrecedesInterpretarigaMacros(string temp)
    {
        var root = Path.Combine(temp, "sostituz-before-macros");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(
            Path.Combine(root, LegacyScriptSubstitutionTable.FileName),
            Encoding.Latin1.GetBytes("TOKEN,@(Name)\r\n"));

        var variables = new TestVariableStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Rossi"
        });
        var context = new LegacyScriptInterpretationContext(root, "UltraPrint", Variables: variables);
        var result = LegacyScriptLineInterpreter.InterpretConfirmed("Value=TOKEN", context);
        AssertEqual("Value=Rossi", result.Text,
            "SostituisciRiga runs before @() variable expansion");
    }

    private static void AssertEqual<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}, got {actual}.");
    }

    private sealed class TestVariableStore : ILegacyScriptVariableStore
    {
        private readonly Dictionary<string, string> _values;

        public TestVariableStore(Dictionary<string, string> values) => _values = values;

        public string? Get(string name) =>
            _values.TryGetValue(name.Trim(), out var value) ? value : null;

        public void Set(string name, string value) => _values[name.Trim()] = value;
    }
}
