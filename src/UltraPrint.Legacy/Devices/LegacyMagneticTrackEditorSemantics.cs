namespace UltraPrint.Legacy.Devices;

/// <summary>
/// Pure native contract for UltraPrint 2.2.115 frmTracce, the magnetic-track expression editor.
/// This type documents recovered VB6 form/global behavior only; it does not access a printer,
/// magnetic encoder, COM object, or legacy OLE control.
/// </summary>
public static class LegacyMagneticTrackEditorSemantics
{
    public const int FormLoadNativeAddress = 0x5E83E0;
    public const int FormUnloadNativeAddress = 0x5E87C0;
    public const int OkNativeAddress = 0x5E8A50;
    public const int TrackChangeNativeAddress = 0x5E8B60;
    public const int TrackOleDropNativeAddress = 0x5E8DC0;

    public const int Track1GlobalAddress = 0x62A05C;
    public const int Track2GlobalAddress = 0x62A060;
    public const int Track3GlobalAddress = 0x62A064;
    public const int TrackControlCount = 3;

    public const short VbTrueRawValue = -1;
    public const int OkTargetVtableOffset = 0x6FC;
    public const string AcceptedDropSourceControlName = "lblLabels";

    private static readonly int[] GlobalAddresses =
        [Track1GlobalAddress, Track2GlobalAddress, Track3GlobalAddress];

    public static IReadOnlyList<int> ProvenTrackGlobalAddresses => GlobalAddresses;

    /// <summary>
    /// Native frmTracce's OLE-drop handler accepts lblLabels and writes '[' + Caption + ']'
    /// into the selected Traccia control. The managed editor exposes the same resulting token
    /// without reproducing the obsolete OLE clipboard/control protocol.
    /// </summary>
    public static string BuildDroppedRecordFieldToken(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        return "[" + caption + "]";
    }
}
