using System.Text;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Layout;

namespace UltraPrint.WinForms;

internal static class MagneticStripeIntegration
{
    private const string TracksMenuItemName = "managedMagneticStripeTracksMenuItem";
    private const string PositionMenuItemName = "managedMagneticStripePositionMenuItem";

    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;

        var tools = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Tools", StringComparison.OrdinalIgnoreCase));
        if (tools is not null && !tools.DropDownItems.OfType<ToolStripItem>().Any(item => item.Name == TracksMenuItemName))
        {
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(new ToolStripMenuItem("Magnetic Stripe Tracks...", null, (_, _) => EditTracks(form))
            {
                Name = TracksMenuItemName
            });
        }

        var view = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "View", StringComparison.OrdinalIgnoreCase));
        if (view is not null && !view.DropDownItems.OfType<ToolStripItem>().Any(item => item.Name == PositionMenuItemName))
        {
            var canvas = FindControl<LayoutCanvas>(form);
            var position = new ToolStripMenuItem("Magnetic stripe position")
            {
                Name = PositionMenuItemName,
                CheckOnClick = true,
                Checked = false
            };
            position.CheckedChanged += (_, _) =>
            {
                if (canvas is not null)
                    canvas.ShowMagneticStripePosition = position.Checked;
            };
            view.DropDownItems.Add(position);
        }
    }

    private static void EditTracks(MainForm owner)
    {
        var canvas = FindControl<LayoutCanvas>(owner);
        var layout = canvas?.Layout;
        if (layout is null)
        {
            MessageBox.Show(owner, "Create or open a layout first.", "Magnetic Stripe Tracks",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new LegacyMagneticStripeTracksDialog(layout);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        canvas!.NotifyFieldEdited();
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

/// <summary>
/// Managed editor for native frmTracce. Native Form_Load trims global Traccia1/2/3 into the
/// three text controls; their Change event writes all three controls back to those globals.
/// The OLE drop event accepts lblLabels and inserts '[' + Caption + ']'. This dialog preserves
/// those three expression strings and offers the same bracket-token outcome without depending on
/// the legacy OLE drag format/control implementation.
/// </summary>
internal sealed class LegacyMagneticStripeTracksDialog : Form
{
    private readonly CardLayout _layout;
    private readonly TextBox[] _tracks =
    [
        new TextBox { Dock = DockStyle.Fill, MaxLength = UltraPrint22115LayoutCodec.MagneticTrackStringLength },
        new TextBox { Dock = DockStyle.Fill, MaxLength = UltraPrint22115LayoutCodec.MagneticTrackStringLength },
        new TextBox { Dock = DockStyle.Fill, MaxLength = UltraPrint22115LayoutCodec.MagneticTrackStringLength }
    ];
    private readonly ComboBox _recordField = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private int _activeTrack;

    public LegacyMagneticStripeTracksDialog(CardLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));

        Text = "Magnetic Stripe Tracks";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 360);

        for (var i = 0; i < _tracks.Length; i++)
        {
            var index = i;
            _tracks[i].Text = layout.MagneticStripe[i].Trim();
            _tracks[i].Enter += (_, _) => _activeTrack = index;
        }

        foreach (var token in DiscoverRecordFieldTokens(layout))
            _recordField.Items.Add(token);
        if (_recordField.Items.Count > 0) _recordField.SelectedIndex = 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        for (var i = 0; i < _tracks.Length; i++)
        {
            root.Controls.Add(new Label
            {
                Text = $"Track {i + 1}:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            }, 0, i);
            root.Controls.Add(_tracks[i], 1, i);
        }

        var info = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Native print preparation trims each track, runs Interpretariga, removes NULs, expands +(counter) tokens, then expands [record field] tokens."
        };
        root.Controls.Add(info, 0, 3);
        root.SetColumnSpan(info, 2);

        root.Controls.Add(new Label
        {
            Text = "Record field:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, 4);

        var tokenPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        tokenPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tokenPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        tokenPanel.Controls.Add(_recordField, 0, 0);
        var insert = new Button { Text = "Insert [field]", Dock = DockStyle.Fill };
        insert.Click += (_, _) => InsertRecordFieldToken();
        tokenPanel.Controls.Add(insert, 1, 0);
        root.Controls.Add(tokenPanel, 1, 4);

        var nativeNote = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "The legacy frmTracce OLE-drop path accepted lblLabels and inserted [Caption]. The list above is a managed convenience built only from already recovered layout DataField/placeholder names; you can also type any expression directly."
        };
        root.Controls.Add(nativeNote, 0, 5);
        root.SetColumnSpan(nativeNote, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += (_, _) => Accept();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 6);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private void Accept()
    {
        var values = _tracks.Select(track => track.Text.Trim()).ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            if (!TryValidateTrack(values[i], out var error))
            {
                _activeTrack = i;
                _tracks[i].Focus();
                MessageBox.Show(this, error, $"Track {i + 1}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        for (var i = 0; i < values.Length; i++)
            _layout.MagneticStripe[i] = values[i];

        DialogResult = DialogResult.OK;
        Close();
    }

    private void InsertRecordFieldToken()
    {
        var field = _recordField.Text.Trim().Trim('[', ']');
        if (field.Length == 0) return;

        var target = _tracks[Math.Clamp(_activeTrack, 0, _tracks.Length - 1)];
        target.Focus();
        target.SelectedText = "[" + field + "]";
    }

    private static bool TryValidateTrack(string value, out string error)
    {
        if (value.Any(ch => ch > byte.MaxValue))
        {
            error = "The recovered UltraPrint 2.2.115 track slots currently support only byte-preservable ANSI characters.";
            return false;
        }

        if (Encoding.Latin1.GetByteCount(value) > UltraPrint22115LayoutCodec.MagneticTrackStringLength)
        {
            error = $"A magnetic track expression cannot exceed {UltraPrint22115LayoutCodec.MagneticTrackStringLength} bytes.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> DiscoverRecordFieldTokens(CardLayout layout)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in layout.Fields)
        {
            AddSimpleField(values, LegacyNativeDataFieldSemantics.ReadDataField(field));

            if (field.Name.Length >= 3 && field.Name[0] == '%' && field.Name[^1] == '%')
                AddSimpleField(values, field.Name.Trim('%'));

            if (field.Kind == LayoutFieldKind.Image)
                AddSimpleField(values, field.Image.DatabaseField);
            else
                AddSimpleField(values, field.Text.DatabaseField);
        }

        return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddSimpleField(ISet<string> values, string? value)
    {
        var candidate = value?.Trim().Trim('%') ?? string.Empty;
        if (candidate.Length == 0 || candidate.Contains('[') || candidate.Contains(']') || candidate.Contains(','))
            return;
        values.Add(candidate);
    }
}
