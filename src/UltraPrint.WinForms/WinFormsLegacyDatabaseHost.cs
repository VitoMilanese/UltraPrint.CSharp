using System.Data;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Scripting;

namespace UltraPrint.WinForms;

/// <summary>
/// Bridges the script-visible frmDatabase/Tabella objects to the database state belonging to the
/// layout currently open in MainForm. The normal Database / Records workspace persists source,
/// table and SQL choices into .ly.data.json; this host consumes that same state rather than
/// creating a separate script-only configuration. When the workspace is visible, its exact
/// current grid row wins so legacy script consumers see the same record as the operator.
/// </summary>
internal sealed class WinFormsLegacyDatabaseHost : ILegacyScriptDatabaseHost, IDisposable
{
    private readonly MainForm _form;
    private CardLayout? _layout;
    private ILegacyRecordSource? _source;
    private string? _openedSourcePath;
    private IReadOnlyList<string> _tableNames = Array.Empty<string>();
    private DataTable? _records;
    private int _position = -1;
    private DatabaseWorkspaceRuntimeBridge? _workspace;

    public WinFormsLegacyDatabaseHost(MainForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    internal IReadOnlyList<string> TableNames => _tableNames;
    internal DataTable? Records => _records;
    internal int Position => ActiveWorkspace()?.Position ?? _position;
    internal int RecordCount => ActiveWorkspace()?.Count ?? _records?.Rows.Count ?? 0;

    internal IReadOnlyDictionary<string, object?>? CurrentRecord
    {
        get
        {
            var workspace = ActiveWorkspace();
            if (workspace is not null) return workspace.CurrentRecord;
            if (_records is null || _position < 0 || _position >= _records.Rows.Count) return null;
            return LegacyRecordBinder.Snapshot(_records.Rows[_position]);
        }
    }

    internal void AttachWorkspace(DatabaseWorkspaceForm workspace, CardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(layout);
        _workspace = new DatabaseWorkspaceRuntimeBridge(workspace, layout);
    }

    internal void DetachWorkspace(DatabaseWorkspaceForm workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (_workspace is not null && ReferenceEquals(_workspace.Workspace, workspace))
            _workspace = null;
    }

    public void RefreshTables()
    {
        var layout = CurrentLayout();
        if (layout is null)
        {
            ResetRuntime();
            return;
        }

        EnsureLayout(layout);
        var state = EffectiveState(layout);
        EnsureSource(state.DatabasePath);
        _tableNames = _source?.GetTableNames() ?? Array.Empty<string>();
    }

    public void FindRecord()
    {
        var layout = CurrentLayout();
        if (layout is null)
        {
            ResetRuntime();
            return;
        }

        EnsureLayout(layout);
        var state = EffectiveState(layout);
        EnsureSource(state.DatabasePath);
        if (_source is null)
        {
            _records = null;
            _position = -1;
            return;
        }

        _tableNames = _source.GetTableNames();
        var previousPosition = Position;
        if (!string.IsNullOrWhiteSpace(state.Sql))
        {
            _records = _source.ExecuteQuery(state.Sql);
        }
        else
        {
            var tableName = state.Table;
            if (string.IsNullOrWhiteSpace(tableName)) tableName = _tableNames.FirstOrDefault();
            _records = string.IsNullOrWhiteSpace(tableName) ? new DataTable() : _source.OpenTable(tableName);
        }

        _position = _records.Rows.Count == 0
            ? -1
            : Math.Clamp(previousPosition < 0 ? 0 : previousPosition, 0, _records.Rows.Count - 1);
    }

    private ManagedLayoutDataState EffectiveState(CardLayout layout)
    {
        var persisted = ManagedBindingStore.LoadState(layout);
        var databasePath = FirstNotBlank(persisted.DatabasePath, layout.DatabasePath);
        var sql = FirstNotBlank(persisted.Sql, layout.Sql);
        return persisted with { DatabasePath = databasePath, Sql = sql };
    }

    private void EnsureLayout(CardLayout layout)
    {
        if (ReferenceEquals(_layout, layout)) return;
        DisposeSource();
        _layout = layout;
    }

    private void EnsureSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            DisposeSource();
            return;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { DisposeSource(); return; }

        if (_source is not null && string.Equals(_openedSourcePath, fullPath, StringComparison.OrdinalIgnoreCase))
            return;

        DisposeSource();
        if (!File.Exists(fullPath)) return;
        _source = LegacyRecordSourceFactory.Open(fullPath);
        _openedSourcePath = fullPath;
    }

    private DatabaseWorkspaceRuntimeBridge? ActiveWorkspace()
    {
        var workspace = _workspace;
        var layout = CurrentLayout();
        return workspace is not null && workspace.IsAvailable && layout is not null && ReferenceEquals(workspace.Layout, layout)
            ? workspace
            : null;
    }

    private CardLayout? CurrentLayout() => FindControl<LayoutCanvas>(_form)?.Layout;

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

    private static string? FirstNotBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;

    private void ResetRuntime()
    {
        DisposeSource();
        _layout = null;
        _tableNames = Array.Empty<string>();
        _records = null;
        _position = -1;
    }

    private void DisposeSource()
    {
        _source?.Dispose();
        _source = null;
        _openedSourcePath = null;
        _tableNames = Array.Empty<string>();
        _records = null;
        _position = -1;
    }

    public void Dispose()
    {
        _workspace = null;
        ResetRuntime();
    }
}
