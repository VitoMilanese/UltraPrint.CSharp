namespace UltraPrint.Legacy.Devices;

public readonly record struct LegacyChipPositionBounds(
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm);

/// <summary>
/// Pure compatibility contract for the smart-card/chip position marker drawn by frmCarta.
/// This models editor UI only and performs no smart-card, printer, COM, GDI or ICE_API action.
/// </summary>
public static class LegacyChipPositionSemantics
{
    public const int FrmCartaDrawFieldsNativeAddress = 0x541FB0;
    public const int MainFormTogglePositionMenuNativeAddress = 0x5B1770;
    public const int FrmCartaChipLabelClickNativeAddress = 0x5392D0;

    public const int MainFormPositionMenuVtableOffset = 0x38C;
    public const int FrmCartaChipLabelVtableOffset = 0x320;
    public const int MenuCheckedGetterVtableOffset = 0x68;
    public const int MenuCheckedSetterVtableOffset = 0x6C;
    public const int ChipDisplayStateSetterVtableOffset = 0x9C;

    public const int FrmCartaScalePercentStorageOffset = 0x58;
    public const int MillimeterToTwipsHelperNativeAddress = 0x5FCAD0;

    public const double LeftMm = 8.0;
    public const double TopMm = 18.0;
    public const double WidthMm = 13.0;
    public const double HeightMm = 12.0;

    /// <summary>
    /// DisegnaCampi rebuilds the chip marker at fixed logical card coordinates
    /// left=8 mm, top=18 mm, width=13 mm and height=12 mm. Zoom only scales display twips.
    /// </summary>
    public static LegacyChipPositionBounds GetLogicalBounds() =>
        new(LeftMm, TopMm, WidthMm, HeightMm);

    public static bool ShouldDisplayMarker(bool positionMenuChecked) => positionMenuChecked;
}
