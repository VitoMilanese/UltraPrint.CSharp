using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Data;

internal static class SequenceDuplexCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "UltraPrint.SequenceDuplex", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layout = new CardLayout
            {
                Name = "DuplexFixture",
                SourcePath = Path.Combine(root, "DuplexFixture.ly"),
                WidthMm = 85,
                HeightMm = 54
            };
            File.WriteAllBytes(layout.SourcePath, Array.Empty<byte>());
            var path = Path.Combine(root, "DuplexFixture.Seq");
            File.WriteAllText(path, "[Sequenza]\r\nCustomLegacyKey=KEEP\r\nFronte=1\r\nRetro=0\r\n");

            var settings = ManagedSequenceStore.CreateDefault(layout);
            settings.Side = LayoutSide.Back;
            settings.CutStack = true;
            settings.MirrorBack = true;
            settings.BackOffsetXmm = 1.25;
            settings.BackOffsetYmm = -2.5;
            settings.DrawCutMarks = true;
            LegacySequenceIniStore.Save(path, layout, settings);

            var ini = LegacyIniDocument.Load(path);
            AssertEqual("0", ini.Get("Sequenza", "SoloFronte")!, "SoloFronte option");
            AssertEqual("0", ini.Get("Sequenza", "FronteRetro")!, "FronteRetro option");
            AssertEqual("1", ini.Get("Sequenza", "SoloRetro")!, "SoloRetro option");
            AssertEqual("1", ini.Get("Sequenza", "Taglio")!, "Taglio cut-and-stack option");
            AssertEqual("1", ini.Get("Sequenza", "RetroaSpecchio")!, "RetroaSpecchio option");
            AssertEqual("1,25", ini.Get("Sequenza", "OffsetRetroX")!, "OffsetRetroX Italian decimal");
            AssertEqual("-2,5", ini.Get("Sequenza", "OffsetRetroY")!, "OffsetRetroY signed Italian decimal");
            AssertEqual("KEEP", ini.Get("Sequenza", "CustomLegacyKey")!, "unknown key preserved");
            AssertEqual("1", ini.Get("Sequenza", "Fronte")!, "transient Fronte key preserved verbatim");
            AssertEqual("0", ini.Get("Sequenza", "Retro")!, "transient Retro key preserved verbatim");

            var baseline = ManagedSequenceStore.CreateDefault(layout);
            baseline.DrawCutMarks = true;
            var loaded = LegacySequenceIniStore.Load(path, layout, baseline);
            AssertEqual(LayoutSide.Back, loaded.Side, "SoloRetro loads as managed back-only mode");
            AssertTrue(loaded.CutStack, "Taglio loads as cut-and-stack mode");
            AssertTrue(loaded.MirrorBack, "RetroaSpecchio loads as back slot mirror");
            AssertNearly(1.25, loaded.BackOffsetXmm, 0.0001, "OffsetRetroX loads");
            AssertNearly(-2.5, loaded.BackOffsetYmm, 0.0001, "OffsetRetroY loads");
            AssertTrue(loaded.DrawCutMarks, "managed crop marks remain independent from Taglio");

            File.WriteAllText(path,
                "[Sequenza]\r\n" +
                "SoloFronte=0\r\n" +
                "FronteRetro=1\r\n" +
                "SoloRetro=0\r\n" +
                "Taglio=0\r\n" +
                "RetroaSpecchio=0\r\n");
            loaded = LegacySequenceIniStore.Load(path, layout, baseline);
            AssertEqual(LayoutSide.Unknown, loaded.Side, "FronteRetro loads as front-then-back mode");
            AssertTrue(!loaded.CutStack, "Taglio zero disables cut-and-stack");
            AssertTrue(!loaded.MirrorBack, "RetroaSpecchio zero disables back mirror");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
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
