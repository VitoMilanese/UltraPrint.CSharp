using UltraPrint.Core.Models;

namespace UltraPrint.WinForms;

public sealed class LayoutSettingsDialog : Form
{
    private readonly NumericUpDown _width = CreateNumber(1, 2000, 2, 0.1m);
    private readonly NumericUpDown _height = CreateNumber(1, 2000, 2, 0.1m);
    private readonly NumericUpDown _dpi = CreateNumber(30, 5000, 0, 1);

    private LayoutSettingsDialog(CardLayout layout)
    {
        Text = "Layout properties";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(330, 165);

        _width.Value = ClampDecimal((decimal)layout.WidthMm, _width.Minimum, _width.Maximum);
        _height.Value = ClampDecimal((decimal)layout.HeightMm, _height.Minimum, _height.Maximum);
        _dpi.Value = ClampDecimal(layout.Dpi, _dpi.Minimum, _dpi.Maximum);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12),
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        AddRow(table, 0, "Width (mm)", _width);
        AddRow(table, 1, "Height (mm)", _height);
        AddRow(table, 2, "Resolution (DPI)", _dpi);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 3);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static bool Edit(IWin32Window owner, CardLayout layout)
    {
        using var dialog = new LayoutSettingsDialog(layout);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        layout.WidthMm = (double)dialog._width.Value;
        layout.HeightMm = (double)dialog._height.Value;
        layout.Dpi = decimal.ToInt32(dialog._dpi.Value);
        return true;
    }

    private static NumericUpDown CreateNumber(decimal minimum, decimal maximum, int decimals, decimal increment) =>
        new()
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimals,
            Increment = increment,
            ThousandsSeparator = false
        };

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum) => Math.Min(maximum, Math.Max(minimum, value));
}
