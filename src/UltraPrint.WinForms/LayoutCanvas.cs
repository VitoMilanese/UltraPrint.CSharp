using System.Drawing.Drawing2D;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.WinForms;

public sealed class LayoutCanvas : Control
{
    private readonly Dictionary<string, Image> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private CardLayout? _layout;
    private LayoutSide _side = LayoutSide.Front;
    private LayoutField? _selectedField;
    private bool _dragging;
    private Point _lastMouse;

    public LayoutCanvas()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.ControlDark;
        Cursor = Cursors.Default;
        SetStyle(ControlStyles.ResizeRedraw, true);
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
            if (_selectedField is not null && !IsVisibleOnCurrentSide(_selectedField))
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

    public event EventHandler? SelectedFieldChanged;
    public event EventHandler? FieldChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_layout is null || _layout.WidthMm <= 0 || _layout.HeightMm <= 0)
        {
            using var brush = new SolidBrush(SystemColors.ControlText);
            e.Graphics.DrawString("Open an UltraPrint .ly file", Font, brush, new PointF(16, 16));
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var (card, scale) = GetCardGeometry();
        using (var shadow = new SolidBrush(Color.FromArgb(70, Color.Black)))
            e.Graphics.FillRectangle(shadow, card.X + 5, card.Y + 5, card.Width, card.Height);
        e.Graphics.FillRectangle(Brushes.White, card);

        foreach (var field in VisibleFields().OrderBy(x => x.Level).ThenBy(x => x.Index))
            DrawField(e.Graphics, field, card, scale);

        e.Graphics.DrawRectangle(Pens.DimGray, card.X, card.Y, card.Width, card.Height);

        if (_selectedField is not null && IsVisibleOnCurrentSide(_selectedField))
        {
            var r = FieldRectangle(_selectedField, card, scale);
            using var pen = new Pen(Color.DodgerBlue, 2) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            DrawHandles(e.Graphics, r);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _layout is null) return;
        Focus();

        var (card, scale) = GetCardGeometry();
        var hit = VisibleFields()
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Index)
            .FirstOrDefault(field => FieldRectangle(field, card, scale).Contains(e.Location));

        SelectedField = hit;
        if (hit is not null)
        {
            _dragging = true;
            _lastMouse = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _selectedField is null || _layout is null) return;
        var (_, scale) = GetCardGeometry();
        if (scale <= 0) return;

        var dx = (e.X - _lastMouse.X) / scale;
        var dy = (e.Y - _lastMouse.Y) / scale;
        _selectedField.Xmm += dx;
        _selectedField.Ymm += dy;
        _lastMouse = e.Location;
        FieldChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearImageCache();
        base.Dispose(disposing);
    }

    private IEnumerable<LayoutField> VisibleFields()
    {
        if (_layout is null) return Enumerable.Empty<LayoutField>();
        return _layout.Fields.Where(IsVisibleOnCurrentSide);
    }

    private bool IsVisibleOnCurrentSide(LayoutField field) =>
        _side == LayoutSide.Unknown || field.Side == LayoutSide.Unknown || field.Side == _side;

    private (RectangleF Card, float Scale) GetCardGeometry()
    {
        if (_layout is null) return (RectangleF.Empty, 1);
        const float margin = 28;
        var availableWidth = Math.Max(1, ClientSize.Width - margin * 2);
        var availableHeight = Math.Max(1, ClientSize.Height - margin * 2);
        var scale = (float)Math.Min(availableWidth / _layout.WidthMm, availableHeight / _layout.HeightMm);
        var width = (float)(_layout.WidthMm * scale);
        var height = (float)(_layout.HeightMm * scale);
        var x = (ClientSize.Width - width) / 2f;
        var y = (ClientSize.Height - height) / 2f;
        return (new RectangleF(x, y, width, height), scale);
    }

    private static RectangleF FieldRectangle(LayoutField field, RectangleF card, float scale) =>
        new(
            card.X + (float)(field.Xmm * scale),
            card.Y + (float)(field.Ymm * scale),
            Math.Max(1, (float)(field.WidthMm * scale)),
            Math.Max(1, (float)(field.HeightMm * scale)));

    private void DrawField(Graphics g, LayoutField field, RectangleF card, float scale)
    {
        var rect = FieldRectangle(field, card, scale);
        switch (field.Kind)
        {
            case LayoutFieldKind.Image:
                DrawImageField(g, field, rect);
                break;
            case LayoutFieldKind.Text:
                DrawTextField(g, field, rect, scale);
                break;
            default:
                DrawUnknownField(g, field, rect);
                break;
        }
    }

    private void DrawImageField(Graphics g, LayoutField field, RectangleF rect)
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
                    DrawAspectFit(g, image, rect);
                else
                    g.DrawImage(image, rect);
                return;
            }
        }

        using var pen = new Pen(Color.Gray, 1) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        var label = string.IsNullOrWhiteSpace(field.Name) ? "Image" : field.Name;
        using var font = new Font(Font.FontFamily, 8f, FontStyle.Regular);
        using var brush = new SolidBrush(Color.DimGray);
        g.DrawString(label, font, brush, rect);
    }

    private static void DrawAspectFit(Graphics g, Image image, RectangleF target)
    {
        var scale = Math.Min(target.Width / image.Width, target.Height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        var x = target.X + (target.Width - w) / 2;
        var y = target.Y + (target.Height - h) / 2;
        g.DrawImage(image, x, y, w, h);
    }

    private void DrawTextField(Graphics g, LayoutField field, RectangleF rect, float scale)
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

            var state = g.Save();
            if (field.Appearance.RotationDegrees != 0)
            {
                g.TranslateTransform(rect.X, rect.Y);
                g.RotateTransform(field.Appearance.RotationDegrees);
                g.DrawString(content, drawingFont, brush, new RectangleF(0, 0, rect.Width, rect.Height), format);
            }
            else
            {
                g.DrawString(content, drawingFont, brush, rect, format);
            }
            g.Restore(state);
        }
    }

    private static void DrawUnknownField(Graphics g, LayoutField field, RectangleF rect)
    {
        using var pen = new Pen(Color.DarkOrange, 1) { DashStyle = DashStyle.Dot };
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
        using var brush = new SolidBrush(Color.DarkOrange);
        g.DrawString($"{field.Name} [type {field.LegacyTypeCode}]", font, brush, rect);
    }

    private static void DrawHandles(Graphics g, RectangleF r)
    {
        const float size = 6;
        foreach (var p in new[]
        {
            new PointF(r.Left, r.Top), new PointF(r.Right, r.Top),
            new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom)
        })
        {
            g.FillRectangle(Brushes.White, p.X - size / 2, p.Y - size / 2, size, size);
            g.DrawRectangle(Pens.DodgerBlue, p.X - size / 2, p.Y - size / 2, size, size);
        }
    }

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
        // Standard COLORREF values are 0x00BBGGRR. System OLE colors have the high bit set;
        // leave those to a neutral fallback until their exact VB6 mapping is required.
        if ((ole & unchecked((int)0x80000000)) != 0) return fallback;
        var r = ole & 0xFF;
        var g = (ole >> 8) & 0xFF;
        var b = (ole >> 16) & 0xFF;
        return Color.FromArgb(r, g, b);
    }
}
