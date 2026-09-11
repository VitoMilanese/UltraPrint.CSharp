namespace UltraPrint.Core.Models;

public sealed class CardLayout
{
    public string? SourcePath { get; set; }
    public string Name { get; set; } = string.Empty;
    public double WidthMm { get; set; } = 85;
    public double HeightMm { get; set; } = 54;
    public int Dpi { get; set; } = 300;
    public string? FrontBackground { get; set; }
    public string? BackBackground { get; set; }
    public string? DatabasePath { get; set; }
    public string? Sql { get; set; }
    public string? ScriptApplication { get; set; }
    public bool UseWin32ApiPrinting { get; set; }

    /// <summary>
    /// Layout-global magnetic stripe expressions recovered from the three fixed String * 256
    /// values serialized after the 64 legacy field records. They are not properties of the
    /// BandaMagnetica field itself: native frmTracce edits these globals directly.
    /// </summary>
    public LayoutMagneticStripeSettings MagneticStripe { get; } = new();

    public IList<LayoutField> Fields { get; } = new List<LayoutField>();
}

public sealed class LayoutMagneticStripeSettings
{
    public string Track1 { get; set; } = string.Empty;
    public string Track2 { get; set; } = string.Empty;
    public string Track3 { get; set; } = string.Empty;

    public string this[int index]
    {
        get => index switch
        {
            0 => Track1,
            1 => Track2,
            2 => Track3,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        set
        {
            switch (index)
            {
                case 0: Track1 = value ?? string.Empty; break;
                case 1: Track2 = value ?? string.Empty; break;
                case 2: Track3 = value ?? string.Empty; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public IReadOnlyList<string> ToList() => [Track1, Track2, Track3];
}
