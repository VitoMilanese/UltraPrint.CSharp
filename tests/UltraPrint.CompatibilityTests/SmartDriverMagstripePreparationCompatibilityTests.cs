using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class SmartDriverMagstripePreparationCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeHelperContract();
        TestPreparedTrackLengthGate();
        TestStampaRecordIntegrationGate();
    }

    private static void TestNativeHelperContract()
    {
        AssertEqual(0x603FC0, LegacySmartDriverMagstripePreparationSemantics.NativeAddress,
            "magstripe preparation helper native address");
        AssertEqual(1, LegacySmartDriverMagstripePreparationSemantics.SmartDriverRawArgument,
            "smartDriver preparation helper raw argument");

        var bindings = LegacySmartDriverMagstripePreparationSemantics.ProvenPreparedTrackBindings;
        AssertEqual(3, bindings.Count, "three prepared magnetic-track bindings");
        AssertBinding(bindings[0], "Traccia1", 0x75C, 0x760);
        AssertBinding(bindings[1], "Traccia2", 0x768, 0x76C);
        AssertBinding(bindings[2], "Traccia3", 0x774, 0x778);
    }

    private static void TestPreparedTrackLengthGate()
    {
        AssertEqual(0,
            LegacySmartDriverMagstripePreparationSemantics.GetPreparedTrackDataLength(0, 0, 0),
            "empty prepared tracks return zero total length");
        AssertEqual(7,
            LegacySmartDriverMagstripePreparationSemantics.GetPreparedTrackDataLength(3, 0, 4),
            "helper return is sum of all three prepared track lengths");

        AssertTrue(!LegacySmartDriverMagstripePreparationSemantics.HasPreparedTrackData(0, 0, 0),
            "all-empty prepared tracks fail the native nonzero gate");
        AssertTrue(LegacySmartDriverMagstripePreparationSemantics.HasPreparedTrackData(0, 1, 0),
            "one non-empty prepared track satisfies the native nonzero gate");
    }

    private static void TestStampaRecordIntegrationGate()
    {
        var noData = LegacySmartDriverMagstripePreparationSemantics.HasPreparedTrackData(0, 0, 0);
        var hasData = LegacySmartDriverMagstripePreparationSemantics.HasPreparedTrackData(0, 2, 0);

        AssertTrue(!LegacySmartDriverPrintSemantics.ShouldReachOptionalMagstripeEncode(
                optionalGate: true, preparationHelperResultNonZero: noData),
            "Optional=True still skips EncodeMagStripeWithApi when all prepared tracks are empty");
        AssertTrue(LegacySmartDriverPrintSemantics.ShouldReachOptionalMagstripeEncode(
                optionalGate: true, preparationHelperResultNonZero: hasData),
            "Optional=True reaches EncodeMagStripeWithApi when at least one prepared track has data");
        AssertTrue(!LegacySmartDriverPrintSemantics.ShouldReachOptionalMagstripeEncode(
                optionalGate: false, preparationHelperResultNonZero: hasData),
            "Optional=False skips magnetic-stripe encoding even when prepared track data exists");
    }

    private static void AssertBinding(
        LegacyMagstripePreparedTrackBinding actual,
        string sourceName,
        int getterOffset,
        int setterOffset)
    {
        AssertEqual(sourceName, actual.SourceGlobalName, $"{sourceName} source global");
        AssertEqual(getterOffset, actual.GetterVtableOffset, $"{sourceName} getter offset");
        AssertEqual(setterOffset, actual.SetterVtableOffset, $"{sourceName} setter offset");
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
