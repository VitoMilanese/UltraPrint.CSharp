using System.Globalization;
using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// UI-neutral bridge shared by the script-visible frmDatabase and Tabella objects.
/// The VB6 application injects Db and frmDatabase as aliases of the same global form,
/// while Tabella is a separate global form operating on the same database/record workflow.
/// </summary>
public interface ILegacyScriptDatabaseHost
{
    void RefreshTables();
    void FindRecord();
}

/// <summary>
/// Exact DAO field type names returned by frmDatabase.NometipoCampo in UltraPrint 2.2.115.
/// Unknown values return an empty string, matching the native function's empty default.
/// </summary>
public static class LegacyDaoFieldTypeCatalog
{
    public static string NameForType(int type) => type switch
    {
        1 => "YESNO",
        2 => "BYTE",
        3 => "INTEGER",
        4 => "LONG",
        5 => "CURRENCY",
        6 => "SINGLE",
        7 => "DOUBLE",
        8 => "DATE",
        10 => "TEXT",
        11 => "LONGBINARY",
        12 => "MEMO",
        16 => "AUTOINCRFIELD",
        _ => string.Empty
    };
}

/// <summary>
/// Proven script-callable subset of the global frmDatabase instance. The same facade
/// object is registered under both native names: "Db" and "frmDatabase".
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptDatabaseFacade
{
    private readonly ILegacyScriptDatabaseHost _host;

    public LegacyScriptDatabaseFacade(ILegacyScriptDatabaseHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Native frmDatabase.RiempiTabelle has no explicit arguments and refreshes the
    /// form's table/query inventory from the active database.
    /// </summary>
    public void RiempiTabelle() => _host.RefreshTables();

    /// <summary>
    /// Native frmDatabase.NometipoCampo accepts one DAO field-type value and returns
    /// one of the fixed uppercase labels recovered from the executable.
    /// </summary>
    public string NometipoCampo(object Tipo)
    {
        if (Tipo is null || Tipo is DBNull) return string.Empty;
        try
        {
            var value = Tipo switch
            {
                IConvertible convertible => convertible.ToInt32(CultureInfo.InvariantCulture),
                _ => Convert.ToInt32(Tipo, CultureInfo.InvariantCulture)
            };
            return LegacyDaoFieldTypeCatalog.NameForType(value);
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Proven script-callable subset of the global Tabella form. Trovarecord is a
/// zero-argument Sub in the native binary and rebuilds/executes the form's current
/// record query. The managed host maps that action onto the current persisted
/// database/query/table state.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptTableFacade
{
    private readonly ILegacyScriptDatabaseHost _host;

    public LegacyScriptTableFacade(ILegacyScriptDatabaseHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void Trovarecord() => _host.FindRecord();
}

/// <summary>
/// Native ABI facts recovered for the public database/table methods. Carica is kept
/// documented but deliberately not exposed: its native body consumes a caller value
/// through Funzioni.Parola, while the complete source-level signature/side effects are
/// not yet proven well enough to manufacture a compatible COM surface.
/// </summary>
public static class LegacyScriptDatabaseContract
{
    public static IReadOnlyList<LegacyScriptDatabaseMethodContract> FrmDatabaseMethods { get; } =
    [
        new("RiempiTabelle", 0x00568260, 0, LegacyScriptReturnKind.None, true),
        new("NometipoCampo", 0x0056E4A0, 1, LegacyScriptReturnKind.Value, true)
    ];

    public static IReadOnlyList<LegacyScriptDatabaseMethodContract> TabellaMethods { get; } =
    [
        new("Carica", 0x0051C820, null, LegacyScriptReturnKind.None, false),
        new("Trovarecord", 0x00523850, 0, LegacyScriptReturnKind.None, true)
    ];
}

public sealed record LegacyScriptDatabaseMethodContract(
    string Name,
    int NativeAddress,
    int? ExplicitArgumentCount,
    LegacyScriptReturnKind ReturnKind,
    bool ManagedBehaviorExposed);
