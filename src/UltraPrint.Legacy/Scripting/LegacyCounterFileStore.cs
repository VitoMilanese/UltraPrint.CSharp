using System.Buffers.Binary;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// One logical frmContatori record. FixedName is the exact 10-byte legacy fixed string
/// represented losslessly as U+0000..U+00FF characters so unknown bytes can round-trip.
/// </summary>
public sealed record LegacyCounterFileRecord
{
    public LegacyCounterFileRecord(
        string fixedName,
        short digits,
        short zeroPaddingRaw,
        short unknownWord,
        int currentValue)
    {
        ArgumentNullException.ThrowIfNull(fixedName);
        if (fixedName.Length != LegacyCounterFileStore.FixedNameLengthBytes)
            throw new ArgumentException(
                $"Legacy counter names must occupy exactly {LegacyCounterFileStore.FixedNameLengthBytes} fixed characters.",
                nameof(fixedName));
        if (fixedName.Any(ch => ch > byte.MaxValue))
            throw new ArgumentException(
                "Fixed legacy counter names must use only byte-preservable U+0000..U+00FF characters.",
                nameof(fixedName));

        FixedName = fixedName;
        Digits = digits;
        ZeroPaddingRaw = zeroPaddingRaw;
        UnknownWord = unknownWord;
        CurrentValue = currentValue;
    }

    public string FixedName { get; init; }
    public short Digits { get; init; }
    public short ZeroPaddingRaw { get; init; }
    public short UnknownWord { get; init; }
    public int CurrentValue { get; init; }

    /// <summary>Display-oriented name; the native resolver itself uses space-only Trim().</summary>
    public string Name => FixedName.TrimEnd(' ', '\0');

    /// <summary>
    /// Native SaveContatori writes only records whose Left(Name, 1) compares greater than
    /// one ASCII space. This exact predicate also discards empty/NUL/space-leading records.
    /// </summary>
    public bool IsNativeActive => FixedName[0] > ' ';

    public bool ZeroPadding => ZeroPaddingRaw != 0;

    /// <summary>Exact name shape used by native 0x005F5A90 before token comparison.</summary>
    public string NativeResolverName => FixedName.Trim(' ');

    public LegacyCounterFileRecord WithCurrentValue(int value) => this with { CurrentValue = value };

    public static LegacyCounterFileRecord Create(
        string name,
        short digits = LegacyCounterTokenSemantics.DefaultDigits,
        bool zeroPadding = LegacyCounterTokenSemantics.DefaultZeroPadding,
        int currentValue = 0,
        short unknownWord = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > FixedNameLength)
            throw new ArgumentOutOfRangeException(nameof(name),
                $"A legacy counter name cannot exceed {FixedNameLength} characters.");
        if (name.Any(ch => ch > byte.MaxValue))
            throw new ArgumentException(
                "New legacy counter names currently accept only byte-preservable U+0000..U+00FF characters; " +
                "the original installation code page for wider Unicode input is not yet proven.",
                nameof(name));

        return new(
            name.PadRight(FixedNameLength, ' '),
            digits,
            zeroPadding ? (short)-1 : (short)0,
            unknownWord,
            currentValue);
    }

    public static LegacyCounterFileRecord Blank { get; } =
        new(new string('\0', FixedNameLength), 0, 0, 0, 0);

    private const int FixedNameLength = LegacyCounterFileStore.FixedNameLengthBytes;
}

/// <summary>
/// Byte-compatible Contatori.dat reader/writer recovered from native helpers 0x005F5E70
/// and 0x005F60C0. VB6 keeps a 32-byte Unicode in-memory UDT, but Put/Get serialize the
/// fixed String * 10 as 10 bytes and numeric fields without alignment padding: 20 bytes.
/// </summary>
public sealed class LegacyCounterFileStore
{
    public const int LoadNativeAddress = 0x5F5E70;
    public const int SaveNativeAddress = 0x5F60C0;
    public const string FileName = "Contatori.dat";

    public const int LogicalRecordCount = 99;
    public const int SerializedRecordCount = 100;
    public const int NativeMemoryRecordStrideBytes = 32;
    public const int SerializedRecordSizeBytes = 20;
    public const int FixedNameLengthBytes = 10;

    public const int NameFileOffset = 0x00;
    public const int DigitsFileOffset = 0x0A;
    public const int ZeroPaddingBooleanFileOffset = 0x0C;
    public const int UnknownWordFileOffset = 0x0E;
    public const int CurrentValueFileOffset = 0x10;

    public const int MinimumReadableFileLength = LogicalRecordCount * SerializedRecordSizeBytes;
    public const int NativeSavedFileLength = SerializedRecordCount * SerializedRecordSizeBytes;

    public IReadOnlyList<LegacyCounterFileRecord> Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            return CreateBlankLogicalTable();

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return CreateBlankLogicalTable();
        if (bytes.Length < MinimumReadableFileLength)
            throw new InvalidDataException(
                $"Legacy counter file is truncated: {bytes.Length} bytes; " +
                $"at least {MinimumReadableFileLength} bytes are required for 99 records.");

        var records = new LegacyCounterFileRecord[LogicalRecordCount];
        for (var index = 0; index < LogicalRecordCount; index++)
        {
            var offset = index * SerializedRecordSizeBytes;
            records[index] = DecodeRecord(bytes.AsSpan(offset, SerializedRecordSizeBytes));
        }

        // Native load reads exactly indices 1..99. The canonical 100th record written by
        // native save is a trailing blank/sentinel and is deliberately ignored here.
        return records;
    }

    public void Save(string path, IReadOnlyList<LegacyCounterFileRecord> logicalRecords)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(logicalRecords);
        if (logicalRecords.Count != LogicalRecordCount)
            throw new ArgumentException(
                $"Legacy counter persistence requires exactly {LogicalRecordCount} logical records.",
                nameof(logicalRecords));

        var activeRecords = logicalRecords.Where(record => record.IsNativeActive).ToArray();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var record in activeRecords)
            WriteRecord(stream, record);

        // Native second pass is For i = activeCount To 99 inclusive. That writes
        // 100-activeCount blank UDTs, making every native save exactly 100 records.
        for (var index = activeRecords.Length; index <= 99; index++)
            WriteRecord(stream, LegacyCounterFileRecord.Blank);

        stream.Flush();
        if (stream.Length != NativeSavedFileLength)
            throw new InvalidDataException(
                $"Internal counter serialization error: expected {NativeSavedFileLength} bytes, wrote {stream.Length}.");
    }

    public static string GetDefaultPath(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationDirectory);
        return Path.Combine(applicationDirectory, FileName);
    }

    private static LegacyCounterFileRecord[] CreateBlankLogicalTable() =>
        Enumerable.Repeat(LegacyCounterFileRecord.Blank, LogicalRecordCount).ToArray();

    private static LegacyCounterFileRecord DecodeRecord(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SerializedRecordSizeBytes)
            throw new ArgumentException("A serialized counter record must contain exactly 20 bytes.", nameof(bytes));

        Span<char> fixedName = stackalloc char[FixedNameLengthBytes];
        for (var index = 0; index < FixedNameLengthBytes; index++)
            fixedName[index] = (char)bytes[index];

        return new(
            new string(fixedName),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(DigitsFileOffset, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(ZeroPaddingBooleanFileOffset, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(UnknownWordFileOffset, 2)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(CurrentValueFileOffset, 4)));
    }

    private static void WriteRecord(Stream stream, LegacyCounterFileRecord record)
    {
        Span<byte> bytes = stackalloc byte[SerializedRecordSizeBytes];
        bytes.Clear();

        for (var index = 0; index < FixedNameLengthBytes; index++)
            bytes[index] = checked((byte)record.FixedName[index]);

        BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(DigitsFileOffset, 2), record.Digits);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(ZeroPaddingBooleanFileOffset, 2), record.ZeroPaddingRaw);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(UnknownWordFileOffset, 2), record.UnknownWord);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(CurrentValueFileOffset, 4), record.CurrentValue);
        stream.Write(bytes);
    }
}
