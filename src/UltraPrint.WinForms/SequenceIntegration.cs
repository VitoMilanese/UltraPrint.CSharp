using UltraPrint.Core.Models;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

internal static class SequenceIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;
        if (menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == "managedSequenceMenu")) return;

        SequenceWorkspaceForm? workspace = null;
        CardLayout? workspaceLayout = null;
        var scriptHost = new WinFormsLegacySequenceHost(form);
        LegacyScriptSequenceHostRegistry.Current = scriptHost;

        var sequence = new ToolStripMenuItem("Sequence") { Name = "managedSequenceMenu" };
        var open = new ToolStripMenuItem("Sequence / Sheet printing...")
        {
            Name = "managedSequenceWorkspace",
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.Q
        };
        open.Click += (_, _) =>
        {
            var canvas = FindControl<LayoutCanvas>(form);
            var layout = canvas?.Layout;
            if (layout is null)
            {
                MessageBox.Show(form, "Open an UltraPrint .ly layout first.", "Sequence",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (workspace is null || workspace.IsDisposed || !ReferenceEquals(workspaceLayout, layout))
            {
                workspace?.Dispose();
                var openedWorkspace = new SequenceWorkspaceForm(layout);
                workspace = openedWorkspace;
                workspaceLayout = layout;
                scriptHost.AttachWorkspace(openedWorkspace, layout);
                openedWorkspace.FormClosed += (_, _) =>
                {
                    scriptHost.DetachWorkspace(openedWorkspace);
                    if (ReferenceEquals(workspace, openedWorkspace))
                    {
                        workspace = null;
                        workspaceLayout = null;
                    }
                };
                openedWorkspace.Show(form);
            }
            else
            {
                if (!workspace.Visible) workspace.Show(form);
                if (workspace.WindowState == FormWindowState.Minimized) workspace.WindowState = FormWindowState.Normal;
                workspace.BringToFront();
                workspace.Activate();
            }
        };
        sequence.DropDownItems.Add(open);

        var toolsIndex = menu.Items.OfType<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Text, "Tools", StringComparison.OrdinalIgnoreCase))?.index ?? menu.Items.Count;
        menu.Items.Insert(toolsIndex, sequence);

        form.FormClosed += (_, _) =>
        {
            LegacyScriptSequenceHostRegistry.ClearIfCurrent(scriptHost);
            scriptHost.Dispose();
            workspace?.Dispose();
        };
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
