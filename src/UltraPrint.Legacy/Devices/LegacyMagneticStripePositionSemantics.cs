namespace UltraPrint.Legacy.Devices;

public readonly record struct LegacyMagneticStripePositionBounds(
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm);

/// <summary>
/// Pure compatibility contract for the magnetic-stripe position marker drawn by frmCarta.
/// This models editor geometry/display state only; it does not perform magnetic encoding,
/// printer I/O, GDI passthrough, or any ICE_API operation.
/// </summary>
public static class LegacyMagneticStripePositionSemantics
{
    public const int FrmCartaDrawFieldsNativeAddress = 0x541FB0;
    public const int MainFormTogglePositionMenuNativeAddress = 0x5B1620;
    public const int FrmCartaBandLabelClickNativeAddress = 0x539190;

    public const int MainFormPositionMenuVtableOffset = 0x388;
    public const int FrmCartaBandLabelVtableOffset = 0x324;
    public const int MenuCheckedGetterVtableOffset = 0x68;
    public const int MenuCheckedSetterVtableOffset = 0x6C;
    public const int BandDisplayStateSetterVtableOffset = 0x9C;

    public const int FrmCartaScalePercentStorageOffset = 0x58;
    public const int MillimeterToTwipsHelperNativeAddress = 0x5FCAD0;
    public const double TwipsPerInch = 1440.0;
    public const double MillimetersPerInch = 25.4;
    public const double ScalePercentDivisor = 100.0;

    public const double LeftMm = 0.0;
    public const double TopMm = 5.0;
    public const double HeightMm = 12.0;

    /// <summary>
    /// DisegnaCampi fixes the marker to the full card drawing width, with left=0 mm,
    /// top=5 mm and height=12 mm. The native zoom percentage affects only the conversion
    /// into current display twips, not these logical card coordinates.
    /// </summary>
    public static LegacyMagneticStripePositionBounds GetLogicalBounds(double cardWidthMm)
    {
        if (!double.IsFinite(cardWidthMm) || cardWidthMm < 0)
            throw new ArgumentOutOfRangeException(nameof(cardWidthMm));
        return new LegacyMagneticStripePositionBounds(LeftMm, TopMm, cardWidthMm, HeightMm);
    }

    /// <summary>Exact helper 0x005FCAD0 conversion: millimeters * 1440 / 25.4.</summary>
    public static double MillimetersToTwips(double millimeters) =>
        millimeters * TwipsPerInch / MillimetersPerInch;

    /// <summary>
    /// Native DisegnaCampi takes mnuVisualizzaPosizioneBanda.Checked and sends that WORD
    /// directly to the band-label display-state setter before redrawing frmCarta.
    /// </summary>
    public static bool ShouldDisplayMarker(bool positionMenuChecked) => positionMenuChecked;
}
