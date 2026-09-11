using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Layout;

/// <summary>
/// Managed equivalents for recovered frmCarta capability getters used by native workflows.
/// </summary>
public static class LegacyLayoutCapabilities
{
    /// <summary>
    /// frmDatabase.StampaTutti reads frmCarta.HasChip before applying the frmPrinting
    /// interval countdown. Legacy field type 10 is the recovered SmartCara/chip field.
    /// </summary>
    public static bool HasChip(CardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Fields.Any(field => field.Kind == LayoutFieldKind.Chip || field.LegacyTypeCode == 10);
    }
}
