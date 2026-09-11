namespace UltraPrint.Legacy.Devices;

public enum LegacyMagstripePreparationPipelineStageKind
{
    TrimSourceGlobal,
    InterpretLineWithTrueFlag,
    RemoveNulCharacters,
    ExpandPlusParenthesizedTokens,
    ExpandBracketedTokens,
    AssignPreparedTrackToCard
}

/// <summary>
/// One structural stage in the recovered 0x00603FC0 magnetic-track preparation pipeline.
/// NativeAddress is populated only for private native helper stages; public VB6/runtime
/// calls are described by the constants on LegacySmartDriverMagstripeTransformPipelineSemantics.
/// </summary>
public readonly record struct LegacyMagstripePreparationPipelineStage(
    LegacyMagstripePreparationPipelineStageKind Kind,
    int? NativeAddress = null);

/// <summary>
/// Pure structural contract for the transformation performed by native helper 0x00603FC0
/// before it writes each prepared Traccia1/2/3 value into frmCarta. The +(...) stage is now
/// identified as persistent frmContatori counter expansion; the deeper [...] semantics remain
/// deliberately unclaimed. No legacy macro or hardware code executes here.
/// </summary>
public static class LegacySmartDriverMagstripeTransformPipelineSemantics
{
    public const int TrimVarRuntimeOrdinal = 520;
    public const int InterpretLineVtableOffset = 0x840;
    public const short InterpretLineSecondArgumentVbTrue = -1;
    public const int ReplaceVtableOffset = 0x7F8;
    public const int GetInsideVtableOffset = 0x7CC;

    public const int NulCharacterCode = 0;

    public const int PlusParenthesisExpansionHelperNativeAddress = 0x6079D0;
    public const string PlusParenthesisOpeningDelimiter = "+(";
    public const string PlusParenthesisClosingDelimiter = ")";

    public const int BracketExpansionHelperNativeAddress = 0x606910;
    public const string BracketOpeningDelimiter = "[";
    public const string BracketClosingDelimiter = "]";

    /// <summary>
    /// The private +(...) helper receives the same raw mode value propagated into 0x00603FC0
    /// by smartDriver.StampaRecord. Native counter recovery proves this value is the amount
    /// added to the matched counter; smartDriver therefore increments matching counters by 1.
    /// </summary>
    public const int SmartDriverExpansionRawMode = 1;

    private static readonly LegacyMagstripePreparationPipelineStage[] PipelineStages =
    [
        new(LegacyMagstripePreparationPipelineStageKind.TrimSourceGlobal),
        new(LegacyMagstripePreparationPipelineStageKind.InterpretLineWithTrueFlag),
        new(LegacyMagstripePreparationPipelineStageKind.RemoveNulCharacters),
        new(
            LegacyMagstripePreparationPipelineStageKind.ExpandPlusParenthesizedTokens,
            PlusParenthesisExpansionHelperNativeAddress),
        new(
            LegacyMagstripePreparationPipelineStageKind.ExpandBracketedTokens,
            BracketExpansionHelperNativeAddress),
        new(LegacyMagstripePreparationPipelineStageKind.AssignPreparedTrackToCard)
    ];

    /// <summary>
    /// Exact per-track structural order recovered from 0x00603FC0. This is metadata only;
    /// no legacy macro interpreter, token resolver, COM object, or printer API is invoked.
    /// </summary>
    public static IReadOnlyList<LegacyMagstripePreparationPipelineStage> ProvenPipelineStages =>
        PipelineStages;

    /// <summary>
    /// 0x00603FC0 removes embedded NUL characters after Interpretariga by constructing
    /// Chr$(0) and calling Funzioni.Sostituisci(target, Chr$(0), "").
    /// </summary>
    public const bool RemovesNulCharactersAfterInterpretLine = true;

    /// <summary>
    /// 0x006079D0 repeatedly extracts +(...) bodies through Funzioni.GetInside and replaces
    /// complete occurrences through Funzioni.Sostituisci. Helper 0x005F5A90 is now proven to
    /// resolve the matching frmContatori record, update/persist it, and return its formatted value.
    /// </summary>
    public const int PlusParenthesisReplacementResolverNativeAddress = 0x5F5A90;
    public const bool PlusParenthesisTokensArePersistentCounters = true;
    public const string CounterPersistenceFileName = "Contatori.dat";

    /// <summary>
    /// 0x00606910 begins by extracting [...] bodies through the same GetInside contract.
    /// Its deeper replacement meaning is deliberately left unresolved.
    /// </summary>
    public const bool BracketStageUsesGetInside = true;
}
