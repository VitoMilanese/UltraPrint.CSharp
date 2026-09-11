using System.Globalization;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

internal sealed class LegacyCounterEditorDialog : Form
{
    private readonly LegacyCounterEditorModel _model;
    private readonly ListBox _counters = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _digits = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _value = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _zeroPadding = new() { Text = "Leading zeroes", AutoSize = true };
    private readonly Button _delete = new() { Text = "Delete", AutoSize = true, Enabled = false };
    private readonly ErrorProvider _errors = new();
    private bool _syncing;

    public LegacyCounterEditorDialog(string counterFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(counterFilePath);
        _model = LegacyCounterEditorModel.Load(counterFilePath);

        Text = "Counters";
        Width = 640;
        Height = 390;
        MinimumSize = new Size(560, 330);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        _errors.ContainerControl = this;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var listPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listPanel.Controls.Add(_counters, 0, 0);

        var listButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var create = new Button { Text = "New", AutoSize = true };
        listButtons.Controls.Add(create);
        listButtons.Controls.Add(_delete);
        listPanel.Controls.Add(listButtons, 0, 1);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        editor.Controls.Add(LabelFor("Digits:"), 0, 0);
        editor.Controls.Add(_digits, 1, 0);
        editor.Controls.Add(LabelFor("Value:"), 0, 1);
        editor.Controls.Add(_value, 1, 1);
        editor.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 2);
        editor.Controls.Add(_zeroPadding, 1, 2);

        var pathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = SystemColors.GrayText,
            Text = counterFilePath,
            Margin = new Padding(0, 18, 0, 0)
        };
        editor.Controls.Add(pathLabel, 0, 3);
        editor.SetColumnSpan(pathLabel, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var ok = new Button { Text = "OK", AutoSize = true };
        buttons.Controls.Add(ok);

        root.Controls.Add(listPanel, 0, 0);
        root.Controls.Add(editor, 1, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        AcceptButton = ok;

        _digits.Items.AddRange(_model.DigitsChoices.Cast<object>().ToArray());
        RefreshListFromModel(selectedIndex: -1);

        _counters.SelectedIndexChanged += (_, _) => SelectCounterFromList();
        _digits.SelectedIndexChanged += (_, _) => ChangeDigits();
        _zeroPadding.CheckedChanged += (_, _) => ChangeZeroPadding();
        _value.TextChanged += (_, _) => ChangeCurrentValue();
        create.Click += (_, _) => CreateCounter();
        _delete.Click += (_, _) => DeleteCounter();
        ok.Click += (_, _) => SaveAndClose();
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 12, 6)
    };

    private void SelectCounterFromList()
    {
        if (_syncing || _counters.SelectedIndex < 0) return;
        if (_model.SelectListItem(_counters.SelectedIndex))
            LoadSelectedRecordIntoControls();
        _delete.Enabled = _model.DeleteEnabled;
    }

    private void LoadSelectedRecordIntoControls()
    {
        if (_model.Selection is not { } selection) return;

        _syncing = true;
        try
        {
            _digits.SelectedItem = (int)selection.Digits;
            if (_digits.SelectedIndex < 0)
                _digits.Text = selection.Digits.ToString(CultureInfo.CurrentCulture);
            _value.Text = selection.CurrentValue.ToString(CultureInfo.CurrentCulture);
            _zeroPadding.Checked = selection.ZeroPadding;
            _errors.SetError(_value, string.Empty);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ChangeDigits()
    {
        if (_syncing || _digits.SelectedItem is not int digits) return;
        _model.SetDigits(checked((short)digits));
    }

    private void ChangeZeroPadding()
    {
        if (_syncing) return;
        _model.SetZeroPadding(_zeroPadding.Checked);
    }

    private void ChangeCurrentValue()
    {
        if (_syncing || _model.NativeCurrentLogicalIndex <= 0) return;
        if (int.TryParse(_value.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
        {
            _model.SetCurrentValue(value);
            _errors.SetError(_value, string.Empty);
        }
        else
        {
            _errors.SetError(_value, "Enter a 32-bit integer value.");
        }
    }

    private void CreateCounter()
    {
        var input = PromptForCounterName(this);
        if (input is null) return;

        try
        {
            var result = _model.CreateFromInput(input);
            if (!result.Created || result.ListIndex is null) return;

            RefreshListFromModel(result.ListIndex.Value);
            if (result.SelectionMatchedARecord)
                LoadSelectedRecordIntoControls();
            _delete.Enabled = _model.DeleteEnabled;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "New counter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteCounter()
    {
        if (!_model.DeleteSelected()) return;

        // Native delete intentionally leaves the editor values visible; only the ListBox
        // selection/current logical index and Delete enabled state are cleared.
        RefreshListFromModel(selectedIndex: -1);
        _delete.Enabled = false;
    }

    private void SaveAndClose()
    {
        if (!string.IsNullOrEmpty(_errors.GetError(_value)) && _model.NativeCurrentLogicalIndex > 0)
        {
            MessageBox.Show(this, "Enter a valid 32-bit integer counter value before saving.",
                "Counters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _value.Focus();
            return;
        }

        try
        {
            _model.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Counter save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshListFromModel(int selectedIndex)
    {
        _syncing = true;
        try
        {
            _counters.BeginUpdate();
            _counters.Items.Clear();
            _counters.Items.AddRange(_model.ListItems.Cast<object>().ToArray());
            _counters.SelectedIndex = selectedIndex >= 0 && selectedIndex < _counters.Items.Count
                ? selectedIndex
                : -1;
        }
        finally
        {
            _counters.EndUpdate();
            _syncing = false;
        }
    }

    private static string? PromptForCounterName(IWin32Window owner)
    {
        using var prompt = new Form
        {
            Text = "Counters",
            Width = 390,
            Height = 145,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent
        };

        var label = new Label
        {
            Text = LegacyCounterEditorModel.NewCounterPrompt,
            AutoSize = true,
            Left = 12,
            Top = 15
        };
        var input = new TextBox
        {
            Left = 12,
            Top = 38,
            Width = 350
        };
        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Left = 206,
            Top = 72
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Left = 287,
            Top = 72
        };

        prompt.Controls.AddRange(new Control[] { label, input, ok, cancel });
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancel;
        prompt.Shown += (_, _) => input.Focus();

        return prompt.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
