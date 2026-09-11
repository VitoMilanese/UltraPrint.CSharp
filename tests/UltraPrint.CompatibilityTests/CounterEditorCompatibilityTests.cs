using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Scripting;

internal static class CounterEditorCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeHandlerMapAndDigitsChoices();
        TestFormLoadNormalizesAndCapitalizesNames();
        TestSelectionLoadsFirstMatchingLogicalRecord();
        TestChangeHandlersRequirePositiveLogicalSelection();
        TestDeletePreservesNonNameFieldsAndUsesZeroSentinel();
        TestDeleteThenCreatePreservesLegacyHiddenFields();
        TestNewUsesRawInputListTextAndFixedNameTruncation();
        TestSaveIsDeferredUntilExplicitOkEquivalent();
    }

    private static void TestNativeHandlerMapAndDigitsChoices()
    {
        AssertEqual(0x55B9F0, LegacyCounterEditorModel.DigitsChangedNativeAddress, "cbocifre handler address");
        AssertEqual(0x55BBA0, LegacyCounterEditorModel.ZeroPaddingChangedNativeAddress, "Check1 handler address");
        AssertEqual(0x55BD40, LegacyCounterEditorModel.DeleteNativeAddress, "delete handler address");
        AssertEqual(0x55BF30, LegacyCounterEditorModel.NewNativeAddress, "new handler address");
        AssertEqual(0x55C4C0, LegacyCounterEditorModel.FormLoadNativeAddress, "Form_Load address");
        AssertEqual(0x55CC30, LegacyCounterEditorModel.ListSelectionNativeAddress, "list-selection address");
        AssertEqual(0x55D100, LegacyCounterEditorModel.OkNativeAddress, "OK handler address");
        AssertEqual(0x55D1E0, LegacyCounterEditorModel.ValueChangedNativeAddress, "value-change address");
        AssertEqual(0x4A5710, LegacyCounterEditorModel.CapitalizzatoNativeAddress, "Capitalizzato address");
        AssertEqual("Nome ?", LegacyCounterEditorModel.NewCounterPrompt, "native InputBox prompt");

        WithModel((model, _, _) =>
        {
            AssertSequence(Enumerable.Range(1, 20), model.DigitsChoices, "Form_Load fills cbocifre with 1..20");
            AssertEqual(-1, model.NativeCurrentLogicalIndex, "Form_Load no-selection sentinel");
            AssertTrue(!model.DeleteEnabled, "Delete starts disabled");
        });
    }

    private static void TestFormLoadNormalizesAndCapitalizesNames()
    {
        WithModel((model, _, _) =>
        {
            var records = model.SnapshotRecords();
            AssertEqual("hELLo     ", records[0].FixedName,
                "Form_Load Trim + LSet normalizes fixed String*10 name");
            AssertSequence(new[] { "Hello", "World" }, model.ListItems,
                "Form_Load lists active names through Capitalizzato");
            AssertEqual("Hello", LegacyCounterEditorModel.Capitalizzato("hELLo", CultureInfo.InvariantCulture),
                "Capitalizzato is UCase(first) + LCase(rest)");
            AssertEqual(string.Empty, LegacyCounterEditorModel.Capitalizzato(string.Empty),
                "Capitalizzato preserves empty input");
        });
    }

    private static void TestSelectionLoadsFirstMatchingLogicalRecord()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            var records = BlankRecords();
            records[0] = LegacyCounterFileRecord.Create("DUP", digits: 3, zeroPadding: false, currentValue: 11);
            records[1] = LegacyCounterFileRecord.Create("dup", digits: 7, zeroPadding: true, currentValue: 22);
            store.Save(path, records);

            var model = LegacyCounterEditorModel.Load(path, store);
            AssertSequence(new[] { "Dup", "Dup" }, model.ListItems,
                "duplicate logical names can produce duplicate display items");
            AssertTrue(model.SelectListItem(1), "second duplicate display item still finds a record");
            AssertEqual(1, model.NativeCurrentLogicalIndex,
                "native selection scan binds duplicate display text to first matching logical slot");
            var selection = model.Selection!.Value;
            AssertEqual((short)3, selection.Digits, "first duplicate digits are loaded");
            AssertTrue(!selection.ZeroPadding, "first duplicate Check1 is loaded");
            AssertEqual(11, selection.CurrentValue, "first duplicate current value is loaded");
            AssertTrue(model.DeleteEnabled, "matching selection enables Delete");
        });
    }

    private static void TestChangeHandlersRequirePositiveLogicalSelection()
    {
        WithModel((model, _, _) =>
        {
            AssertTrue(!model.SetDigits(9), "digits change is suppressed with -1 Form_Load sentinel");
            AssertTrue(!model.SetZeroPadding(false), "Check1 change is suppressed with no selection");
            AssertTrue(!model.SetCurrentValue(999), "value change is suppressed with no selection");

            AssertTrue(model.SelectListItem(0), "first visible counter selects successfully");
            AssertTrue(model.SetDigits(9), "digits change writes selected record");
            AssertTrue(model.SetZeroPadding(false), "Check1 change writes selected record");
            AssertTrue(model.SetCurrentValue(999), "value change writes selected record");
            var selected = model.Selection!.Value;
            AssertEqual((short)9, selected.Digits, "selected digits changed in memory");
            AssertTrue(!selected.ZeroPadding, "selected zero padding changed in memory");
            AssertEqual(999, selected.CurrentValue, "selected current value changed in memory");
        });
    }

    private static void TestDeletePreservesNonNameFieldsAndUsesZeroSentinel()
    {
        WithModel((model, _, _) =>
        {
            AssertTrue(model.SelectListItem(0), "select counter before deletion");
            var before = model.SnapshotRecords()[0];
            AssertTrue(model.DeleteSelected(), "Delete removes selected logical counter");
            AssertEqual(0, model.NativeCurrentLogicalIndex, "Delete changes current-index sentinel to zero");
            AssertTrue(!model.DeleteEnabled, "Delete disables itself after removal");
            AssertEqual(-1, model.SelectedListIndex, "no ListBox item remains selected after delete model action");
            AssertSequence(new[] { "World" }, model.ListItems, "selected visible item is removed");

            var after = model.SnapshotRecords()[0];
            AssertEqual(new string(' ', 10), after.FixedName, "Delete LSets name to spaces only");
            AssertEqual(before.Digits, after.Digits, "Delete preserves digits WORD");
            AssertEqual(before.ZeroPaddingRaw, after.ZeroPaddingRaw, "Delete preserves Check1 WORD");
            AssertEqual(before.UnknownWord, after.UnknownWord, "Delete preserves unknown WORD");
            AssertEqual(before.CurrentValue, after.CurrentValue, "Delete preserves current Long");
        });
    }

    private static void TestDeleteThenCreatePreservesLegacyHiddenFields()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            var records = BlankRecords();
            records[0] = LegacyCounterFileRecord.Create(
                "OLD", digits: 13, zeroPadding: false, currentValue: 876, unknownWord: 0x4321);
            store.Save(path, records);

            var model = LegacyCounterEditorModel.Load(path, store);
            AssertTrue(model.SelectListItem(0), "select old counter");
            AssertTrue(model.DeleteSelected(), "delete old counter");
            var created = model.CreateFromInput("New");
            AssertTrue(created.Created, "new counter reuses first freed logical slot");
            AssertEqual(1, created.LogicalIndex!.Value, "freed logical slot is reused first");

            var record = model.SnapshotRecords()[0];
            AssertEqual("New       ", record.FixedName, "new name replaces deleted name");
            AssertEqual((short)6, record.Digits, "new resets digits to native default 6");
            AssertEqual((short)-1, record.ZeroPaddingRaw, "new resets Check1 to native True");
            AssertEqual((short)0x4321, record.UnknownWord,
                "delete/new quirk preserves unknown WORD from reused slot");
            AssertEqual(876, record.CurrentValue,
                "delete/new quirk preserves current value from reused slot");
        });
    }

    private static void TestNewUsesRawInputListTextAndFixedNameTruncation()
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            store.Save(path, BlankRecords());
            var model = LegacyCounterEditorModel.Load(path, store);

            var lower = model.CreateFromInput("lower");
            AssertTrue(lower.Created, "lower-case InputBox text creates counter");
            AssertEqual("lower", model.ListItems[^1], "cmdNuovo AddItem uses raw InputBox text");
            AssertTrue(!lower.SelectionMatchedARecord,
                "raw lower-case list text does not match Capitalizzato backing name on immediate selection");
            AssertEqual(-1, model.NativeCurrentLogicalIndex,
                "failed immediate display-text mapping leaves prior no-selection sentinel untouched");

            var longName = model.CreateFromInput("ABCDEFGHIJKL");
            AssertTrue(longName.Created, "native LSet accepts input longer than fixed name");
            AssertEqual("ABCDEFGHIJKL", model.ListItems[^1], "ListBox keeps untruncated raw input");
            AssertEqual("ABCDEFGHIJ", model.SnapshotRecords()[1].FixedName,
                "fixed String*10 truncates longer InputBox text");
            AssertTrue(!longName.SelectionMatchedARecord,
                "truncated backing name cannot match longer raw ListBox item");
        });
    }

    private static void TestSaveIsDeferredUntilExplicitOkEquivalent()
    {
        WithModel((model, store, path) =>
        {
            AssertTrue(model.SelectListItem(0), "select first counter");
            var originalBytes = File.ReadAllBytes(path);
            AssertTrue(model.SetCurrentValue(123456), "edit current value in memory");
            AssertTrue(originalBytes.SequenceEqual(File.ReadAllBytes(path)),
                "Change handlers do not save Contatori.dat immediately");

            model.Save();
            AssertTrue(!originalBytes.SequenceEqual(File.ReadAllBytes(path)),
                "OK-equivalent Save persists editor changes");
            AssertEqual(123456, store.Load(path)[0].CurrentValue,
                "saved current value reloads from legacy file");
        });
    }

    private static void WithModel(Action<LegacyCounterEditorModel, LegacyCounterFileStore, string> action)
    {
        WithTempDirectory(directory =>
        {
            var store = new LegacyCounterFileStore();
            var path = Path.Combine(directory, LegacyCounterFileStore.FileName);
            WriteRawLegacyFile(path,
                new RawCounterRecord("  hELLo   ", digits: 5, zeroPaddingRaw: -1, unknownWord: 0x1234, currentValue: 77),
                new RawCounterRecord("WORLD     ", digits: 4, zeroPaddingRaw: 0, unknownWord: 0, currentValue: 88));
            action(LegacyCounterEditorModel.Load(path, store), store, path);
        });
    }

    private static void WriteRawLegacyFile(string path, params RawCounterRecord[] records)
    {
        var bytes = new byte[LegacyCounterFileStore.NativeSavedFileLength];
        for (var recordIndex = 0; recordIndex < records.Length; recordIndex++)
        {
            var record = records[recordIndex];
            if (record.FixedName.Length != 10)
                throw new InvalidOperationException("Raw counter test name must be exactly 10 characters.");
            var offset = recordIndex * LegacyCounterFileStore.SerializedRecordSizeBytes;
            for (var charIndex = 0; charIndex < 10; charIndex++)
                bytes[offset + charIndex] = checked((byte)record.FixedName[charIndex]);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 0x0A, 2), record.Digits);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 0x0C, 2), record.ZeroPaddingRaw);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 0x0E, 2), record.UnknownWord);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 0x10, 4), record.CurrentValue);
        }
        File.WriteAllBytes(path, bytes);
    }

    private readonly record struct RawCounterRecord(
        string FixedName,
        short Digits,
        short ZeroPaddingRaw,
        short UnknownWord,
        int CurrentValue);

    private static LegacyCounterFileRecord[] BlankRecords() =>
        Enumerable.Repeat(LegacyCounterFileRecord.Blank, LegacyCounterFileStore.LogicalRecordCount).ToArray();

    private static void WithTempDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "UltraPrintCounterEditorTests", Guid.NewGuid().ToString("N"));
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
