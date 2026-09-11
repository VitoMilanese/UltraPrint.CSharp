namespace UltraPrint.Legacy.Scripting;

public sealed record LegacyScriptProgramStatement(int LineNumber, string Source);

/// <summary>
/// Structural result of the recovered Funzioni.AddProg file-loading algorithm.
/// Statements before the first line containing "sub" or "function" are sent to
/// ScriptControl.ExecuteStatement. From the first matching line onward, every
/// prepared line is accumulated and submitted once through ScriptControl.AddCode.
/// </summary>
public sealed record LegacyScriptProgramPlan(
    IReadOnlyList<LegacyScriptProgramStatement> ExecuteStatements,
    string AddCodeSource,
    int? ProcedureModeStartsAtLine,
    bool Cancelled = false,
    bool RequiresInteraction = false,
    bool HasUnresolvedNativeMacros = false);

/// <summary>
/// Reproduces the directly observed control flow in native Funzioni.AddProg
/// (0x004B6420, vtable 0x848). The native Boolean mode flag is initialized false,
/// set when a trimmed/lower-cased line contains "sub" or "function", and is never
/// reset before EOF. This intentionally preserves that unusual behavior.
/// </summary>
public static class LegacyScriptProgramPlanner
{
    public static LegacyScriptProgramPlan Build(
        string source,
        LegacyScriptInterpretationContext? interpretationContext = null) =>
        Process(source, interpretationContext, executeStatement: null, addCode: null);

    /// <summary>
    /// Runs the recovered AddProg pipeline as a stream. ExecuteStatement callbacks
    /// happen while lines are processed, not after a pre-scan, because the original
    /// program can already have executed top-level statements when a later interactive
    /// Interpretariga macro cancels the load.
    /// </summary>
    public static LegacyScriptProgramPlan Execute(
        string source,
        Action<LegacyScriptProgramStatement> executeStatement,
        Action<string> addCode,
        LegacyScriptInterpretationContext? interpretationContext = null)
    {
        ArgumentNullException.ThrowIfNull(executeStatement);
        ArgumentNullException.ThrowIfNull(addCode);
        return Process(source, interpretationContext, executeStatement, addCode);
    }

    private static LegacyScriptProgramPlan Process(
        string source,
        LegacyScriptInterpretationContext? interpretationContext,
        Action<LegacyScriptProgramStatement>? executeStatement,
        Action<string>? addCode)
    {
        ArgumentNullException.ThrowIfNull(source);

        var executeStatements = new List<LegacyScriptProgramStatement>();
        var addCodeSource = new System.Text.StringBuilder();
        var procedureMode = false;
        int? procedureModeStartsAtLine = null;
        var requiresInteraction = false;
        var hasUnresolvedMacros = false;

        using var reader = new StringReader(source);
        string? rawLine;
        var lineNumber = 0;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var detectionText = rawLine.Trim().ToLowerInvariant();
            if (!procedureMode &&
                (detectionText.Contains("sub", StringComparison.Ordinal) ||
                 detectionText.Contains("function", StringComparison.Ordinal)))
            {
                procedureMode = true;
                procedureModeStartsAtLine = lineNumber;
            }

            // Native AddProg adds one literal space to each side before calling
            // PreparaCodice, so its space-sensitive replacements behave identically.
            var prepared = LegacyScriptCodePreprocessor.Prepare(" " + rawLine + " ");
            if (interpretationContext is not null)
            {
                var interpreted = LegacyScriptLineInterpreter.InterpretConfirmed(prepared, interpretationContext);
                prepared = interpreted.Text;
                requiresInteraction |= interpreted.RequiresInteraction;
                hasUnresolvedMacros |= interpreted.HasUnresolvedNativeMacros;
                if (interpreted.Cancelled)
                {
                    return new LegacyScriptProgramPlan(
                        executeStatements,
                        addCodeSource.ToString(),
                        procedureModeStartsAtLine,
                        Cancelled: true,
                        RequiresInteraction: requiresInteraction,
                        HasUnresolvedNativeMacros: hasUnresolvedMacros);
                }
            }

            if (procedureMode)
            {
                addCodeSource.Append(prepared);
                addCodeSource.Append("\r\n");
            }
            else
            {
                var statement = new LegacyScriptProgramStatement(lineNumber, prepared);
                executeStatements.Add(statement);
                executeStatement?.Invoke(statement);
            }
        }

        var accumulated = addCodeSource.ToString();
        // Native AddProg reaches its final AddCode call even when no procedure was
        // found and the accumulator is empty.
        addCode?.Invoke(accumulated);

        return new LegacyScriptProgramPlan(
            executeStatements,
            accumulated,
            procedureModeStartsAtLine,
            Cancelled: false,
            RequiresInteraction: requiresInteraction,
            HasUnresolvedNativeMacros: hasUnresolvedMacros);
    }
}
