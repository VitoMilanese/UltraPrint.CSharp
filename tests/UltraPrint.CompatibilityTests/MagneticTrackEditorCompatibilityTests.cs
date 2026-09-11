using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class MagneticTrackEditorCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(0x5E83E0, LegacyMagneticTrackEditorSemantics.FormLoadNativeAddress, "frmTracce Form_Load");
        AssertEqual(0x5E87C0, LegacyMagneticTrackEditorSemantics.FormUnloadNativeAddress, "frmTracce Form_Unload");
        AssertEqual(0x5E8A50, LegacyMagneticTrackEditorSemantics.OkNativeAddress, "frmTracce Ok");
        AssertEqual(0x5E8B60, LegacyMagneticTrackEditorSemantics.TrackChangeNativeAddress, "frmTracce Traccia Change");
        AssertEqual(0x5E8DC0, LegacyMagneticTrackEditorSemantics.TrackOleDropNativeAddress, "frmTracce Traccia OLE drop");

        AssertTrue(LegacyMagneticTrackEditorSemantics.ProvenTrackGlobalAddresses.SequenceEqual(
            new[] { 0x62A05C, 0x62A060, 0x62A064 }), "Traccia1/2/3 global address order");
        AssertEqual(3, LegacyMagneticTrackEditorSemantics.TrackControlCount, "frmTracce track control count");
        AssertEqual((short)-1, LegacyMagneticTrackEditorSemantics.VbTrueRawValue, "VB True raw value");
        AssertEqual(0x6FC, LegacyMagneticTrackEditorSemantics.OkTargetVtableOffset, "frmTracce OK target vtable offset");
        AssertEqual("lblLabels", LegacyMagneticTrackEditorSemantics.AcceptedDropSourceControlName,
            "accepted native OLE drop source name");
        AssertEqual("[SERIAL_NUMBER]", LegacyMagneticTrackEditorSemantics.BuildDroppedRecordFieldToken("SERIAL_NUMBER"),
            "frmTracce dropped label Caption becomes bracket record token");
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
