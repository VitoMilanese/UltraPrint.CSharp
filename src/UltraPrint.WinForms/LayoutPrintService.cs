using System.Drawing.Printing;
using UltraPrint.Core.Models;

namespace UltraPrint.WinForms;

/// <summary>
/// Normal Windows print/preview path for the recovered layout. Device-specific card-printer,
/// magnetic-stripe and smart-card APIs are separate compatibility layers; this service covers the
/// standard GDI/PrintDocument path already exposed by the original MainForm print commands.
/// </summary>
public sealed class LayoutPrintService
{
    private PrinterSettings _printerSettings;
    private PageSettings _pageSettings;

    public LayoutPrintService()
    {
        using var document = new PrintDocument();
        _printerSettings = (PrinterSettings)document.PrinterSettings.Clone();
        _pageSettings = (PageSettings)document.DefaultPageSettings.Clone();
    }

    public void ShowPageSetup(IWin32Window owner)
    {
        using var document = CreateBaseDocument("UltraPrint page setup");
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
    }

    public void ShowPreview(IWin32Window owner, CardLayout layout, LayoutSide side, LayoutCanvas renderer)
    {
        using var document = CreateLayoutDocument(layout, side, renderer);
        using var dialog = new PrintPreviewDialog
        {
            Document = document,
            Width = 1100,
            Height = 800,
            StartPosition = FormStartPosition.CenterParent,
            UseAntiAlias = true
        };
        dialog.ShowDialog(owner);
    }

    public void Print(IWin32Window owner, CardLayout layout, LayoutSide side, LayoutCanvas renderer)
    {
        using var document = CreateLayoutDocument(layout, side, renderer);
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

    /// <summary>
    /// Prints with the currently selected/default Windows printer without presenting a dialog.
    /// This is the closest managed equivalent of the script-callable MainForm.StampaRecord path;
    /// device-specific card-printer handling is layered separately as it is recovered.
    /// </summary>
    public void PrintDirect(CardLayout layout, LayoutSide side, LayoutCanvas renderer)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(renderer);
        using var document = CreateLayoutDocument(layout, side, renderer);
        document.Print();
    }

    private PrintDocument CreateLayoutDocument(CardLayout layout, LayoutSide side, LayoutCanvas renderer)
    {
        var document = CreateBaseDocument(string.IsNullOrWhiteSpace(layout.Name) ? "UltraPrint layout" : layout.Name);
        var sides = side == LayoutSide.Unknown
            ? new[] { LayoutSide.Front, LayoutSide.Back }
            : new[] { side };
        var pageIndex = 0;

        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics;
            if (graphics is null)
            {
                e.HasMorePages = false;
                return;
            }

            // The original UltraPrint preview/standard print path places the card at the
            // upper-left paper origin rather than centering it inside MarginBounds. Keep the
            // physical legacy card size and let the printer's hard printable region clip only
            // what the actual device cannot reach.
            graphics.PageUnit = GraphicsUnit.Pixel;
            var pageLeft = e.PageBounds.Left * graphics.DpiX / 100f;
            var pageTop = e.PageBounds.Top * graphics.DpiY / 100f;
            var cardWidth = (float)(layout.WidthMm / 25.4 * graphics.DpiX);
            var cardHeight = (float)(layout.HeightMm / 25.4 * graphics.DpiY);

            renderer.RenderTo(
                graphics,
                new RectangleF(pageLeft, pageTop, cardWidth, cardHeight),
                sides[pageIndex]);

            pageIndex++;
            e.HasMorePages = pageIndex < sides.Length;
        };

        document.EndPrint += (_, _) => pageIndex = 0;
        return document;
    }

    private PrintDocument CreateBaseDocument(string documentName)
    {
        var document = new PrintDocument
        {
            DocumentName = documentName,
            PrinterSettings = (PrinterSettings)_printerSettings.Clone()
        };
        document.DefaultPageSettings = (PageSettings)_pageSettings.Clone();
        return document;
    }
}
