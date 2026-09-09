using System.Text.Json;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Non-destructive persistence for the managed Sequenza replacement. Exact native
/// LeggiSetup/ScriviSetup storage has not yet been proven byte-for-byte, so these
/// settings are kept beside the layout instead of being written into unknown
/// legacy state.
/// </summary>
public static class ManagedSequenceStore
{
    private const int CurrentVersion = 1;

    public static string? GetPath(CardLayout layout) =>
        string.IsNullOrWhiteSpace(layout.SourcePath) ? null : layout.SourcePath + ".sequence.json";

    public static SequencePrintSettings Load(CardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var path = GetPath(layout);
        if (path is null || !File.Exists(path)) return CreateDefault(layout);
        try
        {
            var file = JsonSerializer.Deserialize<SequenceFile>(File.ReadAllText(path));
            if (file?.Version != CurrentVersion || file.Settings is null) return CreateDefault(layout);
            file.Settings.Validate(layout.WidthMm, layout.HeightMm);
            return file.Settings;
        }
        catch
        {
            return CreateDefault(layout);
        }
    }

    public static void Save(CardLayout layout, SequencePrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(layout.WidthMm, layout.HeightMm);
        var path = GetPath(layout) ?? throw new InvalidOperationException("Save the layout before persisting sequence settings.");
        var json = JsonSerializer.Serialize(
            new SequenceFile(CurrentVersion, settings.Clone()),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static SequencePrintSettings CreateDefault(CardLayout layout) => new()
    {
        Rows = 5,
        Columns = 2,
        MarginLeftMm = 10,
        MarginTopMm = 10,
        HorizontalPitchMm = layout.WidthMm,
        VerticalPitchMm = layout.HeightMm,
        StartSlot = 0,
        Side = LayoutSide.Front,
        DrawCutMarks = false
    };

    private sealed record SequenceFile(int Version, SequencePrintSettings? Settings);
}
