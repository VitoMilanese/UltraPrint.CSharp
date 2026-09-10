using System.Data;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Compact managed counterpart of the original FormQBE used by Sequenza.Pescarecord. The native
/// form has one field selector, one condition combo, one value control and OK/Cancel; this dialog
/// intentionally keeps the same shape rather than turning record search into a separate workspace.
/// </summary>
internal sealed class LegacyQbeDialog : Form
{
    private readonly ComboBox _field = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _operator = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _value = new() { Dock = DockStyle.Fill };

    public LegacyQbeDialog(DataTable records)
    {
        ArgumentNullException.ThrowIfNull(records);
        Text = "Ricerca record";
        Width = 560;
        Height = 205;
        MinimumSize = new Size(470, 205);
        MaximumSize = new Size(900, 205);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        foreach (DataColumn column in records.Columns) _field.Items.Add(column.ColumnName);
        foreach (var item in LegacyQbeMatcher.Operators) _operator.Items.Add(item);
        if (_field.Items.Count > 0) _field.SelectedIndex = 0;
        if (_operator.Items.Count > 0) _operator.SelectedIndex = 0;
        _operator.SelectedIndexChanged += (_, _) => UpdateValueAvailability();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 2,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(Label("Campo:"), 0, 0);
        root.Controls.Add(_field, 1, 0);
        root.Controls.Add(Label("Condizione:"), 0, 1);
        root.Controls.Add(_operator, 1, 1);
        root.Controls.Add(Label("Contenuto:"), 0, 2);
        root.Controls.Add(_value, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var cancel = new Button { Text = "Annulla", Width = 90, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 3);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(root);
        UpdateValueAvailability();
    }

    public string FieldName => Convert.ToString(_field.SelectedItem) ?? string.Empty;
    public string OperatorToken => (_operator.SelectedItem as LegacyQbeOperator)?.Token ?? string.Empty;
    public string Operand => _value.Text;

    private void UpdateValueAvailability()
    {
        var token = OperatorToken;
        var needsValue = token is not "Vero" and not "Falso";
        _value.Enabled = needsValue;
        if (!needsValue) _value.Clear();
    }

    private static Label Label(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight
    };
}
