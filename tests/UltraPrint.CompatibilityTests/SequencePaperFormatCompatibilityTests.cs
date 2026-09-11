using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Data;

internal static class SequencePaperFormatCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "UltraPrint.SequencePaperFormat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestCampoFormats(root);
            TestSequencePersistence(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void TestCampoFormats(string root)
    {
        var path = Path.Combine(root, "Campo.ini");
        File.WriteAllText(path, """
[Formati]
1=A4 [21x29,7 cm]
2=A3 [29,7x42 cm]
3=Card [8,5x5,4 cm]
4=Custom malformed value
20=Last [10x20 cm]
21=Ignored [1x1 cm]
""");

        var formats = LegacySequencePaperFormatStore.Load(path);
        AssertEqual(5, formats.Count, "native Formati loader examines keys 1..20 and skips key 21");
        AssertEqual("A4 [21x29,7 cm]", formats[0].Text, "A4 raw text preserved");
        AssertNearly(210, formats[0].WidthMm!.Value, 0.001, "A4 width parsed from centimetres");
        AssertNearly(297, formats[0].HeightMm!.Value, 0.001, "A4 height parsed from Italian decimal comma");
        AssertNearly(85, formats[2].WidthMm!.Value, 0.001, "Card width parsed");
        AssertNearly(54, formats[2].HeightMm!.Value, 0.001, "Card height parsed");
        AssertFalse(formats[3].HasParsedSize, "malformed non-empty legacy entry remains listable");
        AssertEqual("Last [10x20 cm]", formats[4].Text, "key 20 is included");

        AssertTrue(
            LegacySequencePaperFormatStore.TryGetOrientedSize(
                formats[0].Text,
                SequencePaperOrientation.Landscape,
                out var landscapeWidth,
                out var landscapeHeight),
            "landscape size parses");
        AssertNearly(297, landscapeWidth, 0.001, "landscape swaps width");
        AssertNearly(210, landscapeHeight, 0.001, "landscape swaps height");
        AssertFalse(
            LegacySequencePaperFormatStore.TryParseSize("A4 21x29,7 cm", out _, out _),
            "native-style parser requires opening bracket");
    }

    private static void TestSequencePersistence(string root)
    {
        var layout = new CardLayout
        {
            Name = "PaperFormatFixture",
            SourcePath = Path.Combine(root, "paper-format.ly"),
            WidthMm = 85,
            HeightMm = 54
        };
        File.WriteAllBytes(layout.SourcePath, Array.Empty<byte>());

        var settings = ManagedSequenceStore.CreateDefault(layout);
        settings.PaperOrientation = SequencePaperOrientation.Landscape;
        settings.PaperFormatText = "A3 [29,7x42 cm]";
        ManagedSequenceStore.Save(layout, settings);
        var loaded = ManagedSequenceStore.Load(layout);
        AssertEqual("A3 [29,7x42 cm]", loaded.PaperFormatText!, "managed v4 paper-format text round trip");
        AssertEqual(SequencePaperOrientation.Landscape, loaded.PaperOrientation, "managed v4 orientation round trip");

        File.WriteAllText(layout.SourcePath + ".sequence.json", """
{
  "Version": 3,
  "Settings": {
    "Rows": 2,
    "Columns": 2,
    "MarginLeftMm": 10,
    "MarginTopMm": 10,
    "HorizontalPitchMm": 0,
    "VerticalPitchMm": 0,
    "FillDirection": 1,
    "PaperOrientation": 1,
    "StartSlot": 0,
    "Side": 1,
    "DrawCutMarks": false,
    "SinglePageMode": false
  }
}
""");
        var migratedV3 = ManagedSequenceStore.Load(layout);
        AssertEqual(SequencePaperOrientation.Landscape, migratedV3.PaperOrientation, "v3 orientation is preserved");
        AssertTrue(migratedV3.PaperFormatText is null, "v3 missing cboDimensioni migrates to native first-format fallback");

        var seq = Path.Combine(root, "PaperFormatFixture.Seq");
        File.WriteAllText(seq, "[Sequenza]\r\nCustom=KEEP\r\ncboDimensioni=Card [8,5x5,4 cm]\r\n");
        var fromSeq = LegacySequenceIniStore.Load(seq, layout, settings);
        AssertEqual("Card [8,5x5,4 cm]", fromSeq.PaperFormatText!, "legacy .Seq loads cboDimensioni Text");
        fromSeq.PaperFormatText = "A4 [21x29,7 cm]";
        LegacySequenceIniStore.Save(seq, layout, fromSeq);
        var ini = LegacyIniDocument.Load(seq);
        AssertEqual("A4 [21x29,7 cm]", ini.Get("Sequenza", "cboDimensioni")!, "legacy .Seq saves cboDimensioni Text");
        AssertEqual("KEEP", ini.Get("Sequenza", "Custom")!, "legacy .Seq preserves unknown key while updating cboDimensioni");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }

    private static void AssertNearly(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"FAILED: {message}. Expected {expected}, actual {actual}.");
    }
}
