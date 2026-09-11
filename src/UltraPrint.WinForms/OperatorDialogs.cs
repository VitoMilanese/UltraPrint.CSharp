using UltraPrint.Legacy.Security;

namespace UltraPrint.WinForms;

internal sealed class OperatorEditDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = 50 };
    private readonly NumericUpDown _level = IntegerBox();
    private readonly NumericUpDown _privilege = IntegerBox();
    private readonly TextBox _group = new() { Dock = DockStyle.Fill, MaxLength = 50 };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly bool _creating;

    public OperatorEditDialog(LegacyOperatorInfo? existing)
    {
        _creating = existing is null;
        Text = _creating ? "New operator" : "Edit operator";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, _creating ? 300 : 225);

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _level.Value = existing.Level;
            _privilege.Value = existing.Privilege;
            _group.Text = existing.Group;
        }

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Accept();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = _creating ? 7 : 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Operator:", _name);
        AddRow(layout, 1, "Level (Livello):", _level);
        AddRow(layout, 2, "Privilege:", _privilege);
        AddRow(layout, 3, "Group (Gruppo):", _group);

        var next = 4;
        if (_creating)
        {
            AddRow(layout, next++, "Password:", _password);
            AddRow(layout, next++, "Confirm:", _confirm);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, next);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string OperatorName => _name.Text.Trim();
    public int Level => Decimal.ToInt32(_level.Value);
    public int Privilege => Decimal.ToInt32(_privilege.Value);
    public string Group => _group.Text.Trim();
    public string PasswordText => _password.Text;

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show(this, "Operator name is required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return;
        }

        if (_creating && !string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _confirm.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static NumericUpDown IntegerBox() => new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 0,
        Minimum = int.MinValue,
        Maximum = int.MaxValue,
        ThousandsSeparator = false
    };

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}

internal sealed class PasswordDialog : Form
{
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

    public PasswordDialog(string operatorName)
    {
        Text = $"Password - {operatorName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 155);

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (!string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _confirm.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "New password:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        layout.Controls.Add(_password, 1, 0);
        layout.Controls.Add(new Label { Text = "Confirm:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
        layout.Controls.Add(_confirm, 1, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string PasswordText => _password.Text;
}

internal sealed class PrivilegeEditDialog : Form
{
    private readonly NumericUpDown _privilege = new()
    {
        Dock = DockStyle.Fill,
        DecimalPlaces = 0,
        Minimum = int.MinValue,
        Maximum = int.MaxValue
    };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, MaxLength = 255 };

    public PrivilegeEditDialog(LegacyPrivilegeInfo? existing)
    {
        Text = existing is null ? "New privilege" : "Edit privilege";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 155);

        if (existing is not null)
        {
            _privilege.Value = existing.Privilege;
            _description.Text = existing.Description;
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Privilege:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        layout.Controls.Add(_privilege, 1, 0);
        layout.Controls.Add(new Label { Text = "Description:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
        layout.Controls.Add(_description, 1, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public int Privilege => Decimal.ToInt32(_privilege.Value);
    public string Description => _description.Text.Trim();
}
