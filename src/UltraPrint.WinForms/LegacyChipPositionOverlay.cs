namespace UltraPrint.WinForms;

/// <summary>
/// Transparent, mouse-pass-through child overlay that reproduces frmCarta's recovered chip
/// position marker without entering LayoutCanvas.RenderTo(). That keeps it strictly editor-only.
/// </summary>
internal sealed class LegacyChipPositionOverlay : Control
{
    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);

    private readonly LayoutCanvas _canvas;
    private bool _requestedVisible;

    public LegacyChipPositionOverlay(LayoutCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        Visible = false;

        _canvas.Controls.Add(this);
        _canvas.Resize += (_, _) => RefreshPlacement();
        _canvas.Invalidated += (_, _) => RefreshPlacement();
        RefreshPlacement();
    }

    public bool RequestedVisible
    {
        get => _requestedVisible;
        set
        {
            if (_requestedVisible == value) return;
            _requestedVisible = value;
            RefreshPlacement();
        }
    }

    private void RefreshPlacement()
    {
        if (IsDisposed || _canvas.IsDisposed) return;

        var layout = _canvas.Layout;
        if (!_requestedVisible || _canvas.PreviewMode || layout is null || layout.WidthMm <= 0 || layout.HeightMm <= 0)
        {
            if (Visible) Visible = false;
            return;
        }

        const float margin = 28f;
        var targetWidth = Math.Max(1f, _canvas.ClientSize.Width - margin * 2f);
        var targetHeight = Math.Max(1f, _canvas.ClientSize.Height - margin * 2f);
        var scale = (float)Math.Min(targetWidth / layout.WidthMm, targetHeight / layout.HeightMm);
        if (!float.IsFinite(scale) || scale <= 0)
        {
            if (Visible) Visible = false;
            return;
        }

        var cardWidth = (float)(layout.WidthMm * scale);
        var cardHeight = (float)(layout.HeightMm * scale);
        var cardLeft = margin + (targetWidth - cardWidth) / 2f;
        var cardTop = margin + (targetHeight - cardHeight) / 2f;

        var bounds = UltraPrint.Legacy.Devices.LegacyChipPositionSemantics.GetLogicalBounds();
        var requestedBounds = new Rectangle(
            (int)Math.Round(cardLeft + bounds.LeftMm * scale),
            (int)Math.Round(cardTop + bounds.TopMm * scale),
            Math.Max(1, (int)Math.Round(bounds.WidthMm * scale)),
            Math.Max(1, (int)Math.Round(bounds.HeightMm * scale)));

        if (Bounds != requestedBounds)
            Bounds = requestedBounds;

        if (!Visible)
        {
            Visible = true;
            BringToFront();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var fill = new SolidBrush(Color.FromArgb(65, Color.DimGray));
        using var pen = new Pen(Color.DimGray, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var rect = new RectangleF(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        e.Graphics.FillRectangle(fill, rect);
        e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            m.Result = HtTransparent;
            return;
        }
        base.WndProc(ref m);
    }
}
