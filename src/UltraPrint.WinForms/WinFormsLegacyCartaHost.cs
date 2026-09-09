using UltraPrint.Core.Models;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// Bridges the live managed card canvas to the recovered frmCarta ScriptControl facade.
/// The legacy application keeps frmCarta as a global object; this host gives the explicit
/// script workspace the same live CardLayout instance rather than a detached copy.
/// </summary>
internal sealed class WinFormsLegacyCartaHost : ILegacyScriptCartaHost
{
    private readonly LayoutCanvas _canvas;
    private readonly string _campoIniPath;

    public WinFormsLegacyCartaHost(LayoutCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _campoIniPath = ResolveCampoIniPath(canvas.Layout);
    }

    public CardLayout? Layout => _canvas.Layout;

    public LayoutSide CurrentSide => _canvas.Side;

    public LayoutField? SelectedField => _canvas.SelectedField;

    public string CampoIniPath => _campoIniPath;

    public void SelectField(LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_canvas.Side != LayoutSide.Unknown &&
            field.Side is LayoutSide.Front or LayoutSide.Back &&
            field.Side != _canvas.Side)
        {
            _canvas.Side = field.Side;
        }
        _canvas.SelectedField = field;
    }

    public void NotifyLayoutChanged()
    {
        _canvas.InvalidateAssets();
        _canvas.Invalidate();
    }

    public void Redraw()
    {
        _canvas.InvalidateAssets();
        _canvas.Invalidate();
    }

    private static string ResolveCampoIniPath(CardLayout? layout)
    {
        if (!string.IsNullOrWhiteSpace(layout?.SourcePath))
        {
            var layoutDirectory = Path.GetDirectoryName(Path.GetFullPath(layout.SourcePath));
            if (!string.IsNullOrWhiteSpace(layoutDirectory))
            {
                if (string.Equals(Path.GetFileName(layoutDirectory), "LY", StringComparison.OrdinalIgnoreCase))
                {
                    var root = Directory.GetParent(layoutDirectory)?.FullName;
                    if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(root, "Campo.ini");
                }

                var besideLayout = Path.Combine(layoutDirectory, "Campo.ini");
                if (File.Exists(besideLayout)) return besideLayout;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Campo.ini");
    }
}
