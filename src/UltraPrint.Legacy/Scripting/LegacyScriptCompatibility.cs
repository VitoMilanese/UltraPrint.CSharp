namespace UltraPrint.Legacy.Scripting;

public static class LegacyScriptContract
{
    public static IReadOnlyList<string> ObjectNames { get; } = new[]
    {
        "Me",
        "Mainform",
        "Preview",
        "Db",
        "Sequenza",
        "Stampa",
        "frmDatabase",
        "Carta",
        "Chip",
        "Tabella",
        "frmlogin",
        "Fn",
        "Funzioni",
        "File",
        "SmartDriver",
        "Dispositivi",
        "Printer",
        "Screen",
        "ClipBoard",
        "App"
    };

    public static IReadOnlyList<string> LifecycleEvents { get; } = new[]
    {
        "OnLoad",
        "Load",
        "Main",
        "Unload"
    };

    public const string ScriptControlProgId = "MSScriptControl.ScriptControl";
    public const string Language = "VBScript";
}

public sealed record LegacyScriptDiagnostic(
    string Description,
    int Line = 0,
    int Column = 0,
    string? SourcePath = null)
{
    public override string ToString()
    {
        var location = Line > 0
            ? $" (line {Line}" + (Column > 0 ? $", column {Column})" : ")")
            : string.Empty;
        return Description + location;
    }
}

public sealed class LegacyScriptException : Exception
{
    public LegacyScriptException(LegacyScriptDiagnostic diagnostic, Exception? innerException = null)
        : base(diagnostic.ToString(), innerException)
    {
        Diagnostic = diagnostic;
    }

    public LegacyScriptDiagnostic Diagnostic { get; }
}

public interface ILegacyScriptEngine : IDisposable
{
    string Description { get; }
    void Reset();
    void AddObject(string name, object value, bool addMembers = true);
    void ExecuteStatement(string statement, string? sourcePath = null);
    void AddCode(string code, string? sourcePath = null);
    object? Invoke(string procedureName, params object?[] arguments);
}

/// <summary>
/// Reproduces the directly observed text normalization in Funzioni.PreparaCodice
/// (native entry 0x004B5800, vtable 0x844). Runtime UltraPrint macros interpreted
/// by Funzioni.Interpretariga are deliberately handled by LegacyScriptLineInterpreter
/// instead of being mixed into this VB6-to-VBScript syntax normalization step.
/// </summary>
public static class LegacyScriptCodePreprocessor
{
    public sealed record Transformation(string Search, string Replacement);

    public static IReadOnlyList<Transformation> ConfirmedTransformations { get; } = new[]
    {
        new Transformation("Private ", string.Empty),
        new Transformation("Public ", string.Empty),
        new Transformation(" as Integer", string.Empty),
        new Transformation(" as long", string.Empty),
        new Transformation(" as variant", string.Empty),
        new Transformation(" as date", string.Empty),
        new Transformation(" as double", string.Empty),
        new Transformation(" as currency", string.Empty),
        new Transformation(" as boolean", string.Empty),
        new Transformation(" as object", string.Empty),
        new Transformation(" as any", string.Empty),
        new Transformation(" As MSComctlLib.Node", string.Empty),
        new Transformation(" As MSComctlLib.ListItem", string.Empty),
        new Transformation(" Form_Unload(Cancel)", " Form_Unload()"),
        new Transformation(" Unload Me", " Chiudimi"),
        new Transformation("'Me.'", "Me."),
        new Transformation("'Fn.'", "Fn."),
        new Transformation("'-'", string.Empty)
    };

    public static string Prepare(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source;
        foreach (var transformation in ConfirmedTransformations)
            result = result.Replace(
                transformation.Search,
                transformation.Replacement,
                StringComparison.OrdinalIgnoreCase);
        return result;
    }
}

public sealed record LegacyScriptPathContext(
    string ApplicationDirectory,
    string? ApplicationName = null,
    string? ServerName = null);

/// <summary>
/// Script locations recovered from Funzioni.LoadCodicePerTipo. The original
/// constructs Script\&lt;type&gt;.VBS and also an App\&lt;application&gt;\Script\...
/// path. Server-specific candidates are exposed only when a server name is known.
/// </summary>
public static class LegacyScriptPathResolver
{
    public static IReadOnlyList<string> GetCandidates(LegacyScriptPathContext context, string scriptType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ApplicationDirectory);
        var name = NormalizeScriptName(scriptType);
        var root = Path.GetFullPath(context.ApplicationDirectory);
        var result = new List<string>
        {
            Path.Combine(root, "Script", name + ".VBS")
        };

        AddScopedCandidate(result, root, context.ApplicationName, name);
        AddScopedCandidate(result, root, context.ServerName, name);
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> Discover(LegacyScriptPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ApplicationDirectory);
        var root = Path.GetFullPath(context.ApplicationDirectory);
        var directories = new List<string> { Path.Combine(root, "Script") };
        AddScopedDirectory(directories, root, context.ApplicationName);
        AddScopedDirectory(directories, root, context.ServerName);

        var result = new List<string>();
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                result.AddRange(Directory.EnumerateFiles(directory, "*.vbs", SearchOption.TopDirectoryOnly));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        result.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    public static string? FindFirstExisting(LegacyScriptPathContext context, string scriptType) =>
        GetCandidates(context, scriptType).FirstOrDefault(File.Exists);

    public static string NormalizeScriptName(string scriptType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptType);
        var candidate = scriptType.Trim();
        if (candidate.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[..^4];
        if (candidate.Length == 0 || candidate.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            throw new ArgumentException("A legacy script type must be a single file-name segment.", nameof(scriptType));
        if (candidate is "." or "..")
            throw new ArgumentException("Invalid legacy script type.", nameof(scriptType));
        return candidate;
    }

    private static void AddScopedCandidate(List<string> result, string root, string? scopeName, string scriptName)
    {
        if (!TryNormalizeScope(scopeName, out var normalized)) return;
        result.Add(Path.Combine(root, "App", normalized, "Script", scriptName + ".VBS"));
    }

    private static void AddScopedDirectory(List<string> result, string root, string? scopeName)
    {
        if (!TryNormalizeScope(scopeName, out var normalized)) return;
        result.Add(Path.Combine(root, "App", normalized, "Script"));
    }

    private static bool TryNormalizeScope(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (candidate.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            return false;
        if (candidate is "." or "..") return false;
        normalized = candidate;
        return true;
    }
}

public sealed partial class LegacyScriptSession : IDisposable
{
    private sealed record RegisteredObject(string Name, object Value, bool AddMembers);

    private readonly ILegacyScriptEngine _engine;
    private readonly List<RegisteredObject> _registeredObjects = new();
    private bool _loaded;

    public LegacyScriptSession(ILegacyScriptEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public string EngineDescription => _engine.Description;
    public string? SourcePath { get; private set; }
    public string? PreparedSource { get; private set; }
    public LegacyScriptProgramPlan? LastProgramPlan { get; private set; }

    public void RegisterObject(string name, object value, bool addMembers = true)
    {
        if (_loaded)
            throw new InvalidOperationException("Register script objects before loading code, matching the recovered AddObjects -> AddProg lifecycle.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _registeredObjects.Add(new RegisteredObject(name.Trim(), value, addMembers));
    }

    public void Load(
        string source,
        string? sourcePath = null,
        bool prepareLegacyCode = true,
        LegacyScriptInterpretationContext? interpretationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (prepareLegacyCode)
        {
            LoadProgram(source, sourcePath, interpretationContext);
            return;
        }

        ResetAndRegisterObjects();
        PreparedSource = source;
        SourcePath = sourcePath;
        LastProgramPlan = null;
        _engine.AddCode(source, sourcePath);
        _loaded = true;
    }

    /// <summary>
    /// Executes the recovered Funzioni.AddProg pipeline. Top-level lines are sent
    /// immediately through ExecuteStatement until the first line containing "sub"
    /// or "function". From that line onward source is accumulated and submitted
    /// once through AddCode at EOF. This preserves native side effects that happen
    /// before a later line cancels during Interpretariga processing.
    /// </summary>
    public LegacyScriptProgramPlan LoadProgram(
        string source,
        string? sourcePath = null,
        LegacyScriptInterpretationContext? interpretationContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ResetAndRegisterObjects();
        SourcePath = sourcePath;
        PreparedSource = null;
        LastProgramPlan = null;

        var plan = LegacyScriptProgramPlanner.Execute(
            source,
            statement => _engine.ExecuteStatement(statement.Source, sourcePath),
            code => _engine.AddCode(code, sourcePath),
            interpretationContext);

        LastProgramPlan = plan;
        PreparedSource = plan.AddCodeSource;
        _loaded = !plan.Cancelled;
        return plan;
    }

    public object? Invoke(string eventName, params object?[] arguments) =>
        InvokeLegacy(string.Empty, eventName, arguments);

    private void ResetAndRegisterObjects()
    {
        _loaded = false;
        ResetDispatchState();
        _engine.Reset();
        foreach (var item in _registeredObjects)
            _engine.AddObject(item.Name, item.Value, item.AddMembers);
    }

    public void Dispose() => _engine.Dispose();
}
