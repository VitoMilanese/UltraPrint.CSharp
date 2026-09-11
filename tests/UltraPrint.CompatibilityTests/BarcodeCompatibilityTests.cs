using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Layout;

internal static class BarcodeCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(0x5FCC60, LegacyBarcodeFormatter.NativeFormatterAddress,
            "barcode native formatter address");
        AssertTrue(LegacyBarcodeFormatter.RecoveredFontPrefixes.SequenceEqual(
            new[] { "UPC", "3 OF 9", "2 OF 5", "CODE 128", "CODABAR" }),
            "frmBarcode recovered category order");

        AssertFormat("2 OF 5", "123", "(123)");
        AssertFormat("2 OF 5 INTERLEAVED", "123", "(1G)");
        AssertFormat("3 OF 9", "ABC", "*ABC*");
        AssertFormat("CODABAR", "123", "A123B");
        AssertFormat("CODE 128", "ABC", "hABCb\u0080");

        AssertFormat("UPCH", "0123456", "<!\"#$=efgf<");
        AssertFormat("UPCH", "012345678901", "u<\"#$%&'=hijabc<");
        AssertFormat("UPCH", "123", "<!!!!=bcdg<");

        AssertTrue(!LegacyBarcodeFormatter.TryFormat("Arial", "123", out var raw),
            "ordinary fonts do not invoke barcode formatter");
        AssertEqual("123", raw, "unknown barcode font leaves data unchanged");
    }

    private static void AssertFormat(string fontName, string value, string expected)
    {
        AssertTrue(LegacyBarcodeFormatter.TryFormat(fontName, value, out var actual),
            $"{fontName} is recognized");
        AssertEqual(expected, actual, $"{fontName} native glyph string");
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
