using UltraPrint.Core.Models;

namespace UltraPrint.WinForms;

/// <summary>
/// Interactive managed replacement for the legacy Sequenza page/grid preview.
/// Clicking a slot on the first logical sheet chooses where record 1 starts.
/// </summary>
internal sealed class SequenceSheetPreviewControl : Control
{
    private readonly List<(int Slot, RectangleF Bounds)> _slotBounds = new();
    private CardLayout? _layout;
    private SequencePrintSettings? _settings;
    private double _pageWidthMm = 210;
    private double _pageHeightMm = 297;
    private int _recordCount;
    private int _sheetIndex;

    public SequenceSheetPreviewControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.ControlDark;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public CardLayout? Layout
    {
        get => _layout;
        set { _layout = value; Invalidate(); }
    }

    public SequencePrintSettings? Settings
    {
        get => _settings;
        set { _settings = value; Invalidate(); }
    }

    public double PageWidthMm
    {
        get => _pageWidthMm;
        set { _pageWidthMm = Math.Max(1, value); Invalidate(); }
    }

    public double PageHeightMm
    {
        get => _pageHeightMm;
        set { _pageHeightMm = Math.Max(1, value); Invalidate(); }
    }

    public int RecordCount
    {
        get => _recordCount;
        set { _recordCount = Math.Max(0, value); Invalidate(); }
    }

    public int SheetIndex
    {
        get => _sheetIndex;
        set { _sheetIndex = Math.Max(0, value); Invalidate(); }
    }

    public event Action<int>? StartSlotSelected;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _slotBounds.Clear();
        if (_layout is null || _settings is null) return;

        const float padding = 18;
        var scale = Math.Min(
            (ClientSize.Width - padding * 2) / (float)_pageWidthMm,
            (ClientSize.Height - padding * 2) / (float)_pageHeightMm);
        if (scale <= 0) return;

        var pageWidth = (float)(_pageWidthMm * scale);
        var pageHeight = (float)(_pageHeightMm * scale);
        var page = new RectangleF(
            (ClientSize.Width - pageWidth) / 2f,
            (ClientSize.Height - pageHeight) / 2f,
            pageWidth,
            pageHeight);

        e.Graphics.FillRectangle(SystemBrushes.Window, page);
        e.Graphics.DrawRectangle(SystemPens.ControlDarkDark, page.X, page.Y, page.Width, page.Height);

        IReadOnlyList<SequenceSlotPlacement> placements = Array.Empty<SequenceSlotPlacement>();
        var sheetCount = SequencePrintPlanner.GetSheetCount(_recordCount, _settings);
        if (_recordCount > 0 && _sheetIndex < sheetCount)
            placements = SequencePrintPlanner.GetSheetPlacements(
                _recordCount, _layout.WidthMm, _layout.HeightMm, _settings, _sheetIndex);
        var placementBySlot = placements.ToDictionary(x => x.SlotIndex);
        var pitchX = _settings.EffectiveHorizontalPitchMm(_layout.WidthMm);
        var pitchY = _settings.EffectiveVerticalPitchMm(_layout.HeightMm);

        for (var slot = 0; slot < _settings.Capacity; slot++)
        {
            var row = slot / _settings.Columns;
            var column = slot % _settings.Columns;
            var r = new RectangleF(
                page.X + (float)((_settings.MarginLeftMm + column * pitchX) * scale),
                page.Y + (float)((_settings.MarginTopMm + row * pitchY) * scale),
                (float)(_layout.WidthMm * scale),
                (float)(_layout.HeightMm * scale));
            _slotBounds.Add((slot, r));

            var isStart = _sheetIndex == 0 && slot == _settings.StartSlot;
            var occupied = placementBySlot.TryGetValue(slot, out var placement);
            if (isStart)
                e.Graphics.FillRectangle(SystemBrushes.Highlight, r);
            else if (occupied)
                e.Graphics.FillRectangle(SystemBrushes.ControlLight, r);
            else
                e.Graphics.FillRectangle(SystemBrushes.Window, r);

            e.Graphics.DrawRectangle(SystemPens.ControlDarkDark, r.X, r.Y, r.Width, r.Height);
            var label = occupied ? $"{slot + 1}\nR{placement.RecordIndex + 1}" : (slot + 1).ToString();
            var textColor = isStart ? SystemColors.HighlightText : SystemColors.ControlText;
            TextRenderer.DrawText(
                e.Graphics,
                label,
                Font,
                Rectangle.Round(r),
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _sheetIndex != 0) return;
        var hit = _slotBounds.FirstOrDefault(x => x.Bounds.Contains(e.Location));
        if (hit.Bounds == RectangleF.Empty) return;
        StartSlotSelected?.Invoke(hit.Slot);
    }
}
