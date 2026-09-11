using System.Data;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for the core frmDatabase + Tabella record workflow: choose a legacy data
/// source, browse tables, run SQL, move through records, bind columns to card fields, preview the
/// resulting card and print one/all records.
/// </summary>
public sealed class DatabaseWorkspaceForm : Form
{
    private readonly CardLayout _layout;
    private readonly BindingSource _rows = new();
    private readonly Dictionary<int, string> _bindings;
    private readonly LayoutCanvas _preview = new() { Dock = DockStyle.Fill, PreviewMode = true, ShowGrid = false };
    private readonly LayoutPrintService _printService = new();
    private readonly RecordBatchPrintService _batchPrintService = new();

    private readonly TextBox _sourcePath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox _tables = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TextBox _sql = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = true
    };
    private readonly ComboBox _layoutFields = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _columns = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Label _recordLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label _bindingStatus = new() { Dock = DockStyle.Top, Height = 42, AutoEllipsis = true };

    private ILegacyRecordSource? _source;
    private DataTable? _table;
    private CardLayout? _currentBoundLayout;
    private string? _selectedTable;

    public DatabaseWorkspaceForm(CardLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        var saved = ManagedBindingStore.LoadState(layout);
        _bindings = new Dictionary<int, string>(saved.Bindings);

        Text = $"UltraPrint Database / Records — {layout.Name}";
        Width = 1380;
        Height = 850;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterParent;

        _side.Items.AddRange(new object[] { "Front", "Back", "All" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (_, _) => RefreshRecordPreview();

        _grid.DataSource = _rows;
        _rows.PositionChanged += (_, _) => RefreshRecordPreview();
        _layoutFields.SelectedIndexChanged += (_, _) => RefreshBindingSelection();

        Controls.Add(BuildUi());
        PopulateLayoutFields();

        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(saved.Sql)) _sql.Text = saved.Sql;
            if (!string.IsNullOrWhiteSpace(saved.DatabasePath) && File.Exists(saved.DatabasePath))
                OpenSource(saved.DatabasePath, saved.Table);
            else
                ShowTemplatePreview();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source?.Dispose();
            _rows.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(6)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var sourceBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        sourceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        sourceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        sourceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        sourceBar.Controls.Add(MakeButton("Open data...", (_, _) => ChooseSource()), 0, 0);
        sourceBar.Controls.Add(_sourcePath, 1, 0);
        sourceBar.Controls.Add(new Label { Text = "Table:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 2, 0);
        sourceBar.Controls.Add(_tables, 3, 0);
        _tables.SelectedIndexChanged += (_, _) =>
        {
            if (_tables.SelectedItem is string name) _selectedTable = name;
        };

        var queryBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        queryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        queryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        queryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        queryBar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        queryBar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        queryBar.Controls.Add(MakeButton("Open table", (_, _) => LoadSelectedTable()), 0, 0);
        queryBar.SetRowSpan(queryBar.Controls[^1], 2);
        queryBar.Controls.Add(_sql, 1, 0);
        queryBar.SetRowSpan(_sql, 2);
        queryBar.Controls.Add(MakeButton("Run query", (_, _) => RunQuery()), 2, 0);
        queryBar.Controls.Add(MakeButton("Execute action", (_, _) => ExecuteAction()), 3, 0);
        queryBar.Controls.Add(MakeButton("Refresh", (_, _) => RefreshCurrentData()), 2, 1);
        queryBar.Controls.Add(MakeButton("Clear SQL", (_, _) => _sql.Clear()), 3, 1);

        var main = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 820
        };
        main.Panel1.Controls.Add(_grid);
        main.Panel1.Controls.Add(new Label
        {
            Text = " Records",
            Dock = DockStyle.Top,
            Height = 25,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 390
        };
        right.Panel1.Controls.Add(_preview);
        right.Panel1.Controls.Add(BuildPreviewBar());
        right.Panel2.Controls.Add(BuildBindingPanel());
        main.Panel2.Controls.Add(right);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        nav.Controls.Add(MakeButton("|<", (_, _) => MoveTo(0), 48));
        nav.Controls.Add(MakeButton("<", (_, _) => MoveTo(_rows.Position - 1), 48));
        nav.Controls.Add(MakeButton(">", (_, _) => MoveTo(_rows.Position + 1), 48));
        nav.Controls.Add(MakeButton(">|", (_, _) => MoveTo(_rows.Count - 1), 48));
        nav.Controls.Add(_recordLabel);
        nav.Controls.Add(new Label { Width = 24 });
        nav.Controls.Add(MakeButton("Print preview current", (_, _) => PrintCurrent(preview: true), 145));
        nav.Controls.Add(MakeButton("Print current", (_, _) => PrintCurrent(preview: false), 110));
        nav.Controls.Add(MakeButton("Preview all", (_, _) => PrintAll(preview: true), 100));
        nav.Controls.Add(MakeButton("Print all", (_, _) => PrintAll(preview: false), 90));

        root.Controls.Add(sourceBar, 0, 0);
        root.Controls.Add(queryBar, 0, 1);
        root.Controls.Add(main, 0, 2);
        root.Controls.Add(nav, 0, 3);
        root.Controls.Add(_status, 0, 4);
        return root;
    }

    private Control BuildPreviewBar()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        panel.Controls.Add(new Label { Text = "Preview side:", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
        panel.Controls.Add(_side);
        return panel;
    }

    private Control BuildBindingPanel()
    {
        var group = new GroupBox { Dock = DockStyle.Fill, Text = "Database field binding" };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 6
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        panel.Controls.Add(new Label { Text = "Card field:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        panel.Controls.Add(_layoutFields, 1, 0);
        panel.Controls.Add(new Label { Text = "DB column:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
        panel.Controls.Add(_columns, 1, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(MakeButton("Bind", (_, _) => BindSelectedField(), 80));
        buttons.Controls.Add(MakeButton("Clear override", (_, _) => ClearSelectedBinding(), 110));
        panel.Controls.Add(buttons, 1, 2);
        panel.Controls.Add(_bindingStatus, 0, 3);
        panel.SetColumnSpan(_bindingStatus, 2);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Bindings already encoded by a legacy layout are used automatically when recovered. New managed bindings are saved beside the .ly in a .data.json sidecar so the unknown legacy bytes are not guessed or corrupted.",
            AutoSize = false
        };
        panel.Controls.Add(note, 0, 4);
        panel.SetColumnSpan(note, 2);

        var openSidecar = MakeButton("Open state folder", (_, _) => OpenStateFolder(), 130);
        panel.Controls.Add(openSidecar, 1, 5);
        group.Controls.Add(panel);
        return group;
    }

    private void ChooseSource()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "UltraPrint data|*.mdb;*.ffm;*.dbf;*.xls;*.xlsx;*.csv;*.txt;*.dat|Access/Operator DB|*.mdb;*.ffm|DBF|*.dbf|Excel|*.xls;*.xlsx|CSV/Text|*.csv;*.txt;*.dat|All files|*.*",
            Title = "Open UltraPrint database / data file"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenSource(dialog.FileName, null);
    }

    private void OpenSource(string path, string? preferredTable)
    {
        try
        {
            _source?.Dispose();
            _source = LegacyRecordSourceFactory.Open(path);
            _sourcePath.Text = _source.SourcePath;
            _layout.DatabasePath = _source.SourcePath;

            _tables.BeginUpdate();
            _tables.Items.Clear();
            foreach (var table in _source.GetTableNames()) _tables.Items.Add(table);
            _tables.EndUpdate();

            var selectIndex = -1;
            if (!string.IsNullOrWhiteSpace(preferredTable))
            {
                for (var i = 0; i < _tables.Items.Count; i++)
                {
                    if (string.Equals(Convert.ToString(_tables.Items[i]), preferredTable, StringComparison.OrdinalIgnoreCase))
                    {
                        selectIndex = i;
                        break;
                    }
                }
            }
            if (selectIndex < 0 && _tables.Items.Count > 0) selectIndex = 0;
            if (selectIndex >= 0) _tables.SelectedIndex = selectIndex;

            _status.Text = $"Opened {Path.GetFileName(path)} via {_source.ProviderDescription}. {_tables.Items.Count} table(s).";
            PersistState();
            if (_tables.Items.Count == 1 || !string.IsNullOrWhiteSpace(preferredTable)) LoadSelectedTable();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Database open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Database open failed";
        }
    }

    private void LoadSelectedTable()
    {
        if (_source is null || _tables.SelectedItem is not string tableName) return;
        try
        {
            _selectedTable = tableName;
            var table = _source.OpenTable(tableName);
            _sql.Text = $"SELECT * FROM {OleDbRecordSource.QuoteIdentifier(tableName)}";
            _layout.Sql = _sql.Text;
            SetTable(table);
            PersistState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open table failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunQuery()
    {
        if (_source is null) return;
        if (string.IsNullOrWhiteSpace(_sql.Text)) { LoadSelectedTable(); return; }
        try
        {
            _layout.Sql = _sql.Text;
            SetTable(_source.ExecuteQuery(_sql.Text));
            PersistState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SQL query failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExecuteAction()
    {
        if (_source is null || string.IsNullOrWhiteSpace(_sql.Text)) return;
        if (_source.IsReadOnly)
        {
            MessageBox.Show(this, "This data source is read-only.", "Execute SQL", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "Execute this SQL action against the database?", "Execute SQL",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var affected = _source.ExecuteNonQuery(_sql.Text);
            _status.Text = $"SQL action completed. Records affected: {affected}.";
            RefreshCurrentData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SQL action failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshCurrentData()
    {
        if (_source is null) return;
        if (!string.IsNullOrWhiteSpace(_sql.Text)) RunQuery();
        else LoadSelectedTable();
    }

    private void SetTable(DataTable table)
    {
        _table = table;
        _rows.DataSource = table;
        _columns.BeginUpdate();
        _columns.Items.Clear();
        foreach (DataColumn column in table.Columns) _columns.Items.Add(column.ColumnName);
        _columns.EndUpdate();
        if (_columns.Items.Count > 0) _columns.SelectedIndex = 0;
        RefreshBindingSelection();
        RefreshRecordPreview();
        _status.Text = $"Loaded {table.Rows.Count} record(s), {table.Columns.Count} field(s).";
    }

    private void PopulateLayoutFields()
    {
        _layoutFields.BeginUpdate();
        _layoutFields.Items.Clear();
        foreach (var field in _layout.Fields.Where(x => x.Kind is LayoutFieldKind.Text or LayoutFieldKind.Image).OrderBy(x => x.Index))
            _layoutFields.Items.Add(field);
        _layoutFields.EndUpdate();
        if (_layoutFields.Items.Count > 0) _layoutFields.SelectedIndex = 0;
    }

    private void RefreshBindingSelection()
    {
        if (_layoutFields.SelectedItem is not LayoutField field)
        {
            _bindingStatus.Text = string.Empty;
            return;
        }

        var legacyBinding = field.Kind == LayoutFieldKind.Image ? field.Image.DatabaseField : field.Text.DatabaseField;
        if (_bindings.TryGetValue(field.Index, out var managed))
            _bindingStatus.Text = $"Managed: {managed}";
        else if (!string.IsNullOrWhiteSpace(legacyBinding))
            _bindingStatus.Text = $"Layout binding: {legacyBinding}";
        else if (field.Name.StartsWith('%') && field.Name.EndsWith('%'))
            _bindingStatus.Text = $"Placeholder fallback: {field.Name}";
        else
            _bindingStatus.Text = "Not bound";

        var desired = _bindings.TryGetValue(field.Index, out managed) ? managed : legacyBinding;
        if (string.IsNullOrWhiteSpace(desired)) return;
        var normalized = desired.Trim('%');
        for (var i = 0; i < _columns.Items.Count; i++)
        {
            var candidate = Convert.ToString(_columns.Items[i]);
            if (string.Equals(candidate, desired, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _columns.SelectedIndex = i;
                break;
            }
        }
    }

    private void BindSelectedField()
    {
        if (_layoutFields.SelectedItem is not LayoutField field || _columns.SelectedItem is not string column) return;
        _bindings[field.Index] = column;
        PersistState();
        RefreshBindingSelection();
        RefreshRecordPreview();
        _status.Text = $"Bound {field.Name} to {column}.";
    }

    private void ClearSelectedBinding()
    {
        if (_layoutFields.SelectedItem is not LayoutField field) return;
        _bindings.Remove(field.Index);
        PersistState();
        RefreshBindingSelection();
        RefreshRecordPreview();
        _status.Text = $"Cleared managed binding override for {field.Name}.";
    }

    private void RefreshRecordPreview()
    {
        if (_rows.Current is not DataRowView rowView)
        {
            ShowTemplatePreview();
            _recordLabel.Text = _rows.Count == 0 ? "0 / 0" : $"{Math.Max(0, _rows.Position + 1)} / {_rows.Count}";
            return;
        }

        var snapshot = LegacyRecordBinder.Snapshot(rowView);
        _currentBoundLayout = LegacyRecordBinder.CreateBoundLayout(_layout, snapshot, _bindings);
        _preview.Layout = _currentBoundLayout;
        _preview.Side = CurrentSide();
        _preview.InvalidateAssets();
        _recordLabel.Text = $"Record {_rows.Position + 1} / {_rows.Count}";
    }

    private void ShowTemplatePreview()
    {
        _currentBoundLayout = _layout;
        _preview.Layout = _layout;
        _preview.Side = CurrentSide();
        _preview.InvalidateAssets();
    }

    private void MoveTo(int position)
    {
        if (_rows.Count == 0) return;
        _rows.Position = Math.Clamp(position, 0, _rows.Count - 1);
    }

    private LayoutSide CurrentSide() => _side.SelectedIndex switch
    {
        1 => LayoutSide.Back,
        2 => LayoutSide.Unknown,
        _ => LayoutSide.Front
    };

    private void PrintCurrent(bool preview)
    {
        if (_currentBoundLayout is null || _rows.Current is not DataRowView) return;
        try
        {
            if (preview) _printService.ShowPreview(this, _currentBoundLayout, CurrentSide(), _preview);
            else _printService.Print(this, _currentBoundLayout, CurrentSide(), _preview);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Print failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrintAll(bool preview)
    {
        if (_table is null || _table.Rows.Count == 0) return;
        try
        {
            var records = _table.Rows.Cast<DataRow>()
                .Select(LegacyRecordBinder.Snapshot)
                .Cast<IReadOnlyDictionary<string, object?>>()
                .ToList();
            if (preview) _batchPrintService.ShowPreview(this, _layout, records, _bindings, CurrentSide());
            else _batchPrintService.Print(this, _layout, records, _bindings, CurrentSide());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Batch print failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PersistState()
    {
        try
        {
            ManagedBindingStore.SaveState(_layout, new ManagedLayoutDataState(
                _source?.SourcePath ?? _layout.DatabasePath,
                string.IsNullOrWhiteSpace(_sql.Text) ? _layout.Sql : _sql.Text,
                _selectedTable,
                new Dictionary<int, string>(_bindings)));
        }
        catch (InvalidOperationException)
        {
            // Unsaved layouts have no stable sidecar path yet. Keep the state in memory.
        }
    }

    private void OpenStateFolder()
    {
        var statePath = ManagedBindingStore.GetPath(_layout);
        if (statePath is null) return;
        try
        {
            var directory = Path.GetDirectoryName(statePath)!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static Button MakeButton(string text, EventHandler click, int width = 120)
    {
        var button = new Button { Text = text, Width = width, Height = 28, Margin = new Padding(3) };
        button.Click += click;
        return button;
    }
}
