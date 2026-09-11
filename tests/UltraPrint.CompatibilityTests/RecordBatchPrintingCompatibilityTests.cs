using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;

internal static class RecordBatchPrintingCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestController();
        TestLayoutCapabilities();
        TestSettingsStore();
    }

    private static void TestController()
    {
        var controller = new RecordBatchPrintJobController();
        AssertTrue(!controller.HasStarted && !controller.CancelRequested && !controller.IsRunning,
            "batch controller starts idle");

        controller.Start();
        AssertTrue(controller.HasStarted && controller.IsRunning && !controller.CancelRequested,
            "Inizia starts the cooperative batch job");

        controller.ReportProgress(2, 5, LayoutSide.Back);
        AssertEqual(2, controller.CurrentRecordNumber, "batch progress current record");
        AssertEqual(5, controller.TotalRecordCount, "batch progress total records");
        AssertEqual(LayoutSide.Back, controller.CurrentSide, "batch progress current side");

        controller.ReportCountdown(30);
        AssertEqual(30, controller.RemainingIntervalSeconds, "batch countdown state");

        controller.RequestCancel();
        AssertTrue(controller.CancelRequested, "Annulla raises the cooperative cancel flag");

        controller.Complete();
        AssertTrue(controller.IsCompleted && !controller.IsRunning, "batch completion ends running state");
        AssertEqual(0, controller.RemainingIntervalSeconds, "batch completion clears countdown");

        controller.Reset();
        AssertTrue(!controller.HasStarted && !controller.CancelRequested && !controller.IsCompleted,
            "batch reset clears transient native state");
        AssertEqual(LayoutSide.Unknown, controller.CurrentSide, "batch reset clears side");
    }

    private static void TestLayoutCapabilities()
    {
        var layout = new CardLayout();
        AssertTrue(!LegacyLayoutCapabilities.HasChip(layout), "layout without SmartCara has HasChip false");

        layout.Fields.Add(new LayoutField
        {
            Index = 0,
            Kind = LayoutFieldKind.Unknown,
            LegacyTypeCode = 10
        });
        AssertTrue(LegacyLayoutCapabilities.HasChip(layout),
            "legacy field type 10 drives recovered frmCarta.HasChip even before kind normalization");

        layout.Fields.Clear();
        layout.Fields.Add(new LayoutField { Index = 0, Kind = LayoutFieldKind.Chip });
        AssertTrue(LegacyLayoutCapabilities.HasChip(layout), "managed Chip kind drives recovered HasChip");
    }

    private static void TestSettingsStore()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.BatchPrintingCompatibility", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "UP.ini");
            AssertEqual(5, LegacyBatchPrintSettingsStore.LoadIntervalSeconds(path),
                "frmPrinting Form_Load uses native Intervallo default 5");

            File.WriteAllText(path, "[Setup]\r\nIntervallo=17\r\n\r\n[Other]\r\nKeep=Yes\r\n");
            AssertEqual(17, LegacyBatchPrintSettingsStore.LoadIntervalSeconds(path),
                "frmPrinting loads Setup Intervallo");

            LegacyBatchPrintSettingsStore.SaveIntervalSeconds(path, 41);
            AssertEqual(41, LegacyBatchPrintSettingsStore.LoadIntervalSeconds(path),
                "frmPrinting unload persists Setup Intervallo");
            var text = File.ReadAllText(path);
            AssertTrue(text.Contains("[Other]", StringComparison.Ordinal) && text.Contains("Keep=Yes", StringComparison.Ordinal),
                "Intervallo persistence preserves unrelated UP.ini content");

            File.WriteAllText(path, "[Setup]\r\nIntervallo=not-a-number\r\n");
            AssertEqual(5, LegacyBatchPrintSettingsStore.LoadIntervalSeconds(path),
                "invalid Intervallo falls back to native default");

            AssertEqual(30, LegacyBatchPrintSettingsStore.NormalizeChipInterval(5),
                "HasChip wait raises intervals below 30 seconds");
            AssertEqual(30, LegacyBatchPrintSettingsStore.NormalizeChipInterval(30),
                "HasChip wait preserves the 30-second boundary");
            AssertEqual(45, LegacyBatchPrintSettingsStore.NormalizeChipInterval(45),
                "HasChip wait preserves longer intervals");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
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
}
