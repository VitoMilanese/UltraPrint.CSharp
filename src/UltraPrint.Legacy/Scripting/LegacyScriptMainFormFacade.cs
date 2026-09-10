using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// UI-neutral bridge for the script-visible VB6 MainForm singleton.
/// Only behavior with a sufficiently clear native contract is exposed here; methods whose
/// arguments or side effects are still ambiguous remain documented but unavailable to scripts.
/// </summary>
public interface ILegacyScriptMainFormHost
{
    void PrintCurrentRecord();
    void ReloadBackground();
    void TerminateApplication();
}

/// <summary>
/// First proven managed subset of the object injected by Funzioni.AddObjects as "Mainform".
/// Native stack cleanup confirms StampaRecord(), CaricaSfondo() and Termina() are zero-argument
/// Subs in UltraPrint 2.2.115. Their managed implementations delegate to the live application.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptMainFormFacade
{
    private readonly ILegacyScriptMainFormHost _host;

    public LegacyScriptMainFormFacade(ILegacyScriptMainFormHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void StampaRecord() => _host.PrintCurrentRecord();

    public void CaricaSfondo() => _host.ReloadBackground();

    public void Termina() => _host.TerminateApplication();
}

/// <summary>
/// Native ABI facts recovered from the MainForm procedures. This table intentionally describes
/// methods that are not all exposed yet: knowing an argument count is not enough to invent the
/// meaning of those arguments or their side effects.
/// </summary>
public static class LegacyScriptMainFormContract
{
    public static IReadOnlyList<LegacyScriptMainFormMethodContract> Methods { get; } =
    [
        new("StampaRecord", 0x005B3830, 0, LegacyScriptReturnKind.None, true),
        new("CaricaSfondo", 0x005B5650, 0, LegacyScriptReturnKind.None, true),
        new("SetButton", 0x005B8490, 2, LegacyScriptReturnKind.None, false),
        new("SetCoordinate", 0x005B8850, 1, LegacyScriptReturnKind.None, false),
        new("AdattaCarta", 0x005B8B50, 0, LegacyScriptReturnKind.None, false),
        new("PrinterEscape", 0x005B9150, 2, LegacyScriptReturnKind.Value, false),
        new("Termina", 0x005B9450, 0, LegacyScriptReturnKind.None, true),
        new("PuoFare", 0x005B9AB0, 1, LegacyScriptReturnKind.Value, false),
        new("SetPrivilegi", 0x005BA8B0, 0, LegacyScriptReturnKind.None, false),
        new("GestioneRecord", 0x005BC580, 0, LegacyScriptReturnKind.Variant, false),
        new("GestioneSequenza", 0x005BDF00, 0, LegacyScriptReturnKind.Variant, false),
        new("GestioneLato", 0x005BECE0, 6, LegacyScriptReturnKind.None, false),
        new("GestioneCampo", 0x005BFDB0, 6, LegacyScriptReturnKind.None, false),
        new("VersioneDemo", 0x005BFE30, 0, LegacyScriptReturnKind.None, false)
    ];
}

public sealed record LegacyScriptMainFormMethodContract(
    string Name,
    int NativeAddress,
    int ExplicitArgumentCount,
    LegacyScriptReturnKind ReturnKind,
    bool ManagedBehaviorExposed);

public enum LegacyScriptReturnKind
{
    None,
    Value,
    Variant
}
