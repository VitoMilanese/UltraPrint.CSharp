using System.Globalization;
using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// UI-neutral bridge for the global VB6 Sequenza instance. Native AddObjects exposes
/// the same instance under both "Sequenza" and "Stampa".
/// </summary>
public interface ILegacyScriptSequenceHost
{
    void SaveSetup(string? fileName);
    void LoadSetup(string? fileName);
}

/// <summary>
/// Script-visible subset whose native parameter behavior and managed side effects are
/// sufficiently decoded. ScriviSetup/LeggiSetup each take one Optional Variant filename;
/// when omitted the native code builds App.Path\\ly\\&lt;frmCarta.Caption&gt;.Seq.
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
/// Native ABI facts recovered for the non-event Sequenza methods. Pescarecord's ret 8
/// includes a hidden 16-byte Variant result pointer, so it has zero explicit arguments.
/// PosizionaPagina consumes one explicit Variant. ScriviSetup/LeggiSetup each consume one
/// Optional Variant filename. Only the setup pair is exposed until the other semantics are
/// decoded beyond their calling shape.
/// </summary>
public static class LegacyScriptSequenceContract
{
    public static IReadOnlyList<LegacyScriptSequenceMethodContract> Methods { get; } =
    [
        new("Pescarecord", 0x005C3590, 0, 0, LegacyScriptReturnKind.Variant, false),
        new("PosizionaPagina", 0x005C5250, 1, 0, LegacyScriptReturnKind.None, false),
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
