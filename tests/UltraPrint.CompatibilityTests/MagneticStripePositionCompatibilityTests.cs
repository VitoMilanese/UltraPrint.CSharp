using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class MagneticStripePositionCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(0x541FB0, LegacyMagneticStripePositionSemantics.FrmCartaDrawFieldsNativeAddress,
            "frmCarta.DisegnaCampi native address");
        AssertEqual(0x5B1620, LegacyMagneticStripePositionSemantics.MainFormTogglePositionMenuNativeAddress,
            "MainForm magnetic-position menu handler address");
        AssertEqual(0x539190, LegacyMagneticStripePositionSemantics.FrmCartaBandLabelClickNativeAddress,
            "frmCarta magnetic-band label click address");
        AssertEqual(0x388, LegacyMagneticStripePositionSemantics.MainFormPositionMenuVtableOffset,
            "MainForm magnetic-position menu accessor offset");
        AssertEqual(0x324, LegacyMagneticStripePositionSemantics.FrmCartaBandLabelVtableOffset,
            "frmCarta magnetic-band label accessor offset");
        AssertEqual(0x68, LegacyMagneticStripePositionSemantics.MenuCheckedGetterVtableOffset,
            "menu Checked getter offset");
        AssertEqual(0x6C, LegacyMagneticStripePositionSemantics.MenuCheckedSetterVtableOffset,
            "menu Checked setter offset");
        AssertEqual(0x9C, LegacyMagneticStripePositionSemantics.BandDisplayStateSetterVtableOffset,
            "band marker display-state setter offset");
        AssertEqual(0x58, LegacyMagneticStripePositionSemantics.FrmCartaScalePercentStorageOffset,
            "frmCarta scale percentage storage offset");
        AssertEqual(0x5FCAD0, LegacyMagneticStripePositionSemantics.MillimeterToTwipsHelperNativeAddress,
            "millimeter-to-twips native helper address");

        var bounds = LegacyMagneticStripePositionSemantics.GetLogicalBounds(85.6);
        AssertNear(0, bounds.LeftMm, "band marker left");
        AssertNear(5, bounds.TopMm, "band marker top");
        AssertNear(85.6, bounds.WidthMm, "band marker full-card width");
        AssertNear(12, bounds.HeightMm, "band marker height");
        AssertNear(17, bounds.TopMm + bounds.HeightMm, "band marker lower edge");

        AssertNear(1440, LegacyMagneticStripePositionSemantics.MillimetersToTwips(25.4),
            "one inch converts to 1440 twips");
        AssertNear(5 * 1440.0 / 25.4, LegacyMagneticStripePositionSemantics.MillimetersToTwips(5),
            "5 mm native position conversion");
        AssertTrue(LegacyMagneticStripePositionSemantics.ShouldDisplayMarker(true),
            "checked position menu displays marker");
        AssertTrue(!LegacyMagneticStripePositionSemantics.ShouldDisplayMarker(false),
            "unchecked position menu hides marker");
    }

    private static void AssertNear(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }
}
