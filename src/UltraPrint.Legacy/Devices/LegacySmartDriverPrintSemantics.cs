namespace UltraPrint.Legacy.Devices;

/// <summary>
/// Data-only contract for MainForm.PrinterEscape recovered from UltraPrint 2.2.115.
/// The native routine sends a raw GDI PASSTHROUGH command and then ends the current
/// VB Printer document. The exact legacy payload framing is intentionally not emitted
/// by managed code until its byte ordering is fully recovered and hardware-tested.
/// </summary>
public static class LegacyPrinterEscapeContract
{
    public const int PrinterHdcDispid = 0x10009;
    public const int PrinterEndDocDispid = 0x20000;
    public const int GdiPassthroughEscapeCode = 19;

    /// <summary>The first explicit PrinterEscape argument is not read by this native build.</summary>
    public const bool FirstArgumentIsUsed = false;

    /// <summary>The second explicit PrinterEscape argument is the payload string.</summary>
    public const bool SecondArgumentIsPayload = true;

    /// <summary>The native routine closes the current VB Printer document after Escape.</summary>
    public const bool EndsCurrentPrinterDocument = true;

    /// <summary>
    /// False on purpose: the native Chr()/Variant concatenation that frames the PASSTHROUGH
    /// bytes is not yet reproduced, so this compatibility layer must not send printer data.
    /// </summary>
    public const bool ManagedPassthroughEmissionEnabled = false;
}

public enum LegacySmartDriverPrintStepKind
{
    OptionalPrePrintMagstripePreparation,
    EnableInteractiveMode,
    StartDocument,
    ScriptStartDocument,
    StartPage,
    ScriptStartPage,
    RotateCardSide,
    FeedCard,
    ScriptEncodeChip,
    RearSideGate,
    DisableInteractiveMode
}

public readonly record struct LegacySmartDriverPrintStep(
    LegacySmartDriverPrintStepKind Kind,
    int? RawArgument = null);

/// <summary>
/// Proven, non-executing smartDriver.StampaRecord orchestration facts. This class models
/// branch gates and raw constants only; it never calls ICE_API or GDI printer mutation APIs.
/// </summary>
public static class LegacySmartDriverPrintSemantics
{
    public const int FeedCardRawMode = 0x11;
    public const int RotateCardSideRawTrue = -1;
    public const int InteractiveModeRawTrue = -1;
    public const int InteractiveModeRawFalse = 0;

    public const string StartDocHook = "StartDoc";
    public const string StartPageHook = "StartPage";
    public const string EncodeChipHook = "EncodeChip";
    public const string EndPageHook = "EndPage";
    public const string EndDocHook = "EndDoc";

    private static readonly int[] SmartCardContinueValues = [1, 0, 1];
    private static readonly string[] ScriptHookNames =
        [StartDocHook, StartPageHook, EncodeChipHook, EndPageHook, EndDocHook];

    /// <summary>
    /// Raw second arguments observed at the three SmartCardContinue call sites, in native
    /// address order: 0x0051401D, 0x00514529, 0x00514FC1.
    /// </summary>
    public static IReadOnlyList<int> SmartCardContinueRawValues => SmartCardContinueValues;

    /// <summary>Script lifecycle names directly embedded in StampaRecord.</summary>
    public static IReadOnlyList<string> ProvenScriptHooks => ScriptHookNames;

    /// <summary>
    /// The lone Optional Variant argument defaults to False. When True it enables the
    /// pre-print magnetic-stripe/previous-document branch and the RotateCardSide call.
    /// Its original source-level parameter name has not been proven.
    /// </summary>
    public static IReadOnlyList<LegacySmartDriverPrintStep> BuildProvenPreamble(bool optionalGate)
    {
        var steps = new List<LegacySmartDriverPrintStep>(10);
        if (optionalGate)
            steps.Add(new(LegacySmartDriverPrintStepKind.OptionalPrePrintMagstripePreparation));

        steps.Add(new(LegacySmartDriverPrintStepKind.EnableInteractiveMode, InteractiveModeRawTrue));
        steps.Add(new(LegacySmartDriverPrintStepKind.StartDocument));
        steps.Add(new(LegacySmartDriverPrintStepKind.ScriptStartDocument));
        steps.Add(new(LegacySmartDriverPrintStepKind.StartPage));
        steps.Add(new(LegacySmartDriverPrintStepKind.ScriptStartPage));

        if (optionalGate)
            steps.Add(new(LegacySmartDriverPrintStepKind.RotateCardSide, RotateCardSideRawTrue));

        steps.Add(new(LegacySmartDriverPrintStepKind.FeedCard, FeedCardRawMode));
        steps.Add(new(LegacySmartDriverPrintStepKind.ScriptEncodeChip));
        return steps;
    }

    /// <summary>
    /// Native code reads frmCarta.HasRear before entering its later rear-side continuation.
    /// This is deliberately kept as a pure gate rather than enabling any printer action.
    /// </summary>
    public static bool ShouldEnterRearSide(bool hasRear) => hasRear;

    /// <summary>
    /// Cleanup always clears the two transient smartDriver flags, calls
    /// SetInteractiveMode(hDC, FALSE), and writes frmCarta.InteractiveMode=False.
    /// </summary>
    public static LegacySmartDriverPrintStep CleanupStep =>
        new(LegacySmartDriverPrintStepKind.DisableInteractiveMode, InteractiveModeRawFalse);

    /// <summary>
    /// No ICE card-job cancel export and no AbortDoc/KillDoc/CancelDC call were found in
    /// the recovered production print or PrinterEscape paths. Managed code must therefore
    /// retain application-level cooperative cancellation rather than invent hardware cancel.
    /// </summary>
    public const bool ProvenDeviceLevelCancellationAvailable = false;
}
