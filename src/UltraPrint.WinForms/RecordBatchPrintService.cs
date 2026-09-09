using System.Drawing.Printing;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for the record-oriented StampaRecord / StampaTutti path. Each database row
/// is bound to a fresh preview layout and rendered at the physical card size through PrintDocument.
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
        using var document = CreateDocument(layout, records, bindings, side);
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
        using var document = CreateDocument(layout, records, bindings, side);
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
        document.Print();
    }

    private PrintDocument CreateDocument(
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        LayoutSide requestedSide)
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

            var bound = LegacyRecordBinder.CreateBoundLayout(layout, records[recordIndex], bindings);
            using var renderer = new LayoutCanvas
            {
                Layout = bound,
                Side = sides[sideIndex],
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
            renderer.RenderTo(graphics, new RectangleF(x, y, cardWidth, cardHeight), sides[sideIndex]);

            sideIndex++;
            if (sideIndex >= sides.Length)
            {
                sideIndex = 0;
                recordIndex++;
            }
            e.HasMorePages = recordIndex < records.Count;
        };

        document.EndPrint += (_, _) =>
        {
            recordIndex = 0;
            sideIndex = 0;
        };
        return document;
    }
}
