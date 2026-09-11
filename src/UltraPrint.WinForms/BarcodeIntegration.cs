using UltraPrint.Core.Models;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.WinForms;

internal static class BarcodeIntegration
{
    private const string InsertMenuItemName = "managedBarcodeInsertMenuItem";
    private const string EditMenuItemName = "managedBarcodeEditMenuItem";
    private const string ToolbarButtonName = "managedBarcodeToolbarButton";

    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var canvas = FindControl<LayoutCanvas>(form);
        if (canvas is null) return;

        var menu = form.MainMenuStrip;
        if (menu is not null)
        {
            var insert = menu.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => string.Equals(item.Text, "Insert", StringComparison.OrdinalIgnoreCase));
            if (insert is not null && !insert.DropDownItems.OfType<ToolStripItem>()
                    .Any(item => item.Name == InsertMenuItemName))
            {
                insert.DropDownItems.Add(new ToolStripMenuItem("Barcode Field...", null,
                    (_, _) => InsertBarcode(form, canvas))
                {
                    Name = InsertMenuItemName
                });
            }

            var edit = menu.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => string.Equals(item.Text, "Edit", StringComparison.OrdinalIgnoreCase));
            if (edit is not null && !edit.DropDownItems.OfType<ToolStripItem>()
                    .Any(item => item.Name == EditMenuItemName))
            {
                edit.DropDownItems.Add(new ToolStripSeparator());
                edit.DropDownItems.Add(new ToolStripMenuItem("Edit Barcode...", null,
                    (_, _) => EditSelectedBarcode(form, canvas))
                {
                    Name = EditMenuItemName
                });
            }
        }

        var toolbar = FindControl<ToolStrip>(form,
            strip => strip is not MenuStrip && strip.Items.OfType<ToolStripButton>()
                .Any(item => string.Equals(item.Text, "Photo", StringComparison.OrdinalIgnoreCase)));
        if (toolbar is not null && !toolbar.Items.OfType<ToolStripItem>()
                .Any(item => item.Name == ToolbarButtonName))
        {
            var button = new ToolStripButton("Barcode")
            {
                Name = ToolbarButtonName,
                ToolTipText = "Insert legacy font-backed barcode field",
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            button.Click += (_, _) => InsertBarcode(form, canvas);

            var photoItem = toolbar.Items.OfType<ToolStripItem>()
                .First(item => string.Equals(item.Text, "Photo", StringComparison.OrdinalIgnoreCase));
            toolbar.Items.Insert(toolbar.Items.IndexOf(photoItem) + 1, button);
        }

        canvas.DoubleClick += (_, _) =>
        {
            if (canvas.SelectedField?.Kind == LayoutFieldKind.Barcode)
                EditSelectedBarcode(form, canvas);
        };
    }

    private static void InsertBarcode(MainForm owner, LayoutCanvas canvas)
    {
        var layout = canvas.Layout;
        if (layout is null)
        {
            MessageBox.Show(owner, "Create or open a layout first.", "Barcode",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedSide = canvas.SelectedField?.Side;
        var side = canvas.Side != LayoutSide.Unknown
            ? canvas.Side
            : selectedSide is LayoutSide.Front or LayoutSide.Back
                ? selectedSide.Value
                : LayoutSide.Front;

        var codec = new UltraPrint22115LayoutCodec();
        LayoutField field;
        try
        {
            field = codec.CreateField(layout, LayoutFieldKind.Barcode, side, legacyTypeCode: 4);
            field.Name = CreateBarcodeName(layout, field);
            field.LegacyPayload = string.Empty;

            var discovered = LegacyBarcodeEditorDialog.DiscoverLegacyBarcodeFonts();
            if (discovered.Count > 0)
                field.Text.FontName = discovered[0];
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Insert barcode failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dialog = new LegacyBarcodeEditorDialog(field);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            codec.DeleteField(layout, field);
            return;
        }

        SelectField(owner, canvas, field);
        canvas.NotifyFieldEdited();
    }

    private static void EditSelectedBarcode(MainForm owner, LayoutCanvas canvas)
    {
        var field = canvas.SelectedField;
        if (field is null || field.Kind != LayoutFieldKind.Barcode)
        {
            MessageBox.Show(owner, "Select a barcode field first.", "Barcode",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new LegacyBarcodeEditorDialog(field);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        SelectField(owner, canvas, field);
        canvas.NotifyFieldEdited();
    }

    private static string CreateBarcodeName(CardLayout layout, LayoutField current)
    {
        var sequence = Math.Max(1, layout.Fields.Count(field => field.Kind == LayoutFieldKind.Barcode));
        var candidate = $"Barcode_{sequence}";
        var suffix = sequence + 1;
        while (layout.Fields.Any(field => !ReferenceEquals(field, current) &&
               string.Equals(field.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"Barcode_{suffix++}";
        return candidate;
    }

    private static void SelectField(MainForm owner, LayoutCanvas canvas, LayoutField field)
    {
        canvas.SelectedField = field;

        var fieldList = FindControl<ListBox>(owner);
        if (fieldList is not null && canvas.Layout is { } layout)
        {
            var items = layout.Fields
                .Where(item => canvas.Side == LayoutSide.Unknown ||
                               item.Side == LayoutSide.Unknown ||
                               item.Side == canvas.Side)
                .OrderBy(item => item.Level)
                .ThenBy(item => item.Index)
                .ToList();
            fieldList.DataSource = null;
            fieldList.DataSource = items;
            fieldList.SelectedItem = field;
        }

        var properties = FindControl<PropertyGrid>(owner);
        if (properties is not null)
        {
            properties.SelectedObject = field;
            properties.Refresh();
        }

        canvas.Invalidate();
    }

    private static T? FindControl<T>(Control root, Func<T, bool>? predicate = null) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed && (predicate is null || predicate(typed)))
                return typed;
            var nested = FindControl<T>(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }
}
