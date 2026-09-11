using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

internal static class CounterIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;

        var tools = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Tools", StringComparison.OrdinalIgnoreCase));
        if (tools is null || tools.DropDownItems.OfType<ToolStripItem>()
                .Any(item => item.Name == "managedCountersMenuItem"))
            return;

        if (tools.DropDownItems.Count > 0)
            tools.DropDownItems.Add(new ToolStripSeparator { Name = "managedCountersSeparator" });

        tools.DropDownItems.Add(new ToolStripMenuItem("Counters...", null, (_, _) =>
        {
            var path = LegacyCounterFileStore.GetDefaultPath(AppContext.BaseDirectory);
            try
            {
                using var dialog = new LegacyCounterEditorDialog(path);
                dialog.ShowDialog(form);
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, "Counters", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        })
        {
            Name = "managedCountersMenuItem"
        });
    }
}
