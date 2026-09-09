using UltraPrint.Legacy.Security;

namespace UltraPrint.WinForms;

internal sealed class OperatorsForm : Form
{
    private readonly LegacyOperatorStore _store;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    public OperatorsForm(LegacyOperatorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Text = "UltraPrint - Operators";
        Width = 820;
        Height = 520;
        MinimumSize = new Size(650, 400);
        StartPosition = FormStartPosition.CenterParent;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Operator",
            DataPropertyName = nameof(LegacyOperatorInfo.Name),
            FillWeight = 180
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Level",
            DataPropertyName = nameof(LegacyOperatorInfo.Level),
            FillWeight = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Privilege",
            DataPropertyName = nameof(LegacyOperatorInfo.Privilege),
            FillWeight = 80
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Group",
            DataPropertyName = nameof(LegacyOperatorInfo.Group),
            FillWeight = 150
        });
        _grid.CellDoubleClick += (_, _) => EditSelected();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(6, 6, 6, 3),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toolbar.Controls.Add(MakeButton("New", (_, _) => AddOperator()));
        toolbar.Controls.Add(MakeButton("Edit", (_, _) => EditSelected()));
        toolbar.Controls.Add(MakeButton("Password...", (_, _) => ChangePassword()));
        toolbar.Controls.Add(MakeButton("Delete", (_, _) => DeleteSelected()));
        toolbar.Controls.Add(new Label { Width = 16 });
        toolbar.Controls.Add(MakeButton("Privileges...", (_, _) => OpenPrivileges()));
        toolbar.Controls.Add(MakeButton("Refresh", (_, _) => RefreshOperators()));

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 2, 8, 2) };
        footer.Controls.Add(_status);

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(footer);
        Shown += (_, _) => RefreshOperators();
    }

    private LegacyOperatorInfo? Selected => _grid.CurrentRow?.DataBoundItem as LegacyOperatorInfo;

    private void RefreshOperators()
    {
        try
        {
            var selectedName = Selected?.Name;
            var data = _store.GetOperators().ToList();
            _grid.DataSource = data;

            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.DataBoundItem is LegacyOperatorInfo item &&
                        string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        _grid.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }

            _status.Text = $"{data.Count} operator(s) - {Path.GetFileName(_store.DatabasePath)} - {_store.ProviderDescription}";
        }
        catch (Exception ex)
        {
            ShowError(ex, "Refresh operators");
        }
    }

    private void AddOperator()
    {
        using var dialog = new OperatorEditDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _store.CreateOperator(dialog.OperatorName, dialog.PasswordText, dialog.Level, dialog.Privilege, dialog.Group);
            RefreshOperators();
            SelectByName(dialog.OperatorName);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Create operator");
        }
    }

    private void EditSelected()
    {
        var selected = Selected;
        if (selected is null) return;

        using var dialog = new OperatorEditDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _store.UpdateOperator(
                selected.Name,
                dialog.OperatorName,
                dialog.Level,
                dialog.Privilege,
                dialog.Group);
            RefreshOperators();
            SelectByName(dialog.OperatorName);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Update operator");
        }
    }

    private void ChangePassword()
    {
        var selected = Selected;
        if (selected is null) return;

        using var dialog = new PasswordDialog(selected.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _store.ChangePassword(selected.Name, dialog.PasswordText);
            _status.Text = $"Password updated for {selected.Name}.";
        }
        catch (Exception ex)
        {
            ShowError(ex, "Change password");
        }
    }

    private void DeleteSelected()
    {
        var selected = Selected;
        if (selected is null) return;
        if (MessageBox.Show(
                this,
                $"Delete operator '{selected.Name}'?",
                "Delete operator",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            _store.DeleteOperator(selected.Name);
            RefreshOperators();
        }
        catch (Exception ex)
        {
            ShowError(ex, "Delete operator");
        }
    }

    private void OpenPrivileges()
    {
        using var form = new PrivilegesForm(_store);
        form.ShowDialog(this);
    }

    private void SelectByName(string name)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not LegacyOperatorInfo item ||
                !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            row.Selected = true;
            _grid.CurrentCell = row.Cells[0];
            break;
        }
    }

    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28 };
        button.Click += click;
        return button;
    }

    private void ShowError(Exception ex, string caption) =>
        MessageBox.Show(this, ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
}

internal sealed class PrivilegesForm : Form
{
    private readonly LegacyOperatorStore _store;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    public PrivilegesForm(LegacyOperatorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Text = "UltraPrint - Privileges";
        Width = 640;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Privilege",
            DataPropertyName = nameof(LegacyPrivilegeInfo.Privilege),
            FillWeight = 80
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            DataPropertyName = nameof(LegacyPrivilegeInfo.Description),
            FillWeight = 220
        });
        _grid.CellDoubleClick += (_, _) => EditSelected();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(6),
            WrapContents = false
        };
        toolbar.Controls.Add(MakeButton("New", (_, _) => AddPrivilege()));
        toolbar.Controls.Add(MakeButton("Edit", (_, _) => EditSelected()));
        toolbar.Controls.Add(MakeButton("Delete", (_, _) => DeleteSelected()));
        toolbar.Controls.Add(MakeButton("Refresh", (_, _) => RefreshPrivileges()));

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Shown += (_, _) => RefreshPrivileges();
    }

    private LegacyPrivilegeInfo? Selected => _grid.CurrentRow?.DataBoundItem as LegacyPrivilegeInfo;

    private void RefreshPrivileges()
    {
        try { _grid.DataSource = _store.GetPrivileges().ToList(); }
        catch (Exception ex) { ShowError(ex, "Refresh privileges"); }
    }

    private void AddPrivilege()
    {
        using var dialog = new PrivilegeEditDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _store.SavePrivilege(dialog.Privilege, dialog.Description);
            RefreshPrivileges();
        }
        catch (Exception ex) { ShowError(ex, "Create privilege"); }
    }

    private void EditSelected()
    {
        var selected = Selected;
        if (selected is null) return;
        using var dialog = new PrivilegeEditDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (dialog.Privilege != selected.Privilege)
                _store.DeletePrivilege(selected.Privilege);
            _store.SavePrivilege(dialog.Privilege, dialog.Description);
            RefreshPrivileges();
        }
        catch (Exception ex) { ShowError(ex, "Update privilege"); }
    }

    private void DeleteSelected()
    {
        var selected = Selected;
        if (selected is null) return;
        if (MessageBox.Show(
                this,
                $"Delete privilege {selected.Privilege}?",
                "Delete privilege",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            _store.DeletePrivilege(selected.Privilege);
            RefreshPrivileges();
        }
        catch (Exception ex) { ShowError(ex, "Delete privilege"); }
    }

    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28 };
        button.Click += click;
        return button;
    }

    private void ShowError(Exception ex, string caption) =>
        MessageBox.Show(this, ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
