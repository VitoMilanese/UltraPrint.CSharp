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
            TestLegacyPositioning();
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
            HorizontalPitchMm = 5,
            VerticalPitchMm = 6,
            FillDirection = SequenceFillDirection.Horizontal,
            PaperOrientation = SequencePaperOrientation.Landscape,
            StartSlot = 2,
            Side = LayoutSide.Unknown
        };

        AssertEqual(2, SequencePrintPlanner.GetSheetCount(6, settings), "sequence sheet count honors first start slot");
        var first = SequencePrintPlanner.GetSheetPlacements(6, 85, 54, settings, 0);
        AssertEqual(4, first.Count, "sequence first sheet capacity after start slot");
        AssertEqual(2, first[0].SlotIndex, "sequence first slot");
        AssertEqual(0, first[0].RecordIndex, "sequence first record");
        AssertNearly(187, first[0].Xmm, 0.001, "sequence first record X includes card width plus Passo gap");
        AssertNearly(11, first[0].Ymm, 0.001, "sequence first record Y");
        AssertEqual(5, first[^1].SlotIndex, "sequence first sheet last slot");
        AssertEqual(3, first[^1].RecordIndex, "sequence first sheet last record");

        var second = SequencePrintPlanner.GetSheetPlacements(6, 85, 54, settings, 1);
        AssertEqual(2, second.Count, "sequence second sheet remaining record count");
        AssertEqual(0, second[0].SlotIndex, "sequence later sheets restart at slot zero");
        AssertEqual(4, second[0].RecordIndex, "sequence second sheet first record index");
        AssertNearly(7, second[0].Xmm, 0.001, "sequence second sheet first X");
        AssertNearly(11, second[0].Ymm, 0.001, "sequence second sheet first Y");

        var extent = SequencePrintPlanner.GetUsedExtent(85, 54, settings);
        AssertNearly(272, extent.WidthMm, 0.001, "sequence width uses card widths plus inter-card Passo gaps");
        AssertNearly(125, extent.HeightMm, 0.001, "sequence height uses card heights plus inter-card Passo gaps");

        var vertical = settings.Clone();
        vertical.FillDirection = SequenceFillDirection.Vertical;
        vertical.StartSlot = 1;
        AssertEqual(2, SequencePrintPlanner.GetSheetCount(5, vertical), "vertical fill start slot affects first sheet capacity in column-major order");
        var verticalFirst = SequencePrintPlanner.GetSheetPlacements(5, 85, 54, vertical, 0);
        AssertEqual(4, verticalFirst.Count, "vertical fill first sheet capacity");
        AssertEqual(1, verticalFirst[0].SlotIndex, "vertical fill starts at selected physical slot");
        AssertEqual(4, verticalFirst[1].SlotIndex, "vertical fill advances down the column");
        AssertEqual(2, verticalFirst[2].SlotIndex, "vertical fill then advances to next column");
        AssertEqual(5, verticalFirst[3].SlotIndex, "vertical fill finishes next column");
        AssertNearly(97, verticalFirst[0].Xmm, 0.001, "vertical fill first X");
        AssertNearly(71, verticalFirst[1].Ymm, 0.001, "vertical fill second Y");
        AssertEqual(SequencePaperOrientation.Landscape, vertical.PaperOrientation, "clone preserves paper orientation");
    }

    private static void TestLegacyPositioning()
    {
        AssertEqual(0, LegacySequencePositioning.GetPageCount(0, 10), "legacy zero records have zero pages");
        AssertEqual(2, LegacySequencePositioning.GetPageCount(20, 10), "legacy Pagine is ceiling at exact capacity");
        AssertEqual(3, LegacySequencePositioning.GetPageCount(21, 10), "legacy Pagine rounds a partial page up");

        AssertEqual(1, LegacySequencePositioning.GetPageNumber(1, 10, 3, singlePageMode: false),
            "legacy normal positioning starts on page one");
        AssertEqual(2, LegacySequencePositioning.GetPageNumber(10, 10, 3, singlePageMode: false),
            "legacy normal positioning preserves exact-capacity quotient boundary");
        AssertEqual(3, LegacySequencePositioning.GetPageNumber(20, 10, 3, singlePageMode: false),
            "legacy normal positioning preserves second exact-capacity boundary");
        AssertEqual(1, LegacySequencePositioning.GetPageNumber(30, 10, 3, singlePageMode: false),
            "Pagina_Change resets an out-of-range computed page to one");

        AssertEqual(1, LegacySequencePositioning.GetPageNumber(3, 10, 3, singlePageMode: true),
            "legacy single-page modulo zero is normalized to page one");
        AssertEqual(2, LegacySequencePositioning.GetPageNumber(5, 10, 3, singlePageMode: true),
            "legacy single-page mode uses NumRecord Mod Pagine");
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
            HorizontalPitchMm = 2.5,
            VerticalPitchMm = 3.25,
            FillDirection = SequenceFillDirection.Vertical,
            PaperOrientation = SequencePaperOrientation.Landscape,
            StartSlot = 1,
            Side = LayoutSide.Back,
            SinglePageMode = true,
            DrawCutMarks = true
        };
        ManagedSequenceStore.Save(layout, settings);
        var loaded = ManagedSequenceStore.Load(layout);

        AssertEqual(3, loaded.Rows, "sequence store rows");
        AssertEqual(2, loaded.Columns, "sequence store columns");
        AssertNearly(4.5, loaded.MarginLeftMm, 0.001, "sequence store left margin");
        AssertNearly(8.25, loaded.MarginTopMm, 0.001, "sequence store top margin");
        AssertNearly(2.5, loaded.HorizontalPitchMm, 0.001, "sequence store horizontal Passo gap");
        AssertNearly(3.25, loaded.VerticalPitchMm, 0.001, "sequence store vertical Passo gap");
        AssertEqual(SequenceFillDirection.Vertical, loaded.FillDirection, "sequence store fill direction");
        AssertEqual(SequencePaperOrientation.Landscape, loaded.PaperOrientation, "sequence store paper orientation");
        AssertEqual(1, loaded.StartSlot, "sequence store first slot");
        AssertEqual(LayoutSide.Back, loaded.Side, "sequence store side");
        AssertTrue(loaded.SinglePageMode, "sequence store single-page mode");
        AssertTrue(loaded.DrawCutMarks, "sequence store cut marks");
        AssertTrue(File.Exists(layout.SourcePath + ".sequence.json"), "sequence setup uses non-destructive sidecar");

        File.WriteAllText(layout.SourcePath + ".sequence.json", """
{
  "Version": 2,
  "Settings": {
    "Rows": 3,
    "Columns": 2,
    "MarginLeftMm": 4.5,
    "MarginTopMm": 8.25,
    "HorizontalPitchMm": 2.5,
    "VerticalPitchMm": 3.25,
    "FillDirection": 1,
    "StartSlot": 1,
    "Side": 2,
    "DrawCutMarks": true,
    "SinglePageMode": true
  }
}
""");
        var migratedV2 = ManagedSequenceStore.Load(layout);
        AssertNearly(2.5, migratedV2.HorizontalPitchMm, 0.001, "v2 native Passo gap is preserved");
        AssertEqual(SequenceFillDirection.Vertical, migratedV2.FillDirection, "v2 fill direction is preserved");
        AssertEqual(SequencePaperOrientation.Portrait, migratedV2.PaperOrientation, "v2 missing paper orientation migrates to portrait");

        File.WriteAllText(layout.SourcePath + ".sequence.json", """
{
  "Version": 1,
  "Settings": {
    "Rows": 3,
    "Columns": 2,
    "MarginLeftMm": 4.5,
    "MarginTopMm": 8.25,
    "HorizontalPitchMm": 87.5,
    "VerticalPitchMm": 57.25,
    "StartSlot": 1,
    "Side": 2,
    "DrawCutMarks": true,
    "SinglePageMode": true
  }
}
""");
        var migrated = ManagedSequenceStore.Load(layout);
        AssertNearly(2.5, migrated.HorizontalPitchMm, 0.001, "v1 full horizontal pitch migrates to native Passo gap");
        AssertNearly(3.25, migrated.VerticalPitchMm, 0.001, "v1 full vertical pitch migrates to native Passo gap");
        AssertEqual(SequenceFillDirection.Horizontal, migrated.FillDirection, "v1 managed layouts preserve historical row-major fill");
        AssertEqual(SequencePaperOrientation.Portrait, migrated.PaperOrientation, "v1 managed layouts migrate to portrait");
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
