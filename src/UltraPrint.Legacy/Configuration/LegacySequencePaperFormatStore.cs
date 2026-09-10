using System.Globalization;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Configuration;

/// <summary>
/// One legacy Sequenza sheet-format entry loaded from Campo.ini [Formati]. The raw
/// Text is preserved because cboDimensioni stores/displays the complete string.
/// </summary>
public sealed record LegacySequencePaperFormat(string Text, double? WidthMm, double? HeightMm)
{
    public bool HasParsedSize => WidthMm.HasValue && HeightMm.HasValue;

    public bool TryGetOrientedSize(
        SequencePaperOrientation orientation,
        out double widthMm,
        out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;
        if (!WidthMm.HasValue || !HeightMm.HasValue) return false;

        if (orientation == SequencePaperOrientation.Landscape)
        {
            widthMm = HeightMm.Value;
            heightMm = WidthMm.Value;
        }
        else
        {
            widthMm = WidthMm.Value;
            heightMm = HeightMm.Value;
        }
        return true;
    }
}

/// <summary>
/// Recovered loader/parser for Sequenza.cboDimensioni. Native Form_Load reads keys
/// 1..20 from Campo.ini [Formati], adds every non-empty value, then selects item 0.
/// cboDimensioni_Click parses centimetres from "[widthxheight cm]" and swaps the
/// two axes for FoglioLandscape.
/// </summary>
public static class LegacySequencePaperFormatStore
{
    public const string SectionName = "Formati";
    public const int FirstKey = 1;
    public const int LastKey = 20;

    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public static IReadOnlyList<LegacySequencePaperFormat> Load(string campoIniPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campoIniPath);
        if (!File.Exists(campoIniPath)) return Array.Empty<LegacySequencePaperFormat>();

        var ini = LegacyIniDocument.Load(campoIniPath);
        var result = new List<LegacySequencePaperFormat>();
        for (var i = FirstKey; i <= LastKey; i++)
        {
            var text = ini.Get(SectionName, i.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (TryParseSize(text, out var widthMm, out var heightMm))
                result.Add(new LegacySequencePaperFormat(text, widthMm, heightMm));
            else
                result.Add(new LegacySequencePaperFormat(text, null, null));
        }
        return result;
    }

    public static bool TryParseSize(string? text, out double widthMm, out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Native GetInside calls use exactly '[' -> 'x' and 'x' -> ' '. Keep the
        // parser deliberately narrow so arbitrary text is not mistaken for a size.
        var open = text.IndexOf('[');
        if (open < 0) return false;
        var separator = text.IndexOf('x', open + 1);
        if (separator < 0) return false;
        var end = text.IndexOf(' ', separator + 1);
        if (end < 0) return false;

        var widthText = text[(open + 1)..separator].Trim();
        var heightText = text[(separator + 1)..end].Trim();
        if (!TryParseLegacyNumber(widthText, out var widthCm) ||
            !TryParseLegacyNumber(heightText, out var heightCm) ||
            widthCm <= 0 || heightCm <= 0)
            return false;

        widthMm = widthCm * 10.0;
        heightMm = heightCm * 10.0;
        return true;
    }

    public static bool TryGetOrientedSize(
        string? text,
        SequencePaperOrientation orientation,
        out double widthMm,
        out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;
        if (!TryParseSize(text, out var portraitWidthMm, out var portraitHeightMm)) return false;

        if (orientation == SequencePaperOrientation.Landscape)
        {
            widthMm = portraitHeightMm;
            heightMm = portraitWidthMm;
        }
        else
        {
            widthMm = portraitWidthMm;
            heightMm = portraitHeightMm;
        }
        return true;
    }

    private static bool TryParseLegacyNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, ItalianCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
