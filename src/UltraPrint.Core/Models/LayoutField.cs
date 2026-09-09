using System.ComponentModel;

namespace UltraPrint.Core.Models;

public enum LayoutFieldKind
{
    Unknown,
    Text,
    Image,
    Rectangle,
    Barcode,
    Table,
    MagneticStripe,
    Chip,
    Hardware
}

public enum LayoutSide
{
    Unknown,
    Front,
    Back
}

public sealed class LayoutField
{
    [ReadOnly(true)]
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    [ReadOnly(true)]
    public LayoutFieldKind Kind { get; set; }

    [ReadOnly(true)]
    public LayoutSide Side { get; set; }

    [ReadOnly(true)]
    public int LegacyTypeCode { get; set; }

    [Browsable(false)]
    public string LegacyPayload { get; set; } = string.Empty;

    /// <summary>
    /// Raw 264-byte UltraPrint 2.2.115 field record used as a preservation template.
    /// It lets the editor move/duplicate a record without destroying still-unknown legacy flags.
    /// </summary>
    [Browsable(false)]
    public byte[] LegacyRecordTemplate { get; set; } = Array.Empty<byte>();

    public double Xmm { get; set; }
    public double Ymm { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public int Level { get; set; }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public TextFieldSettings Text { get; } = new();

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public AppearanceSettings Appearance { get; } = new();

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public ImageFieldSettings Image { get; } = new();

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public TableFieldSettings Table { get; } = new();

    public override string ToString()
    {
        var side = Side switch
        {
            LayoutSide.Front => "F",
            LayoutSide.Back => "B",
            _ => "?"
        };
        var label = string.IsNullOrWhiteSpace(Name) ? $"Field {Index}" : Name;
        return $"[{side}] #{Index:00} {label}";
    }
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class TextFieldSettings
{
    public string Content { get; set; } = string.Empty;
    public string DatabaseField { get; set; } = string.Empty;
    public bool Fixed { get; set; }
    public string FontName { get; set; } = "Arial";
    public double FontSize { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Strikeout { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public override string ToString() => Content;
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AppearanceSettings
{
    public bool Opaque { get; set; }
    public int ForeColorOle { get; set; }
    public int BackColorOle { get; set; } = 0x00FFFFFF;
    public bool Border { get; set; }
    public int BorderWidth { get; set; }
    public int BorderColorOle { get; set; }
    public int RotationDegrees { get; set; }

    public override string ToString() => "Appearance";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class ImageFieldSettings
{
    public string File { get; set; } = string.Empty;
    public string DatabaseField { get; set; } = string.Empty;
    public bool KeepOriginalSize { get; set; }
    public bool KeepAspectRatio { get; set; }
    public string DefaultExtension { get; set; } = string.Empty;

    public override string ToString() => File;
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class TableFieldSettings
{
    public int Rows { get; set; }
    public int Columns { get; set; }
    public bool Header { get; set; }
    public bool Grid { get; set; }
    public string Widths { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;

    public override string ToString() => $"{Rows} x {Columns}";
}
