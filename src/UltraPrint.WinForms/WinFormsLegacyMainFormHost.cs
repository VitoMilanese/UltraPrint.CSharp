using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// Connects the recovered script-visible Mainform facade to the live WinForms application.
/// Device-specific printer behavior remains a later compatibility layer; StampaRecord currently
/// uses the same standard PrintDocument renderer as the managed editor, without showing a dialog.
/// </summary>
internal sealed class WinFormsLegacyMainFormHost : ILegacyScriptMainFormHost
{
    private readonly MainForm _form;
    private readonly LayoutCanvas? _canvas;
    private readonly LayoutPrintService _printService = new();

    public WinFormsLegacyMainFormHost(MainForm form, LayoutCanvas? canvas)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _canvas = canvas;
    }

    public void PrintCurrentRecord()
    {
        var layout = _canvas?.Layout
            ?? throw new InvalidOperationException("Mainform.StampaRecord requires an open layout.");
        _printService.PrintDirect(layout, _canvas!.Side, _canvas);
    }

    public void ReloadBackground()
    {
        if (_canvas is null) return;
        _canvas.InvalidateAssets();
        _canvas.Invalidate();
    }

    public void TerminateApplication()
    {
        if (_form.IsDisposed || _form.Disposing) return;
        if (_form.InvokeRequired)
        {
            _form.BeginInvoke((Action)TerminateApplication);
            return;
        }

        // Defer Close until the current ScriptControl callback has unwound, mirroring the old
        // application's habit of completing the script dispatch before tearing down the UI.
        _form.BeginInvoke((Action)_form.Close);
    }
}
