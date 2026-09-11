namespace UltraPrint.Legacy.Devices;

/// <summary>
/// One recovered TracciaN -> frmCarta track-property pairing used by native helper
/// 0x00603FC0. Offsets are VB6 vtable offsets, not managed delegates.
/// </summary>
public readonly record struct LegacyMagstripePreparedTrackBinding(
    string SourceGlobalName,
    int GetterVtableOffset,
    int SetterVtableOffset);

/// <summary>
/// Pure compatibility semantics for native helper 0x00603FC0 used by both Optional-gated
/// magnetic-stripe sites in smartDriver.StampaRecord. The helper prepares the three global
/// Traccia values into frmCarta track properties, rereads them, and returns the sum of their
/// VB Len() values. This class performs no printer, GDI, COM, or ICE_API operation.
/// </summary>
public static class LegacySmartDriverMagstripePreparationSemantics
{
    public const int NativeAddress = 0x603FC0;
    public const int SmartDriverRawArgument = 1;

    private static readonly LegacyMagstripePreparedTrackBinding[] PreparedTrackBindings =
    [
        new("Traccia1", GetterVtableOffset: 0x75C, SetterVtableOffset: 0x760),
        new("Traccia2", GetterVtableOffset: 0x768, SetterVtableOffset: 0x76C),
        new("Traccia3", GetterVtableOffset: 0x774, SetterVtableOffset: 0x778)
    ];

    /// <summary>
    /// Exact source-global and frmCarta getter/setter pairing observed in the native helper,
    /// in Track 1 -> Track 2 -> Track 3 order.
    /// </summary>
    public static IReadOnlyList<LegacyMagstripePreparedTrackBinding> ProvenPreparedTrackBindings =>
        PreparedTrackBindings;

    /// <summary>
    /// Models the helper's return value after preparation: Len(track1) + Len(track2) + Len(track3).
    /// Lengths are supplied explicitly so no unproven VB6 Variant/string coercion is introduced.
    /// </summary>
    public static int GetPreparedTrackDataLength(
        int firstTrackLength,
        int secondTrackLength,
        int thirdTrackLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstTrackLength);
        ArgumentOutOfRangeException.ThrowIfNegative(secondTrackLength);
        ArgumentOutOfRangeException.ThrowIfNegative(thirdTrackLength);

        return checked(firstTrackLength + secondTrackLength + thirdTrackLength);
    }

    /// <summary>
    /// StampaRecord compares the helper result to numeric zero with __vbaVarTstNe. Therefore
    /// magnetic-stripe encoding is eligible only when at least one prepared track has data.
    /// </summary>
    public static bool HasPreparedTrackData(
        int firstTrackLength,
        int secondTrackLength,
        int thirdTrackLength) =>
        GetPreparedTrackDataLength(firstTrackLength, secondTrackLength, thirdTrackLength) != 0;
}
