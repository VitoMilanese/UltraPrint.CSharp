using UltraPrint.Legacy.Security;

namespace UltraPrint.WinForms;

internal sealed class LoginForm : Form
{
    private readonly LegacyOperatorStore _store;
    private readonly ComboBox _operator = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly Label _message = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        ForeColor = SystemColors.GrayText,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public LoginForm(LegacyOperatorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Text = "UltraPrint - Login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 180);

        foreach (var item in _store.GetOperators()) _operator.Items.Add(item.Name);
        if (_operator.Items.Count > 0) _operator.SelectedIndex = 0;

        _message.Text = $"{Path.GetFileName(_store.DatabasePath)} - {_store.ProviderDescription}";

        var login = new Button { Text = "Login", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        login.Click += (_, _) => TryLogin();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label { Text = "Operator:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        layout.Controls.Add(_operator, 1, 0);
        layout.Controls.Add(new Label { Text = "Password:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
        layout.Controls.Add(_password, 1, 1);
        layout.Controls.Add(_message, 0, 2);
        layout.SetColumnSpan(_message, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(login);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = login;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    public LegacyOperatorSession? Session { get; private set; }

    private void TryLogin()
    {
        if (_operator.SelectedItem is not string name)
        {
            _message.Text = "No operator is selected.";
            return;
        }

        try
        {
            Session = _store.Authenticate(name, _password.Text);
            if (Session is null)
            {
                _message.ForeColor = Color.Firebrick;
                _message.Text = "Invalid password.";
                _password.SelectAll();
                _password.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _message.ForeColor = Color.Firebrick;
            _message.Text = ex.Message;
        }
    }
}
