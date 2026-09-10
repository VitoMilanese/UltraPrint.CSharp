using UltraPrint.Core.Models;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

internal static class DatabaseWorkspaceIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;
        if (menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == "managedDatabaseMenu")) return;

        DatabaseWorkspaceForm? workspace = null;
        CardLayout? workspaceLayout = null;
        var scriptHost = new WinFormsLegacyDatabaseHost(form);
        LegacyScriptDatabaseHostRegistry.Current = scriptHost;

        form.FormClosed += (_, _) =>
        {
            LegacyScriptDatabaseHostRegistry.ClearIfCurrent(scriptHost);
            scriptHost.Dispose();
            workspace?.Dispose();
        };

        var database = new ToolStripMenuItem("Database") { Name = "managedDatabaseMenu" };
        database.DropDownItems.Add(new ToolStripMenuItem("Database / Records...", null, (_, _) =>
        {
            var canvas = FindControl<LayoutCanvas>(form);
            var layout = canvas?.Layout;
            if (layout is null)
            {
                MessageBox.Show(form, "Open an UltraPrint .ly layout first.", "Database / Records",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (workspace is null || workspace.IsDisposed || !ReferenceEquals(workspaceLayout, layout))
            {
                workspace?.Dispose();
                workspace = new DatabaseWorkspaceForm(layout);
                workspaceLayout = layout;
                workspace.FormClosed += (_, _) =>
                {
                    workspace = null;
                    workspaceLayout = null;
                };
                workspace.Show(form);
            }
            else
            {
                if (!workspace.Visible) workspace.Show(form);
                if (workspace.WindowState == FormWindowState.Minimized) workspace.WindowState = FormWindowState.Normal;
                workspace.BringToFront();
                workspace.Activate();
            }
        })
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.D
        });

        var toolsIndex = menu.Items.OfType<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Text, "Tools", StringComparison.OrdinalIgnoreCase))?.index ?? menu.Items.Count;
        menu.Items.Insert(toolsIndex, database);
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
