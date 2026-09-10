using System.Runtime.InteropServices;
using UltraPrint.Legacy.Configuration;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Minimal managed counterpart of the VB6 intrinsic App object injected by
/// Funzioni.AddObjects. Only members directly required by recovered native logic
/// are exposed; additional App members are added only when proven necessary.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptAppFacade
{
    public LegacyScriptAppFacade(string applicationDirectory, string executableName = "UltraPrint")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        Path = System.IO.Path.GetFullPath(applicationDirectory).TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        EXEName = executableName.Trim();
    }

    // Names intentionally match the VB6 App object spelling used by legacy scripts.
    public string Path { get; }
    public string EXEName { get; }
}

/// <summary>
/// Proven subset of the legacy File class exposed to ScriptControl as "File".
/// INI signatures come from recovered VB6 type information. SoloExt/SoloNomeFile/
/// SoloPath/Esiste are reconstructed directly from their native implementations.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptFileFacade
{
    public string GetIni(string Sezione, string Variabile, string Default, string Filename)
    {
        ArgumentNullException.ThrowIfNull(Sezione);
        ArgumentNullException.ThrowIfNull(Variabile);
        ArgumentNullException.ThrowIfNull(Default);
        ArgumentException.ThrowIfNullOrWhiteSpace(Filename);

        if (!System.IO.File.Exists(Filename)) return Default;
        var ini = LegacyIniDocument.Load(Filename);
        return ini.Get(Sezione, Variabile, Default) ?? Default;
    }

    public void WriteIni(string Appname, string KeyName, string keydefault, string Filename)
    {
        ArgumentNullException.ThrowIfNull(Appname);
        ArgumentNullException.ThrowIfNull(KeyName);
        ArgumentNullException.ThrowIfNull(keydefault);
        ArgumentException.ThrowIfNullOrWhiteSpace(Filename);

        var fullPath = System.IO.Path.GetFullPath(Filename);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var ini = System.IO.File.Exists(fullPath)
            ? LegacyIniDocument.Load(fullPath)
            : LegacyIniDocument.Parse(string.Empty);
        ini.Set(Appname, KeyName, keydefault);
        ini.Save(fullPath);
    }

    /// <summary>
    /// Native SoloExt scans backward for the final literal '.' and returns the text
    /// after it. It does not apply Path APIs or stop at a directory separator.
    /// </summary>
    public string SoloExt(string Filename)
    {
        Filename ??= string.Empty;
        var dot = Filename.LastIndexOf('.');
        return dot < 0 || dot == Filename.Length - 1 ? string.Empty : Filename[(dot + 1)..];
    }

    /// <summary>
    /// Native SoloNomeFile uses only the Windows backslash separator. Its optional
    /// flag defaults True; False removes everything from the first dot in the base
    /// name, matching the recovered InStr/Left branch.
    /// </summary>
    public string SoloNomeFile(string Filename, bool Estensione = true)
    {
        Filename ??= string.Empty;
        var slash = Filename.LastIndexOf('\\');
        var name = slash >= 0 ? Filename[(slash + 1)..] : Filename;
        if (Estensione) return name;
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>
    /// Native SoloPath walks the string and keeps the substring through the last
    /// literal backslash, including that trailing separator.
    /// </summary>
    public string SoloPath(string Filename)
    {
        Filename ??= string.Empty;
        var slash = Filename.LastIndexOf('\\');
        return slash < 0 ? string.Empty : Filename[..(slash + 1)];
    }

    /// <summary>
    /// File.Esiste is implemented in VB6 through the legacy Dir-style path. The
    /// managed replacement preserves normal file checks plus the wildcard behavior
    /// relied on by old scripts; errors follow the native Resume Next false path.
    /// </summary>
    public bool Esiste(string Filename)
    {
        if (string.IsNullOrWhiteSpace(Filename)) return false;
        try
        {
            if (!Filename.Contains('*') && !Filename.Contains('?'))
                return System.IO.File.Exists(Filename);

            var directory = System.IO.Path.GetDirectoryName(Filename);
            var pattern = System.IO.Path.GetFileName(Filename);
            if (string.IsNullOrEmpty(pattern)) return false;
            directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
            return Directory.Exists(directory) &&
                   Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Proven script-callable subset of the original Funzioni class. Variable methods
/// use the recovered fixed Variant table. Sostituisci is the native binary,
/// case-sensitive replace-all helper used throughout the original executable.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptFunctionsFacade
{
    private readonly LegacyScriptVariableTable _variables;

    public LegacyScriptFunctionsFacade(LegacyScriptVariableTable variables)
    {
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
    }

    public object? GetVariabile(string Nome) => _variables.GetVariabile(Nome);

    public void SetVariabile(string Nome, object? Valore) => _variables.SetVariabile(Nome, Valore);

    public void IncVariabile(string Nome) => _variables.IncVariabile(Nome);

    public string Sostituisci(string Testo, string Trova, string Sostituzione)
    {
        Testo ??= string.Empty;
        Trova ??= string.Empty;
        Sostituzione ??= string.Empty;
        return Trova.Length == 0
            ? Testo
            : Testo.Replace(Trova, Sostituzione, StringComparison.Ordinal);
    }
}

/// <summary>
/// Small host surface for the rewritten "Unload Me" -> "Chiudimi" contract.
/// Native AddObjects injects the caller as "Me" with AddMembers=True, so an
/// unqualified Chiudimi call is resolved through the caller object.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptHostFacade
{
    private readonly Action _requestUnload;

    public LegacyScriptHostFacade(Action requestUnload)
    {
        _requestUnload = requestUnload ?? throw new ArgumentNullException(nameof(requestUnload));
    }

    public void Chiudimi() => _requestUnload();
}

public sealed record LegacyScriptCoreFacadeSet(
    LegacyScriptVariableTable Variables,
    LegacyScriptFunctionsFacade Funzioni,
    LegacyScriptFileFacade File,
    LegacyScriptAppFacade App,
    LegacyScriptCartaFacade? Carta = null,
    LegacyScriptMainFormFacade? Mainform = null,
    LegacyScriptDatabaseFacade? Database = null,
    LegacyScriptTableFacade? Tabella = null);

/// <summary>
/// Registers only managed objects whose current member contracts are supported by
/// direct native evidence. The original injects twenty names; unproven form/device
/// facades are deliberately not replaced with misleading empty stubs.
/// </summary>
public static class LegacyScriptCoreFacadeRegistration
{
    public static LegacyScriptCoreFacadeSet Register(
        LegacyScriptSession session,
        string applicationDirectory,
        string executableName = "UltraPrint",
        ILegacyScriptCartaHost? cartaHost = null,
        ILegacyScriptMainFormHost? mainFormHost = null,
        ILegacyScriptDatabaseHost? databaseHost = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var variables = new LegacyScriptVariableTable();
        var funzioni = new LegacyScriptFunctionsFacade(variables);
        var app = new LegacyScriptAppFacade(applicationDirectory, executableName);
        var file = new LegacyScriptFileFacade();
        var host = new LegacyScriptHostFacade(session.RequestUnload);

        var effectiveMainFormHost = mainFormHost ?? LegacyScriptMainFormHostRegistry.Current;
        var mainform = effectiveMainFormHost is null ? null : new LegacyScriptMainFormFacade(effectiveMainFormHost);
        var effectiveDatabaseHost = databaseHost ?? LegacyScriptDatabaseHostRegistry.Current;
        var database = effectiveDatabaseHost is null ? null : new LegacyScriptDatabaseFacade(effectiveDatabaseHost);
        var tabella = effectiveDatabaseHost is null ? null : new LegacyScriptTableFacade(effectiveDatabaseHost);
        var effectiveCartaHost = cartaHost ?? LegacyScriptCartaHostRegistry.Current;
        var carta = effectiveCartaHost is null ? null : new LegacyScriptCartaFacade(effectiveCartaHost);

        // Preserve the relative native AddObjects order among every facade currently available:
        // Me -> Mainform -> Db -> frmDatabase -> Carta -> Tabella -> Fn -> Funzioni -> File -> App.
        // Missing Preview/Sequenza/Stampa/Chip/etc. are skipped rather than replaced by fake stubs.
        session.RegisterObject("Me", host, addMembers: true);
        if (mainform is not null) session.RegisterObject("Mainform", mainform, addMembers: true);
        if (database is not null) session.RegisterObject("Db", database, addMembers: true);
        if (database is not null) session.RegisterObject("frmDatabase", database, addMembers: true);
        if (carta is not null) session.RegisterObject("Carta", carta, addMembers: true);
        if (tabella is not null) session.RegisterObject("Tabella", tabella, addMembers: true);

        // Native AddObjects resolves both "Fn" and "Funzioni" through global
        // 0x62B2C8 / object-info 0x41751C. They are two names for the exact same
        // singleton, so managed state/identity must also be shared.
        session.RegisterObject("Fn", funzioni, addMembers: true);
        session.RegisterObject("Funzioni", funzioni, addMembers: true);
        session.RegisterObject("File", file, addMembers: true);
        session.RegisterObject("App", app, addMembers: true);
        return new LegacyScriptCoreFacadeSet(variables, funzioni, file, app, carta, mainform, database, tabella);
    }
}
