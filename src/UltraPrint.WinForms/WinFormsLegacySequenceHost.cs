using System.Globalization;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// Connects script-visible Sequenza/Stampa calls to the layout and Sequence workspace
/// currently owned by MainForm. Pescarecord shares the live database host; PosizionaPagina
/// drives the live Sequence navigation controls and its managed sheet preview.
/// </summary>
internal sealed class WinFormsLegacySequenceHost : ILegacyScriptSequenceHost, IDisposable
{
    private readonly MainForm _form;
    private SequenceWorkspaceForm? _workspace;
    private CardLayout? _workspaceLayout;
    private int? _pendingRecordNumber;

    public WinFormsLegacySequenceHost(MainForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    internal void AttachWorkspace(SequenceWorkspaceForm workspace, CardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(layout);
        _workspace = workspace;
        _workspaceLayout = layout;
        // SequenceWorkspaceForm registers its own Shown reload handler in its constructor,
        // so this later handler sees the final RecordCount/settings for pending script calls.
        workspace.Shown += (_, _) => ApplyPendingPosition();
    }

    internal void DetachWorkspace(SequenceWorkspaceForm workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!ReferenceEquals(_workspace, workspace)) return;
        _workspace = null;
        _workspaceLayout = null;
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

    public void PositionPage(object? recordNumber)
    {
        var number = Convert.ToInt32(recordNumber, CultureInfo.CurrentCulture);
        if (number <= 0) return;
        _pendingRecordNumber = number;
        ApplyPendingPosition();
    }

    public void SaveSetup(string? fileName)
    {
        var layout = RequireCurrentLayout();
        var preview = ActivePreview(layout);
        var settings = preview?.Settings?.Clone() ?? ManagedSequenceStore.Load(layout);
        LegacySequenceIniStore.Save(ResolvePath(layout, fileName), layout, settings);
    }

    public void LoadSetup(string? fileName)
    {
        var layout = RequireCurrentLayout();
        var baseline = ActivePreview(layout)?.Settings?.Clone() ?? ManagedSequenceStore.Load(layout);
        var loaded = LegacySequenceIniStore.Load(ResolvePath(layout, fileName), layout, baseline);

        if (!string.IsNullOrWhiteSpace(layout.SourcePath))
            ManagedSequenceStore.Save(layout, loaded);
    }

    private void ApplyPendingPosition()
    {
        if (_pendingRecordNumber is not { } recordNumber) return;
        var layout = CurrentLayout();
        if (layout is null) return;
        var workspace = ActiveWorkspace(layout);
        var preview = workspace is null ? null : FindControl<SequenceSheetPreviewControl>(workspace);
        var settings = preview?.Settings;
        if (workspace is null || !workspace.Visible || preview is null || settings is null || preview.RecordCount <= 0)
            return;

        var pageCount = LegacySequencePositioning.GetPageCount(preview.RecordCount, settings.Capacity);
        var legacyPage = LegacySequencePositioning.GetPageNumber(
            recordNumber,
            settings.Capacity,
            pageCount,
            settings.SinglePageMode);

        MoveWorkspaceToSheet(workspace, preview, legacyPage - 1);
        preview.HighlightedRecordNumber = recordNumber;
        _pendingRecordNumber = null;
    }

    private static void MoveWorkspaceToSheet(
        SequenceWorkspaceForm workspace,
        SequenceSheetPreviewControl preview,
        int targetSheetIndex)
    {
        targetSheetIndex = Math.Max(0, targetSheetIndex);
        var delta = targetSheetIndex - preview.SheetIndex;
        if (delta == 0) return;

        var buttonText = delta > 0 ? ">" : "<";
        var button = FindControls<Button>(workspace)
            .FirstOrDefault(candidate => string.Equals(candidate.Text, buttonText, StringComparison.Ordinal));
        if (button is null) return;

        for (var i = 0; i < Math.Abs(delta); i++)
            button.PerformClick();
    }

    private SequenceWorkspaceForm? ActiveWorkspace(CardLayout layout)
    {
        var workspace = _workspace;
        return workspace is not null && !workspace.IsDisposed && ReferenceEquals(_workspaceLayout, layout)
            ? workspace
            : null;
    }

    private SequenceSheetPreviewControl? ActivePreview(CardLayout layout)
    {
        var workspace = ActiveWorkspace(layout);
        return workspace is null ? null : FindControl<SequenceSheetPreviewControl>(workspace);
    }

    private string ResolvePath(CardLayout layout, string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return Path.GetFullPath(requestedPath);
        return LegacySequenceIniStore.GetDefaultPath(layout, GetLegacyApplicationRoot(layout));
    }

    private CardLayout RequireCurrentLayout() =>
        CurrentLayout() ?? throw new InvalidOperationException("No UltraPrint layout is currently open.");

    private CardLayout? CurrentLayout() => FindControl<LayoutCanvas>(_form)?.Layout;

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

    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var nested in FindControls<T>(child)) yield return nested;
        }
    }

    private static T? FindControl<T>(Control parent) where T : Control
    {
        if (parent is T direct) return direct;
        return FindControls<T>(parent).FirstOrDefault();
    }

    public void Dispose()
    {
        _workspace = null;
        _workspaceLayout = null;
        _pendingRecordNumber = null;
    }
}
