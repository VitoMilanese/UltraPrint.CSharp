using System.Drawing.Drawing2D;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.WinForms;

public sealed class LayoutCanvas : Control
{
    private const float HandleSize = 8f;
    private const double MinimumFieldSizeMm = 0.5;

    private readonly Dictionary<string, Image> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private CardLayout? _layout;
    private LayoutSide _side = LayoutSide.Front;
    private LayoutField? _selectedField;
    private DragMode _dragMode;
    private Point _lastMouse;
    private bool _showGrid = true;
    private bool _snapToGrid;
    private bool _previewMode;
    private double _gridSizeMm = 1.0;

    public LayoutCanvas()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.ControlDark;
        Cursor = Cursors.Default;
        TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    public CardLayout? Layout
    {
        get => _layout;
        set
        {
            if (ReferenceEquals(_layout, value)) return;
            _layout = value;
            _selectedField = null;
            ClearImageCache();
            Invalidate();
        }
    }

    public LayoutSide Side
    {
        get => _side;
        set
        {
            if (_side == value) return;
            _side = value;
            if (_selectedField is not null && !IsVisibleOnSide(_selectedField, _side))
                SelectedField = null;
            Invalidate();
        }
    }

    public LayoutField? SelectedField
    {
        get => _selectedField;
        set
        {
            if (ReferenceEquals(_selectedField, value)) return;
            _selectedField = value;
            SelectedFieldChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value) return;
            _showGrid = value;
            Invalidate();
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => _snapToGrid = value;
    }

    public bool PreviewMode
    {
        get => _previewMode;
        set
        {
            if (_previewMode == value) return;
            _previewMode = value;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    public double GridSizeMm
    {
        get => _gridSizeMm;
        set => _gridSizeMm = Math.Clamp(value, 0.1, 10.0);
    }

    public event EventHandler? SelectedFieldChanged;
    public event EventHandler? FieldChanged;

    /// <summary>
    /// Renders the current layout into an arbitrary pixel rectangle. This is shared by print/
    /// preview so screen and printer use the same recovered geometry and asset resolution rules.
    /// </summary>
    public void RenderTo(Graphics graphics, RectangleF targetPixels, LayoutSide side)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (_layout is null || _layout.WidthMm <= 0 || _layout.HeightMm <= 0) return;

        var card = FitCardInTarget(targetPixels);
        var scale = card.Width / (float)_layout.WidthMm;
        ConfigureGraphics(graphics);
        DrawLayout(graphics, card, scale, side, editorOverlays: false);
    }

    public void InvalidateAssets()
    {
        ClearImageCache();
        Invalidate();
    }

    /// <summary>
    /// Lets field-specific editors participate in the same dirty-state/property refresh pipeline
    /// used by drag/resize edits without reaching into MainForm private state.
    /// </summary>
    public void NotifyFieldEdited()
    {
        FieldChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_layout is null || _layout.WidthMm <= 0 || _layout.HeightMm <= 0)
        {
            using var brush = new SolidBrush(SystemColors.ControlText);
            e.Graphics.DrawString("Open an UltraPrint .ly file", Font, brush, new PointF(16, 16));
            return;
        }

        ConfigureGraphics(e.Graphics);
        var (card, scale) = GetCardGeometry();
        using (var shadow = new SolidBrush(Color.FromArgb(70, Color.Black)))
            e.Graphics.FillRectangle(shadow, card.X + 5, card.Y + 5, card.Width, card.Height);

        DrawLayout(e.Graphics, card, scale, _side, editorOverlays: !_previewMode);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _layout is null || _previewMode) return;
        Focus();

        var (card, scale) = GetCardGeometry();
        if (_selectedField is not null && IsVisibleOnSide(_selectedField, _side))
        {
            var selectedRect = FieldRectangle(_selectedField, card, scale);
            var handle = HitTestHandle(selectedRect, e.Location);
            if (handle != DragMode.None)
            {
                _dragMode = handle;
                _lastMouse = e.Location;
                Capture = true;
                Cursor = CursorForDragMode(handle);
                return;
            }
        }

        var hit = FieldsForSide(_side)
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Index)
            .FirstOrDefault(field => FieldRectangle(field, card, scale).Contains(e.Location));

        SelectedField = hit;
        if (hit is not null)
        {
            _dragMode = DragMode.Move;
            _lastMouse = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_previewMode || _layout is null) return;

        if (_dragMode != DragMode.None && _selectedField is not null)
        {
            var (_, scale) = GetCardGeometry();
            if (scale <= 0) return;

            var dx = (e.X - _lastMouse.X) / scale;
            var dy = (e.Y - _lastMouse.Y) / scale;
            ApplyDrag(_selectedField, _dragMode, dx, dy);
            _lastMouse = e.Location;
            FieldChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        Cursor = HitTestCursor(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragMode = DragMode.None;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_previewMode || _selectedField is null) return;

        var step = e.Control ? 0.01 : e.Shift ? 1.0 : 0.1;
        var changed = true;
        switch (e.KeyCode)
        {
            case Keys.Left:
                _selectedField.Xmm -= step;
                break;
            case Keys.Right:
                _selectedField.Xmm += step;
                break;
            case Keys.Up:
                _selectedField.Ymm -= step;
                break;
            case Keys.Down:
                _selectedField.Ymm += step;
                break;
            default:
                changed = false;
                break;
        }

        if (!changed) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        FieldChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearImageCache();
        base.Dispose(disposing);
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
    }

    private void DrawLayout(Graphics graphics, RectangleF card, float scale, LayoutSide side, bool editorOverlays)
    {
        graphics.FillRectangle(Brushes.White, card);

        if (editorOverlays && _showGrid)
            DrawGrid(graphics, card, scale);

        foreach (var field in FieldsForSide(side).OrderBy(x => x.Level).ThenBy(x => x.Index))
            DrawField(graphics, field, card, scale);

        graphics.DrawRectangle(Pens.DimGray, card.X, card.Y, card.Width, card.Height);

        if (editorOverlays && _selectedField is not null && IsVisibleOnSide(_selectedField, side))
        {
            var rect = FieldRectangle(_selectedField, card, scale);
            using var pen = new Pen(Color.DodgerBlue, 2) { DashStyle = DashStyle.Dash };
            graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            DrawHandles(graphics, rect);
        }
    }

    private IEnumerable<LayoutField> FieldsForSide(LayoutSide side)
    {
        if (_layout is null) return Enumerable.Empty<LayoutField>();
        return _layout.Fields.Where(field => IsVisibleOnSide(field, side));
    }

    private static bool IsVisibleOnSide(LayoutField field, LayoutSide side) =>
        side == LayoutSide.Unknown || field.Side == LayoutSide.Unknown || field.Side == side;

    private (RectangleF Card, float Scale) GetCardGeometry()
    {
        if (_layout is null) return (RectangleF.Empty, 1);
        const float margin = 28;
        var target = new RectangleF(
            margin,
            margin,
            Math.Max(1, ClientSize.Width - margin * 2),
            Math.Max(1, ClientSize.Height - margin * 2));
        var card = FitCardInTarget(target);
        return (card, card.Width / (float)_layout.WidthMm);
    }

    private RectangleF FitCardInTarget(RectangleF target)
    {
        if (_layout is null) return RectangleF.Empty;
        var scale = (float)Math.Min(target.Width / _layout.WidthMm, target.Height / _layout.HeightMm);
        var width = (float)(_layout.WidthMm * scale);
        var height = (float)(_layout.HeightMm * scale);
        var x = target.Left + (target.Width - width) / 2f;
        var y = target.Top + (target.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }

    private static RectangleF FieldRectangle(LayoutField field, RectangleF card, float scale) =>
        new(
            card.X + (float)(field.Xmm * scale),
            card.Y + (float)(field.Ymm * scale),
            Math.Max(1, (float)(field.WidthMm * scale)),
            Math.Max(1, (float)(field.HeightMm * scale)));

    private void DrawGrid(Graphics graphics, RectangleF card, float scale)
    {
        if (_layout is null || _gridSizeMm <= 0) return;
        using var minorPen = new Pen(Color.FromArgb(28, Color.Black), 1);
        using var majorPen = new Pen(Color.FromArgb(55, Color.Black), 1);

        var step = Math.Max(0.1, _gridSizeMm);
        for (var x = step; x < _layout.WidthMm; x += step)
        {
            var px = card.Left + (float)(x * scale);
            var isMajor = Math.Abs(x / 5.0 - Math.Round(x / 5.0)) < 0.0001;
            graphics.DrawLine(isMajor ? majorPen : minorPen, px, card.Top, px, card.Bottom);
        }

        for (var y = step; y < _layout.HeightMm; y += step)
        {
            var py = card.Top + (float)(y * scale);
            var isMajor = Math.Abs(y / 5.0 - Math.Round(y / 5.0)) < 0.0001;
            graphics.DrawLine(isMajor ? majorPen : minorPen, card.Left, py, card.Right, py);
        }
    }

    private void DrawField(Graphics graphics, LayoutField field, RectangleF card, float scale)
    {
        var rect = FieldRectangle(field, card, scale);
        if (field.Appearance.Opaque)
        {
            using var background = new SolidBrush(OleToColor(field.Appearance.BackColorOle, Color.White));
            graphics.FillRectangle(background, rect);
        }

        switch (field.Kind)
        {
            case LayoutFieldKind.Image:
                DrawImageField(graphics, field, rect);
                break;
            case LayoutFieldKind.Text:
                DrawTextField(graphics, field, rect, scale);
                break;
            case LayoutFieldKind.Barcode:
                DrawBarcodeField(graphics, field, rect, scale);
                break;
            case LayoutFieldKind.Table:
                DrawTableField(graphics, field, rect);
                break;
            default:
                DrawUnknownField(graphics, field, rect);
                break;
        }

        if (field.Appearance.Border)
        {
            var width = Math.Max(1, field.Appearance.BorderWidth);
            using var border = new Pen(OleToColor(field.Appearance.BorderColorOle, Color.Black), width);
            graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private void DrawImageField(Graphics graphics, LayoutField field, RectangleF rect)
    {
        var storedPath = field.Image.File;
        var resolved = _layout?.SourcePath is { Length: > 0 } source
            ? LegacyAssetResolver.Resolve(source, storedPath)
            : null;

        if (resolved is not null)
        {
            var image = GetCachedImage(resolved);
            if (image is not null)
            {
                if (field.Image.KeepAspectRatio)
                    DrawAspectFit(graphics, image, rect);
                else
                    graphics.DrawImage(image, rect);
                return;
            }
        }

        using var pen = new Pen(Color.Gray, 1) { DashStyle = DashStyle.Dash };
        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        var label = string.IsNullOrWhiteSpace(field.Name) ? "Image" : field.Name;
        using var font = new Font(Font.FontFamily, 8f, FontStyle.Regular);
        using var brush = new SolidBrush(Color.DimGray);
        graphics.DrawString(label, font, brush, rect);
    }

    private static void DrawAspectFit(Graphics graphics, Image image, RectangleF target)
    {
        var scale = Math.Min(target.Width / image.Width, target.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var x = target.X + (target.Width - width) / 2;
        var y = target.Y + (target.Height - height) / 2;
        graphics.DrawImage(image, x, y, width, height);
    }

    private void DrawTextField(Graphics graphics, LayoutField field, RectangleF rect, float scale)
    {
        var content = string.IsNullOrEmpty(field.Text.Content) ? field.Name : field.Text.Content;
        if (string.IsNullOrEmpty(content)) return;

        var style = FontStyle.Regular;
        if (field.Text.Bold) style |= FontStyle.Bold;
        if (field.Text.Italic) style |= FontStyle.Italic;
        if (field.Text.Strikeout) style |= FontStyle.Strikeout;

        var pixelSize = Math.Max(5f, (float)(field.Text.FontSize * 25.4 / 72.0 * scale));
        Font drawingFont;
        try { drawingFont = new Font(field.Text.FontName, pixelSize, style, GraphicsUnit.Pixel); }
        catch { drawingFont = new Font(Font.FontFamily, pixelSize, style, GraphicsUnit.Pixel); }

        using (drawingFont)
        using (var brush = new SolidBrush(OleToColor(field.Appearance.ForeColorOle, Color.Black)))
        using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            format.Alignment = field.Text.Alignment switch
            {
                TextAlignment.Center => StringAlignment.Center,
                TextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Near
            };
            format.LineAlignment = StringAlignment.Near;

            var state = graphics.Save();
            if (field.Appearance.RotationDegrees != 0)
            {
                graphics.TranslateTransform(rect.X, rect.Y);
                graphics.RotateTransform(field.Appearance.RotationDegrees);
                graphics.DrawString(content, drawingFont, brush, new RectangleF(0, 0, rect.Width, rect.Height), format);
            }
            else
            {
                graphics.DrawString(content, drawingFont, brush, rect, format);
            }
            graphics.Restore(state);
        }
    }

    private void DrawBarcodeField(Graphics graphics, LayoutField field, RectangleF rect, float scale)
    {
        if (string.IsNullOrEmpty(field.LegacyPayload)) return;

        LegacyBarcodeFormatter.TryFormat(field.Text.FontName, field.LegacyPayload, out var glyphs);
        if (glyphs.Length == 0) return;

        var pixelSize = Math.Max(5f, (float)(field.Text.FontSize * 25.4 / 72.0 * scale));
        Font drawingFont;
        try
        {
            drawingFont = new Font(field.Text.FontName, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch
        {
            drawingFont = new Font(Font.FontFamily, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        using (drawingFont)
        using (var brush = new SolidBrush(OleToColor(field.Appearance.ForeColorOle, Color.Black)))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            var state = graphics.Save();
            if (field.Appearance.RotationDegrees != 0)
            {
                graphics.TranslateTransform(rect.X, rect.Y);
                graphics.RotateTransform(field.Appearance.RotationDegrees);
                graphics.DrawString(glyphs, drawingFont, brush,
                    new RectangleF(0, 0, rect.Width, rect.Height), format);
            }
            else
            {
                graphics.DrawString(glyphs, drawingFont, brush, rect, format);
            }
            graphics.Restore(state);
        }
    }

    private static void DrawTableField(Graphics graphics, LayoutField field, RectangleF rect)
    {
        var rows = Math.Max(1, field.Table.Rows);
        var columns = Math.Max(1, field.Table.Columns);
        using var pen = new Pen(OleToColor(field.Appearance.BorderColorOle, Color.Black), 1);
        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

        if (!field.Table.Grid) return;
        for (var row = 1; row < rows; row++)
        {
            var y = rect.Top + rect.Height * row / rows;
            graphics.DrawLine(pen, rect.Left, y, rect.Right, y);
        }
        for (var column = 1; column < columns; column++)
        {
            var x = rect.Left + rect.Width * column / columns;
            graphics.DrawLine(pen, x, rect.Top, x, rect.Bottom);
        }
    }

    private static void DrawUnknownField(Graphics graphics, LayoutField field, RectangleF rect)
    {
        using var pen = new Pen(Color.DarkOrange, 1) { DashStyle = DashStyle.Dot };
        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
        using var brush = new SolidBrush(Color.DarkOrange);
        graphics.DrawString($"{field.Name} [type {field.LegacyTypeCode}]", font, brush, rect);
    }

    private static void DrawHandles(Graphics graphics, RectangleF rect)
    {
        foreach (var point in HandleCenters(rect))
        {
            graphics.FillRectangle(Brushes.White, point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize);
            graphics.DrawRectangle(Pens.DodgerBlue, point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize);
        }
    }

    private static PointF[] HandleCenters(RectangleF rect) =>
    [
        new(rect.Left, rect.Top),
        new(rect.Right, rect.Top),
        new(rect.Left, rect.Bottom),
        new(rect.Right, rect.Bottom)
    ];

    private static DragMode HitTestHandle(RectangleF rect, Point point)
    {
        var handles = HandleCenters(rect);
        var modes = new[] { DragMode.ResizeTopLeft, DragMode.ResizeTopRight, DragMode.ResizeBottomLeft, DragMode.ResizeBottomRight };
        for (var i = 0; i < handles.Length; i++)
        {
            var handle = handles[i];
            var hit = new RectangleF(handle.X - HandleSize, handle.Y - HandleSize, HandleSize * 2, HandleSize * 2);
            if (hit.Contains(point)) return modes[i];
        }
        return DragMode.None;
    }

    private Cursor HitTestCursor(Point point)
    {
        if (_selectedField is not null && IsVisibleOnSide(_selectedField, _side))
        {
            var (card, scale) = GetCardGeometry();
            var rect = FieldRectangle(_selectedField, card, scale);
            var handle = HitTestHandle(rect, point);
            if (handle != DragMode.None) return CursorForDragMode(handle);
            if (rect.Contains(point)) return Cursors.SizeAll;
        }
        return Cursors.Default;
    }

    private static Cursor CursorForDragMode(DragMode mode) => mode switch
    {
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
        DragMode.Move => Cursors.SizeAll,
        _ => Cursors.Default
    };

    private void ApplyDrag(LayoutField field, DragMode mode, double dx, double dy)
    {
        var x = field.Xmm;
        var y = field.Ymm;
        var width = field.WidthMm;
        var height = field.HeightMm;

        switch (mode)
        {
            case DragMode.Move:
                x += dx;
                y += dy;
                break;
            case DragMode.ResizeTopLeft:
                x += dx;
                y += dy;
                width -= dx;
                height -= dy;
                break;
            case DragMode.ResizeTopRight:
                y += dy;
                width += dx;
                height -= dy;
                break;
            case DragMode.ResizeBottomLeft:
                x += dx;
                width -= dx;
                height += dy;
                break;
            case DragMode.ResizeBottomRight:
                width += dx;
                height += dy;
                break;
        }

        if (width < MinimumFieldSizeMm)
        {
            if (mode is DragMode.ResizeTopLeft or DragMode.ResizeBottomLeft)
                x -= MinimumFieldSizeMm - width;
            width = MinimumFieldSizeMm;
        }
        if (height < MinimumFieldSizeMm)
        {
            if (mode is DragMode.ResizeTopLeft or DragMode.ResizeTopRight)
                y -= MinimumFieldSizeMm - height;
            height = MinimumFieldSizeMm;
        }

        if (_snapToGrid)
        {
            x = Snap(x);
            y = Snap(y);
            width = Math.Max(MinimumFieldSizeMm, Snap(width));
            height = Math.Max(MinimumFieldSizeMm, Snap(height));
        }

        field.Xmm = x;
        field.Ymm = y;
        field.WidthMm = width;
        field.HeightMm = height;
    }

    private double Snap(double value) => Math.Round(value / _gridSizeMm) * _gridSizeMm;

    private Image? GetCachedImage(string path)
    {
        if (_imageCache.TryGetValue(path, out var image)) return image;
        try
        {
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream);
            image = new Bitmap(source);
            _imageCache[path] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void ClearImageCache()
    {
        foreach (var image in _imageCache.Values) image.Dispose();
        _imageCache.Clear();
    }

    private static Color OleToColor(int ole, Color fallback)
    {
        try { return ColorTranslator.FromOle(ole); }
        catch { return fallback; }
    }

    private enum DragMode
    {
        None,
        Move,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }
}
