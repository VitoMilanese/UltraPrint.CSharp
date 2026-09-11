using System.Drawing.Printing;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Devices;
using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

internal sealed class DeviceDiagnosticsForm : Form
{
    private readonly StartupPaths _paths = StartupPaths.FromBaseDirectory(AppContext.BaseDirectory);
    private readonly ComboBox _currentDevice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    private readonly Label _configPath = new() { AutoSize = true };
    private readonly Label _iceStatus = new() { AutoSize = true, MaximumSize = new Size(920, 0) };
    private readonly Label _architecture = new() { AutoSize = true };
    private readonly DataGridView _profiles = Grid();
    private readonly DataGridView _modules = Grid();
    private readonly DataGridView _runtime = Grid();
    private readonly ListBox _exports = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 9) };
    private LegacyDeviceConfiguration _configuration = new(null, Array.Empty<LegacyHardwareModuleDefinition>(), Array.Empty<LegacyDeviceProfile>());

    public DeviceDiagnosticsForm()
    {
        Text = "UltraPrint printer / device diagnostics";
        Width = 1120;
        Height = 820;
        MinimumSize = new Size(860, 640);
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(BuildUi());
        Shown += (_, _) => RefreshState();
        _profiles.SelectionChanged += (_, _) => RefreshSelectedProfile();
    }

    private Control BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 205));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        header.Controls.Add(new Label { AutoSize = true, Text = "Current device:", Margin = new Padding(3, 9, 3, 0) });
        header.Controls.Add(_currentDevice);
        header.Controls.Add(Button("Set current", (_, _) => SaveCurrentDevice()));
        header.Controls.Add(Button("Refresh", (_, _) => RefreshState()));
        header.Controls.Add(Button("Read ICE status", (_, _) => ReadIceStatus()));
        header.Controls.Add(new Label { AutoSize = true, Text = "   " });
        header.Controls.Add(_architecture);
        var headerStack = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        headerStack.Controls.Add(header, 0, 0);
        headerStack.Controls.Add(_configPath, 0, 1);
        headerStack.Controls.Add(_iceStatus, 0, 2);
        root.Controls.Add(headerStack, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 590 };
        var profilePanel = new Panel { Dock = DockStyle.Fill };
        profilePanel.Controls.Add(_profiles);
        profilePanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 26, Text = "  Legacy device profiles", TextAlign = ContentAlignment.MiddleLeft });
        split.Panel1.Controls.Add(profilePanel);
        var modulePanel = new Panel { Dock = DockStyle.Fill };
        modulePanel.Controls.Add(_modules);
        modulePanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 26, Text = "  Selected profile modules", TextAlign = ContentAlignment.MiddleLeft });
        split.Panel2.Controls.Add(modulePanel);
        root.Controls.Add(split, 0, 1);

        var runtimePanel = new Panel { Dock = DockStyle.Fill };
        runtimePanel.Controls.Add(_runtime);
        runtimePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  Read-only ICE printer state (errors are not cleared; polling is not changed)",
            TextAlign = ContentAlignment.MiddleLeft
        });
        root.Controls.Add(runtimePanel, 0, 2);

        var exportsPanel = new Panel { Dock = DockStyle.Fill };
        exportsPanel.Controls.Add(_exports);
        exportsPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "  Recovered ICE_API.DLL x86 exports (destructive calls are not invoked)",
            TextAlign = ContentAlignment.MiddleLeft
        });
        root.Controls.Add(exportsPanel, 0, 3);
        return root;
    }

    private void RefreshState()
    {
        _configuration = LegacyDeviceConfigurationStore.Load(_paths.CampoIni);
        _configPath.Text = "Campo.ini: " + _paths.CampoIni;
        _architecture.Text = $"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var printer in GetInstalledPrinters()) names.Add(printer);
        foreach (var profile in _configuration.Devices) names.Add(profile.Name);
        if (!string.IsNullOrWhiteSpace(_configuration.CurrentDevice)) names.Add(_configuration.CurrentDevice);

        var ordered = names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToArray();
        _currentDevice.BeginUpdate();
        _currentDevice.Items.Clear();
        _currentDevice.Items.AddRange(ordered);
        _currentDevice.EndUpdate();
        if (!string.IsNullOrWhiteSpace(_configuration.CurrentDevice))
            _currentDevice.SelectedItem = names.FirstOrDefault(x => x.Equals(_configuration.CurrentDevice, StringComparison.OrdinalIgnoreCase));
        if (_currentDevice.SelectedIndex < 0 && _currentDevice.Items.Count > 0) _currentDevice.SelectedIndex = 0;

        _profiles.Columns.Clear();
        _profiles.Rows.Clear();
        _profiles.Columns.Add("Device", "Device");
        _profiles.Columns.Add("Driver", "Driver enabled");
        _profiles.Columns.Add("Modules", "Enabled modules");
        _profiles.Columns.Add("ActiveX", "ActiveX DLL");
        foreach (var profile in _configuration.Devices)
        {
            var enabledModules = string.Join(", ", profile.Modules.Where(x => x.Enabled).Select(x => x.Key));
            var row = _profiles.Rows[_profiles.Rows.Add(profile.Name, profile.DriverEnabled ? "Yes" : "No", enabledModules, profile.ActiveXDll ?? string.Empty)];
            row.Tag = profile;
            if (string.Equals(profile.Name, _configuration.CurrentDevice, StringComparison.OrdinalIgnoreCase))
                row.Selected = true;
        }
        if (_profiles.SelectedRows.Count == 0 && _profiles.Rows.Count > 0) _profiles.Rows[0].Selected = true;

        _exports.BeginUpdate();
        _exports.Items.Clear();
        foreach (var export in LegacyIceApiProbe.Exports)
            _exports.Items.Add($"{export.NativeName,-38} stack={export.StackBytes,2}  {(export.ReadOnlyQuery ? "query" : "action")}");
        _exports.EndUpdate();

        ClearRuntime();
        var probe = LegacyIceApiProbe.Probe(_paths.BaseDirectory);
        _iceStatus.Text = "ICE_API: " + probe.Message;
        RefreshSelectedProfile();
    }

    private void ReadIceStatus()
    {
        if (_currentDevice.SelectedItem is not string device || string.IsNullOrWhiteSpace(device))
        {
            MessageBox.Show(this, "Select a printer/device first.", "ICE_API diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        UseWaitCursor = true;
        try
        {
            var snapshot = LegacyIceApiReader.ReadPrinter(device, _paths.BaseDirectory);
            _iceStatus.Text = "ICE_API: " + snapshot.Message;
            ClearRuntime();
            AddRuntime("Printer", snapshot.PrinterName);
            AddRuntime("Availability", snapshot.Availability.ToString());
            AddRuntime("API version", snapshot.ApiVersion ?? "<unavailable>");
            AddRuntime("Polling", FormatPolling(snapshot.PollingState));
            AddRuntime("Model", snapshot.ModelName ?? "<unavailable>");
            AddRuntime("Serial", snapshot.SerialNumber ?? "<unavailable>");
            AddRuntime("Magstripe head", snapshot.MagstripeHeadType ?? "<unavailable>");
            AddRuntime("Active job", snapshot.ActiveJobId?.ToString() ?? "<unavailable>");
            AddRuntime("First error", snapshot.FirstError ?? (snapshot.Availability == LegacyIceApiAvailability.Available ? "No Errors" : "<unavailable>"));
            AddRuntime("Warnings", snapshot.Warnings.Count == 0 ? "<none>" : string.Join(" | ", snapshot.Warnings));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ICE_API diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ClearRuntime()
    {
        _runtime.Columns.Clear();
        _runtime.Rows.Clear();
        _runtime.Columns.Add("Property", "Property");
        _runtime.Columns.Add("Value", "Value");
        _runtime.Columns[0].FillWeight = 25;
        _runtime.Columns[1].FillWeight = 75;
    }

    private void AddRuntime(string property, string value) => _runtime.Rows.Add(property, value);

    private static string FormatPolling(LegacyIcePrinterPollingState? state) => state switch
    {
        LegacyIcePrinterPollingState.Responding => "PRINTER IS RESPONDING",
        LegacyIcePrinterPollingState.NotResponding => "PRINTER IS NOT RESPONDING",
        LegacyIcePrinterPollingState.Suspended => "PRINTER IS SUSPENDED",
        _ => "<unavailable/unknown>"
    };

    private void RefreshSelectedProfile()
    {
        _modules.Columns.Clear();
        _modules.Rows.Clear();
        _modules.Columns.Add("Slot", "Slot");
        _modules.Columns.Add("Module", "Module");
        _modules.Columns.Add("Key", "Key");
        _modules.Columns.Add("Enabled", "Enabled");
        if (_profiles.SelectedRows.Count == 0 || _profiles.SelectedRows[0].Tag is not LegacyDeviceProfile profile) return;
        foreach (var module in profile.Modules)
            _modules.Rows.Add(module.Slot, module.DisplayName, module.Key, module.Enabled ? "Yes" : "No");
    }

    private void SaveCurrentDevice()
    {
        if (_currentDevice.SelectedItem is not string device || string.IsNullOrWhiteSpace(device)) return;
        try
        {
            LegacyDeviceConfigurationStore.SaveCurrentDevice(_paths.CampoIni, device);
            RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Set current device", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false
    };

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { AutoSize = true, Text = text, Margin = new Padding(3) };
        button.Click += click;
        return button;
    }
}
