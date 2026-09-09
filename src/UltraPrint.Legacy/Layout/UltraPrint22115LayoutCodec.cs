using System.Buffers.Binary;
using System.Text;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Layout;

/// <summary>
/// Partially decoded UltraPrint 2.2.115 .ly reader/writer.
///
/// The format was recovered from TPMFAO19.ly and correlated with Campo.ini/native VB6 code.
/// Unknown bytes are deliberately preserved. Existing records keep their original 264-byte raw
/// template; moving/duplicating records therefore keeps still-unknown flags instead of rebuilding
/// them from guesses.
/// </summary>
public sealed class UltraPrint22115LayoutCodec : ILegacyLayoutCodec
{
    public const int KnownFieldTableOffset = 2029;
    public const int FieldRecordSize = 264;
    public const int MaxFieldSlots = 64;
    public const double TwipsPerMillimeter = 1440.0 / 25.4;

    private static readonly Encoding LegacyEncoding = Encoding.Latin1;

    public CardLayout Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = File.ReadAllBytes(path);
        if (data.Length < 64)
            throw new InvalidDataException("The file is too small to be an UltraPrint .ly layout.");

        var height = ReadSingle(data, 0);
        var width = ReadSingle(data, 4);
        var dpi = ReadInt16(data, 8);
        if (!IsPlausibleCardSize(width, height) || dpi is < 30 or > 5000)
            throw new InvalidDataException("The .ly header does not match the recovered UltraPrint 2.2.115 layout header.");

        var fieldTableOffset = FindFieldTableOffset(data);
        if (fieldTableOffset < 0)
            throw new InvalidDataException("Could not locate the recovered 264-byte UltraPrint field table.");

        var layout = new CardLayout
        {
            SourcePath = Path.GetFullPath(path),
            Name = Path.GetFileNameWithoutExtension(path),
            WidthMm = width,
            HeightMm = height,
            Dpi = dpi
        };

        var currentSide = LayoutSide.Unknown;
        var slots = Math.Min(MaxFieldSlots, (data.Length - fieldTableOffset) / FieldRecordSize);
        for (var index = 0; index < slots; index++)
        {
            var offset = fieldTableOffset + index * FieldRecordSize;
            var span = data.AsSpan(offset, FieldRecordSize);
            var field = DecodeField(span, index);
            if (field is null) continue;

            var sideHint = InferSideHint(field);
            if (sideHint != LayoutSide.Unknown)
                currentSide = sideHint;
            field.Side = currentSide;

            if (field.Kind == LayoutFieldKind.Image && IsBackgroundLike(layout, field))
            {
                if (field.Side == LayoutSide.Front) layout.FrontBackground = field.Image.File;
                if (field.Side == LayoutSide.Back) layout.BackBackground = field.Image.File;
            }

            layout.Fields.Add(field);
        }

        return layout;
    }

    public void Save(CardLayout layout, string path)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = layout.SourcePath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new InvalidOperationException("Safe legacy save requires the original .ly file so unknown bytes can be preserved.");

        var data = File.ReadAllBytes(source);
        var fieldTableOffset = FindFieldTableOffset(data);
        if (fieldTableOffset < 0)
            throw new InvalidDataException("Could not locate the recovered UltraPrint field table in the source file.");

        ValidateFieldIndices(layout);

        WriteSingle(data, 0, checked((float)layout.HeightMm));
        WriteSingle(data, 4, checked((float)layout.WidthMm));
        WriteInt16(data, 8, checked((short)layout.Dpi));

        var slots = Math.Min(MaxFieldSlots, (data.Length - fieldTableOffset) / FieldRecordSize);

        // Clear only records that were active in the source file. They are rewritten below from
        // each field's preserved raw template. This is what makes delete/reorder/insert possible
        // without touching the still-unknown header/footer ranges.
        for (var index = 0; index < slots; index++)
        {
            var record = data.AsSpan(fieldTableOffset + index * FieldRecordSize, FieldRecordSize);
            if (!IsEmptyRecord(record)) record.Clear();
        }

        foreach (var field in layout.Fields.OrderBy(x => x.Index))
        {
            if (field.Index is < 0 or >= MaxFieldSlots || field.Index >= slots) continue;
            var target = data.AsSpan(fieldTableOffset + field.Index * FieldRecordSize, FieldRecordSize);
            target.Clear();

            var template = field.LegacyRecordTemplate;
            if (template.Length == FieldRecordSize)
                template.AsSpan().CopyTo(target);
            else
                CreateDefaultRecord(field.Kind).AsSpan().CopyTo(target);

            PatchField(target, field);
            field.LegacyRecordTemplate = target.ToArray();
        }

        var fullTarget = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
        File.WriteAllBytes(fullTarget, data);
        layout.SourcePath = fullTarget;
    }

    /// <summary>
    /// Creates a new editable record by cloning a same-kind legacy record where possible. This is
    /// safer than manufacturing unknown bytes. Front records are inserted before the first Back
    /// record so the recovered UltraPrint record-order side semantics remain intact.
    /// </summary>
    public LayoutField CreateField(CardLayout layout, LayoutFieldKind kind, LayoutSide side, int legacyTypeCode)
    {
        ArgumentNullException.ThrowIfNull(layout);
        EnsureCapacity(layout);

        var template = layout.Fields.FirstOrDefault(x => x.Kind == kind && x.LegacyTypeCode == legacyTypeCode)
                       ?? layout.Fields.FirstOrDefault(x => x.Kind == kind);
        var insertionIndex = AllocateInsertionIndex(layout, side, afterIndex: null);
        ShiftIndices(layout, insertionIndex);

        var field = template is null
            ? CreateBlankField(kind, side, legacyTypeCode)
            : CloneField(template);

        field.Index = insertionIndex;
        field.Side = side;
        field.Kind = kind;
        field.LegacyTypeCode = legacyTypeCode;
        field.Xmm = Math.Clamp(field.Xmm + 1, 0, Math.Max(0, layout.WidthMm - Math.Max(0.5, field.WidthMm)));
        field.Ymm = Math.Clamp(field.Ymm + 1, 0, Math.Max(0, layout.HeightMm - Math.Max(0.5, field.HeightMm)));
        field.Level = NextLevel(layout, side);

        var sequence = layout.Fields.Count(x => x.Kind == kind) + 1;
        switch (kind)
        {
            case LayoutFieldKind.Text:
                field.Name = UniqueName(layout, $"Text_{sequence}");
                field.Text.Content = "Text";
                field.Text.DatabaseField = string.Empty;
                field.LegacyPayload = field.Text.Content;
                break;
            case LayoutFieldKind.Image:
                field.Name = UniqueName(layout, legacyTypeCode == 6 ? $"Foto_{sequence}" : $"Immagine_{sequence}");
                field.Image.File = string.Empty;
                field.Image.DatabaseField = string.Empty;
                field.LegacyPayload = string.Empty;
                break;
            default:
                field.Name = UniqueName(layout, $"Campo_{sequence}");
                field.LegacyPayload = string.Empty;
                break;
        }

        layout.Fields.Add(field);
        return field;
    }

    public LayoutField DuplicateField(CardLayout layout, LayoutField source)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(source);
        EnsureCapacity(layout);

        var insertionIndex = AllocateInsertionIndex(layout, source.Side, source.Index);
        ShiftIndices(layout, insertionIndex);

        var clone = CloneField(source);
        clone.Index = insertionIndex;
        clone.Name = UniqueName(layout, string.IsNullOrWhiteSpace(source.Name) ? "Campo_copy" : source.Name + "_copy");
        clone.Xmm = Math.Min(source.Xmm + 1, Math.Max(0, layout.WidthMm - Math.Max(0.5, source.WidthMm)));
        clone.Ymm = Math.Min(source.Ymm + 1, Math.Max(0, layout.HeightMm - Math.Max(0.5, source.HeightMm)));
        clone.Level = source.Level + 1;
        layout.Fields.Add(clone);
        return clone;
    }

    public bool DeleteField(CardLayout layout, LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(field);
        if (!layout.Fields.Remove(field)) return false;

        foreach (var other in layout.Fields.Where(x => x.Index > field.Index))
            other.Index--;
        return true;
    }

    private static LayoutField? DecodeField(ReadOnlySpan<byte> record, int index)
    {
        var name = ReadFixedString(record.Slice(0, 20));
        var type = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(20, 2));
        var payload = ReadFixedString(record.Slice(38, 124));

        if (type == 0 && name.Length == 0 && payload.Length == 0)
            return null;

        var field = new LayoutField
        {
            Index = index,
            Name = name,
            LegacyTypeCode = type,
            Kind = LegacyFieldTypeCatalog.KindForType(type),
            Ymm = ReadSingle(record, 22) / TwipsPerMillimeter,
            Xmm = ReadSingle(record, 26) / TwipsPerMillimeter,
            HeightMm = ReadSingle(record, 30) / TwipsPerMillimeter,
            WidthMm = ReadSingle(record, 34) / TwipsPerMillimeter,
            Level = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(262, 2)),
            LegacyPayload = payload,
            LegacyRecordTemplate = record.ToArray()
        };

        field.Text.FontName = ReadFixedString(record.Slice(188, 32));
        if (field.Text.FontName.Length == 0) field.Text.FontName = "Arial";
        field.Text.FontSize = ReadSingle(record, 220);
        if (field.Text.FontSize is <= 0 or > 200) field.Text.FontSize = 10;
        field.Text.Fixed = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(224, 2)) != 0;

        // Confirmed by the fixture and Campo.ini Aspetto values.
        field.Appearance.BackColorOle = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(180, 4));
        field.Appearance.ForeColorOle = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(184, 4));

        switch (field.Kind)
        {
            case LayoutFieldKind.Text:
                field.Text.Content = payload;
                if (field.Name.StartsWith('%') && field.Name.EndsWith('%'))
                    field.Text.DatabaseField = field.Name;
                break;

            case LayoutFieldKind.Image:
                field.Image.File = payload;
                if (field.Name.StartsWith('%') && field.Name.EndsWith('%'))
                    field.Image.DatabaseField = field.Name;
                break;
        }

        return field;
    }

    private static void PatchField(Span<byte> record, LayoutField field)
    {
        WriteFixedString(record.Slice(0, 20), field.Name);
        BinaryPrimitives.WriteInt16LittleEndian(record.Slice(20, 2), checked((short)field.LegacyTypeCode));
        WriteSingle(record, 22, checked((float)(field.Ymm * TwipsPerMillimeter)));
        WriteSingle(record, 26, checked((float)(field.Xmm * TwipsPerMillimeter)));
        WriteSingle(record, 30, checked((float)(field.HeightMm * TwipsPerMillimeter)));
        WriteSingle(record, 34, checked((float)(field.WidthMm * TwipsPerMillimeter)));

        var payload = field.Kind switch
        {
            LayoutFieldKind.Text => field.Text.Content,
            LayoutFieldKind.Image => field.Image.File,
            _ => field.LegacyPayload
        };
        WriteFixedString(record.Slice(38, 124), payload);

        // Every active record in the supplied 2.2.115 fixture carries 1 at +164. Keep an existing
        // template value; initialize it only for a newly manufactured blank record.
        if (BinaryPrimitives.ReadInt16LittleEndian(record.Slice(164, 2)) == 0)
            BinaryPrimitives.WriteInt16LittleEndian(record.Slice(164, 2), 1);

        BinaryPrimitives.WriteInt32LittleEndian(record.Slice(180, 4), field.Appearance.BackColorOle);
        BinaryPrimitives.WriteInt32LittleEndian(record.Slice(184, 4), field.Appearance.ForeColorOle);
        WriteFixedString(record.Slice(188, 32), field.Text.FontName);
        WriteSingle(record, 220, checked((float)field.Text.FontSize));
        BinaryPrimitives.WriteInt16LittleEndian(record.Slice(224, 2), field.Text.Fixed ? (short)-1 : (short)0);
        BinaryPrimitives.WriteInt16LittleEndian(record.Slice(262, 2), checked((short)field.Level));
    }

    private static int FindFieldTableOffset(ReadOnlySpan<byte> data)
    {
        if (LooksLikeFieldTable(data, KnownFieldTableOffset))
            return KnownFieldTableOffset;

        var scanEnd = Math.Min(4096, data.Length - FieldRecordSize * 3);
        for (var offset = 128; offset <= scanEnd; offset++)
        {
            if (LooksLikeFieldTable(data, offset)) return offset;
        }
        return -1;
    }

    private static bool LooksLikeFieldTable(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + FieldRecordSize * 3 > data.Length) return false;
        var plausible = 0;
        for (var i = 0; i < 3; i++)
        {
            var record = data.Slice(offset + i * FieldRecordSize, FieldRecordSize);
            var type = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(20, 2));
            var x = ReadSingle(record, 26) / TwipsPerMillimeter;
            var y = ReadSingle(record, 22) / TwipsPerMillimeter;
            var w = ReadSingle(record, 34) / TwipsPerMillimeter;
            var h = ReadSingle(record, 30) / TwipsPerMillimeter;
            if (type is > 0 and < 32 && IsFiniteReasonable(x) && IsFiniteReasonable(y) &&
                w is >= 0 and < 1000 && h is >= 0 and < 1000)
                plausible++;
        }
        return plausible >= 2;
    }

    private static bool IsEmptyRecord(ReadOnlySpan<byte> record)
    {
        var name = ReadFixedString(record.Slice(0, 20));
        var type = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(20, 2));
        var payload = ReadFixedString(record.Slice(38, 124));
        return type == 0 && name.Length == 0 && payload.Length == 0;
    }

    private static LayoutSide InferSideHint(LayoutField field)
    {
        if (field.Kind != LayoutFieldKind.Image) return LayoutSide.Unknown;
        var hint = (field.Name + " " + field.Image.File).ToLowerInvariant();
        if (hint.Contains("fronte") || hint.Contains("front")) return LayoutSide.Front;
        if (hint.Contains("retro") || hint.Contains("back")) return LayoutSide.Back;
        return LayoutSide.Unknown;
    }

    private static bool IsBackgroundLike(CardLayout layout, LayoutField field) =>
        field.WidthMm >= layout.WidthMm * 0.70 && field.HeightMm >= layout.HeightMm * 0.60;

    private static byte[] CreateDefaultRecord(LayoutFieldKind kind)
    {
        var record = new byte[FieldRecordSize];
        record.AsSpan(0, 20).Fill(0x20);
        record.AsSpan(38, 124).Fill(0x20);
        record.AsSpan(188, 32).Fill(0x20);
        record.AsSpan(230, 28).Fill(0x20);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(164, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(180, 4), 0x00FFFFFF);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(184, 4), 0x00000000);
        WriteFixedString(record.AsSpan(188, 32), "Arial");
        WriteSingle(record, 220, 10f);
        return record;
    }

    private static LayoutField CreateBlankField(LayoutFieldKind kind, LayoutSide side, int legacyTypeCode)
    {
        var field = new LayoutField
        {
            Kind = kind,
            Side = side,
            LegacyTypeCode = legacyTypeCode,
            Xmm = 5,
            Ymm = 5,
            WidthMm = kind == LayoutFieldKind.Image ? 25 : 30,
            HeightMm = kind == LayoutFieldKind.Image ? 25 : 7,
            LegacyRecordTemplate = CreateDefaultRecord(kind)
        };
        field.Text.FontName = "Arial";
        field.Text.FontSize = 10;
        field.Appearance.ForeColorOle = 0;
        field.Appearance.BackColorOle = 0x00FFFFFF;
        return field;
    }

    private static LayoutField CloneField(LayoutField source)
    {
        var clone = new LayoutField
        {
            Index = source.Index,
            Name = source.Name,
            Kind = source.Kind,
            Side = source.Side,
            LegacyTypeCode = source.LegacyTypeCode,
            LegacyPayload = source.LegacyPayload,
            LegacyRecordTemplate = source.LegacyRecordTemplate.ToArray(),
            Xmm = source.Xmm,
            Ymm = source.Ymm,
            WidthMm = source.WidthMm,
            HeightMm = source.HeightMm,
            Level = source.Level
        };

        clone.Text.Content = source.Text.Content;
        clone.Text.DatabaseField = source.Text.DatabaseField;
        clone.Text.Fixed = source.Text.Fixed;
        clone.Text.FontName = source.Text.FontName;
        clone.Text.FontSize = source.Text.FontSize;
        clone.Text.Bold = source.Text.Bold;
        clone.Text.Italic = source.Text.Italic;
        clone.Text.Strikeout = source.Text.Strikeout;
        clone.Text.Alignment = source.Text.Alignment;

        clone.Appearance.Opaque = source.Appearance.Opaque;
        clone.Appearance.ForeColorOle = source.Appearance.ForeColorOle;
        clone.Appearance.BackColorOle = source.Appearance.BackColorOle;
        clone.Appearance.Border = source.Appearance.Border;
        clone.Appearance.BorderWidth = source.Appearance.BorderWidth;
        clone.Appearance.BorderColorOle = source.Appearance.BorderColorOle;
        clone.Appearance.RotationDegrees = source.Appearance.RotationDegrees;

        clone.Image.File = source.Image.File;
        clone.Image.DatabaseField = source.Image.DatabaseField;
        clone.Image.KeepOriginalSize = source.Image.KeepOriginalSize;
        clone.Image.KeepAspectRatio = source.Image.KeepAspectRatio;
        clone.Image.DefaultExtension = source.Image.DefaultExtension;

        clone.Table.Rows = source.Table.Rows;
        clone.Table.Columns = source.Table.Columns;
        clone.Table.Header = source.Table.Header;
        clone.Table.Grid = source.Table.Grid;
        clone.Table.Widths = source.Table.Widths;
        clone.Table.Sql = source.Table.Sql;
        return clone;
    }

    private static int AllocateInsertionIndex(CardLayout layout, LayoutSide side, int? afterIndex)
    {
        if (afterIndex.HasValue) return Math.Min(afterIndex.Value + 1, MaxFieldSlots - 1);

        if (side == LayoutSide.Front)
        {
            var firstBack = layout.Fields.Where(x => x.Side == LayoutSide.Back).Select(x => x.Index).DefaultIfEmpty(-1).Min();
            if (firstBack >= 0) return firstBack;
        }

        return layout.Fields.Count == 0 ? 0 : layout.Fields.Max(x => x.Index) + 1;
    }

    private static void ShiftIndices(CardLayout layout, int insertionIndex)
    {
        foreach (var field in layout.Fields.Where(x => x.Index >= insertionIndex).OrderByDescending(x => x.Index))
        {
            if (field.Index >= MaxFieldSlots - 1)
                throw new InvalidOperationException($"The legacy layout supports at most {MaxFieldSlots} field slots.");
            field.Index++;
        }
    }

    private static int NextLevel(CardLayout layout, LayoutSide side) =>
        layout.Fields.Where(x => side == LayoutSide.Unknown || x.Side == side).Select(x => x.Level).DefaultIfEmpty(-1).Max() + 1;

    private static void EnsureCapacity(CardLayout layout)
    {
        if (layout.Fields.Count >= MaxFieldSlots)
            throw new InvalidOperationException($"The recovered UltraPrint 2.2.115 layout format has {MaxFieldSlots} field slots and this layout is full.");
    }

    private static void ValidateFieldIndices(CardLayout layout)
    {
        var duplicate = layout.Fields.GroupBy(x => x.Index).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Multiple fields use legacy slot #{duplicate.Key}.");
        if (layout.Fields.Any(x => x.Index is < 0 or >= MaxFieldSlots))
            throw new InvalidOperationException($"A field index is outside the 0..{MaxFieldSlots - 1} legacy slot range.");
    }

    private static string UniqueName(CardLayout layout, string requested)
    {
        if (layout.Fields.All(x => !string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase)))
            return requested;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = requested + "_" + i;
            if (layout.Fields.All(x => !string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return requested + "_" + Guid.NewGuid().ToString("N")[..6];
    }

    private static bool IsPlausibleCardSize(float width, float height) =>
        float.IsFinite(width) && float.IsFinite(height) && width is > 1 and < 2000 && height is > 1 and < 2000;

    private static bool IsFiniteReasonable(double value) => double.IsFinite(value) && value is > -1000 and < 1000;

    private static string ReadFixedString(ReadOnlySpan<byte> bytes) =>
        LegacyEncoding.GetString(bytes).TrimEnd('\0', ' ');

    private static void WriteFixedString(Span<byte> destination, string? value)
    {
        destination.Fill(0x20);
        if (string.IsNullOrEmpty(value)) return;
        var bytes = LegacyEncoding.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length)).CopyTo(destination);
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));

    private static void WriteInt16(Span<byte> data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.Slice(offset, 2), value);

    private static void WriteSingle(Span<byte> data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), BitConverter.SingleToInt32Bits(value));
}
