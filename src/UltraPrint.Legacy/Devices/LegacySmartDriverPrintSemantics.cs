namespace UltraPrint.Legacy.Devices;

/// <summary>
/// Data-only contract for MainForm.PrinterEscape recovered from UltraPrint 2.2.115.
/// The native routine sends a raw GDI PASSTHROUGH command and then ends the current
/// VB Printer document. The exact VB6 Chr()/Variant frame ordering is recovered here,
/// but managed code still does not emit PASSTHROUGH data to real printer hardware.
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

    /// <summary>
    /// Native __vbaVarCat call ordering proves the complete frame expression:
    /// Chr(0) &amp; Chr(Len(payload)) &amp; payload &amp; Chr(0).
    /// </summary>
    public const bool PayloadFramingOrderRecovered = true;

    /// <summary>The fifth Escape argument is a VT_NULL Variant in the native call.</summary>
    public const bool EscapeOutputArgumentIsNullVariant = true;

    private static readonly LegacyPrinterEscapeFramePartKind[] PayloadFrameParts =
    [
        LegacyPrinterEscapeFramePartKind.LeadingNullCharacter,
        LegacyPrinterEscapeFramePartKind.PayloadLengthCharacter,
        LegacyPrinterEscapeFramePartKind.Payload,
        LegacyPrinterEscapeFramePartKind.TrailingNullCharacter
    ];

    /// <summary>
    /// Exact left-to-right VB6 Variant concatenation used to form lpInData before the
    /// dynamically imported GDI Escape call.
    /// </summary>
    public static IReadOnlyList<LegacyPrinterEscapeFramePartKind> ProvenPayloadFrameParts => PayloadFrameParts;

    /// <summary>
    /// Native code passes Len(payload), not the framed-string length, as Escape.cbInput.
    /// </summary>
    public static int GetEscapeInputCount(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Length;
    }

    /// <summary>The native routine closes the current VB Printer document after Escape.</summary>
    public const bool EndsCurrentPrinterDocument = true;

    /// <summary>
    /// False on purpose. The frame ordering is recovered, but actual VB6 ANSI/DBCS
    /// marshaling and the target printer/driver behavior have not been hardware-validated.
    /// This compatibility layer therefore remains non-executing.
    /// </summary>
    public const bool ManagedPassthroughEmissionEnabled = false;
}

public enum LegacyPrinterEscapeFramePartKind
{
    LeadingNullCharacter,
    PayloadLengthCharacter,
    Payload,
    TrailingNullCharacter
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

public enum LegacySmartDriverCardSide
{
    Front = 0,
    Rear = 1
}

public readonly record struct LegacySmartDriverPrintStep(
    LegacySmartDriverPrintStepKind Kind,
    int? RawArgument = null);

/// <summary>
/// Proven branch effect of the native FeedCard return test. This remains pure state:
/// no ICE or GDI function is invoked by the managed compatibility model.
/// </summary>
public readonly record struct LegacyFeedCardContinuation(
    bool RunsEncodeChip,
    int? SmartCardContinueRawArgument,
    bool ClosesCurrentPageAndDocumentImmediately);

/// <summary>
/// Proven branch effect immediately after the numeric EncodeChip script result is compared
/// with zero. This is data only; it never invokes SmartCardContinue or closes a GDI job.
/// </summary>
public readonly record struct LegacyEncodeChipContinuation(
    int SmartCardContinueRawArgument,
    bool SetsSecondTransientBooleanToTrue,
    bool ClosesCurrentPageAndDocumentImmediately);

/// <summary>
/// Proven branch effect after one of StampaRecord's Optional-gated calls to
/// EncodeMagStripeWithApi has already been reached and its return value has been coerced
/// through the native VB6 Boolean/Null helper. This record performs no printer I/O.
/// </summary>
public readonly record struct LegacyOptionalMagstripeEncodeContinuation(
    bool SetsFirstTransientBooleanToTrue,
    bool ClosesCurrentPageAndDocumentImmediately);

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

    public const int SmartDriverExamplesModulePublicBaseNativeAddress = 0x62B670;
    public const int FirstTransientBooleanNativeOffset = 0x10;
    public const int SecondTransientBooleanNativeOffset = 0x12;
    public const int FirstTransientBooleanNativeAddress =
        SmartDriverExamplesModulePublicBaseNativeAddress + FirstTransientBooleanNativeOffset;
    public const int SecondTransientBooleanNativeAddress =
        SmartDriverExamplesModulePublicBaseNativeAddress + SecondTransientBooleanNativeOffset;
    public const short VbBooleanTrueRaw = -1;
    public const short VbBooleanFalseRaw = 0;

    public const int NegativeEncodeChipResultContinueRawValue = 1;
    public const int NonNegativeEncodeChipResultContinueRawValue = 0;
    public const int FeedCardFailureContinueRawValue = 1;

    /// <summary>
    /// Private side-processing helper called by smartDriver.StampaRecord after the post-chip
    /// optional magnetic-stripe stage. The source-level helper name is not recovered.
    /// </summary>
    public const int SideProcessingHelperNativeAddress = 0x613800;

    /// <summary>
    /// smartDriver passes raw first argument 1 to both Front and Rear helper calls.
    /// Other callers also use 0, but the meaning of this mode value is not proven.
    /// </summary>
    public const int SmartDriverSideProcessingHelperRawMode = 1;

    /// <summary>
    /// StampaRecord contains two structurally matching Optional-gated magnetic-stripe encode
    /// sites: one before interactive-mode setup and one after the non-negative EncodeChip path.
    /// </summary>
    public const int OptionalMagstripeEncodeCallSiteCount = 2;

    /// <summary>
    /// The unknown preparation helper at native 0x00603FC0 receives a numeric Variant 1 in
    /// both StampaRecord magnetic-stripe sites. Its source-level meaning is intentionally unclaimed.
    /// </summary>
    public const int OptionalMagstripePreparationHelperRawArgument = 1;

    public const int FirstMagstripeInputGetterVtableOffset = 0x75C;
    public const int SecondMagstripeInputGetterVtableOffset = 0x768;
    public const int ThirdMagstripeInputGetterVtableOffset = 0x774;
    public const int EncodeMagStripeWithApiVtableOffset = 0x6F8;

    public const string StartDocHook = "StartDoc";
    public const string StartPageHook = "StartPage";
    public const string EncodeChipHook = "EncodeChip";
    public const string EndPageHook = "EndPage";
    public const string EndDocHook = "EndDoc";

    private static readonly int[] SmartCardContinueValues =
    [
        NegativeEncodeChipResultContinueRawValue,
        NonNegativeEncodeChipResultContinueRawValue,
        FeedCardFailureContinueRawValue
    ];
    private static readonly int[] MagstripeInputGetterOffsets =
    [
        FirstMagstripeInputGetterVtableOffset,
        SecondMagstripeInputGetterVtableOffset,
        ThirdMagstripeInputGetterVtableOffset
    ];
    private static readonly LegacySmartDriverCardSide[] FrontOnlySideProcessingOrder =
        [LegacySmartDriverCardSide.Front];
    private static readonly LegacySmartDriverCardSide[] FrontThenRearSideProcessingOrder =
        [LegacySmartDriverCardSide.Front, LegacySmartDriverCardSide.Rear];
    private static readonly string[] ScriptHookNames =
        [StartDocHook, StartPageHook, EncodeChipHook, EndPageHook, EndDocHook];

    /// <summary>
    /// Raw second arguments observed at the three SmartCardContinue call sites, in native
    /// address order: 0x0051401D, 0x00514529, 0x00514FC1.
    /// </summary>
    public static IReadOnlyList<int> SmartCardContinueRawValues => SmartCardContinueValues;

    /// <summary>
    /// The three frmCarta getter vtable offsets read in order before both native
    /// EncodeMagStripeWithApi calls. Source-level property names are not claimed here.
    /// </summary>
    public static IReadOnlyList<int> ProvenMagstripeInputGetterVtableOffsets => MagstripeInputGetterOffsets;

    /// <summary>Script lifecycle names directly embedded in StampaRecord.</summary>
    public static IReadOnlyList<string> ProvenScriptHooks => ScriptHookNames;

    /// <summary>
    /// The lone Optional Variant argument defaults to False. When True it enables the
    /// pre-print magnetic-stripe/previous-document branch and the RotateCardSide call.
    /// This preamble stops at FeedCard because the next native action depends on FeedCard's return.
    /// Its original source-level parameter name has not been proven.
    /// </summary>
    public static IReadOnlyList<LegacySmartDriverPrintStep> BuildProvenPreamble(bool optionalGate)
    {
        var steps = new List<LegacySmartDriverPrintStep>(9);
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
        return steps;
    }

    /// <summary>
    /// A nonzero FeedCard return proceeds to script EncodeChip. A zero return branches
    /// directly to SmartCardContinue(hDC, 1), EndPage/EndDoc and matching script hooks.
    /// </summary>
    public static LegacyFeedCardContinuation ResolveFeedCardContinuation(bool feedSucceeded) =>
        feedSucceeded
            ? new(
                RunsEncodeChip: true,
                SmartCardContinueRawArgument: null,
                ClosesCurrentPageAndDocumentImmediately: false)
            : new(
                RunsEncodeChip: false,
                SmartCardContinueRawArgument: FeedCardFailureContinueRawValue,
                ClosesCurrentPageAndDocumentImmediately: true);

    /// <summary>
    /// Native __vbaVarTstLt compares the EncodeChip result with numeric zero. For a negative
    /// result StampaRecord calls SmartCardContinue(hDC, 1) and immediately closes page/doc;
    /// otherwise it sets the second smartdriverExamples public Boolean and calls raw value 0.
    /// The model accepts a signed integer deliberately rather than guessing full VB Variant coercion.
    /// </summary>
    public static LegacyEncodeChipContinuation ResolveEncodeChipContinuation(int numericResult) =>
        numericResult < 0
            ? new(
                NegativeEncodeChipResultContinueRawValue,
                SetsSecondTransientBooleanToTrue: false,
                ClosesCurrentPageAndDocumentImmediately: true)
            : new(
                NonNegativeEncodeChipResultContinueRawValue,
                SetsSecondTransientBooleanToTrue: true,
                ClosesCurrentPageAndDocumentImmediately: false);

    /// <summary>
    /// Each Optional-gated magnetic-stripe site first calls the still-unnamed helper at
    /// 0x00603FC0 and compares its returned Variant with numeric zero using __vbaVarTstNe.
    /// EncodeMagStripeWithApi is reached only when Optional=True and that helper result is nonzero.
    /// </summary>
    public static bool ShouldReachOptionalMagstripeEncode(
        bool optionalGate,
        bool preparationHelperResultNonZero) =>
        optionalGate && preparationHelperResultNonZero;

    /// <summary>
    /// After EncodeMagStripeWithApi is reached, native __vbaBoolVarNull coerces its result.
    /// False sets the first public WORD Boolean and continues without immediate EndPage/EndDoc;
    /// True leaves that Boolean cleared and immediately closes page/doc with matching script hooks.
    /// </summary>
    public static LegacyOptionalMagstripeEncodeContinuation ResolveOptionalMagstripeEncodeResult(
        bool encodeSucceeded) =>
        encodeSucceeded
            ? new(
                SetsFirstTransientBooleanToTrue: false,
                ClosesCurrentPageAndDocumentImmediately: true)
            : new(
                SetsFirstTransientBooleanToTrue: true,
                ClosesCurrentPageAndDocumentImmediately: false);

    /// <summary>
    /// The second public Boolean is checked immediately before FeedCard. A nonzero value
    /// branches directly to final cleanup. Its source-level name/meaning is still unknown.
    /// </summary>
    public static bool ShouldExitAtPreFeedTransientGate(bool secondTransientBoolean) =>
        secondTransientBoolean;

    /// <summary>
    /// The shared native helper at 0x005F2540 compares the first public WORD Boolean to
    /// VB True (0xFFFF) and returns immediately when it is set. The flag's source name and
    /// broader semantic meaning remain intentionally unclaimed.
    /// </summary>
    public static bool ShouldExitSharedModuleHelperAtFirstTransientGate(bool firstTransientBoolean) =>
        firstTransientBoolean;

    /// <summary>
    /// When control reaches the private 0x00613800 side-processing stage, smartDriver always
    /// processes raw side Variant 0 (Fronte) first. If frmCarta.HasRear is true it then invokes
    /// the same helper for raw side Variant 1 (Retro). The Rear call returns directly to cleanup.
    /// </summary>
    public static IReadOnlyList<LegacySmartDriverCardSide> BuildSideProcessingOrder(bool hasRear) =>
        hasRear ? FrontThenRearSideProcessingOrder : FrontOnlySideProcessingOrder;

    public static int GetRawSideVariant(LegacySmartDriverCardSide side) => (int)side;

    /// <summary>
    /// Native code reads frmCarta.HasRear after the Front side-processing helper call.
    /// </summary>
    public static bool ShouldEnterRearSide(bool hasRear) => hasRear;

    /// <summary>
    /// Cleanup clears both public WORD Booleans in the smartdriverExamples BAS module,
    /// calls SetInteractiveMode(hDC, FALSE), and writes frmCarta.InteractiveMode=False.
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
