using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
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

        TestBarcodeLayoutRoundTrip();
    }

    private static void TestBarcodeLayoutRoundTrip()
    {
        var codec = new UltraPrint22115LayoutCodec();
        var layout = codec.CreateNewLayout("BarcodeRoundTrip");
        var barcode = codec.CreateField(layout, LayoutFieldKind.Barcode, LayoutSide.Front, legacyTypeCode: 4);
        barcode.Name = "Barcode_Test";
        barcode.LegacyPayload = "12345678";
        barcode.Text.FontName = "3 OF 9";
        barcode.Text.FontSize = 18.5;

        var path = Path.Combine(Path.GetTempPath(), "UltraPrint.Barcode." + Guid.NewGuid().ToString("N") + ".ly");
        try
        {
            codec.Save(layout, path);
            var loaded = codec.Load(path);
            var recovered = loaded.Fields.Single(field => field.Kind == LayoutFieldKind.Barcode);

            AssertEqual(4, recovered.LegacyTypeCode, "barcode legacy type survives .ly round-trip");
            AssertEqual("12345678", recovered.LegacyPayload, "barcode payload survives .ly round-trip");
            AssertEqual("3 OF 9", recovered.Text.FontName, "barcode font survives .ly round-trip");
            AssertEqual(18.5, recovered.Text.FontSize, "barcode font size survives .ly round-trip");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
