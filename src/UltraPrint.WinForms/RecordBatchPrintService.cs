using System.Drawing.Printing;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for the record-oriented StampaRecord / StampaTutti path. Each database row
/// is bound to a fresh preview layout and rendered at the physical card size through PrintDocument.
/// Direct batch printing also reproduces the separate native frmPrinting Inizia/Annulla shell.
/// </summary>
public sealed class RecordBatchPrintService
{
    private PrinterSettings _printerSettings;
    private PageSettings _pageSettings;

    public RecordBatchPrintService()
    {
        using var document = new PrintDocument();
        _printerSettings = (PrinterSettings)document.PrinterSettings.Clone();
        _pageSettings = (PageSettings)document.DefaultPageSettings.Clone();
    }

    public void ShowPreview(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        LayoutSide side)
    {
        using var document = CreateDocument(layout, records, bindings, side, null, applyChipInterval: false, null);
        using var dialog = new PrintPreviewDialog
        {
            Document = document,
            Width = 1150,
            Height = 820,
            StartPosition = FormStartPosition.CenterParent,
            UseAntiAlias = true
        };
        dialog.ShowDialog(owner);
    }

    public void Print(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        LayoutSide side)
    {
        if (records.Count == 0) throw new InvalidOperationException("There are no records to print.");

        var controller = new RecordBatchPrintJobController();
        var hasChip = LegacyLayoutCapabilities.HasChip(layout);
        var upIni = StartupPaths.FromBaseDirectory(AppContext.BaseDirectory).UpIni;
        var interval = LegacyBatchPrintSettingsStore.LoadIntervalSeconds(upIni);
        using var shell = new RecordBatchPrintingForm(controller, hasChip, interval, upIni);
        using var document = CreateDocument(
            layout,
            records,
            bindings,
            side,
            controller,
            hasChip,
            () => shell.IntervalSeconds);
        using var dialog = new PrintDialog
        {
            Document = document,
            UseEXDialog = true,
            AllowCurrentPage = false,
            AllowSelection = false,
            AllowSomePages = false
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        _printerSettings = (PrinterSettings)document.PrinterSettings.Clone();
        _pageSettings = (PageSettings)document.DefaultPageSettings.Clone();

        // The native frmDatabase.StampaTutti path shows frmPrinting and pumps
        // events until Command1 changes from Inizia to Annulla. Keep the shell
        // separate from the Windows printer-selection dialog used by the managed path.
        if (!shell.WaitForStart(owner)) return;
        if (controller.CancelRequested)
        {
            shell.Finish();
            return;
        }

        try
        {
            document.Print();
        }
        finally
        {
            shell.Finish();
        }
    }

    private PrintDocument CreateDocument(
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        LayoutSide requestedSide,
        RecordBatchPrintJobController? jobController,
        bool applyChipInterval,
        Func<int>? intervalSecondsProvider)
    {
        if (records.Count == 0) throw new InvalidOperationException("There are no records to print.");

        var sides = requestedSide == LayoutSide.Unknown
            ? new[] { LayoutSide.Front, LayoutSide.Back }
            : new[] { requestedSide };
        var recordIndex = 0;
        var sideIndex = 0;

        var document = new PrintDocument
        {
            DocumentName = string.IsNullOrWhiteSpace(layout.Name) ? "UltraPrint records" : layout.Name + " records",
            PrinterSettings = (PrinterSettings)_printerSettings.Clone()
        };
        document.DefaultPageSettings = (PageSettings)_pageSettings.Clone();

        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics;
            if (graphics is null)
            {
                e.HasMorePages = false;
                return;
            }

            var side = sides[sideIndex];
            jobController?.ReportProgress(recordIndex + 1, records.Count, side);
            PumpJobMessages(jobController);

            var bound = LegacyRecordBinder.CreateBoundLayout(layout, records[recordIndex], bindings);
            using var renderer = new LayoutCanvas
            {
                Layout = bound,
                Side = side,
                PreviewMode = true,
                ShowGrid = false
            };

            graphics.PageUnit = GraphicsUnit.Pixel;
            var marginLeft = e.MarginBounds.Left * graphics.DpiX / 100f;
            var marginTop = e.MarginBounds.Top * graphics.DpiY / 100f;
            var marginWidth = e.MarginBounds.Width * graphics.DpiX / 100f;
            var marginHeight = e.MarginBounds.Height * graphics.DpiY / 100f;
            var cardWidth = (float)(layout.WidthMm / 25.4 * graphics.DpiX);
            var cardHeight = (float)(layout.HeightMm / 25.4 * graphics.DpiY);
            var x = marginLeft + Math.Max(0, (marginWidth - cardWidth) / 2f);
            var y = marginTop + Math.Max(0, (marginHeight - cardHeight) / 2f);
            renderer.RenderTo(graphics, new RectangleF(x, y, cardWidth, cardHeight), side);

            // Native cancellation is cooperative through MainForm.CancelOp. Finish
            // the current logical card first so Front+Back remains a matched pair.
            PumpJobMessages(jobController);
            var completedRecord = false;
            sideIndex++;
            if (sideIndex >= sides.Length)
            {
                sideIndex = 0;
                recordIndex++;
                completedRecord = true;
            }

            if (completedRecord && jobController?.CancelRequested == true)
            {
                e.HasMorePages = false;
                return;
            }

            if (completedRecord && applyChipInterval && recordIndex < records.Count)
            {
                var requestedInterval = intervalSecondsProvider?.Invoke() ?? LegacyBatchPrintSettingsStore.DefaultIntervalSeconds;
                if (!WaitBetweenCards(jobController, requestedInterval))
                {
                    e.HasMorePages = false;
                    return;
                }
            }

            e.HasMorePages = recordIndex < records.Count;
        };

        document.EndPrint += (_, _) =>
        {
            recordIndex = 0;
            sideIndex = 0;
            jobController?.ReportCountdown(0);
        };
        return document;
    }

    private static bool WaitBetweenCards(RecordBatchPrintJobController? jobController, int requestedIntervalSeconds)
    {
        var seconds = LegacyBatchPrintSettingsStore.NormalizeChipInterval(requestedIntervalSeconds);
        var deadline = Environment.TickCount64 + checked((long)seconds * 1000L);
        var lastReported = -1;

        while (true)
        {
            if (jobController?.CancelRequested == true)
            {
                jobController.ReportCountdown(0);
                return false;
            }

            var remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0) break;

            var remainingSeconds = checked((int)Math.Ceiling(remainingMilliseconds / 1000.0));
            if (jobController is not null && remainingSeconds != lastReported)
            {
                lastReported = remainingSeconds;
                jobController.ReportCountdown(remainingSeconds);
            }

            Application.DoEvents();
            System.Threading.Thread.Sleep(25);
        }

        jobController?.ReportCountdown(0);
        return jobController?.CancelRequested != true;
    }

    private static void PumpJobMessages(RecordBatchPrintJobController? jobController)
    {
        if (jobController is not null) Application.DoEvents();
    }
}
