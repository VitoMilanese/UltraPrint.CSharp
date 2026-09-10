namespace UltraPrint.Core.Models;

/// <summary>
/// Managed model for the recovered Sequenza sheet-imposition workflow. Values are
/// stored in millimetres because the legacy editor and card model are millimetre-
/// based even though VB6 eventually converted them to printer units.
/// </summary>
public sealed class SequencePrintSettings
{
    public int Rows { get; set; } = 5;
    public int Columns { get; set; } = 2;
    public double MarginLeftMm { get; set; } = 10;
    public double MarginTopMm { get; set; } = 10;
    public double HorizontalPitchMm { get; set; }
    public double VerticalPitchMm { get; set; }
    public int StartSlot { get; set; }
    public LayoutSide Side { get; set; } = LayoutSide.Front;
    public bool DrawCutMarks { get; set; }

    /// <summary>
    /// Mirrors the native Sequenza.PaginaSingola checkbox. The recovered
    /// PosizionaPagina method uses a different page-number formula when it is set.
    /// </summary>
    public bool SinglePageMode { get; set; }

    public int Capacity => checked(Rows * Columns);

    public double EffectiveHorizontalPitchMm(double cardWidthMm) =>
        HorizontalPitchMm > 0 ? HorizontalPitchMm : cardWidthMm;

    public double EffectiveVerticalPitchMm(double cardHeightMm) =>
        VerticalPitchMm > 0 ? VerticalPitchMm : cardHeightMm;

    public void Validate(double cardWidthMm, double cardHeightMm)
    {
        if (Rows is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(Rows));
        if (Columns is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(Columns));
        if (MarginLeftMm < 0 || MarginTopMm < 0) throw new ArgumentOutOfRangeException(nameof(MarginLeftMm));
        if (HorizontalPitchMm < 0 || VerticalPitchMm < 0) throw new ArgumentOutOfRangeException(nameof(HorizontalPitchMm));
        if (cardWidthMm <= 0 || cardHeightMm <= 0) throw new ArgumentOutOfRangeException(nameof(cardWidthMm));
        if (StartSlot < 0 || StartSlot >= Capacity) throw new ArgumentOutOfRangeException(nameof(StartSlot));
    }

    public SequencePrintSettings Clone() => new()
    {
        Rows = Rows,
        Columns = Columns,
        MarginLeftMm = MarginLeftMm,
        MarginTopMm = MarginTopMm,
        HorizontalPitchMm = HorizontalPitchMm,
        VerticalPitchMm = VerticalPitchMm,
        StartSlot = StartSlot,
        Side = Side,
        DrawCutMarks = DrawCutMarks,
        SinglePageMode = SinglePageMode
    };
}

public readonly record struct SequenceSlotPlacement(
    int SheetIndex,
    int SlotIndex,
    int Row,
    int Column,
    int RecordIndex,
    double Xmm,
    double Ymm,
    double WidthMm,
    double HeightMm);

/// <summary>
/// Literal page-number rules recovered from Sequenza.cmdImposta_Click,
/// PosizionaPagina and Pagina_Change. These intentionally preserve the native
/// exact-capacity boundary quirk instead of silently correcting it.
/// </summary>
public static class LegacySequencePositioning
{
    public static int GetPageCount(int recordCount, int recordsPerPage)
    {
        if (recordsPerPage <= 0) throw new ArgumentOutOfRangeException(nameof(recordsPerPage));
        if (recordCount <= 0) return 0;
        return 1 + (recordCount - 1) / recordsPerPage;
    }

    public static int GetPageNumber(
        int recordNumber,
        int recordsPerPage,
        int pageCount,
        bool singlePageMode)
    {
        if (recordsPerPage <= 0) throw new ArgumentOutOfRangeException(nameof(recordsPerPage));
        if (pageCount <= 0) return 1;

        // Native PosizionaPagina literally performs:
        //   Pagina = Fix(NumRecord / RecordxPagina) + 1
        // or, with PaginaSingola checked:
        //   Pagina = NumRecord Mod Pagine
        // Pagina_Change then resets any value outside 1..Pagine to 1.
        var rawPage = singlePageMode
            ? recordNumber % pageCount
            : checked((int)Math.Truncate(recordNumber / (double)recordsPerPage) + 1);

        return rawPage < 1 || rawPage > pageCount ? 1 : rawPage;
    }
}

/// <summary>
/// Pure placement engine behind the managed Sequenza replacement. The first sheet
/// can begin at a selected slot (matching the legacy grid workflow); subsequent
/// sheets always start at slot zero. Records are filled row-major.
/// </summary>
public static class SequencePrintPlanner
{
    public static int GetSheetCount(int recordCount, SequencePrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (recordCount <= 0) return 0;
        var firstCapacity = settings.Capacity - settings.StartSlot;
        if (recordCount <= firstCapacity) return 1;
        var remaining = recordCount - firstCapacity;
        return 1 + (remaining + settings.Capacity - 1) / settings.Capacity;
    }

    public static IReadOnlyList<SequenceSlotPlacement> GetSheetPlacements(
        int recordCount,
        double cardWidthMm,
        double cardHeightMm,
        SequencePrintSettings settings,
        int sheetIndex)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(cardWidthMm, cardHeightMm);
        if (recordCount < 0) throw new ArgumentOutOfRangeException(nameof(recordCount));
        var sheetCount = GetSheetCount(recordCount, settings);
        if (sheetIndex < 0 || sheetIndex >= sheetCount) throw new ArgumentOutOfRangeException(nameof(sheetIndex));

        var capacity = settings.Capacity;
        var firstCapacity = capacity - settings.StartSlot;
        var startRecord = sheetIndex == 0 ? 0 : firstCapacity + (sheetIndex - 1) * capacity;
        var firstSlot = sheetIndex == 0 ? settings.StartSlot : 0;
        var available = capacity - firstSlot;
        var count = Math.Min(available, recordCount - startRecord);
        var pitchX = settings.EffectiveHorizontalPitchMm(cardWidthMm);
        var pitchY = settings.EffectiveVerticalPitchMm(cardHeightMm);

        var result = new List<SequenceSlotPlacement>(count);
        for (var i = 0; i < count; i++)
        {
            var slot = firstSlot + i;
            var row = slot / settings.Columns;
            var column = slot % settings.Columns;
            result.Add(new SequenceSlotPlacement(
                sheetIndex,
                slot,
                row,
                column,
                startRecord + i,
                settings.MarginLeftMm + column * pitchX,
                settings.MarginTopMm + row * pitchY,
                cardWidthMm,
                cardHeightMm));
        }
        return result;
    }

    public static (double WidthMm, double HeightMm) GetUsedExtent(
        double cardWidthMm,
        double cardHeightMm,
        SequencePrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(cardWidthMm, cardHeightMm);
        var pitchX = settings.EffectiveHorizontalPitchMm(cardWidthMm);
        var pitchY = settings.EffectiveVerticalPitchMm(cardHeightMm);
        return (
            settings.MarginLeftMm + (settings.Columns - 1) * pitchX + cardWidthMm,
            settings.MarginTopMm + (settings.Rows - 1) * pitchY + cardHeightMm);
    }
}
