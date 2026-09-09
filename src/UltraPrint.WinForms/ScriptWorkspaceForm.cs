using UltraPrint.Core.Models;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

public sealed class ScriptWorkspaceForm : Form
{
    private readonly CardLayout? _layout;
    private readonly RichTextBox _code = new()
    {
        Dock = DockStyle.Fill,
        AcceptsTab = true,
        Font = new Font("Consolas", 10),
        WordWrap = false
    };
    private readonly RichTextBox _diagnostics = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Font = new Font("Consolas", 9),
        WordWrap = false
    };
    private readonly ListBox _scripts = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _objects = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _event = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _enableExecution = new()
    {
        AutoSize = true,
        Text = "Enable execution for this session"
    };
    private readonly CheckBox _allowScriptUi = new()
    {
        AutoSize = true,
        Text = "Allow script UI"
    };
    private readonly Label _engineStatus = new()
    {
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ToolStripStatusLabel _status = new("Ready");

    private LegacyScriptSession? _session;
    private string? _sourcePath;
    private bool _compiled;
    private bool _executionWarningAccepted;

    public ScriptWorkspaceForm(CardLayout? layout)
    {
        _layout = layout;
        Text = "UltraPrint legacy VBScript";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(850, 560);
        StartPosition = FormStartPosition.CenterParent;

        _event.Items.AddRange(LegacyScriptContract.LifecycleEvents.Cast<object>().ToArray());
        _event.SelectedItem = "Load";
        foreach (var name in LegacyScriptContract.ObjectNames) _objects.Items.Add(name);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        root.Controls.Add(BuildCommandPanel(), 0, 0);
        root.Controls.Add(BuildEditorPanel(), 0, 1);
        root.Controls.Add(BuildDiagnosticsPanel(), 0, 2);

        var status = new StatusStrip();
        status.Items.Add(_status);
        Controls.Add(root);
        Controls.Add(status);

        _code.TextChanged += (_, _) =>
        {
            _compiled = false;
            UpdateTitle();
        };
        _scripts.DoubleClick += (_, _) => OpenSelectedDiscoveredScript();
        _allowScriptUi.CheckedChanged += (_, _) => DisposeSession();
        Shown += (_, _) =>
        {
            UpdateEngineStatus();
            DiscoverScripts();
        };
        FormClosed += (_, _) => DisposeSession();
        UpdateTitle();
    }

    private Control BuildCommandPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(6)
        };

        panel.Controls.Add(Button("Open...", (_, _) => OpenScript()));
        panel.Controls.Add(Button("Save", (_, _) => SaveScript(saveAs: false)));
        panel.Controls.Add(Button("Save As...", (_, _) => SaveScript(saveAs: true)));
        panel.Controls.Add(Button("Discover", (_, _) => DiscoverScripts()));
        panel.Controls.Add(Button("Prepared code...", (_, _) => ShowPreparedCode()));
        panel.Controls.Add(new Label { AutoSize = true, Text = "   Event:", Margin = new Padding(8, 8, 2, 0) });
        panel.Controls.Add(_event);
        panel.Controls.Add(Button("Compile", (_, _) => CompileCurrent()));
        panel.Controls.Add(Button("Run event", (_, _) => RunSelectedEvent()));
        panel.Controls.Add(_enableExecution);
        panel.Controls.Add(_allowScriptUi);
        panel.Controls.Add(new Label { AutoSize = true, Text = "   " });
        panel.Controls.Add(_engineStatus);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 820
        };
        split.Panel1.Controls.Add(_code);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var scriptsTab = new TabPage("Discovered scripts");
        scriptsTab.Controls.Add(_scripts);
        var objectsTab = new TabPage("Recovered AddObjects names");
        objectsTab.Controls.Add(_objects);
        tabs.TabPages.Add(scriptsTab);
        tabs.TabPages.Add(objectsTab);
        split.Panel2.Controls.Add(tabs);
        return split;
    }

    private Control BuildDiagnosticsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "  Script diagnostics",
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_diagnostics);
        panel.Controls.Add(label);
        return panel;
    }

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { AutoSize = true, Text = text, Margin = new Padding(3) };
        button.Click += click;
        return button;
    }

    private void UpdateEngineStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            _engineStatus.Text = "Script engine: Windows only";
            return;
        }

        _engineStatus.Text = MsScriptControlEngine.IsAvailable(out var reason)
            ? "Script engine: MSSCRIPT available"
            : "Script engine: unavailable";
        _engineStatus.ToolTipText(reason);
        if (!string.IsNullOrWhiteSpace(reason)) AppendDiagnostic(reason);
    }

    private void OpenScript()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "VBScript (*.vbs)|*.vbs|All files (*.*)|*.*",
            Title = "Open legacy UltraPrint script",
            InitialDirectory = GetInitialDirectory()
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName);
    }

    private void OpenSelectedDiscoveredScript()
    {
        if (_scripts.SelectedItem is not string path || !File.Exists(path)) return;
        LoadFile(path);
    }

    private void LoadFile(string path)
    {
        try
        {
            _code.Text = File.ReadAllText(path, System.Text.Encoding.Default);
            _sourcePath = Path.GetFullPath(path);
            _compiled = false;
            DisposeSession();
            UpdateTitle();
            _status.Text = $"Loaded {Path.GetFileName(path)}";
            AppendDiagnostic($"Loaded: {_sourcePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open script", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveScript(bool saveAs)
    {
        var target = _sourcePath;
        if (saveAs || string.IsNullOrWhiteSpace(target))
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "VBScript (*.vbs)|*.vbs|All files (*.*)|*.*",
                Title = "Save legacy UltraPrint script",
                FileName = target is null ? "Script.vbs" : Path.GetFileName(target),
                InitialDirectory = target is null ? GetInitialDirectory() : Path.GetDirectoryName(target),
                AddExtension = true,
                DefaultExt = "vbs"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            target = dialog.FileName;
        }

        try
        {
            File.WriteAllText(target!, _code.Text, System.Text.Encoding.Default);
            _sourcePath = Path.GetFullPath(target!);
            UpdateTitle();
            _status.Text = $"Saved {Path.GetFileName(_sourcePath)}";
            AppendDiagnostic($"Saved raw script source: {_sourcePath}");
            DiscoverScripts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save script", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DiscoverScripts()
    {
        try
        {
            var paths = new List<string>();
            foreach (var root in GetCandidateApplicationRoots())
            {
                var context = new LegacyScriptPathContext(root, _layout?.ScriptApplication);
                paths.AddRange(LegacyScriptPathResolver.Discover(context));
            }

            var unique = paths.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            _scripts.BeginUpdate();
            _scripts.Items.Clear();
            _scripts.Items.AddRange(unique.Cast<object>().ToArray());
            _scripts.EndUpdate();
            _status.Text = $"Discovered {unique.Length} legacy script(s)";
        }
        catch (Exception ex)
        {
            AppendDiagnostic("Script discovery failed: " + ex.Message);
        }
    }

    private void ShowPreparedCode()
    {
        var prepared = LegacyScriptCodePreprocessor.Prepare(_code.Text);
        using var dialog = new Form
        {
            Text = "Prepared VBScript — recovered PreparaCodice subset",
            Width = 900,
            Height = 650,
            StartPosition = FormStartPosition.CenterParent
        };
        dialog.Controls.Add(new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            Font = new Font("Consolas", 10),
            Text = prepared
        });
        dialog.ShowDialog(this);
    }

    private bool CompileCurrent()
    {
        if (!EnsureExecutionAllowed()) return false;
        DisposeSession();

        if (!MsScriptControlEngine.TryCreate(out var engine, out var error, _allowScriptUi.Checked) || engine is null)
        {
            var message = error ?? "Microsoft Script Control is unavailable.";
            AppendDiagnostic(message);
            MessageBox.Show(this, message, "Legacy VBScript", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            _session = new LegacyScriptSession(engine);
            _session.Load(_code.Text, _sourcePath, prepareLegacyCode: true);
            _compiled = true;
            _status.Text = "VBScript compiled";
            AppendDiagnostic($"Compiled with {_session.EngineDescription}.");
            AppendDiagnostic("No managed AddObjects facades are registered yet; scripts using recovered UltraPrint objects may fail at runtime.");
            return true;
        }
        catch (LegacyScriptException ex)
        {
            engine.Dispose();
            _session = null;
            ShowScriptDiagnostic(ex.Diagnostic, "Compile failed");
            return false;
        }
        catch (Exception ex)
        {
            engine.Dispose();
            _session = null;
            AppendDiagnostic("Compile failed: " + ex.Message);
            return false;
        }
    }

    private void RunSelectedEvent()
    {
        if (!_compiled && !CompileCurrent()) return;
        if (_session is null) return;
        var eventName = Convert.ToString(_event.SelectedItem) ?? "Load";
        try
        {
            var result = _session.Invoke(eventName);
            _status.Text = $"Executed {eventName}";
            AppendDiagnostic($"Executed {eventName}; result = {result ?? "<null>"}");
        }
        catch (LegacyScriptException ex)
        {
            ShowScriptDiagnostic(ex.Diagnostic, $"{eventName} failed");
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"{eventName} failed: {ex.Message}");
        }
    }

    private bool EnsureExecutionAllowed()
    {
        if (!_enableExecution.Checked)
        {
            MessageBox.Show(
                this,
                "Legacy script execution is disabled for this session. Enable it explicitly to compile or run VBScript.",
                "Legacy VBScript",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        if (_executionWarningAccepted) return true;
        var answer = MessageBox.Show(
            this,
            "Legacy UltraPrint VBScript is executable code and can call COM/OS functionality. Run only scripts you trust. Continue for this session?",
            "Enable legacy VBScript execution",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            _enableExecution.Checked = false;
            return false;
        }

        _executionWarningAccepted = true;
        return true;
    }

    private void ShowScriptDiagnostic(LegacyScriptDiagnostic diagnostic, string caption)
    {
        AppendDiagnostic(caption + ": " + diagnostic);
        if (diagnostic.Line > 0) SelectLine(diagnostic.Line, diagnostic.Column);
        _status.Text = caption;
    }

    private void SelectLine(int oneBasedLine, int oneBasedColumn)
    {
        if (oneBasedLine <= 0) return;
        var lines = _code.Lines;
        var lineIndex = Math.Min(lines.Length - 1, oneBasedLine - 1);
        if (lineIndex < 0) return;
        var position = _code.GetFirstCharIndexFromLine(lineIndex);
        if (position < 0) return;
        position += Math.Max(0, oneBasedColumn - 1);
        position = Math.Min(position, _code.TextLength);
        _code.SelectionStart = position;
        _code.SelectionLength = 0;
        _code.ScrollToCaret();
        _code.Focus();
    }

    private void AppendDiagnostic(string text)
    {
        if (_diagnostics.TextLength > 0) _diagnostics.AppendText(Environment.NewLine);
        _diagnostics.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}");
    }

    private IEnumerable<string> GetCandidateApplicationRoots()
    {
        var current = Path.GetFullPath(AppContext.BaseDirectory);
        yield return current;

        if (_layout?.SourcePath is not { Length: > 0 } layoutPath) yield break;
        var layoutDirectory = Path.GetDirectoryName(Path.GetFullPath(layoutPath));
        if (layoutDirectory is null) yield break;
        if (string.Equals(Path.GetFileName(layoutDirectory), "Ly", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(layoutDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && !string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                yield return parent;
        }
    }

    private string? GetInitialDirectory()
    {
        var firstRoot = GetCandidateApplicationRoots().FirstOrDefault();
        if (firstRoot is null) return null;
        var scriptDirectory = Path.Combine(firstRoot, "Script");
        return Directory.Exists(scriptDirectory) ? scriptDirectory : firstRoot;
    }

    private void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
        _compiled = false;
    }

    private void UpdateTitle()
    {
        Text = _sourcePath is null
            ? "UltraPrint legacy VBScript"
            : $"UltraPrint legacy VBScript — {Path.GetFileName(_sourcePath)}";
    }
}

internal static class LabelExtensions
{
    public static void ToolTipText(this Control control, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var toolTip = new ToolTip();
        toolTip.SetToolTip(control, text);
    }
}
