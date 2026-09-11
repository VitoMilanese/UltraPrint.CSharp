using System.Drawing.Text;
using System.Globalization;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.WinForms;

internal sealed class LegacyBarcodeEditorDialog : Form
{
    private readonly LayoutField _field;
    private readonly TextBox _valueText = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _fontCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _sizeText = new() { Dock = DockStyle.Left, Width = 100 };
    private readonly Panel _preview = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Label _previewInfo = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly IReadOnlyList<string> _installedBarcodeFonts;

    public LegacyBarcodeEditorDialog(LayoutField field)
    {
        _field = field ?? throw new ArgumentNullException(nameof(field));
        if (field.Kind != LayoutFieldKind.Barcode)
            throw new ArgumentException("The barcode editor requires a barcode layout field.", nameof(field));

        Text = "Barcode";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 390);

        _installedBarcodeFonts = DiscoverLegacyBarcodeFonts();
        foreach (var name in _installedBarcodeFonts)
            _fontCombo.Items.Add(name);

        if (!string.IsNullOrWhiteSpace(field.Text.FontName) &&
            !_fontCombo.Items.Cast<string>().Any(name => string.Equals(name, field.Text.FontName, StringComparison.OrdinalIgnoreCase)))
            _fontCombo.Items.Insert(0, field.Text.FontName);

        if (!string.IsNullOrWhiteSpace(field.Text.FontName))
            _fontCombo.Text = field.Text.FontName;
        else if (_fontCombo.Items.Count > 0)
            _fontCombo.SelectedIndex = 0;

        _valueText.Text = field.LegacyPayload;
        _sizeText.Text = field.Text.FontSize.ToString("0.###", CultureInfo.CurrentCulture);

        _fontCombo.TextChanged += (_, _) => UpdatePreview();
        _sizeText.TextChanged += (_, _) => UpdatePreview();
        _preview.Paint += PreviewOnPaint;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(LabelFor("Field value:"), 0, 0);
        root.Controls.Add(_valueText, 1, 0);
        root.Controls.Add(LabelFor("Barcode font:"), 0, 1);
        root.Controls.Add(_fontCombo, 1, 1);
        root.Controls.Add(LabelFor("Font size:"), 0, 2);
        root.Controls.Add(_sizeText, 1, 2);
        root.Controls.Add(LabelFor("Native preview:"), 0, 3);
        root.Controls.Add(_previewInfo, 1, 3);
        root.Controls.Add(_preview, 0, 4);
        root.SetColumnSpan(_preview, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 5);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        UpdatePreview();
    }

    internal static IReadOnlyList<string> DiscoverLegacyBarcodeFonts()
    {
        using var collection = new InstalledFontCollection();
        var installed = collection.Families
            .Select(family => family.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<string>();
        foreach (var prefix in LegacyBarcodeFormatter.RecoveredFontPrefixes)
        {
            foreach (var name in installed
                         .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        return result;
    }

    private void SaveAndClose()
    {
        var fontName = _fontCombo.Text.Trim();
        if (fontName.Length == 0)
        {
            MessageBox.Show(this, "Choose or enter a barcode font name.", "Barcode",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!TryReadSize(out var size))
        {
            MessageBox.Show(this, "Enter a font size greater than 0 and not greater than 200.", "Barcode",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _field.LegacyPayload = _valueText.Text;
        _field.Text.FontName = fontName;
        _field.Text.FontSize = size;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdatePreview()
    {
        var fontName = _fontCombo.Text.Trim();
        var recovered = LegacyBarcodeFormatter.TryFormat(
            fontName,
            LegacyBarcodeFormatter.DefaultPreviewValue,
            out _);
        var installed = _installedBarcodeFonts.Any(name =>
            string.Equals(name, fontName, StringComparison.OrdinalIgnoreCase));

        _previewInfo.Text = recovered
            ? installed
                ? $"{LegacyBarcodeFormatter.DefaultPreviewValue} — recovered formatter + installed legacy font"
                : $"{LegacyBarcodeFormatter.DefaultPreviewValue} — recovered formatter; selected font is not in the installed legacy-font list"
            : $"{LegacyBarcodeFormatter.DefaultPreviewValue} — selected font name does not match a recovered formatter branch";
        _preview.Invalidate();
    }

    private void PreviewOnPaint(object? sender, PaintEventArgs e)
    {
        var fontName = _fontCombo.Text.Trim();
        LegacyBarcodeFormatter.TryFormat(
            fontName,
            LegacyBarcodeFormatter.DefaultPreviewValue,
            out var glyphs);

        var size = TryReadSize(out var parsed) ? parsed : 10d;
        Font drawingFont;
        try
        {
            drawingFont = new Font(fontName.Length == 0 ? Font.FontFamily.Name : fontName,
                Math.Max(8f, (float)(size * 1.5)), FontStyle.Regular, GraphicsUnit.Point);
        }
        catch
        {
            drawingFont = new Font(Font.FontFamily, Math.Max(8f, (float)(size * 1.5)), FontStyle.Regular, GraphicsUnit.Point);
        }

        using (drawingFont)
        using (var brush = new SolidBrush(Color.Black))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            e.Graphics.DrawString(glyphs, drawingFont, brush, _preview.ClientRectangle, format);
        }
    }

    private bool TryReadSize(out double size)
    {
        if (double.TryParse(_sizeText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out size) ||
            double.TryParse(_sizeText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            return double.IsFinite(size) && size > 0 && size <= 200;
        size = 0;
        return false;
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
}
