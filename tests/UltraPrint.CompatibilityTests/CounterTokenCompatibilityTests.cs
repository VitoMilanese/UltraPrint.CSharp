using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class CounterTokenCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeCounterLayout();
        TestMatchedCounterResolution();
        TestLegacyZeriFormatting();
        TestNoMatchContract();
        TestIdenticalTokensShareOneResolvedIncrement();
    }

    private static void TestNativeCounterLayout()
    {
        AssertEqual(0x5F5A90, LegacyCounterTokenSemantics.ResolverNativeAddress,
            "counter token resolver native address");
        AssertEqual(0x5F5E70, LegacyCounterTokenSemantics.LoadCounterTableNativeAddress,
            "counter-table load helper native address");
        AssertEqual(0x5F60C0, LegacyCounterTokenSemantics.SaveCounterTableNativeAddress,
            "counter-table save helper native address");
        AssertEqual("Contatori.dat", LegacyCounterTokenSemantics.PersistenceFileName,
            "counter persistence filename");
        AssertEqual(1, LegacyCounterTokenSemantics.FirstCounterIndex, "first native counter index");
        AssertEqual(99, LegacyCounterTokenSemantics.LastCounterIndex, "last native counter index");
        AssertEqual(32, LegacyCounterTokenSemantics.CounterRecordStrideBytes, "counter record stride");
        AssertEqual(10, LegacyCounterTokenSemantics.CounterNameFixedLengthCharacters,
            "fixed counter-name length");
        AssertEqual(0x14, LegacyCounterTokenSemantics.DigitsOffset, "digits field offset");
        AssertEqual(0x16, LegacyCounterTokenSemantics.ZeroPaddingBooleanOffset,
            "zero-padding Boolean offset");
        AssertEqual(0x1C, LegacyCounterTokenSemantics.CurrentValueOffset,
            "current counter value offset");
        AssertEqual(6, LegacyCounterTokenSemantics.DefaultDigits,
            "new-counter default digit count");
        AssertTrue(LegacyCounterTokenSemantics.DefaultZeroPadding,
            "new counters default to legacy zero padding");
        AssertEqual(1, LegacyCounterTokenSemantics.SmartDriverIncrement,
            "smartDriver counter increment");
    }

    private static void TestMatchedCounterResolution()
    {
        var result = LegacyCounterTokenSemantics.ResolveMatchedCounter(
            currentValue: 41,
            increment: 1,
            digits: 6,
            zeroPadding: true);

        AssertTrue(result.Matched, "matched counter result is marked matched");
        AssertEqual(42, result.UpdatedValue!.Value,
            "counter increments before token substitution");
        AssertEqual("000042", result.Replacement,
            "matched counter uses configured zero-padded replacement");
        AssertTrue(result.PersistsUpdatedCounterTable,
            "matched counter update is immediately persisted by native save helper");

        var unpadded = LegacyCounterTokenSemantics.ResolveMatchedCounter(
            currentValue: 41,
            increment: 1,
            digits: 6,
            zeroPadding: false);
        AssertEqual("42", unpadded.Replacement,
            "disabled Check1/zero-padding returns ordinary decimal value");
    }

    private static void TestLegacyZeriFormatting()
    {
        AssertEqual("000042", LegacyCounterTokenSemantics.FormatWithLegacyZeri("42", 6),
            "Zeri left-pads a shorter value");
        AssertEqual("000042", LegacyCounterTokenSemantics.FormatWithLegacyZeri(" 42 ", 6),
            "Zeri trims the supplied value before formatting");
        AssertEqual("000000", LegacyCounterTokenSemantics.FormatWithLegacyZeri("1000000", 6),
            "Zeri reproduces VB Right truncation when value exceeds width");
        AssertEqual(string.Empty, LegacyCounterTokenSemantics.FormatWithLegacyZeri("42", 0),
            "Zeri width zero returns empty text");
    }

    private static void TestNoMatchContract()
    {
        var result = LegacyCounterTokenSemantics.NoMatch;
        AssertTrue(!result.Matched, "unmatched token result is not matched");
        AssertTrue(result.UpdatedValue is null, "unmatched token does not update a value");
        AssertEqual(string.Empty, result.Replacement, "unmatched resolver returns empty text");
        AssertTrue(!result.PersistsUpdatedCounterTable,
            "unmatched resolver does not save the counter table");
    }

    private static void TestIdenticalTokensShareOneResolvedIncrement()
    {
        var result = LegacyCounterTokenSemantics.ResolveMatchedCounter(
            currentValue: 7,
            increment: 1,
            digits: 3,
            zeroPadding: true);
        var expanded = LegacyCounterTokenSemantics.ReplaceAllResolvedTokenOccurrences(
            "A+(CARD)-B+(CARD)-C",
            "CARD",
            result.Replacement);

        AssertEqual(8, result.UpdatedValue!.Value,
            "one resolver call performs one counter increment");
        AssertEqual("A008-B008-C", expanded,
            "Funzioni.Sostituisci replaces every identical exact token with that one resolved value");
        AssertEqual("+(CARD)", LegacyCounterTokenSemantics.BuildToken("CARD"),
            "counter token syntax");
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
