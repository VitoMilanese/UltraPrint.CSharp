using System.Text.Json;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Data;

public sealed record ManagedLayoutDataState(
    string? DatabasePath,
    string? Sql,
    string? Table,
    Dictionary<int, string> Bindings)
{
    public static ManagedLayoutDataState Empty { get; } = new(null, null, null, new Dictionary<int, string>());
}

/// <summary>
/// Persists managed data-source state without guessing still-unverified global/binding offsets in
/// the legacy .ly binary. Existing .ly bytes remain untouched. Once those native offsets are
/// confirmed with real legacy database-bound fixtures, the state can be migrated into .ly writes.
/// </summary>
public static class ManagedBindingStore
{
    private const int CurrentVersion = 1;

    public static string? GetPath(CardLayout layout) =>
        string.IsNullOrWhiteSpace(layout.SourcePath) ? null : layout.SourcePath + ".data.json";

    public static ManagedLayoutDataState LoadState(CardLayout layout)
    {
        var path = GetPath(layout);
        if (path is null || !File.Exists(path)) return ManagedLayoutDataState.Empty;
        try
        {
            var model = JsonSerializer.Deserialize<DataFile>(File.ReadAllText(path));
            if (model?.Version != CurrentVersion) return ManagedLayoutDataState.Empty;
            var bindings = (model.Bindings ?? new Dictionary<int, string>())
                .Where(pair => pair.Key is >= 0 and < 64 && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
            return new ManagedLayoutDataState(model.DatabasePath, model.Sql, model.Table, bindings);
        }
        catch
        {
            return ManagedLayoutDataState.Empty;
        }
    }

    public static void SaveState(CardLayout layout, ManagedLayoutDataState state)
    {
        var path = GetPath(layout) ?? throw new InvalidOperationException("Save the layout before persisting database state.");
        var normalized = state.Bindings
            .Where(pair => pair.Key is >= 0 and < 64 && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());

        var hasState = !string.IsNullOrWhiteSpace(state.DatabasePath) ||
                       !string.IsNullOrWhiteSpace(state.Sql) ||
                       !string.IsNullOrWhiteSpace(state.Table) ||
                       normalized.Count > 0;
        if (!hasState)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var model = new DataFile(CurrentVersion, state.DatabasePath, state.Sql, state.Table, normalized);
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static Dictionary<int, string> Load(CardLayout layout) => LoadState(layout).Bindings;

    public static void Save(CardLayout layout, IReadOnlyDictionary<int, string> bindings)
    {
        var previous = LoadState(layout);
        SaveState(layout, previous with { Bindings = new Dictionary<int, string>(bindings) });
    }

    private sealed record DataFile(
        int Version,
        string? DatabasePath,
        string? Sql,
        string? Table,
        Dictionary<int, string>? Bindings);
}
