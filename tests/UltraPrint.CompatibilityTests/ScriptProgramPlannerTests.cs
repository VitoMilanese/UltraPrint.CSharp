using UltraPrint.Legacy.Scripting;

internal static class ScriptProgramPlannerTests
{
    public static void Run()
    {
        TestProcedureModeNeverResets();
        TestFunctionAlsoStartsProcedureMode();
        TestNoProcedureUsesExecuteStatementOnly();
    }

    private static void TestProcedureModeNeverResets()
    {
        var source = "x = 1\r\n" +
                     "Private Sub Load()\r\n" +
                     "x = 2\r\n" +
                     "End Sub\r\n" +
                     "y = 3\r\n";

        var plan = LegacyScriptProgramPlanner.Build(source);
        AssertEqual(2, plan.ProcedureModeStartsAtLine!.Value, "AddProg procedure mode begins on first sub line");
        AssertEqual(1, plan.ExecuteStatements.Count, "only lines before first procedure use ExecuteStatement");
        AssertEqual(1, plan.ExecuteStatements[0].LineNumber, "top-level statement line number");
        AssertTrue(plan.ExecuteStatements[0].Source.Contains("x = 1", StringComparison.Ordinal), "top-level statement preserved");
        AssertTrue(plan.AddCodeSource.Contains("Sub Load()", StringComparison.OrdinalIgnoreCase), "Private Sub is normalized into AddCode source");
        AssertTrue(plan.AddCodeSource.Contains("End Sub", StringComparison.OrdinalIgnoreCase), "procedure terminator remains in AddCode source");
        AssertTrue(plan.AddCodeSource.Contains("y = 3", StringComparison.Ordinal), "native AddProg mode remains set after End Sub");
        AssertTrue(plan.AddCodeSource.EndsWith("\r\n", StringComparison.Ordinal), "native AddProg appends CRLF after accumulated lines");
    }

    private static void TestFunctionAlsoStartsProcedureMode()
    {
        var source = "value = 1\nPublic Function F()\nF = value\nEnd Function";
        var plan = LegacyScriptProgramPlanner.Build(source);
        AssertEqual(2, plan.ProcedureModeStartsAtLine!.Value, "function starts AddCode mode");
        AssertEqual(1, plan.ExecuteStatements.Count, "function leaves prior line as ExecuteStatement");
        AssertTrue(plan.AddCodeSource.Contains("Function F()", StringComparison.OrdinalIgnoreCase), "Public Function normalization is accumulated");
    }

    private static void TestNoProcedureUsesExecuteStatementOnly()
    {
        var plan = LegacyScriptProgramPlanner.Build("a = 1\r\nb = 2");
        AssertTrue(plan.ProcedureModeStartsAtLine is null, "no sub/function leaves AddProg outside procedure mode");
        AssertEqual(2, plan.ExecuteStatements.Count, "all lines use ExecuteStatement without sub/function");
        AssertEqual(string.Empty, plan.AddCodeSource, "AddCode receives an empty accumulator when no procedure is found");
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
}
