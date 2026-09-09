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
    public IList<LayoutField> Fields { get; } = new List<LayoutField>();
}
