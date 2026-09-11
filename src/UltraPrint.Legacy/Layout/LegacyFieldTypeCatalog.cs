using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Layout;

/// <summary>
/// Field-type values recovered directly from frmCarta.NuovoCampo in UltraPrint 2.2.115.
/// The native routine compares its Variant type argument against these numeric values
/// and selects the corresponding Italian label. Unknown values remain Unknown instead
/// of being guessed.
/// </summary>
public static class LegacyFieldTypeCatalog
{
    public sealed record Entry(int Code, string NativeLabel, LayoutFieldKind Kind);

    private static readonly IReadOnlyDictionary<int, Entry> Entries = new Dictionary<int, Entry>
    {
        [2] = new(2, "Rettangolo", LayoutFieldKind.Rectangle),
        [3] = new(3, "Testo", LayoutFieldKind.Text),
        [4] = new(4, "Barcode", LayoutFieldKind.Barcode),
        [5] = new(5, "Immagine", LayoutFieldKind.Image),
        // Type 6 is used by the supplied production fixture for a database/photo image field.
        // NuovoCampo does not assign a dedicated label for it, so keep the native default label.
        [6] = new(6, "Campo", LayoutFieldKind.Image),
        [7] = new(7, "Twain", LayoutFieldKind.Hardware),
        [8] = new(8, "Telecamera", LayoutFieldKind.Hardware),
        [9] = new(9, "BandaMagnetica", LayoutFieldKind.MagneticStripe),
        // The executable contains the literal "SmartCara" here; the domain behavior is the
        // smart-card/chip field and is represented by the managed Chip kind.
        [10] = new(10, "SmartCara", LayoutFieldKind.Chip),
        [13] = new(13, "Tabella", LayoutFieldKind.Table)
    };

    public static IReadOnlyCollection<Entry> RecoveredEntries => Entries.Values.ToArray();

    public static LayoutFieldKind KindForType(int legacyTypeCode) =>
        Entries.TryGetValue(legacyTypeCode, out var entry) ? entry.Kind : LayoutFieldKind.Unknown;

    public static string NativeLabelForType(int legacyTypeCode) =>
        Entries.TryGetValue(legacyTypeCode, out var entry) ? entry.NativeLabel : "Campo";

    /// <summary>
    /// frmCarta keeps its UI/current-field index one-based. The recovered native code scans
    /// a zero-based free-slot value and increments it before storing/using the public field
    /// index. The managed .ly codec stores slots zero-based, so script-facing Carta methods
    /// translate n to managed slot n-1.
    /// </summary>
    public static LayoutField? FindByLegacyFieldNumber(CardLayout layout, int legacyFieldNumber)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (legacyFieldNumber <= 0 || legacyFieldNumber > UltraPrint22115LayoutCodec.MaxFieldSlots)
            return null;
        var managedIndex = legacyFieldNumber - 1;
        return layout.Fields.FirstOrDefault(field => field.Index == managedIndex);
    }

    public static int ToLegacyFieldNumber(LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return checked(field.Index + 1);
    }
}
