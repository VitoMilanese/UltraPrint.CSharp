using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

public sealed class MainForm : Form
{
    private readonly LegacyStartupOptions _startupOptions;
    private readonly UltraPrint22115LayoutCodec _codec = new();
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
        Text = "UltraPrint C# Recovery";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var menu = BuildMenu();
        var editorTab = new TabPage("Layout editor");
        editorTab.Controls.Add(BuildEditor());

        var valuesTab = new TabPage("Campo.ini current values");
        valuesTab.Controls.Add(_values);

        var diagTab = new TabPage("Diagnostics");
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
        _properties.PropertyValueChanged += (_, _) => MarkModified(refreshProperties: false);
        _sideCombo.SelectedIndexChanged += (_, _) => ChangeSide();

        Shown += (_, _) => HandleStartupOptions();
        FormClosing += OnFormClosing;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Open layout (.ly)...", null, (_, _) => OpenLayout());
        file.DropDownItems.Add("Save", null, (_, _) => SaveLayout(false));
        file.DropDownItems.Add("Save As...", null, (_, _) => SaveLayout(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Open Campo.ini...", null, (_, _) => OpenCampo());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        menu.Items.Add(file);
        return menu;
    }

    private Control BuildEditor()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        toolbar.Items.Add(new ToolStripLabel("Side:"));
        _sideCombo.Items.AddRange(new object[] { "Front", "Back", "All" });
        _sideCombo.SelectedIndex = 0;
        toolbar.Items.Add(_sideCombo);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Drag fields on the card; edit exact values in Properties."));

        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 230,
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
            _canvas.Side = LayoutSide.Front;
            _sideCombo.SelectedIndex = 0;
            RefreshFieldList();

            var probe = LegacyLayoutProbe.Probe(path);
            _diagnostics.Text = probe.ToReport();
            _diagnostics.AppendText("\r\nDecoded UltraPrint 2.2.115 fields:\r\n");
            foreach (var field in _layout.Fields)
            {
                var payload = field.Kind == LayoutFieldKind.Image ? field.Image.File : field.Text.Content;
                _diagnostics.AppendText(
                    $"  #{field.Index:00} {field.Side,-5} type={field.LegacyTypeCode} " +
                    $"X={field.Xmm:0.###} Y={field.Ymm:0.###} W={field.WidthMm:0.###} H={field.HeightMm:0.###} " +
                    $"{field.Name} -> {payload}\r\n");
            }

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
            _status.Text = $"Saved {Path.GetFileName(_layoutPath)} — unknown legacy bytes preserved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Layout save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCampo()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Campo.ini|Campo.ini|INI files (*.ini)|*.ini|All files (*.*)|*.*",
            Title = "Open legacy Campo.ini"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var document = LegacyIniDocument.Load(dialog.FileName);
            var values = document.GetSection(CampoCurrentValueStore.CurrentSection)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { Key = x.Key, Value = x.Value.TrimEnd() })
                .ToList();
            _values.DataSource = values;
            var schema = CampoSchemaParser.ParseFile(dialog.FileName);
            _tabs.SelectedIndex = 1;
            _status.Text = $"Campo.ini: {schema.Sections.Count} sections, {values.Count} current values";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Campo.ini read failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangeSide()
    {
        var side = _sideCombo.SelectedIndex switch
        {
            1 => LayoutSide.Back,
            2 => LayoutSide.Unknown,
            _ => LayoutSide.Front
        };
        _canvas.Side = side;
        RefreshFieldList();
        _status.Text = side == LayoutSide.Unknown ? "Showing all layout sides" : $"Showing {side}";
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

    private void UpdateTitle()
    {
        var file = _layoutPath is null ? string.Empty : " — " + Path.GetFileName(_layoutPath);
        Text = "UltraPrint C# Recovery" + file + (_dirty ? " *" : string.Empty);
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
}
