namespace UltraPrint.Core.Models;

/// <summary>
/// Cooperative state used by the managed Sequenza print loop. The native form
/// keeps pause state in the Pausa button caption and implements Stop by setting
/// FinoAPagina to the current Pagina; this model exposes the equivalent behavior
/// without coupling the print engine to WinForms controls.
/// </summary>
public sealed class SequencePrintJobController
{
    public bool IsPaused { get; private set; }
    public bool StopAfterCurrentSheetRequested { get; private set; }
    public int CurrentSheetNumber { get; private set; }
    public int TotalSheetCount { get; private set; }
    public LayoutSide CurrentSide { get; private set; } = LayoutSide.Unknown;

    public event Action? StateChanged;

    public bool TogglePause()
    {
        IsPaused = !IsPaused;
        StateChanged?.Invoke();
        return IsPaused;
    }

    public void RequestStopAfterCurrentSheet()
    {
        StopAfterCurrentSheetRequested = true;
        StateChanged?.Invoke();
    }

    public void ReportProgress(int currentSheetNumber, int totalSheetCount, LayoutSide currentSide)
    {
        if (currentSheetNumber < 1) throw new ArgumentOutOfRangeException(nameof(currentSheetNumber));
        if (totalSheetCount < currentSheetNumber) throw new ArgumentOutOfRangeException(nameof(totalSheetCount));
        if (currentSide is not (LayoutSide.Front or LayoutSide.Back))
            throw new ArgumentOutOfRangeException(nameof(currentSide));

        CurrentSheetNumber = currentSheetNumber;
        TotalSheetCount = totalSheetCount;
        CurrentSide = currentSide;
        StateChanged?.Invoke();
    }

    public void Reset()
    {
        IsPaused = false;
        StopAfterCurrentSheetRequested = false;
        CurrentSheetNumber = 0;
        TotalSheetCount = 0;
        CurrentSide = LayoutSide.Unknown;
        StateChanged?.Invoke();
    }
}
