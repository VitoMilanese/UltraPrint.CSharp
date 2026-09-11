using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class SmartDriverPrintCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestPrinterEscapeContract();
        TestPrinterEscapeFraming();
        TestSmartDriverPreambleWithoutOptionalGate();
        TestSmartDriverPreambleWithOptionalGate();
        TestEncodeChipContinuationBranches();
        TestTransientBooleanStorageAndPreFeedGate();
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
            "managed code does not emit PASSTHROUGH payloads before hardware validation");
    }

    private static void TestPrinterEscapeFraming()
    {
        AssertTrue(LegacyPrinterEscapeContract.PayloadFramingOrderRecovered,
            "PrinterEscape Chr/Variant payload framing order is recovered");
        AssertTrue(LegacyPrinterEscapeContract.EscapeOutputArgumentIsNullVariant,
            "PrinterEscape fifth Escape argument is a VT_NULL Variant");
        AssertSequence(
            new[]
            {
                LegacyPrinterEscapeFramePartKind.LeadingNullCharacter,
                LegacyPrinterEscapeFramePartKind.PayloadLengthCharacter,
                LegacyPrinterEscapeFramePartKind.Payload,
                LegacyPrinterEscapeFramePartKind.TrailingNullCharacter
            },
            LegacyPrinterEscapeContract.ProvenPayloadFrameParts,
            "PrinterEscape frame is Chr(0) + Chr(Len(payload)) + payload + Chr(0)");
        AssertEqual(3, LegacyPrinterEscapeContract.GetEscapeInputCount("ABC"),
            "Escape cbInput uses Len(payload), not framed length");
        AssertEqual(0, LegacyPrinterEscapeContract.GetEscapeInputCount(string.Empty),
            "empty payload preserves native Len(payload) call shape");
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

    private static void TestEncodeChipContinuationBranches()
    {
        var negative = LegacySmartDriverPrintSemantics.ResolveEncodeChipContinuation(-1);
        AssertEqual(1, negative.SmartCardContinueRawArgument,
            "negative EncodeChip result uses SmartCardContinue raw 1");
        AssertTrue(!negative.SetsSecondTransientBooleanToTrue,
            "negative EncodeChip branch does not set second transient Boolean");
        AssertTrue(negative.ClosesCurrentPageAndDocumentImmediately,
            "negative EncodeChip branch immediately closes page/document");

        var moreNegative = LegacySmartDriverPrintSemantics.ResolveEncodeChipContinuation(-17);
        AssertEqual(1, moreNegative.SmartCardContinueRawArgument,
            "all negative numeric EncodeChip results follow raw 1 branch");

        var zero = LegacySmartDriverPrintSemantics.ResolveEncodeChipContinuation(0);
        AssertEqual(0, zero.SmartCardContinueRawArgument,
            "zero EncodeChip result uses SmartCardContinue raw 0");
        AssertTrue(zero.SetsSecondTransientBooleanToTrue,
            "zero EncodeChip result sets second transient Boolean");
        AssertTrue(!zero.ClosesCurrentPageAndDocumentImmediately,
            "zero EncodeChip result continues module processing");

        var positive = LegacySmartDriverPrintSemantics.ResolveEncodeChipContinuation(5);
        AssertEqual(0, positive.SmartCardContinueRawArgument,
            "positive EncodeChip result uses same raw 0 branch as zero");
        AssertTrue(positive.SetsSecondTransientBooleanToTrue,
            "positive EncodeChip result sets second transient Boolean");
    }

    private static void TestTransientBooleanStorageAndPreFeedGate()
    {
        AssertEqual(0x62B670,
            LegacySmartDriverPrintSemantics.SmartDriverExamplesModulePublicBaseNativeAddress,
            "smartdriverExamples module public-data base");
        AssertEqual(0x10, LegacySmartDriverPrintSemantics.FirstTransientBooleanNativeOffset,
            "first transient Boolean public-data offset");
        AssertEqual(0x12, LegacySmartDriverPrintSemantics.SecondTransientBooleanNativeOffset,
            "second transient Boolean public-data offset");
        AssertEqual(0x62B680, LegacySmartDriverPrintSemantics.FirstTransientBooleanNativeAddress,
            "first transient Boolean native address");
        AssertEqual(0x62B682, LegacySmartDriverPrintSemantics.SecondTransientBooleanNativeAddress,
            "second transient Boolean native address");
        AssertEqual((short)-1, LegacySmartDriverPrintSemantics.VbBooleanTrueRaw,
            "native WORD Boolean True value");
        AssertEqual((short)0, LegacySmartDriverPrintSemantics.VbBooleanFalseRaw,
            "native WORD Boolean False value");
        AssertTrue(!LegacySmartDriverPrintSemantics.ShouldExitAtPreFeedTransientGate(false),
            "cleared second transient Boolean permits FeedCard path");
        AssertTrue(LegacySmartDriverPrintSemantics.ShouldExitAtPreFeedTransientGate(true),
            "set second transient Boolean exits to cleanup before FeedCard");
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
        AssertEqual(1, LegacySmartDriverPrintSemantics.NegativeEncodeChipResultContinueRawValue,
            "negative EncodeChip raw continue value");
        AssertEqual(0, LegacySmartDriverPrintSemantics.NonNegativeEncodeChipResultContinueRawValue,
            "non-negative EncodeChip raw continue value");
        AssertEqual(1, LegacySmartDriverPrintSemantics.RearOrFeedFallbackContinueRawValue,
            "rear/feed fallback raw continue value");
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
