namespace UltraPrint.WinForms;

internal static class ScriptIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;

        var tools = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Tools", StringComparison.OrdinalIgnoreCase));
        if (tools is null) return;
        if (tools.DropDownItems.OfType<ToolStripItem>().Any(item => item.Name == "managedLegacyScriptWorkspace")) return;

        tools.DropDownItems.Add(new ToolStripSeparator());
        var item = new ToolStripMenuItem("Legacy VBScript...")
        {
            Name = "managedLegacyScriptWorkspace",
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.V
        };
        item.Click += (_, _) =>
        {
            var layout = FindControl<LayoutCanvas>(form)?.Layout;
            var workspace = new ScriptWorkspaceForm(layout);
            workspace.Show(form);
        };
        tools.DropDownItems.Add(item);
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
