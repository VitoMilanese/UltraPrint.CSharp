namespace UltraPrint.WinForms;

internal static class DeviceIntegration
{
    private const string DevicesMenuName = "managedDevicesMenu";
    private const string ChipPositionMenuItemName = "managedChipPositionMenuItem";

    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;

        var canvas = FindControl<LayoutCanvas>(form);
        var chipOverlay = canvas is null ? null : new LegacyChipPositionOverlay(canvas);

        var view = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "View", StringComparison.OrdinalIgnoreCase));
        if (view is not null && !view.DropDownItems.OfType<ToolStripItem>().Any(item => item.Name == ChipPositionMenuItemName))
        {
            var chipPosition = new ToolStripMenuItem("Chip position")
            {
                Name = ChipPositionMenuItemName,
                CheckOnClick = true,
                Checked = false
            };
            chipPosition.CheckedChanged += (_, _) =>
            {
                if (chipOverlay is not null)
                    chipOverlay.RequestedVisible = chipPosition.Checked;
            };
            view.DropDownItems.Add(chipPosition);
        }

        if (menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == DevicesMenuName)) return;

        var devices = new ToolStripMenuItem("Devices") { Name = DevicesMenuName };
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

    private static T? FindControl<T>(Control parent) where T : Control
    {
        if (parent is T direct) return direct;
        foreach (Control child in parent.Controls)
        {
            var found = FindControl<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
