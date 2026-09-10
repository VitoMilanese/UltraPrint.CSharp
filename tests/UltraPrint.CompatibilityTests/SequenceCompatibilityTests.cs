using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

internal static class SequenceCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.SequenceCompatibility", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            TestPlanner();
            TestStore(temp);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void TestPlanner()
    {
        var settings = new SequencePrintSettings
        {
            Rows = 2,
            Columns = 3,
            MarginLeftMm = 7,
            MarginTopMm = 11,
            HorizontalPitchMm = 90,
            VerticalPitchMm = 60,
            StartSlot = 2,
            Side = LayoutSide.Unknown
        };

        AssertEqual(2, SequencePrintPlanner.GetSheetCount(6, settings), "sequence sheet count honors first start slot");
        var first = SequencePrintPlanner.GetSheetPlacements(6, 85, 54, settings, 0);
        AssertEqual(4, first.Count, "sequence first sheet capacity after start slot");
        AssertEqual(2, first[0].SlotIndex, "sequence first slot");
        AssertEqual(0, first[0].RecordIndex, "sequence first record");
        AssertNearly(187, first[0].Xmm, 0.001, "sequence first record X");
        AssertNearly(11, first[0].Ymm, 0.001, "sequence first record Y");
        AssertEqual(5, first[^1].SlotIndex, "sequence first sheet last slot");
        AssertEqual(3, first[^1].RecordIndex, "sequence first sheet last record");

        var second = SequencePrintPlanner.GetSheetPlacements(6, 85, 54, settings, 1);
        AssertEqual(2, second.Count, "sequence second sheet remaining record count");
        AssertEqual(0, second[0].SlotIndex, "sequence later sheets restart at slot zero");
        AssertEqual(4, second[0].RecordIndex, "sequence second sheet first record index");
        AssertNearly(7, second[0].Xmm, 0.001, "sequence second sheet first X");
        AssertNearly(11, second[0].Ymm, 0.001, "sequence second sheet first Y");
    }

    private static void TestStore(string temp)
    {
        var layout = new CardLayout
        {
            Name = "SequenceFixture",
            SourcePath = Path.Combine(temp, "sequence-fixture.ly"),
            WidthMm = 85,
            HeightMm = 54
        };
        File.WriteAllBytes(layout.SourcePath, Array.Empty<byte>());
        var settings = new SequencePrintSettings
        {
            Rows = 3,
            Columns = 2,
            MarginLeftMm = 4.5,
            MarginTopMm = 8.25,
            HorizontalPitchMm = 87.5,
            VerticalPitchMm = 57.25,
            StartSlot = 1,
            Side = LayoutSide.Back,
            DrawCutMarks = true
        };
        ManagedSequenceStore.Save(layout, settings);
        var loaded = ManagedSequenceStore.Load(layout);

        AssertEqual(3, loaded.Rows, "sequence store rows");
        AssertEqual(2, loaded.Columns, "sequence store columns");
        AssertNearly(4.5, loaded.MarginLeftMm, 0.001, "sequence store left margin");
        AssertNearly(8.25, loaded.MarginTopMm, 0.001, "sequence store top margin");
        AssertNearly(87.5, loaded.HorizontalPitchMm, 0.001, "sequence store horizontal pitch");
        AssertNearly(57.25, loaded.VerticalPitchMm, 0.001, "sequence store vertical pitch");
        AssertEqual(1, loaded.StartSlot, "sequence store first slot");
        AssertEqual(LayoutSide.Back, loaded.Side, "sequence store side");
        AssertTrue(loaded.DrawCutMarks, "sequence store cut marks");
        AssertTrue(File.Exists(layout.SourcePath + ".sequence.json"), "sequence setup uses non-destructive sidecar");
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

    private static void AssertNearly(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"FAILED: {message}. Expected {expected}, actual {actual}.");
    }
}
