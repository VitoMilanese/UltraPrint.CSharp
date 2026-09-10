using System.Text.Json;
using UltraPrint.Core.Models;

namespace UltraPrint.Legacy.Data;

/// <summary>
/// Non-destructive persistence for the managed Sequenza replacement. Settings that
/// are still managed-only remain beside the layout instead of being written into
/// unknown legacy state.
/// </summary>
public static class ManagedSequenceStore
{
    private const int CurrentVersion = 5;

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
            if (file?.Settings is null) return CreateDefault(layout);

            var settings = file.Settings;
            switch (file.Version)
            {
                case CurrentVersion:
                    break;
                case 4:
                    // Version 4 already persisted legacy sheet-format text. Taglio,
                    // back-slot mirroring and back offsets were not managed yet.
                    InitializeRecoveredBackAndStackDefaults(settings);
                    break;
                case 3:
                    // Version 3 already persisted native paper orientation. The
                    // legacy cboDimensioni text was not managed yet; null means the
                    // Sequence form will select the first Campo.ini [Formati] item.
                    settings.PaperFormatText = null;
                    InitializeRecoveredBackAndStackDefaults(settings);
                    break;
                case 2:
                    // Version 2 already uses native Passo gap semantics. Paper
                    // orientation and cboDimensioni were not persisted yet.
                    settings.PaperOrientation = SequencePaperOrientation.Portrait;
                    settings.PaperFormatText = null;
                    InitializeRecoveredBackAndStackDefaults(settings);
                    break;
                case 1:
                    // Version 1 treated HorizontalPitchMm/VerticalPitchMm as the full
                    // slot pitch. Native recovery proved Passo* is only the gap between
                    // cards, so migrate without changing the physical placement.
                    settings.HorizontalPitchMm = Math.Max(
                        0,
                        settings.HorizontalPitchMm > 0
                            ? settings.HorizontalPitchMm - layout.WidthMm
                            : 0);
                    settings.VerticalPitchMm = Math.Max(
                        0,
                        settings.VerticalPitchMm > 0
                            ? settings.VerticalPitchMm - layout.HeightMm
                            : 0);
                    settings.FillDirection = SequenceFillDirection.Horizontal;
                    settings.PaperOrientation = SequencePaperOrientation.Portrait;
                    settings.PaperFormatText = null;
                    InitializeRecoveredBackAndStackDefaults(settings);
                    break;
                default:
                    return CreateDefault(layout);
            }

            settings.Validate(layout.WidthMm, layout.HeightMm);
            return settings;
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
        HorizontalPitchMm = 0,
        VerticalPitchMm = 0,
        FillDirection = SequenceFillDirection.Horizontal,
        PaperOrientation = SequencePaperOrientation.Portrait,
        PaperFormatText = null,
        CutStack = false,
        MirrorBack = false,
        BackOffsetXmm = 0,
        BackOffsetYmm = 0,
        StartSlot = 0,
        Side = LayoutSide.Front,
        DrawCutMarks = false
    };

    private static void InitializeRecoveredBackAndStackDefaults(SequencePrintSettings settings)
    {
        settings.CutStack = false;
        settings.MirrorBack = false;
        settings.BackOffsetXmm = 0;
        settings.BackOffsetYmm = 0;
    }

    private sealed record SequenceFile(int Version, SequencePrintSettings? Settings);
}
