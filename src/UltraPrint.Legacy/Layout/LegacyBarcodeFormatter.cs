namespace UltraPrint.Legacy.Layout;

/// <summary>
/// Reproduces the barcode-font text formatter at native UltraPrint 2.2.115 helper
/// 0x005FCC60. The original application did not draw bars itself: it transformed the
/// field value into a glyph string and rendered that string with the selected legacy
/// barcode font.
/// </summary>
public static class LegacyBarcodeFormatter
{
    public const int NativeFormatterAddress = 0x5FCC60;
    public const string DefaultPreviewValue = "01234567";

    public static IReadOnlyList<string> RecoveredFontPrefixes { get; } =
        ["UPC", "3 OF 9", "2 OF 5", "CODE 128", "CODABAR"];

    public static bool IsRecoveredBarcodeFont(string? fontName)
    {
        var normalized = NormalizeFontName(fontName);
        return normalized == "3 OF 9" ||
               normalized == "2 OF 5" ||
               normalized == "2 OF 5 INTERLEAVED" ||
               normalized == "CODE 128" ||
               normalized == "CODABAR" ||
               normalized.StartsWith("UPCH", StringComparison.Ordinal);
    }

    public static bool MatchesRecoveredFontPrefix(string? fontName)
    {
        var normalized = NormalizeFontName(fontName);
        return RecoveredFontPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool TryFormat(string? fontName, string? value, out string formatted)
    {
        value ??= string.Empty;
        var normalized = NormalizeFontName(fontName);

        switch (normalized)
        {
            case "2 OF 5":
                formatted = "(" + value + ")";
                return true;

            case "2 OF 5 INTERLEAVED":
                formatted = FormatInterleavedTwoOfFive(value);
                return true;

            case "3 OF 9":
                formatted = "*" + value + "*";
                return true;

            case "CODABAR":
                formatted = "A" + value + "B";
                return true;

            case "CODE 128":
                return TryFormatCode128(value, out formatted);

            default:
                if (normalized.StartsWith("UPCH", StringComparison.Ordinal))
                    return TryFormatUpch(value, out formatted);

                formatted = value;
                return false;
        }
    }

    private static string FormatInterleavedTwoOfFive(string value)
    {
        if ((value.Length & 1) != 0)
            value = "0" + value;

        var result = new char[(value.Length / 2) + 2];
        result[0] = '(';
        for (var source = 0; source < value.Length; source += 2)
        {
            // Native code uses VB Val(pair), then Chr(value + 48). Val returns zero for
            // a pair with no numeric prefix; emulate the common decimal path exactly and
            // its zero fallback conservatively for malformed data.
            var pairValue = 0;
            if (value[source] is >= '0' and <= '9')
            {
                pairValue = value[source] - '0';
                if (source + 1 < value.Length && value[source + 1] is >= '0' and <= '9')
                    pairValue = pairValue * 10 + (value[source + 1] - '0');
            }

            result[(source / 2) + 1] = (char)(pairValue + 48);
        }
        result[^1] = ')';
        return new string(result);
    }

    private static bool TryFormatCode128(string value, out string formatted)
    {
        // The 2.2.115 native code always emits the Set-B start glyph Chr(104). It also
        // performs a digit-only scan but never uses that Boolean to select Chr(105).
        var checksum = 104;
        for (var index = 0; index < value.Length; index++)
        {
            // Important compatibility quirk: the native routine searches each source
            // character in the string Chr(0)..Chr(32), then subtracts one from the
            // 1-based InStr result. Printable characters therefore contribute -1.
            var ch = value[index];
            var codeValue = ch <= (char)32 ? (int)ch : -1;
            checksum += codeValue * (index + 1);
        }

        var checksumGlyph = checksum % 103; // VB6 Mod keeps the sign of the dividend.
        if (checksumGlyph < 0 || checksumGlyph > char.MaxValue)
        {
            formatted = value;
            return false;
        }

        formatted = ((char)104) + value + ((char)checksumGlyph).ToString() + ((char)128);
        return true;
    }

    private static bool TryFormatUpch(string value, out string formatted)
    {
        var data = value.Trim();
        var shortForm = data.Length == 7 || data.Length < 7;
        var targetLength = shortForm ? 7 : 12;

        if (data.Length != 7 && data.Length != 12)
            data = LegacyZeri(data, targetLength);

        if (data.Length is not (7 or 12) || data.Any(ch => ch is < '0' or > '9'))
        {
            formatted = value;
            return false;
        }

        var checksum = ComputeUpcCheckDigit(data);
        var full = data + (char)('0' + checksum);
        var isUpca = data.Length == 12;
        var firstDigit = data[0] - '0';
        var prefix = isUpca ? PrefixGlyph(firstDigit).ToString() : string.Empty;
        var leftCount = isUpca ? 6 : 4;
        var parity = isUpca ? ParityPatterns[firstDigit] : "AAAA";

        var encoded = new System.Text.StringBuilder(prefix.Length + full.Length + 3);
        encoded.Append(prefix);
        encoded.Append('<');

        var sourceStart = isUpca ? 1 : 0;
        var relative = 0;
        for (var index = sourceStart; index < full.Length; index++)
        {
            var digit = full[index] - '0';
            relative++;

            if (relative <= leftCount)
            {
                var side = parity[relative - 1];
                encoded.Append(side == 'B' ? BAlphabet[digit] : AAlphabet[digit]);
                if (relative == leftCount)
                    encoded.Append('=');
            }
            else
            {
                encoded.Append(CAlphabet[digit]);
            }
        }

        encoded.Append('<');
        formatted = encoded.ToString();
        return true;
    }

    private static int ComputeUpcCheckDigit(string data)
    {
        var weighted = 0;
        var alternate = 0;
        var fromRight = 0;
        for (var index = data.Length - 1; index >= 0; index--, fromRight++)
        {
            var digit = data[index] - '0';
            if ((fromRight & 1) == 0) weighted += digit;
            else alternate += digit;
        }

        var total = weighted * 3 + alternate;
        return (10 - (total % 10)) % 10;
    }

    private static string LegacyZeri(string value, int width)
    {
        value = value.Trim();
        var padded = new string('0', Math.Max(0, width)) + value;
        return padded.Length <= width ? padded : padded[^width..];
    }

    private static char PrefixGlyph(int firstDigit) => (char)('u' + firstDigit);

    private static string NormalizeFontName(string? fontName) =>
        (fontName ?? string.Empty).Trim().ToUpperInvariant();

    private const string AAlphabet = "!\"#$%&'()*";
    private const string BAlphabet = "klmnopqrst";
    private const string CAlphabet = "abcdefghij";

    private static readonly string[] ParityPatterns =
    [
        "AAAAAA",
        "AABABB",
        "AABBAB",
        "AABBBA",
        "ABAABB",
        "ABBAAB",
        "ABBBAA",
        "ABABAB",
        "ABABBA",
        "ABBABA"
    ];
}
