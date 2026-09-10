using System.Data;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for the user-facing Sequenza form: page imposition, first-slot selection,
/// front/back choice, setup persistence, sheet navigation and current/all sheet print commands.
/// </summary>
internal sealed class SequenceWorkspaceForm : Form
{
    private readonly CardLayout _layout;
    private readonly SequencePrintService _printService = new();
    private readonly SequenceSheetPreviewControl _preview = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<int, string> _bindings;
    private readonly NumericUpDown _rows = Number(1, 100, 5, 0);
    private readonly NumericUpDown _columns = Number(1, 100, 2, 0);
    private readonly NumericUpDown _marginLeft = Number(0, 1000, 10, 2);
    private readonly NumericUpDown _marginTop = Number(0, 1000, 10, 2);
    private readonly NumericUpDown _pitchX = Number(0, 1000, 0, 2);
    private readonly NumericUpDown _pitchY = Number(0, 1000, 0, 2);
    private readonly ComboBox _fillDirection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _startSlot = Number(1, 10000, 1, 0);
    private readonly NumericUpDown _copies = Number(1, 10000, 1, 0);
    private readonly ComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _cutMarks = new() { AutoSize = true, Text = "Draw cut marks" };
    private readonly CheckBox _useDatabase = new() { AutoSize = true, Text = "Use database records", Checked = true };
    private readonly Label _dataStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _pageLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ToolStripStatusLabel _status = new("Ready");

    private SequencePrintSettings _settings;
    private List<IReadOnlyDictionary<string, object?>> _records = new();
    private int _sheetIndex;
    private bool _syncingControls;

    public SequenceWorkspaceForm(CardLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _settings = ManagedSequenceStore.Load(layout);
        _bindings = new Dictionary<int, string>(ManagedBindingStore.LoadState(layout).Bindings);

        Text = $"UltraPrint Sequence / Sheet printing — {layout.Name}";
        Width = 1180;
        Height = 790;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;

        _fillDirection.Items.AddRange(new object[] { "Horizontal (row-major)", "Vertical (column-major)" });
        _side.Items.AddRange(new object[] { "Front", "Back", "Front + Back" });
        Controls.Add(BuildUi());
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(statusStrip);

        ApplySettingsToControls();
        WireEvents();
        Shown += (_, _) => ReloadRecords();
        FormClosing += (_, _) => TryPersistSettings(silent: true);
    }

    private Control BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(BuildSettingsPanel(), 0, 0);
        root.Controls.Add(BuildPreviewPanel(), 1, 0);
        var commandBar = BuildCommandBar();
        root.Controls.Add(commandBar, 0, 1);
        root.SetColumnSpan(commandBar, 2);
        return root;
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 17,
            Padding = new Padding(4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        var row = 0;
        var sheetHeader = Header("Sheet layout");
        panel.Controls.Add(sheetHeader, 0, row);
        panel.SetColumnSpan(sheetHeader, 2);
        row++;
        AddSetting(panel, ref row, "Rows:", _rows);
        AddSetting(panel, ref row, "Columns:", _columns);
        AddSetting(panel, ref row, "X offset / MargineDestro (mm):", _marginLeft);
        AddSetting(panel, ref row, "Top margin (mm):", _marginTop);
        AddSetting(panel, ref row, "Horizontal gap / Passo (mm):", _pitchX);
        AddSetting(panel, ref row, "Vertical gap / Passo (mm):", _pitchY);
        AddSetting(panel, ref row, "Fill direction:", _fillDirection);
        AddSetting(panel, ref row, "First slot:", _startSlot);
        AddSetting(panel, ref row, "Side:", _side);
        panel.Controls.Add(_cutMarks, 1, row++);

        var recordHeader = Header("Records");
        panel.Controls.Add(recordHeader, 0, row);
        panel.SetColumnSpan(recordHeader, 2);
        row++;
        panel.Controls.Add(_useDatabase, 0, row);
        panel.SetColumnSpan(_useDatabase, 2);
        row++;
        AddSetting(panel, ref row, "Template copies:", _copies);
        var reload = new Button { Text = "Reload records", Dock = DockStyle.Fill, Height = 28 };
        reload.Click += (_, _) => ReloadRecords();
        panel.Controls.Add(reload, 1, row++);
        panel.Controls.Add(_dataStatus, 0, row);
        panel.SetColumnSpan(_dataStatus, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return panel;
    }

    private Control BuildPreviewPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        nav.Controls.Add(Button("|<", (_, _) => MoveSheet(0), 45));
        nav.Controls.Add(Button("<", (_, _) => MoveSheet(_sheetIndex - 1), 45));
        nav.Controls.Add(_pageLabel);
        nav.Controls.Add(Button(">", (_, _) => MoveSheet(_sheetIndex + 1), 45));
        nav.Controls.Add(Button(">|", (_, _) => MoveSheet(Math.Max(0, SheetCount() - 1)), 45));
        nav.Controls.Add(new Label { Width = 18 });
        nav.Controls.Add(Button("Page setup...", (_, _) => PageSetup(), 105));

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Text = "Click a slot on sheet 1 to choose where the first record starts.",
            TextAlign = ContentAlignment.MiddleCenter
        };
        panel.Controls.Add(_preview);
        panel.Controls.Add(nav);
        panel.Controls.Add(hint);
        return panel;
    }

    private Control BuildCommandBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2)
        };
        bar.Controls.Add(Button("Load setup", (_, _) => LoadSetup(), 95));
        bar.Controls.Add(Button("Save setup", (_, _) => SaveSetup(), 95));
        bar.Controls.Add(new Label { Width = 20 });
        bar.Controls.Add(Button("Preview sheet", (_, _) => PreviewCurrent(), 110));
        bar.Controls.Add(Button("Print sheet", (_, _) => PrintCurrent(), 100));
        bar.Controls.Add(Button("Preview all", (_, _) => PreviewAll(), 100));
        bar.Controls.Add(Button("Print all", (_, _) => PrintAll(), 90));
        bar.Controls.Add(Button("Close", (_, _) => Close(), 80));
        return bar;
    }

    private void WireEvents()
    {
        foreach (var numeric in new[] { _rows, _columns, _marginLeft, _marginTop, _pitchX, _pitchY, _startSlot })
            numeric.ValueChanged += (_, _) => SettingsChanged();
        _fillDirection.SelectedIndexChanged += (_, _) => SettingsChanged();
        _side.SelectedIndexChanged += (_, _) => SettingsChanged();
        _cutMarks.CheckedChanged += (_, _) => SettingsChanged();
        _copies.ValueChanged += (_, _) =>
        {
            if (!_useDatabase.Checked) ReloadRecords();
        };
        _useDatabase.CheckedChanged += (_, _) =>
        {
            _copies.Enabled = !_useDatabase.Checked;
            ReloadRecords();
        };
        _preview.StartSlotSelected += slot =>
        {
            _startSlot.Value = Math.Clamp(slot + 1, (int)_startSlot.Minimum, (int)_startSlot.Maximum);
        };
    }

    private void ApplySettingsToControls()
    {
        _syncingControls = true;
        try
        {
            _rows.Value = Clamp(_settings.Rows, _rows);
            _columns.Value = Clamp(_settings.Columns, _columns);
            _marginLeft.Value = Clamp((decimal)_settings.MarginLeftMm, _marginLeft);
            _marginTop.Value = Clamp((decimal)_settings.MarginTopMm, _marginTop);
            _pitchX.Value = Clamp((decimal)_settings.HorizontalPitchMm, _pitchX);
            _pitchY.Value = Clamp((decimal)_settings.VerticalPitchMm, _pitchY);
            _fillDirection.SelectedIndex = _settings.FillDirection == SequenceFillDirection.Vertical ? 1 : 0;
            UpdateStartSlotMaximum();
            _startSlot.Value = Clamp(_settings.StartSlot + 1, _startSlot);
            _side.SelectedIndex = _settings.Side switch
            {
                LayoutSide.Back => 1,
                LayoutSide.Unknown => 2,
                _ => 0
            };
            _cutMarks.Checked = _settings.DrawCutMarks;
            _copies.Enabled = !_useDatabase.Checked;
        }
        finally
        {
            _syncingControls = false;
        }
        UpdatePreview();
    }

    private void SettingsChanged()
    {
        if (_syncingControls) return;
        _syncingControls = true;
        try
        {
            UpdateStartSlotMaximum();
            _settings.Rows = (int)_rows.Value;
            _settings.Columns = (int)_columns.Value;
            _settings.MarginLeftMm = (double)_marginLeft.Value;
            _settings.MarginTopMm = (double)_marginTop.Value;
            _settings.HorizontalPitchMm = (double)_pitchX.Value;
            _settings.VerticalPitchMm = (double)_pitchY.Value;
            _settings.FillDirection = _fillDirection.SelectedIndex == 1
                ? SequenceFillDirection.Vertical
                : SequenceFillDirection.Horizontal;
            _settings.StartSlot = (int)_startSlot.Value - 1;
            _settings.Side = _side.SelectedIndex switch
            {
                1 => LayoutSide.Back,
                2 => LayoutSide.Unknown,
                _ => LayoutSide.Front
            };
            _settings.DrawCutMarks = _cutMarks.Checked;
        }
        finally
        {
            _syncingControls = false;
        }
        UpdatePreview();
    }

    private void UpdateStartSlotMaximum()
    {
        var capacity = Math.Max(1, (int)_rows.Value * (int)_columns.Value);
        _startSlot.Maximum = capacity;
        if (_startSlot.Value > capacity) _startSlot.Value = capacity;
    }

    private void ReloadRecords()
    {
        _records = new List<IReadOnlyDictionary<string, object?>>();
        if (_useDatabase.Checked)
        {
            var state = ManagedBindingStore.LoadState(_layout);
            _bindings.Clear();
            foreach (var pair in state.Bindings) _bindings[pair.Key] = pair.Value;
            var path = FirstNotBlank(state.DatabasePath, _layout.DatabasePath);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    using var source = LegacyRecordSourceFactory.Open(path);
                    var sql = FirstNotBlank(state.Sql, _layout.Sql);
                    DataTable table;
                    if (!string.IsNullOrWhiteSpace(sql))
                        table = source.ExecuteQuery(sql);
                    else
                    {
                        var tableName = !string.IsNullOrWhiteSpace(state.Table)
                            ? state.Table
                            : source.GetTableNames().FirstOrDefault();
                        table = string.IsNullOrWhiteSpace(tableName) ? new DataTable() : source.OpenTable(tableName);
                    }
                    _records = table.Rows.Cast<DataRow>()
                        .Select(LegacyRecordBinder.Snapshot)
                        .Cast<IReadOnlyDictionary<string, object?>>()
                        .ToList();
                    _dataStatus.Text = $"{Path.GetFileName(path)} — {_records.Count} record(s) via {source.ProviderDescription}.";
                    _status.Text = $"Loaded {_records.Count} sequence record(s)";
                    _sheetIndex = 0;
                    UpdatePreview();
                    return;
                }
                catch (Exception ex)
                {
                    _dataStatus.Text = "Database load failed: " + ex.Message;
                    _status.Text = "Database load failed; template copies are available by disabling database records";
                    UpdatePreview();
                    return;
                }
            }

            _dataStatus.Text = "No database is configured for this layout. Disable database records to print template copies.";
            _status.Text = "No database configured";
            UpdatePreview();
            return;
        }

        var empty = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        _records = Enumerable.Range(0, (int)_copies.Value).Select(_ => empty).ToList();
        _dataStatus.Text = $"Template-only sequence — {_records.Count} copy/copies.";
        _status.Text = $"Prepared {_records.Count} template copy/copies";
        _sheetIndex = 0;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var (pageWidth, pageHeight) = _printService.GetPageSizeMm();
        _preview.Layout = _layout;
        _preview.Settings = _settings;
        _preview.PageWidthMm = pageWidth;
        _preview.PageHeightMm = pageHeight;
        _preview.RecordCount = _records.Count;

        var sheets = SheetCount();
        if (sheets == 0) _sheetIndex = 0;
        else _sheetIndex = Math.Clamp(_sheetIndex, 0, sheets - 1);
        _preview.SheetIndex = _sheetIndex;
        _pageLabel.Text = sheets == 0 ? "  Sheet 0 / 0  " : $"  Sheet {_sheetIndex + 1} / {sheets}  ";

        try
        {
            var extent = SequencePrintPlanner.GetUsedExtent(_layout.WidthMm, _layout.HeightMm, _settings);
            var fits = extent.WidthMm <= pageWidth + 0.01 && extent.HeightMm <= pageHeight + 0.01;
            if (!fits)
                _status.Text = $"Warning: sequence extent {extent.WidthMm:0.##} x {extent.HeightMm:0.##} mm exceeds paper {pageWidth:0.##} x {pageHeight:0.##} mm";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private int SheetCount() => SequencePrintPlanner.GetSheetCount(_records.Count, _settings);

    private void MoveSheet(int index)
    {
        var count = SheetCount();
        if (count == 0) return;
        _sheetIndex = Math.Clamp(index, 0, count - 1);
        UpdatePreview();
    }

    private void PageSetup()
    {
        _printService.ShowPageSetup(this);
        UpdatePreview();
    }

    private void LoadSetup()
    {
        _settings = ManagedSequenceStore.Load(_layout);
        _sheetIndex = 0;
        ApplySettingsToControls();
        _status.Text = "Sequence setup loaded";
    }

    private void SaveSetup()
    {
        if (TryPersistSettings(silent: false)) _status.Text = "Sequence setup saved";
    }

    private bool TryPersistSettings(bool silent)
    {
        try
        {
            ManagedSequenceStore.Save(_layout, _settings);
            return true;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(this, ex.Message, "Save sequence setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
    }

    private void PreviewCurrent() => RunPrint(() =>
        _printService.ShowPreviewSheet(this, _layout, RequireRecords(), _bindings, _settings, _sheetIndex));

    private void PrintCurrent() => RunPrint(() =>
        _printService.PrintSheet(this, _layout, RequireRecords(), _bindings, _settings, _sheetIndex));

    private void PreviewAll() => RunPrint(() =>
        _printService.ShowPreview(this, _layout, RequireRecords(), _bindings, _settings));

    private void PrintAll() => RunPrint(() =>
        _printService.Print(this, _layout, RequireRecords(), _bindings, _settings));

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> RequireRecords()
    {
        if (_records.Count == 0)
            throw new InvalidOperationException("There are no records/copies to print. Reload database records or disable database records and choose a template copy count.");
        return _records;
    }

    private void RunPrint(Action action)
    {
        try
        {
            _settings.Validate(_layout.WidthMm, _layout.HeightMm);
            TryPersistSettings(silent: true);
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sequence printing", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void AddSetting(TableLayoutPanel panel, ref int row, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row++);
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Height = 28,
        Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        DecimalPlaces = decimals,
        Increment = decimals == 0 ? 1 : 0.5m,
        ThousandsSeparator = false
    };

    private static Button Button(string text, EventHandler click, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 28, Margin = new Padding(3) };
        button.Click += click;
        return button;
    }

    private static decimal Clamp(decimal value, NumericUpDown control) =>
        Math.Min(control.Maximum, Math.Max(control.Minimum, value));

    private static string? FirstNotBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;
}
