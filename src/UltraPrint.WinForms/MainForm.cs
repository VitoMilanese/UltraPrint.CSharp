using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

public sealed class MainForm : Form
{
    private readonly LegacyStartupOptions _startupOptions;
    private readonly UltraPrint22115LayoutCodec _codec = new();
    private readonly LayoutPrintService _printService = new();
    private readonly LayoutCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly ListBox _fields = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = true };
    private readonly RichTextBox _diagnostics = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
    private readonly DataGridView _values = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _sideCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };

    private CardLayout? _layout;
    private string? _layoutPath;
    private bool _dirty;
    private bool _syncingSelection;

    public MainForm(LegacyStartupOptions startupOptions)
    {
        _startupOptions = startupOptions;
        Text = "UltraPrint";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var menu = BuildMenu();
        var editorTab = new TabPage("Layout editor");
        editorTab.Controls.Add(BuildEditor());

        var valuesTab = new TabPage("Campo.ini current values");
        valuesTab.Controls.Add(_values);

        var diagTab = new TabPage("Compatibility diagnostics");
        diagTab.Controls.Add(_diagnostics);

        _tabs.TabPages.Add(editorTab);
        _tabs.TabPages.Add(valuesTab);
        _tabs.TabPages.Add(diagTab);

        var status = new StatusStrip();
        status.Items.Add(_status);

        Controls.Add(_tabs);
        Controls.Add(status);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _fields.SelectedIndexChanged += FieldsOnSelectedIndexChanged;
        _canvas.SelectedFieldChanged += CanvasOnSelectedFieldChanged;
        _canvas.FieldChanged += (_, _) => MarkModified(refreshProperties: true);
        _properties.PropertyValueChanged += (_, _) =>
        {
            _canvas.InvalidateAssets();
            MarkModified(refreshProperties: false);
        };
        _sideCombo.SelectedIndexChanged += (_, _) => ChangeSideFromCombo();

        Shown += (_, _) => HandleStartupOptions();
        FormClosing += OnFormClosing;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(Item("Open layout (.ly)...", Keys.Control | Keys.O, (_, _) => OpenLayout()));
        file.DropDownItems.Add(Item("Save", Keys.Control | Keys.S, (_, _) => SaveLayout(false)));
        file.DropDownItems.Add(Item("Save As...", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveLayout(true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Print Preview", Keys.Control | Keys.Shift | Keys.P, (_, _) => PrintPreview()));
        file.DropDownItems.Add(Item("Print...", Keys.Control | Keys.P, (_, _) => PrintLayout()));
        file.DropDownItems.Add(Item("Page Setup...", Keys.None, (_, _) => _printService.ShowPageSetup(this)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Layout Properties...", Keys.Alt | Keys.Enter, (_, _) => EditLayoutProperties()));
        file.DropDownItems.Add(Item("Open Campo.ini...", Keys.None, (_, _) => OpenCampo()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Close", Keys.Control | Keys.W, (_, _) => CloseLayout()));
        file.DropDownItems.Add(Item("Exit", Keys.Alt | Keys.F4, (_, _) => Close()));

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add(Item("Duplicate Field", Keys.Control | Keys.D, (_, _) => DuplicateSelectedField()));
        edit.DropDownItems.Add(Item("Delete Field", Keys.Delete, (_, _) => DeleteSelectedField()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("Bring to Front", Keys.Control | Keys.Shift | Keys.OemCloseBrackets, (_, _) => MoveSelectedToFront()));
        edit.DropDownItems.Add(Item("Send to Back", Keys.Control | Keys.Shift | Keys.OemOpenBrackets, (_, _) => MoveSelectedToBack()));

        var insert = new ToolStripMenuItem("Insert");
        insert.DropDownItems.Add(Item("Text Field", Keys.Control | Keys.T, (_, _) => InsertTextField()));
        insert.DropDownItems.Add(Item("Image Field...", Keys.Control | Keys.I, (_, _) => InsertImageField(photoPlaceholder: false)));
        insert.DropDownItems.Add(Item("Photo Placeholder...", Keys.Control | Keys.Shift | Keys.I, (_, _) => InsertImageField(photoPlaceholder: true)));

        var view = new ToolStripMenuItem("View");
        view.DropDownItems.Add(Item("Front", Keys.Control | Keys.D1, (_, _) => SetSide(LayoutSide.Front)));
        view.DropDownItems.Add(Item("Back", Keys.Control | Keys.D2, (_, _) => SetSide(LayoutSide.Back)));
        view.DropDownItems.Add(Item("All sides", Keys.Control | Keys.D3, (_, _) => SetSide(LayoutSide.Unknown)));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("Refresh", Keys.F5, (_, _) =>
        {
            _canvas.InvalidateAssets();
            RefreshFieldList();
        }));

        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add(Item("Campo.ini current values", Keys.None, (_, _) => _tabs.SelectedIndex = 1));
        tools.DropDownItems.Add(Item("Compatibility diagnostics", Keys.None, (_, _) => _tabs.SelectedIndex = 2));

        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add(Item("About UltraPrint", Keys.None, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, insert, view, tools, help });
        return menu;
    }

    private Control BuildEditor()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

        toolbar.Items.Add(Button("Text", (_, _) => InsertTextField(), "Insert text field"));
        toolbar.Items.Add(Button("Image", (_, _) => InsertImageField(photoPlaceholder: false), "Insert image field"));
        toolbar.Items.Add(Button("Photo", (_, _) => InsertImageField(photoPlaceholder: true), "Insert photo placeholder"));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(Button("Duplicate", (_, _) => DuplicateSelectedField(), "Duplicate selected field"));
        toolbar.Items.Add(Button("Delete", (_, _) => DeleteSelectedField(), "Delete selected field"));
        toolbar.Items.Add(Button("Front +", (_, _) => MoveSelectedToFront(), "Bring selected field to front"));
        toolbar.Items.Add(Button("Back -", (_, _) => MoveSelectedToBack(), "Send selected field to back"));
        toolbar.Items.Add(new ToolStripSeparator());

        toolbar.Items.Add(new ToolStripLabel("Side:"));
        _sideCombo.Items.AddRange(new object[] { "Front", "Back", "All" });
        _sideCombo.SelectedIndex = 0;
        toolbar.Items.Add(_sideCombo);
        toolbar.Items.Add(new ToolStripSeparator());

        var gridButton = new ToolStripButton("Grid") { CheckOnClick = true, Checked = true, ToolTipText = "Show 1 mm layout grid" };
        gridButton.CheckedChanged += (_, _) => _canvas.ShowGrid = gridButton.Checked;
        toolbar.Items.Add(gridButton);

        var snapButton = new ToolStripButton("Snap 1 mm") { CheckOnClick = true, ToolTipText = "Snap moved/resized fields to the 1 mm grid" };
        snapButton.CheckedChanged += (_, _) => _canvas.SnapToGrid = snapButton.Checked;
        toolbar.Items.Add(snapButton);

        var previewButton = new ToolStripButton("Preview") { CheckOnClick = true, ToolTipText = "Hide editor grid and selection handles" };
        previewButton.CheckedChanged += (_, _) =>
        {
            _canvas.PreviewMode = previewButton.Checked;
            _status.Text = previewButton.Checked ? "Preview mode" : "Edit mode";
        };
        toolbar.Items.Add(previewButton);

        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Drag = move; handles = resize; arrows 0.1 mm; Shift 1 mm; Ctrl 0.01 mm"));

        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 260,
            FixedPanel = FixedPanel.Panel1
        };

        var fieldPanel = new Panel { Dock = DockStyle.Fill };
        var fieldLabel = new Label { Dock = DockStyle.Top, Height = 26, Text = "  Fields", TextAlign = ContentAlignment.MiddleLeft };
        fieldPanel.Controls.Add(_fields);
        fieldPanel.Controls.Add(fieldLabel);
        outer.Panel1.Controls.Add(fieldPanel);

        var right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 690
        };
        right.Panel1.Controls.Add(_canvas);

        var propPanel = new Panel { Dock = DockStyle.Fill };
        var propLabel = new Label { Dock = DockStyle.Top, Height = 26, Text = "  Properties", TextAlign = ContentAlignment.MiddleLeft };
        propPanel.Controls.Add(_properties);
        propPanel.Controls.Add(propLabel);
        right.Panel2.Controls.Add(propPanel);
        outer.Panel2.Controls.Add(right);

        root.Controls.Add(outer);
        root.Controls.Add(toolbar);
        return root;
    }

    private void HandleStartupOptions()
    {
        TryLoadDefaultCampo();
        if (_startupOptions.ErasePassword)
            EraseLegacyPassword();

        if (_startupOptions.Layout is { Length: > 0 } path && File.Exists(path))
            LoadLayout(path);

        if (_startupOptions.ErasePassword || _startupOptions.PrintForm || _startupOptions.UnknownArguments.Count > 0)
        {
            _diagnostics.AppendText("Recovered legacy command-line options:\r\n");
            _diagnostics.AppendText($"/erasepw = {_startupOptions.ErasePassword}\r\n");
            _diagnostics.AppendText($"/printform = {_startupOptions.PrintForm}\r\n");
            _diagnostics.AppendText($"/Layout = {_startupOptions.Layout ?? "<none>"}\r\n");
            if (_startupOptions.UnknownArguments.Count > 0)
                _diagnostics.AppendText("Unknown: " + string.Join(" ", _startupOptions.UnknownArguments) + "\r\n");
            _diagnostics.AppendText("\r\n");
        }

        if (_startupOptions.PrintForm && _layout is not null)
            BeginInvoke((Action)PrintLayout);
    }

    private void OpenLayout()
    {
        if (!ConfirmDiscardChanges()) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "UltraPrint layout (*.ly)|*.ly|All files (*.*)|*.*",
            Title = "Open UltraPrint layout"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadLayout(dialog.FileName);
    }

    private void LoadLayout(string path)
    {
        try
        {
            _layout = _codec.Load(path);
            _layoutPath = Path.GetFullPath(path);
            _dirty = false;
            _canvas.Layout = _layout;
            SetSide(LayoutSide.Front);
            RefreshFieldList();
            RefreshDiagnostics(path);

            _tabs.SelectedIndex = 0;
            UpdateTitle();
            _status.Text = $"Loaded {Path.GetFileName(path)} — {_layout.WidthMm:0.###} x {_layout.HeightMm:0.###} mm, {_layout.Dpi} DPI, {_layout.Fields.Count} fields";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Layout load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveLayout(bool saveAs)
    {
        if (_layout is null)
        {
            MessageBox.Show(this, "Open a .ly file first.", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var target = _layoutPath;
        if (saveAs || string.IsNullOrWhiteSpace(target))
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "UltraPrint layout (*.ly)|*.ly|All files (*.*)|*.*",
                Title = "Save UltraPrint layout",
                FileName = target is null ? _layout.Name + ".ly" : Path.GetFileName(target),
                InitialDirectory = target is null ? null : Path.GetDirectoryName(target)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            target = dialog.FileName;
        }

        try
        {
            _codec.Save(_layout, target!);
            _layoutPath = Path.GetFullPath(target!);
            _dirty = false;
            UpdateTitle();
            RefreshDiagnostics(_layoutPath);
            _status.Text = $"Saved {Path.GetFileName(_layoutPath)} — legacy unknown bytes preserved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Layout save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseLayout()
    {
        if (!ConfirmDiscardChanges()) return;
        _layout = null;
        _layoutPath = null;
        _dirty = false;
        _canvas.Layout = null;
        _fields.DataSource = null;
        _properties.SelectedObject = null;
        UpdateTitle();
        _status.Text = "Ready";
    }

    private void InsertTextField()
    {
        if (_layout is null) return;
        try
        {
            var field = _codec.CreateField(_layout, LayoutFieldKind.Text, CurrentInsertSide(), legacyTypeCode: 3);
            SelectField(field);
            MarkModified(refreshProperties: true);
            _status.Text = $"Inserted {field.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Insert field failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InsertImageField(bool photoPlaceholder)
    {
        if (_layout is null || string.IsNullOrWhiteSpace(_layoutPath)) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Images|*.bmp;*.gif;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.pcx|All files (*.*)|*.*",
            Title = photoPlaceholder ? "Choose placeholder image" : "Choose image"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var field = _codec.CreateField(_layout, LayoutFieldKind.Image, CurrentInsertSide(), photoPlaceholder ? 6 : 5);
            field.Image.File = LegacyAssetResolver.ImportForLayout(_layoutPath, dialog.FileName);
            field.LegacyPayload = field.Image.File;
            field.Image.KeepAspectRatio = photoPlaceholder;
            _canvas.InvalidateAssets();
            SelectField(field);
            MarkModified(refreshProperties: true);
            _status.Text = $"Inserted {field.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Insert image failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DuplicateSelectedField()
    {
        if (_layout is null || _canvas.SelectedField is not { } selected) return;
        try
        {
            var clone = _codec.DuplicateField(_layout, selected);
            SelectField(clone);
            MarkModified(refreshProperties: true);
            _status.Text = $"Duplicated {selected.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Duplicate field failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedField()
    {
        if (_layout is null || _canvas.SelectedField is not { } selected) return;
        var answer = MessageBox.Show(this, $"Delete field '{selected.Name}'?", "Delete field",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        if (!_codec.DeleteField(_layout, selected)) return;
        _canvas.SelectedField = null;
        RefreshFieldList();
        MarkModified(refreshProperties: true);
        _status.Text = $"Deleted {selected.Name}";
    }

    private void MoveSelectedToFront()
    {
        if (_layout is null || _canvas.SelectedField is not { } selected) return;
        var sameSide = _layout.Fields.Where(x => x.Side == selected.Side).ToList();
        selected.Level = sameSide.Select(x => x.Level).DefaultIfEmpty(0).Max() + 1;
        NormalizeLevels(selected.Side);
        MarkModified(refreshProperties: true);
    }

    private void MoveSelectedToBack()
    {
        if (_layout is null || _canvas.SelectedField is not { } selected) return;
        var sameSide = _layout.Fields.Where(x => x.Side == selected.Side).ToList();
        selected.Level = sameSide.Select(x => x.Level).DefaultIfEmpty(0).Min() - 1;
        NormalizeLevels(selected.Side);
        MarkModified(refreshProperties: true);
    }

    private void NormalizeLevels(LayoutSide side)
    {
        if (_layout is null) return;
        var ordered = _layout.Fields.Where(x => x.Side == side).OrderBy(x => x.Level).ThenBy(x => x.Index).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Level = i;
    }

    private void EditLayoutProperties()
    {
        if (_layout is null) return;
        if (!LayoutSettingsDialog.Edit(this, _layout)) return;
        MarkModified(refreshProperties: false);
        _status.Text = $"Layout: {_layout.WidthMm:0.##} x {_layout.HeightMm:0.##} mm, {_layout.Dpi} DPI";
    }

    private void PrintPreview()
    {
        if (_layout is null) return;
        try { _printService.ShowPreview(this, _layout, _canvas.Side, _canvas); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Print preview failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PrintLayout()
    {
        if (_layout is null) return;
        try { _printService.Print(this, _layout, _canvas.Side, _canvas); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Print failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenCampo()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Campo.ini|Campo.ini|INI files (*.ini)|*.ini|All files (*.*)|*.*",
            Title = "Open legacy Campo.ini"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadCampo(dialog.FileName, selectTab: true);
    }

    private void LoadCampo(string path, bool selectTab)
    {
        try
        {
            var document = LegacyIniDocument.Load(path);
            var values = document.GetSection(CampoCurrentValueStore.CurrentSection)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { Key = x.Key, Value = x.Value.TrimEnd() })
                .ToList();
            _values.DataSource = values;
            var schema = CampoSchemaParser.ParseFile(path);
            if (selectTab) _tabs.SelectedIndex = 1;
            _status.Text = $"Campo.ini: {schema.Sections.Count} sections, {values.Count} current values";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Campo.ini read failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TryLoadDefaultCampo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Campo.ini");
        if (File.Exists(path)) LoadCampo(path, selectTab: false);
    }

    private void EraseLegacyPassword()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UP.ini");
        if (!File.Exists(path)) return;
        try
        {
            var ini = LegacyIniDocument.Load(path);
            ini.Set("Setup", "Pw", string.Empty);
            ini.Save(path);
            _status.Text = "Legacy UP.ini password cleared";
        }
        catch (Exception ex)
        {
            _diagnostics.AppendText($"/erasepw failed: {ex.Message}\r\n");
        }
    }

    private void ChangeSideFromCombo()
    {
        if (_syncingSelection) return;
        var side = _sideCombo.SelectedIndex switch
        {
            1 => LayoutSide.Back,
            2 => LayoutSide.Unknown,
            _ => LayoutSide.Front
        };
        SetSide(side, updateCombo: false);
    }

    private void SetSide(LayoutSide side, bool updateCombo = true)
    {
        if (updateCombo)
        {
            _syncingSelection = true;
            _sideCombo.SelectedIndex = side switch
            {
                LayoutSide.Back => 1,
                LayoutSide.Unknown => 2,
                _ => 0
            };
            _syncingSelection = false;
        }

        _canvas.Side = side;
        RefreshFieldList();
        _status.Text = side == LayoutSide.Unknown ? "Showing all layout sides" : $"Showing {side}";
    }

    private LayoutSide CurrentInsertSide()
    {
        if (_canvas.Side != LayoutSide.Unknown) return _canvas.Side;
        var selectedSide = _canvas.SelectedField?.Side ?? LayoutSide.Unknown;
        return selectedSide is LayoutSide.Front or LayoutSide.Back ? selectedSide : LayoutSide.Front;
    }

    private void RefreshFieldList()
    {
        if (_layout is null)
        {
            _fields.DataSource = null;
            _properties.SelectedObject = null;
            return;
        }

        var selected = _canvas.SelectedField;
        var side = _canvas.Side;
        var items = _layout.Fields
            .Where(field => side == LayoutSide.Unknown || field.Side == LayoutSide.Unknown || field.Side == side)
            .OrderBy(field => field.Level)
            .ThenBy(field => field.Index)
            .ToList();

        _syncingSelection = true;
        _fields.DataSource = null;
        _fields.DataSource = items;
        if (selected is not null && items.Contains(selected)) _fields.SelectedItem = selected;
        _syncingSelection = false;

        if (_fields.SelectedItem is null && items.Count > 0)
            _fields.SelectedIndex = 0;
    }

    private void SelectField(LayoutField field)
    {
        if (_canvas.Side != LayoutSide.Unknown && field.Side != LayoutSide.Unknown && field.Side != _canvas.Side)
            SetSide(field.Side);
        _canvas.SelectedField = field;
        RefreshFieldList();
        _fields.SelectedItem = field;
        _properties.SelectedObject = field;
    }

    private void FieldsOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection) return;
        var field = _fields.SelectedItem as LayoutField;
        _canvas.SelectedField = field;
        _properties.SelectedObject = field;
    }

    private void CanvasOnSelectedFieldChanged(object? sender, EventArgs e)
    {
        var field = _canvas.SelectedField;
        _properties.SelectedObject = field;
        if (field is null) return;

        _syncingSelection = true;
        _fields.SelectedItem = field;
        _syncingSelection = false;
        _status.Text = $"#{field.Index} {field.Name} — X {field.Xmm:0.###}, Y {field.Ymm:0.###}, W {field.WidthMm:0.###}, H {field.HeightMm:0.###} mm";
    }

    private void MarkModified(bool refreshProperties)
    {
        if (_layout is null) return;
        _dirty = true;
        _canvas.Invalidate();
        if (refreshProperties) _properties.Refresh();
        RefreshFieldListPreservingSelection();
        UpdateTitle();
    }

    private void RefreshFieldListPreservingSelection()
    {
        var selected = _canvas.SelectedField;
        if (selected is null) return;
        var index = _fields.Items.IndexOf(selected);
        if (index >= 0) _fields.Refresh();
    }

    private void RefreshDiagnostics(string path)
    {
        var probe = LegacyLayoutProbe.Probe(path);
        _diagnostics.Text = probe.ToReport();
        if (_layout is null) return;

        _diagnostics.AppendText("\r\nDecoded UltraPrint 2.2.115 fields:\r\n");
        foreach (var field in _layout.Fields.OrderBy(x => x.Index))
        {
            var payload = field.Kind == LayoutFieldKind.Image ? field.Image.File : field.Text.Content;
            _diagnostics.AppendText(
                $"  #{field.Index:00} {field.Side,-5} type={field.LegacyTypeCode} " +
                $"X={field.Xmm:0.###} Y={field.Ymm:0.###} W={field.WidthMm:0.###} H={field.HeightMm:0.###} " +
                $"L={field.Level} {field.Name} -> {payload}\r\n");
        }
    }

    private void UpdateTitle()
    {
        var file = _layoutPath is null ? string.Empty : " — " + Path.GetFileName(_layoutPath);
        Text = "UltraPrint" + file + (_dirty ? " *" : string.Empty);
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this, "The current layout has unsaved changes. Discard them?", "Unsaved changes",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return result == DialogResult.Yes;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges()) e.Cancel = true;
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "UltraPrint C# compatibility implementation\r\n\r\n" +
            "Target: functional parity with UltraPrint 2.2.115 while preserving legacy .ly/Campo.ini workflows.",
            "About UltraPrint", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler click)
    {
        var item = new ToolStripMenuItem(text, null, click);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        return item;
    }

    private static ToolStripButton Button(string text, EventHandler click, string toolTip)
    {
        var button = new ToolStripButton(text) { ToolTipText = toolTip, DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += click;
        return button;
    }
}
