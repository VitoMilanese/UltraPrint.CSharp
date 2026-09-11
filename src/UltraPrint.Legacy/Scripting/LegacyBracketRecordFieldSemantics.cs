namespace UltraPrint.Legacy.Scripting;

public enum LegacyBracketRecordSourceKind
{
    None,
    FrmDatabase,
    Tabella
}

/// <summary>
/// Pure compatibility contract for the [...] expansion performed by native helper 0x00606910.
/// It captures source selection, Recordset/Fields lookup shape, optional Mid slicing, and exact
/// token replacement without opening a database or invoking a VB6 data control.
/// </summary>
public static class LegacyBracketRecordFieldSemantics
{
    public const int ExpansionHelperNativeAddress = 0x606910;

    public const int FrmDatabaseLoadedFlagNativeAddress = 0x62A04C;
    public const int TabellaLoadedFlagNativeAddress = 0x62A04A;
    public const string FrmDatabaseFormName = "frmDatabase";
    public const string TabellaFormName = "Tabella";
    public const string FrmDatabaseDataControlName = "Data1";
    public const string TabellaDataControlName = "datPrimaryRS";
    public const int DataControlAccessorVtableOffset = 0x2FC;

    public const string RecordsetMemberName = "Recordset";
    public const string FieldsMemberName = "Fields";
    public const string FieldNameMemberName = "Name";

    public const string OpeningDelimiter = "[";
    public const string ClosingDelimiter = "]";
    public const string ArgumentSeparator = ",";
    public const int MaximumParsedArguments = 3;

    public const int ParolaVtableOffset = 0x7F0;
    public const int ValRuntimeOrdinal = 581;
    public const int MidRuntimeOrdinal = 632;
    public const int IsNullRuntimeOrdinal = 560;

    /// <summary>
    /// Native selection first installs frmDatabase's data control when its form-loaded flag is
    /// set, then overwrites it with Tabella's data control when Tabella is loaded. Therefore
    /// Tabella has deterministic priority when both forms are active.
    /// </summary>
    public static LegacyBracketRecordSourceKind SelectRecordSource(
        bool frmDatabaseLoaded,
        bool tabellaLoaded) =>
        tabellaLoaded
            ? LegacyBracketRecordSourceKind.Tabella
            : frmDatabaseLoaded
                ? LegacyBracketRecordSourceKind.FrmDatabase
                : LegacyBracketRecordSourceKind.None;

    /// <summary>
    /// The native late-bound Fields lookup wraps the first parsed token back in square brackets
    /// before passing it to Recordset.Fields(...).
    /// </summary>
    public static string BuildRecordsetFieldLookupKey(string fieldToken)
    {
        ArgumentNullException.ThrowIfNull(fieldToken);
        return OpeningDelimiter + fieldToken + ClosingDelimiter;
    }

    /// <summary>Builds the exact source token later passed to Funzioni.Sostituisci.</summary>
    public static string BuildOriginalBracketToken(string rawBody)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        return OpeningDelimiter + rawBody + ClosingDelimiter;
    }

    /// <summary>
    /// Models the value branch after native Recordset.Fields lookup. Lookup errors and Null
    /// values become empty text. When the second numeric argument is greater than zero, native
    /// code applies VB Mid(value, start [, length]); a third argument greater than zero supplies
    /// length, otherwise Mid(value, start) runs to the end. Numeric values are parsed through
    /// VB Val() and converted to I4 by the native code before this stage.
    /// </summary>
    public static string ResolveFieldReplacement(
        bool fieldLookupSucceeded,
        string? fieldValue,
        int startOneBased = 0,
        int length = 0)
    {
        if (!fieldLookupSucceeded || fieldValue is null)
            return string.Empty;

        if (startOneBased <= 0)
            return fieldValue;

        var startIndex = startOneBased - 1;
        if (startIndex >= fieldValue.Length)
            return string.Empty;

        if (length <= 0)
            return fieldValue[startIndex..];

        return fieldValue.Substring(startIndex, Math.Min(length, fieldValue.Length - startIndex));
    }

    /// <summary>
    /// No active legacy record source makes 0x00606910 return its input unchanged. With an active
    /// source, the exact original [rawBody] occurrence is replaced everywhere by the already
    /// resolved field text, matching Funzioni.Sostituisci's binary replace-all behavior.
    /// </summary>
    public static string ReplaceResolvedTokenOccurrences(
        string source,
        string rawBody,
        LegacyBracketRecordSourceKind recordSource,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(replacement);

        if (recordSource == LegacyBracketRecordSourceKind.None)
            return source;

        return source.Replace(
            BuildOriginalBracketToken(rawBody),
            replacement,
            StringComparison.Ordinal);
    }
}
