namespace UltraPrint.WinForms;

internal static class DeviceIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;
        if (menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == "managedDevicesMenu")) return;

        var devices = new ToolStripMenuItem("Devices") { Name = "managedDevicesMenu" };
        devices.DropDownItems.Add(new ToolStripMenuItem("Printer / Device diagnostics...", null, (_, _) =>
        {
            using var dialog = new DeviceDiagnosticsForm();
            dialog.ShowDialog(form);
        }));

        var toolsIndex = menu.Items.OfType<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Text, "Tools", StringComparison.OrdinalIgnoreCase))?.index ?? menu.Items.Count;
        menu.Items.Insert(toolsIndex, devices);
    }
}
