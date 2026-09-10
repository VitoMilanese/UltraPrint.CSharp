using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for the core Sequenza sheet-imposition path. Multiple
/// database records are rendered on one physical sheet using the recovered
/// rows/columns, offsets, Passo gaps, fill order and paper orientation.
/// </summary>
public sealed class SequencePrintService
{
    private PrinterSettings _printerSettings;
    private PageSettings _pageSettings;

    public SequencePrintService()
    {
        using var document = new PrintDocument();
        _printerSettings = (PrinterSettings)document.PrinterSettings.Clone();
        _pageSettings = (PageSettings)document.DefaultPageSettings.Clone();
    }

    public (double WidthMm, double HeightMm) GetPageSizeMm(SequencePrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var document = CreateBaseDocument("UltraPrint sequence page size", settings);
        var bounds = document.DefaultPageSettings.Bounds;
        var width = bounds.Width * 25.4 / 100.0;
        var height = bounds.Height * 25.4 / 100.0;
        return (width, height);
    }

    public void ShowPageSetup(IWin32Window owner, SequencePrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var document = CreateBaseDocument("UltraPrint sequence page setup", settings);
        using var dialog = new PageSetupDialog
        {
            Document = document,
            AllowMargins = true,
            AllowOrientation = true,
            AllowPaper = true,
            AllowPrinter = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        _printerSettings = (PrinterSettings)document.PrinterSettings.Clone();
        _pageSettings = (PageSettings)document.DefaultPageSettings.Clone();
        settings.PaperOrientation = document.DefaultPageSettings.Landscape
            ? SequencePaperOrientation.Landscape
            : SequencePaperOrientation.Portrait;
    }

    public void ShowPreview(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings) =>
        ShowPreviewCore(owner, layout, records, bindings, settings, singleSheetIndex: null);

    public void ShowPreviewSheet(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings,
        int sheetIndex) =>
        ShowPreviewCore(owner, layout, records, bindings, settings, sheetIndex);

    private void ShowPreviewCore(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings,
        int? singleSheetIndex)
    {
        using var document = CreateDocument(layout, records, bindings, settings, singleSheetIndex);
        using var dialog = new PrintPreviewDialog
        {
            Document = document,
            Width = 1180,
            Height = 840,
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
        SequencePrintSettings settings) =>
        PrintCore(owner, layout, records, bindings, settings, singleSheetIndex: null);

    public void PrintSheet(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings,
        int sheetIndex) =>
        PrintCore(owner, layout, records, bindings, settings, sheetIndex);

    private void PrintCore(
        IWin32Window owner,
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings,
        int? singleSheetIndex)
    {
        using var document = CreateDocument(layout, records, bindings, settings, singleSheetIndex);
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
        settings.PaperOrientation = document.DefaultPageSettings.Landscape
            ? SequencePaperOrientation.Landscape
            : SequencePaperOrientation.Portrait;
        document.Print();
    }

    private PrintDocument CreateDocument(
        CardLayout layout,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        IReadOnlyDictionary<int, string> bindings,
        SequencePrintSettings settings,
        int? singleSheetIndex)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(settings);
        if (records.Count == 0) throw new InvalidOperationException("There are no records to impose on the sheet.");
        settings.Validate(layout.WidthMm, layout.HeightMm);

        var sides = settings.Side == LayoutSide.Unknown
            ? new[] { LayoutSide.Front, LayoutSide.Back }
            : new[] { settings.Side };
        var totalSheetCount = SequencePrintPlanner.GetSheetCount(records.Count, settings);
        var firstSheetIndex = singleSheetIndex ?? 0;
        if (firstSheetIndex < 0 || firstSheetIndex >= totalSheetCount)
            throw new ArgumentOutOfRangeException(nameof(singleSheetIndex));
        var lastSheetExclusive = singleSheetIndex.HasValue ? firstSheetIndex + 1 : totalSheetCount;
        var sheetIndex = firstSheetIndex;
        var sideIndex = 0;

        var document = CreateBaseDocument(
            string.IsNullOrWhiteSpace(layout.Name) ? "UltraPrint sequence" : layout.Name + " sequence",
            settings);
        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics;
            if (graphics is null)
            {
                e.HasMorePages = false;
                return;
            }

            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // VB6 Printer coordinates used by the native Report.PrintPage path are
            // measured from the physical page's upper-left corner. PrintDocument's
            // default Graphics origin is the upper-left of the printer's printable
            // area, so subtract the hard margins to restore the legacy coordinate
            // system. The printer driver still clips physically unprintable pixels.
            TranslateToPhysicalPageOrigin(graphics, e.PageSettings);

            var placements = SequencePrintPlanner.GetSheetPlacements(
                records.Count,
                layout.WidthMm,
                layout.HeightMm,
                settings,
                sheetIndex);
            var side = sides[sideIndex];

            foreach (var placement in placements)
            {
                var bound = LegacyRecordBinder.CreateBoundLayout(layout, records[placement.RecordIndex], bindings);
                using var renderer = new LayoutCanvas
                {
                    Layout = bound,
                    Side = side,
                    PreviewMode = true,
                    ShowGrid = false
                };

                var target = MmRectangleToPixels(placement, graphics);
                renderer.RenderTo(graphics, target, side);
                if (settings.DrawCutMarks) DrawCutMarks(graphics, target);
            }

            sideIndex++;
            if (sideIndex >= sides.Length)
            {
                sideIndex = 0;
                sheetIndex++;
            }
            e.HasMorePages = sheetIndex < lastSheetExclusive;
        };

        document.EndPrint += (_, _) =>
        {
            sheetIndex = firstSheetIndex;
            sideIndex = 0;
        };
        return document;
    }

    private PrintDocument CreateBaseDocument(string name, SequencePrintSettings settings)
    {
        var document = new PrintDocument
        {
            DocumentName = name,
            PrinterSettings = (PrinterSettings)_printerSettings.Clone(),
            OriginAtMargins = false
        };
        var pageSettings = (PageSettings)_pageSettings.Clone();
        pageSettings.Landscape = settings.PaperOrientation == SequencePaperOrientation.Landscape;
        document.DefaultPageSettings = pageSettings;
        return document;
    }

    internal static void TranslateToPhysicalPageOrigin(Graphics graphics, PageSettings pageSettings)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(pageSettings);
        var offsetX = pageSettings.HardMarginX / 100f * graphics.DpiX;
        var offsetY = pageSettings.HardMarginY / 100f * graphics.DpiY;
        if (Math.Abs(offsetX) > float.Epsilon || Math.Abs(offsetY) > float.Epsilon)
            graphics.TranslateTransform(-offsetX, -offsetY, MatrixOrder.Append);
    }

    private static RectangleF MmRectangleToPixels(SequenceSlotPlacement placement, Graphics graphics) =>
        new(
            (float)(placement.Xmm / 25.4 * graphics.DpiX),
            (float)(placement.Ymm / 25.4 * graphics.DpiY),
            (float)(placement.WidthMm / 25.4 * graphics.DpiX),
            (float)(placement.HeightMm / 25.4 * graphics.DpiY));

    private static void DrawCutMarks(Graphics graphics, RectangleF card)
    {
        var mark = Math.Max(6f, Math.Min(graphics.DpiX, graphics.DpiY) * 0.06f);
        using var pen = new Pen(Color.Black, Math.Max(1f, Math.Min(graphics.DpiX, graphics.DpiY) / 300f));

        graphics.DrawLine(pen, card.Left - mark, card.Top, card.Left, card.Top);
        graphics.DrawLine(pen, card.Left, card.Top - mark, card.Left, card.Top);
        graphics.DrawLine(pen, card.Right, card.Top - mark, card.Right, card.Top);
        graphics.DrawLine(pen, card.Right, card.Top, card.Right + mark, card.Top);
        graphics.DrawLine(pen, card.Left - mark, card.Bottom, card.Left, card.Bottom);
        graphics.DrawLine(pen, card.Left, card.Bottom, card.Left, card.Bottom + mark);
        graphics.DrawLine(pen, card.Right, card.Bottom, card.Right + mark, card.Bottom);
        graphics.DrawLine(pen, card.Right, card.Bottom, card.Right, card.Bottom + mark);
    }
}
