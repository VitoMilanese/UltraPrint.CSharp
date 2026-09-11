using System.Globalization;

namespace UltraPrint.Legacy.Scripting;

public readonly record struct LegacyCounterEditorSelection(
    int LogicalIndex,
    string DisplayName,
    short Digits,
    bool ZeroPadding,
    int CurrentValue);

public readonly record struct LegacyCounterCreateResult(
    bool Created,
    int? LogicalIndex,
    int? ListIndex,
    bool SelectionMatchedARecord);

/// <summary>
/// Non-visual model of the native frmContatori editor. It preserves the recovered list,
/// selection, create/delete and delayed-save behavior while keeping WinForms concerns out
/// of the legacy binary/persistence layer.
/// </summary>
public sealed class LegacyCounterEditorModel
{
    public const int DigitsChangedNativeAddress = 0x55B9F0;
    public const int ZeroPaddingChangedNativeAddress = 0x55BBA0;
    public const int DeleteNativeAddress = 0x55BD40;
    public const int NewNativeAddress = 0x55BF30;
    public const int FormLoadNativeAddress = 0x55C4C0;
    public const int ListSelectionNativeAddress = 0x55CC30;
    public const int OkNativeAddress = 0x55D100;
    public const int ValueChangedNativeAddress = 0x55D1E0;
    public const int CapitalizzatoNativeAddress = 0x4A5710;

    public const int InitialNoSelectionNativeValue = -1;
    public const int DeletedNoSelectionNativeValue = 0;
    public const int FirstLogicalIndex = 1;
    public const int LastLogicalIndex = 99;
    public const int FirstDigitsChoice = 1;
    public const int LastDigitsChoice = 20;
    public const string NewCounterPrompt = "Nome ?";

    private readonly LegacyCounterFileStore _store;
    private readonly string _path;
    private readonly LegacyCounterFileRecord[] _records;
    private readonly List<string> _listItems = [];
    private int _selectedListIndex = -1;

    private LegacyCounterEditorModel(
        LegacyCounterFileStore store,
        string path,
        LegacyCounterFileRecord[] records)
    {
        _store = store;
        _path = path;
        _records = records;
        RebuildListLikeFormLoad();
    }

    public int NativeCurrentLogicalIndex { get; private set; } = InitialNoSelectionNativeValue;
    public bool DeleteEnabled => NativeCurrentLogicalIndex > 0;
    public int SelectedListIndex => _selectedListIndex;
    public IReadOnlyList<string> ListItems => _listItems;
    public IReadOnlyList<int> DigitsChoices { get; } =
        Enumerable.Range(FirstDigitsChoice, LastDigitsChoice - FirstDigitsChoice + 1).ToArray();

    public LegacyCounterEditorSelection? Selection =>
        NativeCurrentLogicalIndex > 0
            ? BuildSelection(NativeCurrentLogicalIndex)
            : null;

    public static LegacyCounterEditorModel Load(
        string path,
        LegacyCounterFileStore? store = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var actualStore = store ?? new LegacyCounterFileStore();
        var records = actualStore.Load(path).ToArray();
        return new(actualStore, path, records);
    }

    /// <summary>
    /// Native Form_Load trims every fixed name and LSets it back into String * 10 before
    /// applying Capitalizzato and the Left(display,1) &gt; " " active-list predicate.
    /// </summary>
    public void RebuildListLikeFormLoad()
    {
        _listItems.Clear();
        for (var logicalIndex = FirstLogicalIndex; logicalIndex <= LastLogicalIndex; logicalIndex++)
        {
            var arrayIndex = logicalIndex - 1;
            var normalized = NormalizeFixedName(_records[arrayIndex]);
            _records[arrayIndex] = normalized;
            var display = Capitalizzato(normalized.NativeResolverName);
            if (HasNativeActiveDisplayPrefix(display))
                _listItems.Add(display);
        }

        NativeCurrentLogicalIndex = InitialNoSelectionNativeValue;
        _selectedListIndex = -1;
    }

    /// <summary>
    /// Models ListaContatori click/selection. The list text is transformed independently
    /// from the backing logical slot: native code scans slots 1..99 and selects the first
    /// Capitalizzato(name) exact match. Duplicate display names therefore bind to the first slot.
    /// </summary>
    public bool SelectListItem(int listIndex)
    {
        if ((uint)listIndex >= (uint)_listItems.Count)
            throw new ArgumentOutOfRangeException(nameof(listIndex));

        _selectedListIndex = listIndex;
        var selectedText = _listItems[listIndex];
        for (var logicalIndex = FirstLogicalIndex; logicalIndex <= LastLogicalIndex; logicalIndex++)
        {
            var arrayIndex = logicalIndex - 1;
            var normalized = NormalizeFixedName(_records[arrayIndex]);
            _records[arrayIndex] = normalized;
            var display = Capitalizzato(normalized.NativeResolverName);
            if (!string.Equals(display, selectedText, StringComparison.Ordinal))
                continue;

            // Native uses -1 temporarily while programmatically assigning cbocifre,
            // txtValore and Check1 so their Change handlers cannot write back mid-selection.
            NativeCurrentLogicalIndex = InitialNoSelectionNativeValue;
            NativeCurrentLogicalIndex = logicalIndex;
            return true;
        }

        // If the displayed ListBox text cannot be mapped, native code leaves the previous
        // logical-index Variant untouched. This can happen immediately after cmdNuovo when
        // raw InputBox casing differs from Capitalizzato(truncated fixed name).
        return false;
    }

    /// <summary>
    /// Models cmdNuovo: non-empty InputBox result, first native-free logical slot, AddItem
    /// with the raw entered text, LSet into String*10, defaults 6/True, then select last item.
    /// CurrentValue and the unknown WORD are deliberately preserved in a reused deleted slot.
    /// </summary>
    public LegacyCounterCreateResult CreateFromInput(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0)
            return new(false, null, null, false);
        EnsureBytePreservable(input);

        for (var logicalIndex = FirstLogicalIndex; logicalIndex <= LastLogicalIndex; logicalIndex++)
        {
            var arrayIndex = logicalIndex - 1;
            var normalized = NormalizeFixedName(_records[arrayIndex]);
            _records[arrayIndex] = normalized;
            var display = Capitalizzato(normalized.NativeResolverName);
            if (!HasNativeFreeDisplayPrefix(display))
                continue;

            var existing = _records[arrayIndex];
            _records[arrayIndex] = new LegacyCounterFileRecord(
                LSetFixedName(input),
                digits: LegacyCounterTokenSemantics.DefaultDigits,
                zeroPaddingRaw: -1,
                unknownWord: existing.UnknownWord,
                currentValue: existing.CurrentValue);

            _listItems.Add(input);
            var newListIndex = _listItems.Count - 1;
            var matched = SelectListItem(newListIndex);
            return new(true, logicalIndex, newListIndex, matched);
        }

        return new(false, null, null, false);
    }

    /// <summary>
    /// Native CmdElimina clears only the fixed name with LSet " ", removes the selected
    /// ListBox item, sets current logical index to 0, and disables Delete. Other fields survive.
    /// </summary>
    public bool DeleteSelected()
    {
        if (NativeCurrentLogicalIndex <= 0)
            return false;

        var arrayIndex = NativeCurrentLogicalIndex - 1;
        var existing = _records[arrayIndex];
        _records[arrayIndex] = new LegacyCounterFileRecord(
            new string(' ', LegacyCounterFileStore.FixedNameLengthBytes),
            existing.Digits,
            existing.ZeroPaddingRaw,
            existing.UnknownWord,
            existing.CurrentValue);

        if ((uint)_selectedListIndex < (uint)_listItems.Count)
            _listItems.RemoveAt(_selectedListIndex);

        _selectedListIndex = -1;
        NativeCurrentLogicalIndex = DeletedNoSelectionNativeValue;
        return true;
    }

    public bool SetDigits(short digits)
    {
        if (NativeCurrentLogicalIndex <= 0)
            return false;
        var index = NativeCurrentLogicalIndex - 1;
        _records[index] = _records[index] with { Digits = digits };
        return true;
    }

    public bool SetZeroPadding(bool enabled)
    {
        if (NativeCurrentLogicalIndex <= 0)
            return false;
        var index = NativeCurrentLogicalIndex - 1;
        _records[index] = _records[index] with { ZeroPaddingRaw = enabled ? (short)-1 : (short)0 };
        return true;
    }

    public bool SetCurrentValue(int value)
    {
        if (NativeCurrentLogicalIndex <= 0)
            return false;
        var index = NativeCurrentLogicalIndex - 1;
        _records[index] = _records[index] with { CurrentValue = value };
        return true;
    }

    /// <summary>Native OK calls SaveContatori and then unloads the form.</summary>
    public void Save() => _store.Save(_path, _records);

    public IReadOnlyList<LegacyCounterFileRecord> SnapshotRecords() => _records.ToArray();

    /// <summary>
    /// Exact high-level body of Funzioni.Capitalizzato (0x004A5710):
    /// UCase(Left(value, 1)) + LCase(Mid(value, 2)). VB uses the process locale.
    /// </summary>
    public static string Capitalizzato(string value, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return string.Empty;
        culture ??= CultureInfo.CurrentCulture;
        return value[..1].ToUpper(culture) + value[1..].ToLower(culture);
    }

    private LegacyCounterEditorSelection BuildSelection(int logicalIndex)
    {
        var record = _records[logicalIndex - 1];
        return new(
            logicalIndex,
            Capitalizzato(record.NativeResolverName),
            record.Digits,
            record.ZeroPadding,
            record.CurrentValue);
    }

    private static LegacyCounterFileRecord NormalizeFixedName(LegacyCounterFileRecord record)
    {
        var trimmed = record.FixedName.Trim(' ');
        return record with { FixedName = LSetFixedName(trimmed) };
    }

    private static bool HasNativeActiveDisplayPrefix(string display) =>
        display.Length > 0 && display[0] > ' ';

    private static bool HasNativeFreeDisplayPrefix(string display) =>
        display.Length == 0 || display[0] < ' ';

    private static string LSetFixedName(string value)
    {
        EnsureBytePreservable(value);
        var length = LegacyCounterFileStore.FixedNameLengthBytes;
        if (value.Length >= length)
            return value[..length];
        return value.PadRight(length, ' ');
    }

    private static void EnsureBytePreservable(string value)
    {
        if (value.Any(ch => ch > byte.MaxValue))
            throw new ArgumentException(
                "Legacy frmContatori input currently accepts only U+0000..U+00FF so the original ANSI/DBCS code page is not guessed.",
                nameof(value));
    }
}
