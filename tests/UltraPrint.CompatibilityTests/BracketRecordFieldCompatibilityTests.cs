using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class BracketRecordFieldCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeContractMetadata();
        TestRecordSourcePriority();
        TestLookupAndTokenShape();
        TestFieldReplacementAndMidSlicing();
        TestMissingAndNullValues();
        TestReplacementBehavior();
    }

    private static void TestNativeContractMetadata()
    {
        AssertEqual(0x606910, LegacyBracketRecordFieldSemantics.ExpansionHelperNativeAddress,
            "bracket expansion helper native address");
        AssertEqual(0x62A04C, LegacyBracketRecordFieldSemantics.FrmDatabaseLoadedFlagNativeAddress,
            "frmDatabase loaded flag address");
        AssertEqual(0x62A04A, LegacyBracketRecordFieldSemantics.TabellaLoadedFlagNativeAddress,
            "Tabella loaded flag address");
        AssertEqual("Data1", LegacyBracketRecordFieldSemantics.FrmDatabaseDataControlName,
            "frmDatabase data control");
        AssertEqual("datPrimaryRS", LegacyBracketRecordFieldSemantics.TabellaDataControlName,
            "Tabella data control");
        AssertEqual(0x2FC, LegacyBracketRecordFieldSemantics.DataControlAccessorVtableOffset,
            "data-control accessor vtable offset");
        AssertEqual("Recordset", LegacyBracketRecordFieldSemantics.RecordsetMemberName,
            "late-bound Recordset member");
        AssertEqual("Fields", LegacyBracketRecordFieldSemantics.FieldsMemberName,
            "late-bound Fields member");
        AssertEqual("Name", LegacyBracketRecordFieldSemantics.FieldNameMemberName,
            "field-validation Name member");
        AssertEqual(",", LegacyBracketRecordFieldSemantics.ArgumentSeparator,
            "bracket argument separator");
        AssertEqual(3, LegacyBracketRecordFieldSemantics.MaximumParsedArguments,
            "maximum parsed bracket arguments");
        AssertEqual(0x7F0, LegacyBracketRecordFieldSemantics.ParolaVtableOffset,
            "Funzioni.Parola vtable offset");
        AssertEqual(581, LegacyBracketRecordFieldSemantics.ValRuntimeOrdinal,
            "VB Val runtime ordinal");
        AssertEqual(632, LegacyBracketRecordFieldSemantics.MidRuntimeOrdinal,
            "VB Mid runtime ordinal");
        AssertEqual(560, LegacyBracketRecordFieldSemantics.IsNullRuntimeOrdinal,
            "VB IsNull runtime ordinal");
    }

    private static void TestRecordSourcePriority()
    {
        AssertEqual(LegacyBracketRecordSourceKind.None,
            LegacyBracketRecordFieldSemantics.SelectRecordSource(false, false),
            "no loaded data form means no bracket record source");
        AssertEqual(LegacyBracketRecordSourceKind.FrmDatabase,
            LegacyBracketRecordFieldSemantics.SelectRecordSource(true, false),
            "frmDatabase source is used when it alone is loaded");
        AssertEqual(LegacyBracketRecordSourceKind.Tabella,
            LegacyBracketRecordFieldSemantics.SelectRecordSource(false, true),
            "Tabella source is used when it alone is loaded");
        AssertEqual(LegacyBracketRecordSourceKind.Tabella,
            LegacyBracketRecordFieldSemantics.SelectRecordSource(true, true),
            "Tabella overwrites frmDatabase and wins when both forms are loaded");
    }

    private static void TestLookupAndTokenShape()
    {
        AssertEqual("[Surname]",
            LegacyBracketRecordFieldSemantics.BuildRecordsetFieldLookupKey("Surname"),
            "field lookup key is bracket-wrapped");
        AssertEqual("[Surname,2,4]",
            LegacyBracketRecordFieldSemantics.BuildOriginalBracketToken("Surname,2,4"),
            "replacement uses the complete original bracket body");
    }

    private static void TestFieldReplacementAndMidSlicing()
    {
        AssertEqual("ABCDEFG",
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABCDEFG"),
            "no positive start returns the whole field value");
        AssertEqual("CDEFG",
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABCDEFG", 3),
            "positive second argument applies one-based Mid to end");
        AssertEqual("CDE",
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABCDEFG", 3, 3),
            "positive third argument supplies Mid length");
        AssertEqual("FG",
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABCDEFG", 6, 99),
            "Mid length is naturally capped by remaining text");
        AssertEqual(string.Empty,
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABC", 9, 2),
            "Mid start beyond field length yields empty text");
        AssertEqual("ABCDEFG",
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, "ABCDEFG", 0, 3),
            "length alone does not slice when parsed start is not positive");
    }

    private static void TestMissingAndNullValues()
    {
        AssertEqual(string.Empty,
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(false, "ignored", 1, 2),
            "field lookup error resolves to empty text");
        AssertEqual(string.Empty,
            LegacyBracketRecordFieldSemantics.ResolveFieldReplacement(true, null, 1, 2),
            "Null field value resolves to empty text");
    }

    private static void TestReplacementBehavior()
    {
        AssertEqual("X[Name]Y[Name]Z",
            LegacyBracketRecordFieldSemantics.ReplaceResolvedTokenOccurrences(
                "X[Name]Y[Name]Z", "Name", LegacyBracketRecordSourceKind.None, "Alice"),
            "no active record source preserves bracket tokens unchanged");
        AssertEqual("XAliceYAliceZ",
            LegacyBracketRecordFieldSemantics.ReplaceResolvedTokenOccurrences(
                "X[Name]Y[Name]Z", "Name", LegacyBracketRecordSourceKind.Tabella, "Alice"),
            "active record source replaces all identical exact bracket tokens");
        AssertEqual("X[Name,2]Y",
            LegacyBracketRecordFieldSemantics.ReplaceResolvedTokenOccurrences(
                "X[Name,2]Y", "Name", LegacyBracketRecordSourceKind.FrmDatabase, "Alice"),
            "replacement matches the complete raw bracket body, not just field-name prefix");
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }
}
