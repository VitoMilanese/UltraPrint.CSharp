using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class SmartDriverPrintCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestPrinterEscapeContract();
        TestSmartDriverPreambleWithoutOptionalGate();
        TestSmartDriverPreambleWithOptionalGate();
        TestRearAndCleanupSemantics();
        TestRawSmartCardContinueSequence();
    }

    private static void TestPrinterEscapeContract()
    {
        AssertEqual(0x10009, LegacyPrinterEscapeContract.PrinterHdcDispid, "VB Printer hDC DISPID");
        AssertEqual(0x20000, LegacyPrinterEscapeContract.PrinterEndDocDispid, "VB Printer EndDoc DISPID");
        AssertEqual(19, LegacyPrinterEscapeContract.GdiPassthroughEscapeCode, "GDI PASSTHROUGH escape code");
        AssertTrue(!LegacyPrinterEscapeContract.FirstArgumentIsUsed, "PrinterEscape first explicit argument is unused");
        AssertTrue(LegacyPrinterEscapeContract.SecondArgumentIsPayload, "PrinterEscape second explicit argument is payload");
        AssertTrue(LegacyPrinterEscapeContract.EndsCurrentPrinterDocument, "PrinterEscape closes the current Printer document");
        AssertTrue(!LegacyPrinterEscapeContract.ManagedPassthroughEmissionEnabled,
            "managed code does not emit unresolved raw PASSTHROUGH payloads");
    }

    private static void TestSmartDriverPreambleWithoutOptionalGate()
    {
        var steps = LegacySmartDriverPrintSemantics.BuildProvenPreamble(optionalGate: false);
        AssertSequence(
            new[]
            {
                LegacySmartDriverPrintStepKind.EnableInteractiveMode,
                LegacySmartDriverPrintStepKind.StartDocument,
                LegacySmartDriverPrintStepKind.ScriptStartDocument,
                LegacySmartDriverPrintStepKind.StartPage,
                LegacySmartDriverPrintStepKind.ScriptStartPage,
                LegacySmartDriverPrintStepKind.FeedCard,
                LegacySmartDriverPrintStepKind.ScriptEncodeChip
            },
            steps.Select(x => x.Kind),
            "smartDriver preamble without Optional=True branch");

        AssertEqual(LegacySmartDriverPrintSemantics.InteractiveModeRawTrue,
            steps[0].RawArgument!.Value, "SetInteractiveMode TRUE raw value");
        AssertEqual(LegacySmartDriverPrintSemantics.FeedCardRawMode,
            steps.Single(x => x.Kind == LegacySmartDriverPrintStepKind.FeedCard).RawArgument!.Value,
            "FeedCard raw mode");
    }

    private static void TestSmartDriverPreambleWithOptionalGate()
    {
        var steps = LegacySmartDriverPrintSemantics.BuildProvenPreamble(optionalGate: true);
        AssertEqual(LegacySmartDriverPrintStepKind.OptionalPrePrintMagstripePreparation,
            steps[0].Kind, "Optional=True enters pre-print magnetic branch");
        var rotate = steps.Single(x => x.Kind == LegacySmartDriverPrintStepKind.RotateCardSide);
        AssertEqual(-1, rotate.RawArgument!.Value, "RotateCardSide receives VB True");

        var rotateIndex = steps.ToList().FindIndex(x => x.Kind == LegacySmartDriverPrintStepKind.RotateCardSide);
        var feedIndex = steps.ToList().FindIndex(x => x.Kind == LegacySmartDriverPrintStepKind.FeedCard);
        AssertTrue(rotateIndex > 0 && rotateIndex < feedIndex, "RotateCardSide precedes first FeedCard");
    }

    private static void TestRearAndCleanupSemantics()
    {
        AssertTrue(!LegacySmartDriverPrintSemantics.ShouldEnterRearSide(false), "HasRear=False skips rear continuation");
        AssertTrue(LegacySmartDriverPrintSemantics.ShouldEnterRearSide(true), "HasRear=True permits rear continuation");
        AssertEqual(LegacySmartDriverPrintStepKind.DisableInteractiveMode,
            LegacySmartDriverPrintSemantics.CleanupStep.Kind, "cleanup disables interactive mode");
        AssertEqual(0, LegacySmartDriverPrintSemantics.CleanupStep.RawArgument!.Value,
            "cleanup SetInteractiveMode FALSE raw value");
        AssertTrue(!LegacySmartDriverPrintSemantics.ProvenDeviceLevelCancellationAvailable,
            "no unproven device-level cancellation is exposed");
    }

    private static void TestRawSmartCardContinueSequence()
    {
        AssertSequence(new[] { 1, 0, 1 },
            LegacySmartDriverPrintSemantics.SmartCardContinueRawValues,
            "SmartCardContinue raw arguments by native call-site order");
        AssertSequence(new[] { "StartDoc", "StartPage", "EncodeChip", "EndPage", "EndDoc" },
            LegacySmartDriverPrintSemantics.ProvenScriptHooks,
            "smartDriver script hook names");
    }

    private static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
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
