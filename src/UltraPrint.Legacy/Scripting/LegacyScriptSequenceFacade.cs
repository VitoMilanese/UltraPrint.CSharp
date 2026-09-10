using System.Globalization;
using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// UI-neutral bridge for the global VB6 Sequenza instance. Native AddObjects exposes
/// the same instance under both "Sequenza" and "Stampa".
/// </summary>
public interface ILegacyScriptSequenceHost
{
    object PickRecord();
    void PositionPage(object? recordNumber);
    void SaveSetup(string? fileName);
    void LoadSetup(string? fileName);
}

/// <summary>
/// Script-visible Sequenza subset whose native parameter behavior and managed side effects are
/// decoded. Pescarecord returns the one-based matched record; PosizionaPagina consumes that
/// record number, applies the recovered legacy page arithmetic and highlights the matching grid
/// cell. ScriviSetup/LeggiSetup each take one Optional Variant filename.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class LegacyScriptSequenceFacade
{
    private readonly ILegacyScriptSequenceHost _host;

    public LegacyScriptSequenceFacade(ILegacyScriptSequenceHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public object Pescarecord() => _host.PickRecord();

    public void PosizionaPagina(object NumRecord) => _host.PositionPage(NumRecord);

    public void ScriviSetup(object? Filename = null) =>
        _host.SaveSetup(NormalizeOptionalFileName(Filename));

    public void LeggiSetup(object? Filename = null) =>
        _host.LoadSetup(NormalizeOptionalFileName(Filename));

    private static string? NormalizeOptionalFileName(object? value)
    {
        if (value is null || ReferenceEquals(value, Type.Missing) || value is DBNull)
            return null;
        var text = Convert.ToString(value, CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

/// <summary>
/// Native ABI/behavior facts recovered for the non-event Sequenza methods. Pescarecord has zero
/// explicit arguments and returns a Variant. PosizionaPagina consumes one explicit Variant
/// NumRecord; cmdPosiziona_Click passes it the Pescarecord result. It updates Pagina, invokes
/// cmdImposta_Click, scans MSFlexGrid1, and marks a matching record with QBColor(12) and bold.
/// ScriviSetup/LeggiSetup each consume one Optional Variant filename.
/// </summary>
public static class LegacyScriptSequenceContract
{
    public static IReadOnlyList<LegacyScriptSequenceMethodContract> Methods { get; } =
    [
        new("Pescarecord", 0x005C3590, 0, 0, LegacyScriptReturnKind.Variant, true),
        new("PosizionaPagina", 0x005C5250, 1, 0, LegacyScriptReturnKind.None, true),
        new("ScriviSetup", 0x005D5810, 1, 1, LegacyScriptReturnKind.None, true),
        new("LeggiSetup", 0x005D65B0, 1, 1, LegacyScriptReturnKind.None, true)
    ];
}

public sealed record LegacyScriptSequenceMethodContract(
    string Name,
    int NativeAddress,
    int ExplicitArgumentCount,
    int OptionalArgumentCount,
    LegacyScriptReturnKind ReturnKind,
    bool ManagedBehaviorExposed);
