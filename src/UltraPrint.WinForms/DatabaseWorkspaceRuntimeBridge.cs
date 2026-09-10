using System.Data;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

namespace UltraPrint.WinForms;

/// <summary>
/// Small bridge over the existing DatabaseWorkspaceForm UI so ScriptControl compatibility can
/// observe the exact row the operator is looking at without duplicating the visible BindingSource.
/// Keeping this outside DatabaseWorkspaceForm avoids widening its public surface.
/// </summary>
internal sealed class DatabaseWorkspaceRuntimeBridge
{
    private readonly DatabaseWorkspaceForm _workspace;
    private readonly CardLayout _layout;

    public DatabaseWorkspaceRuntimeBridge(DatabaseWorkspaceForm workspace, CardLayout layout)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public CardLayout Layout => _layout;
    public bool IsAvailable => !_workspace.IsDisposed;

    public int Position => FindGrid()?.CurrentRow?.Index ?? -1;
    public int Count => FindGrid()?.Rows.Count ?? 0;

    public IReadOnlyDictionary<string, object?>? CurrentRecord
    {
        get
        {
            var item = FindGrid()?.CurrentRow?.DataBoundItem;
            return item switch
            {
                DataRowView rowView => LegacyRecordBinder.Snapshot(rowView),
                _ => null
            };
        }
    }

    private DataGridView? FindGrid() => FindControl<DataGridView>(_workspace);

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
