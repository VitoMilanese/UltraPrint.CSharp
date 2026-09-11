using System.Text;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Conservative writer for the now-proven frmCarta DataField fixed string. It mutates only
/// bytes 230..257 of the preserved 264-byte field template; all other legacy bytes remain exact.
/// The enclosing .ly file is still written only by the ordinary layout Save workflow.
/// </summary>
public static class LegacyNativeDataFieldWriter
{
    public static void Write(LayoutField field, string? dataField)
    {
        ArgumentNullException.ThrowIfNull(field);
        var record = field.LegacyRecordTemplate;
        if (record.Length != 264)
            throw new InvalidOperationException(
                "Native DataField persistence requires the preserved 264-byte legacy field record.");

        var value = (dataField ?? string.Empty).Trim();
        if (value.Any(ch => ch > byte.MaxValue))
            throw new ArgumentException(
                "Native DataField currently accepts only byte-preservable ANSI characters.",
                nameof(dataField));

        var bytes = Encoding.Latin1.GetBytes(value);
        if (bytes.Length > LegacyNativeDataFieldSemantics.SerializedDataFieldLength)
            throw new ArgumentOutOfRangeException(nameof(dataField),
                $"Native DataField cannot exceed {LegacyNativeDataFieldSemantics.SerializedDataFieldLength} bytes.");

        Array.Fill(
            record,
            (byte)' ',
            LegacyNativeDataFieldSemantics.SerializedDataFieldOffset,
            LegacyNativeDataFieldSemantics.SerializedDataFieldLength);
        bytes.CopyTo(record, LegacyNativeDataFieldSemantics.SerializedDataFieldOffset);

        if (field.Kind == LayoutFieldKind.Image)
            field.Image.DatabaseField = value;
        else
            field.Text.DatabaseField = value;
    }
}
