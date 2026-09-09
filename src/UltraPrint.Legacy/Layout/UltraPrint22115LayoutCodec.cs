using System.Buffers.Binary;
using System.Text;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Layout;

/// <summary>
/// Partially decoded UltraPrint 2.2.115 .ly reader/writer.
///
/// The format was recovered from TPMFAO19.ly and correlated with Campo.ini/native VB6 code.
/// Unknown bytes are deliberately preserved on save: Save patches only fields that have been
/// confirmed. This makes round-tripping much safer while reverse engineering continues.
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

        WriteSingle(data, 0, checked((float)layout.HeightMm));
        WriteSingle(data, 4, checked((float)layout.WidthMm));
        WriteInt16(data, 8, checked((short)layout.Dpi));

        foreach (var field in layout.Fields)
        {
            if (field.Index is < 0 or >= MaxFieldSlots) continue;
            var offset = fieldTableOffset + field.Index * FieldRecordSize;
            if (offset + FieldRecordSize > data.Length) continue;
            PatchField(data.AsSpan(offset, FieldRecordSize), field);
        }

        var fullTarget = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
        File.WriteAllBytes(fullTarget, data);
        layout.SourcePath = fullTarget;
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
            Kind = TypeToKind(type),
            Ymm = ReadSingle(record, 22) / TwipsPerMillimeter,
            Xmm = ReadSingle(record, 26) / TwipsPerMillimeter,
            HeightMm = ReadSingle(record, 30) / TwipsPerMillimeter,
            WidthMm = ReadSingle(record, 34) / TwipsPerMillimeter,
            Level = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(262, 2)),
            LegacyPayload = payload
        };

        field.Text.FontName = ReadFixedString(record.Slice(188, 32));
        if (field.Text.FontName.Length == 0) field.Text.FontName = "Arial";
        field.Text.FontSize = ReadSingle(record, 220);
        if (field.Text.FontSize is <= 0 or > 200) field.Text.FontSize = 10;
        field.Text.Fixed = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(224, 2)) != 0;

        // These two OLE colors are confirmed by the sample and Campo.ini Aspetto values.
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

    private static LayoutFieldKind TypeToKind(int type) => type switch
    {
        3 => LayoutFieldKind.Text,
        5 => LayoutFieldKind.Image,
        6 => LayoutFieldKind.Image,
        _ => LayoutFieldKind.Unknown
    };

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
