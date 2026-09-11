using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class CounterFilePersistenceCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestBinaryLayoutContract();
        TestMissingAndZeroLengthFilesLoadAsBlankTable();
        TestNativeSaveCompactsAndWritesTrailingBlankRecord();
        TestKnownAndUnknownFieldsRoundTrip();
        TestPersistentResolverIncrementsAndSaves();
        TestTruncatedFileIsRejected();
    }

    private static void TestBinaryLayoutContract()
    {
        AssertEqual(0x5F5E70, LegacyCounterFileStore.LoadNativeAddress, "counter load native address");
        AssertEqual(0x5F60C0, LegacyCounterFileStore.SaveNativeAddress, "counter save native address");
        AssertEqual("Contatori.dat", LegacyCounterFileStore.FileName, "counter filename");
        AssertEqual(99, LegacyCounterFileStore.LogicalRecordCount, "logical record count");
        AssertEqual(100, LegacyCounterFileStore.SerializedRecordCount, "native saved record count");
        AssertEqual(32, LegacyCounterFileStore.NativeMemoryRecordStrideBytes, "native memory stride");
        AssertEqual(20, LegacyCounterFileStore.SerializedRecordSizeBytes, "serialized UDT size");
        AssertEqual(10, LegacyCounterFileStore.FixedNameLengthBytes, "fixed String*10 byte count");
        AssertEqual(0x0A, LegacyCounterFileStore.DigitsFileOffset, "serialized digits offset");
        AssertEqual(0x0C, LegacyCounterFileStore.ZeroPaddingBooleanFileOffset, "serialized Boolean offset");
        AssertEqual(0x0E, LegacyCounterFileStore.UnknownWordFileOffset, "serialized unknown WORD offset");
        AssertEqual(0x10, LegacyCounterFileStore.CurrentValueFileOffset, "serialized Long offset");
        AssertEqual(1980, LegacyCounterFileStore.MinimumReadableFileLength, "99-record readable size");
        AssertEqual(2000, LegacyCounterFileStore.NativeSavedFileLength, "canonical native save size");
    }

    private static void TestMissingAndZeroLengthFilesLoadAsBlankTable()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);

            var missing = store.Load(path);
            AssertEqual(99, missing.Count, "missing counter file produces blank logical table");
            AssertTrue(missing.All(record => !record.IsNativeActive), "missing-file table is entirely inactive");
            AssertTrue(missing.All(record => record.FixedName == new string('\0', 10)),
                "blank fixed strings preserve native NUL initialization");

            File.WriteAllBytes(path, Array.Empty<byte>());
            var empty = store.Load(path);
            AssertEqual(99, empty.Count, "zero-length legacy fixture produces blank logical table");
            AssertTrue(empty.All(record => record.CurrentValue == 0), "zero-length table values are zero");
        });
    }

    private static void TestNativeSaveCompactsAndWritesTrailingBlankRecord()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            var logical = Enumerable.Repeat(LegacyCounterFileRecord.Blank, 99).ToArray();
            logical[10] = LegacyCounterFileRecord.Create("SECOND", currentValue: 2);
            logical[80] = LegacyCounterFileRecord.Create("THIRD", currentValue: 3);

            store.Save(path, logical);

            var bytes = File.ReadAllBytes(path);
            AssertEqual(2000, bytes.Length, "native-shaped save always writes 100 records");
            AssertEqual("SECOND", ReadFixedName(bytes, 0), "first active logical record is compacted to slot zero");
            AssertEqual("THIRD", ReadFixedName(bytes, 1), "active record order is preserved during compaction");
            AssertTrue(bytes.AsSpan(2 * 20).ToArray().All(value => value == 0),
                "every record after active compacted records is canonical zero padding");

            var reloaded = store.Load(path);
            AssertEqual("SECOND", reloaded[0].Name, "load reads first compacted record");
            AssertEqual("THIRD", reloaded[1].Name, "load reads second compacted record");
            AssertTrue(!reloaded[98].IsNativeActive, "load ignores canonical 100th trailing blank record");
        });
    }

    private static void TestKnownAndUnknownFieldsRoundTrip()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            var logical = Enumerable.Repeat(LegacyCounterFileRecord.Blank, 99).ToArray();
            var fixedName = "RAW" + (char)0x80 + "      ";
            logical[0] = new LegacyCounterFileRecord(
                fixedName,
                digits: -123,
                zeroPaddingRaw: unchecked((short)0x8123),
                unknownWord: unchecked((short)0xA55A),
                currentValue: unchecked((int)0x89ABCDEF));

            store.Save(path, logical);
            var bytes = File.ReadAllBytes(path);

            AssertEqual((byte)0x80, bytes[3], "high legacy name byte is preserved without code-page guessing");
            AssertEqual((short)-123,
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0A, 2)), "digits raw WORD bytes");
            AssertEqual(unchecked((short)0x8123),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0C, 2)), "Boolean raw WORD bytes");
            AssertEqual(unchecked((short)0xA55A),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0E, 2)), "unknown WORD bytes");
            AssertEqual(unchecked((int)0x89ABCDEF),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10, 4)), "Long raw bytes");

            var restored = store.Load(path)[0];
            AssertEqual(fixedName, restored.FixedName, "fixed name round-trips byte-for-byte");
            AssertEqual(unchecked((short)0xA55A), restored.UnknownWord, "unclaimed WORD survives round-trip");
            AssertEqual(unchecked((int)0x89ABCDEF), restored.CurrentValue, "counter Long survives round-trip");
        });
    }

    private static void TestPersistentResolverIncrementsAndSaves()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var resolver = new LegacyPersistentCounterResolver(store);
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            var logical = Enumerable.Repeat(LegacyCounterFileRecord.Blank, 99).ToArray();
            logical[17] = LegacyCounterFileRecord.Create(
                "CARD",
                digits: 6,
                zeroPadding: true,
                currentValue: 41,
                unknownWord: 0x3456);
            store.Save(path, logical);

            var result = resolver.ResolveSmartDriverCounter(path, "CARD");
            AssertTrue(result.Matched, "persistent resolver finds fixed legacy counter name");
            AssertEqual(42, result.UpdatedValue!.Value, "persistent resolver increments before save");
            AssertEqual("000042", result.Replacement, "persistent resolver applies legacy Zeri formatting");
            AssertTrue(result.PersistsUpdatedCounterTable, "persistent resolver reports immediate persistence");

            var reloaded = store.Load(path);
            AssertEqual("CARD", reloaded[0].Name, "native save compacts resolved counter to first active slot");
            AssertEqual(42, reloaded[0].CurrentValue, "incremented counter is persisted");
            AssertEqual((short)0x3456, reloaded[0].UnknownWord, "persistent update preserves unclaimed WORD");

            var beforeNoMatch = File.ReadAllBytes(path);
            var noMatch = resolver.ResolveSmartDriverCounter(path, "MISSING");
            var afterNoMatch = File.ReadAllBytes(path);
            AssertTrue(!noMatch.Matched, "missing persistent counter retains native no-match result");
            AssertTrue(beforeNoMatch.SequenceEqual(afterNoMatch), "no-match does not rewrite Contatori.dat");
        });
    }

    private static void TestTruncatedFileIsRejected()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            File.WriteAllBytes(path, new byte[LegacyCounterFileStore.MinimumReadableFileLength - 1]);

            try
            {
                _ = store.Load(path);
                throw new InvalidOperationException("FAILED: truncated counter file should be rejected.");
            }
            catch (InvalidDataException)
            {
                // expected safety boundary for a damaged legacy file
            }
        });
    }

    private static string ReadFixedName(byte[] fileBytes, int recordIndex)
    {
        var offset = recordIndex * LegacyCounterFileStore.SerializedRecordSizeBytes;
        var chars = fileBytes.AsSpan(offset, 10).ToArray().Select(value => (char)value).ToArray();
        return new string(chars).TrimEnd('\0', ' ');
    }

    private static void WithTempDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "UltraPrintCounterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
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
