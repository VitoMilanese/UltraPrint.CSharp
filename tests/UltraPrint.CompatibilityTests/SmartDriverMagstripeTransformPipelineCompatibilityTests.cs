using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Devices;

internal static class SmartDriverMagstripeTransformPipelineCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestRecoveredPipelineOrder();
        TestRecoveredRuntimeAndFunctionContracts();
        TestTokenExpansionContracts();
    }

    private static void TestRecoveredPipelineOrder()
    {
        AssertSequence(
            new[]
            {
                LegacyMagstripePreparationPipelineStageKind.TrimSourceGlobal,
                LegacyMagstripePreparationPipelineStageKind.InterpretLineWithTrueFlag,
                LegacyMagstripePreparationPipelineStageKind.RemoveNulCharacters,
                LegacyMagstripePreparationPipelineStageKind.ExpandPlusParenthesizedTokens,
                LegacyMagstripePreparationPipelineStageKind.ExpandBracketedTokens,
                LegacyMagstripePreparationPipelineStageKind.AssignPreparedTrackToCard
            },
            LegacySmartDriverMagstripeTransformPipelineSemantics.ProvenPipelineStages.Select(x => x.Kind),
            "0x603FC0 per-track preparation stage order");

        var stages = LegacySmartDriverMagstripeTransformPipelineSemantics.ProvenPipelineStages;
        AssertEqual(0x6079D0,
            stages.Single(x => x.Kind == LegacyMagstripePreparationPipelineStageKind.ExpandPlusParenthesizedTokens)
                .NativeAddress!.Value,
            "+(...) counter expansion helper address");
        AssertEqual(0x606910,
            stages.Single(x => x.Kind == LegacyMagstripePreparationPipelineStageKind.ExpandBracketedTokens)
                .NativeAddress!.Value,
            "[...] private expansion helper address");
    }

    private static void TestRecoveredRuntimeAndFunctionContracts()
    {
        AssertEqual(520, LegacySmartDriverMagstripeTransformPipelineSemantics.TrimVarRuntimeOrdinal,
            "MSVBVM60 rtcTrimVar ordinal");
        AssertEqual(0x840, LegacySmartDriverMagstripeTransformPipelineSemantics.InterpretLineVtableOffset,
            "Funzioni.Interpretariga vtable offset");
        AssertEqual((short)-1,
            LegacySmartDriverMagstripeTransformPipelineSemantics.InterpretLineSecondArgumentVbTrue,
            "Interpretariga second argument receives VB True");
        AssertEqual(0x7F8, LegacySmartDriverMagstripeTransformPipelineSemantics.ReplaceVtableOffset,
            "Funzioni.Sostituisci vtable offset");
        AssertEqual(0x7CC, LegacySmartDriverMagstripeTransformPipelineSemantics.GetInsideVtableOffset,
            "Funzioni.GetInside vtable offset");
        AssertEqual(0, LegacySmartDriverMagstripeTransformPipelineSemantics.NulCharacterCode,
            "Chr$(0) removal character code");
        AssertTrue(LegacySmartDriverMagstripeTransformPipelineSemantics.RemovesNulCharactersAfterInterpretLine,
            "Interpretariga output is stripped of embedded NUL characters");
        AssertEqual(1, LegacySmartDriverMagstripeTransformPipelineSemantics.SmartDriverExpansionRawMode,
            "smartDriver increments matched +(...) counter by 1");
    }

    private static void TestTokenExpansionContracts()
    {
        AssertEqual("+(", LegacySmartDriverMagstripeTransformPipelineSemantics.PlusParenthesisOpeningDelimiter,
            "+(...) opening delimiter");
        AssertEqual(")", LegacySmartDriverMagstripeTransformPipelineSemantics.PlusParenthesisClosingDelimiter,
            "+(...) closing delimiter");
        AssertEqual(0x5F5A90,
            LegacySmartDriverMagstripeTransformPipelineSemantics.PlusParenthesisReplacementResolverNativeAddress,
            "+(...) persistent counter resolver address");
        AssertTrue(LegacySmartDriverMagstripeTransformPipelineSemantics.PlusParenthesisTokensArePersistentCounters,
            "+(...) tokens are backed by frmContatori persistent counters");
        AssertEqual("Contatori.dat",
            LegacySmartDriverMagstripeTransformPipelineSemantics.CounterPersistenceFileName,
            "+(...) counter persistence filename");

        AssertEqual("[", LegacySmartDriverMagstripeTransformPipelineSemantics.BracketOpeningDelimiter,
            "[...] opening delimiter");
        AssertEqual("]", LegacySmartDriverMagstripeTransformPipelineSemantics.BracketClosingDelimiter,
            "[...] closing delimiter");
        AssertTrue(LegacySmartDriverMagstripeTransformPipelineSemantics.BracketStageUsesGetInside,
            "[...] stage uses Funzioni.GetInside");
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
