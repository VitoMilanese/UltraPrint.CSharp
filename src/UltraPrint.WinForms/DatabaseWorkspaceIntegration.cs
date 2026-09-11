using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
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
                HydrateNativeDataFields(layout);
                var openedWorkspace = new DatabaseWorkspaceForm(layout);
                AddBarcodeFieldsToBindingSelector(openedWorkspace, layout);
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
        })
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.D
        });

        var toolsIndex = menu.Items.OfType<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Text, "Tools", StringComparison.OrdinalIgnoreCase))?.index ?? menu.Items.Count;
        menu.Items.Insert(toolsIndex, database);
    }

    private static void HydrateNativeDataFields(CardLayout layout)
    {
        foreach (var field in layout.Fields)
        {
            var nativeDataField = LegacyNativeDataFieldSemantics.ReadDataField(field);
            if (string.IsNullOrWhiteSpace(nativeDataField)) continue;

            if (field.Kind == LayoutFieldKind.Image)
            {
                if (string.IsNullOrWhiteSpace(field.Image.DatabaseField))
                    field.Image.DatabaseField = nativeDataField;
            }
            else if (string.IsNullOrWhiteSpace(field.Text.DatabaseField))
            {
                field.Text.DatabaseField = nativeDataField;
            }
        }
    }

    private static void AddBarcodeFieldsToBindingSelector(DatabaseWorkspaceForm workspace, CardLayout layout)
    {
        var cardFieldLabel = FindControl<Label>(workspace,
            label => string.Equals(label.Text, "Card field:", StringComparison.OrdinalIgnoreCase));
        if (cardFieldLabel?.Parent is not TableLayoutPanel bindingPanel) return;
        if (bindingPanel.GetControlFromPosition(1, 0) is not ComboBox selector) return;

        var selectedIndex = (selector.SelectedItem as LayoutField)?.Index;
        var bindable = layout.Fields
            .Where(field => field.Kind is LayoutFieldKind.Text or LayoutFieldKind.Image or LayoutFieldKind.Barcode)
            .OrderBy(field => field.Index)
            .ToArray();

        selector.BeginUpdate();
        try
        {
            selector.Items.Clear();
            foreach (var field in bindable) selector.Items.Add(field);
        }
        finally
        {
            selector.EndUpdate();
        }

        if (bindable.Length == 0) return;
        var selected = selectedIndex.HasValue
            ? bindable.FirstOrDefault(field => field.Index == selectedIndex.Value)
            : null;
        selector.SelectedItem = selected ?? bindable[0];
    }

    private static T? FindControl<T>(Control parent, Func<T, bool>? predicate = null) where T : Control
    {
        if (parent is T direct && (predicate is null || predicate(direct))) return direct;
        foreach (Control child in parent.Controls)
        {
            var found = FindControl<T>(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }
}
