using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// Connects the script-visible Sequenza/Stampa aliases to the sequence state belonging
/// to the layout currently open in MainForm. Legacy *.Seq reads are mirrored into the
/// managed sequence sidecar so the normal Sequence workspace sees the same setup when
/// it is opened/reloaded. Pescarecord shares the live database host so a successful
/// search moves the same current row observed by Db/frmDatabase/Tabella.
/// </summary>
internal sealed class WinFormsLegacySequenceHost : ILegacyScriptSequenceHost, IDisposable
{
    private readonly MainForm _form;

    public WinFormsLegacySequenceHost(MainForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    public object PickRecord()
    {
        // Native Pescarecord starts with On Error Resume Next and returns Variant 0 unless
        // FormQBE produces criteria that FindFirst can match. Preserve that failure shape.
        try
        {
            if (LegacyScriptDatabaseHostRegistry.Current is not WinFormsLegacyDatabaseHost databaseHost)
                return 0;

            var records = databaseHost.PrepareRecordsForSearch();
            if (records is null || records.Rows.Count == 0 || records.Columns.Count == 0)
                return 0;

            using var dialog = new LegacyQbeDialog(records);
            if (dialog.ShowDialog(_form) != DialogResult.OK)
                return 0;

            var oneBasedPosition = LegacyQbeMatcher.FindFirst(
                records,
                dialog.FieldName,
                dialog.OperatorToken,
                dialog.Operand);
            if (oneBasedPosition == 0)
            {
                MessageBox.Show(_form, "Record non trovato", "UltraPrint",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            databaseHost.MoveToRecord(oneBasedPosition - 1);
            return oneBasedPosition;
        }
        catch
        {
            return 0;
        }
    }

    public void SaveSetup(string? fileName)
    {
        var layout = RequireCurrentLayout();
        var settings = ManagedSequenceStore.Load(layout);
        var path = ResolvePath(layout, fileName);
        LegacySequenceIniStore.Save(path, layout, settings);
    }

    public void LoadSetup(string? fileName)
    {
        var layout = RequireCurrentLayout();
        var baseline = ManagedSequenceStore.Load(layout);
        var path = ResolvePath(layout, fileName);
        var loaded = LegacySequenceIniStore.Load(path, layout, baseline);

        if (!string.IsNullOrWhiteSpace(layout.SourcePath))
            ManagedSequenceStore.Save(layout, loaded);
    }

    private string ResolvePath(CardLayout layout, string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return Path.GetFullPath(requestedPath);
        return LegacySequenceIniStore.GetDefaultPath(layout, GetLegacyApplicationRoot(layout));
    }

    private CardLayout RequireCurrentLayout() =>
        FindControl<LayoutCanvas>(_form)?.Layout
        ?? throw new InvalidOperationException("No UltraPrint layout is currently open.");

    private static string GetLegacyApplicationRoot(CardLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(layout.SourcePath))
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(layout.SourcePath));
                if (directory is not null && string.Equals(Path.GetFileName(directory), "Ly", StringComparison.OrdinalIgnoreCase))
                    return Directory.GetParent(directory)?.FullName ?? Path.GetFullPath(AppContext.BaseDirectory);
            }
            catch
            {
                // Fall back to the managed application's App.Path equivalent below.
            }
        }
        return Path.GetFullPath(AppContext.BaseDirectory);
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

    public void Dispose()
    {
    }
}
