using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class ChipPositionCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(0x541FB0, LegacyChipPositionSemantics.FrmCartaDrawFieldsNativeAddress,
            "frmCarta.DisegnaCampi native address");
        AssertEqual(0x5B1770, LegacyChipPositionSemantics.MainFormTogglePositionMenuNativeAddress,
            "MainForm chip-position menu handler address");
        AssertEqual(0x5392D0, LegacyChipPositionSemantics.FrmCartaChipLabelClickNativeAddress,
            "frmCarta chip label click address");
        AssertEqual(0x38C, LegacyChipPositionSemantics.MainFormPositionMenuVtableOffset,
            "MainForm chip-position menu accessor offset");
        AssertEqual(0x320, LegacyChipPositionSemantics.FrmCartaChipLabelVtableOffset,
            "frmCarta chip label accessor offset");
        AssertEqual(0x68, LegacyChipPositionSemantics.MenuCheckedGetterVtableOffset,
            "menu Checked getter offset");
        AssertEqual(0x6C, LegacyChipPositionSemantics.MenuCheckedSetterVtableOffset,
            "menu Checked setter offset");
        AssertEqual(0x9C, LegacyChipPositionSemantics.ChipDisplayStateSetterVtableOffset,
            "chip marker display-state setter offset");
        AssertEqual(0x58, LegacyChipPositionSemantics.FrmCartaScalePercentStorageOffset,
            "frmCarta scale percentage storage offset");
        AssertEqual(0x5FCAD0, LegacyChipPositionSemantics.MillimeterToTwipsHelperNativeAddress,
            "shared millimeter-to-twips native helper address");

        var bounds = LegacyChipPositionSemantics.GetLogicalBounds();
        AssertNear(8, bounds.LeftMm, "chip marker left");
        AssertNear(18, bounds.TopMm, "chip marker top");
        AssertNear(13, bounds.WidthMm, "chip marker width");
        AssertNear(12, bounds.HeightMm, "chip marker height");
        AssertNear(21, bounds.LeftMm + bounds.WidthMm, "chip marker right edge");
        AssertNear(30, bounds.TopMm + bounds.HeightMm, "chip marker lower edge");
        AssertTrue(LegacyChipPositionSemantics.ShouldDisplayMarker(true),
            "checked chip-position menu displays marker");
        AssertTrue(!LegacyChipPositionSemantics.ShouldDisplayMarker(false),
            "unchecked chip-position menu hides marker");
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
