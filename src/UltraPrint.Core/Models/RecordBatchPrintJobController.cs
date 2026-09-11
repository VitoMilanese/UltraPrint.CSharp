namespace UltraPrint.Core.Models;

/// <summary>
/// Cooperative state for the legacy frmPrinting / frmDatabase.StampaTutti workflow.
/// The original VB6 code keeps cancellation in MainForm.CancelOp and updates the
/// frmPrinting controls while pumping DoEvents between cards.
/// </summary>
public sealed class RecordBatchPrintJobController
{
    public bool HasStarted { get; private set; }
    public bool CancelRequested { get; private set; }
    public bool IsCompleted { get; private set; }
    public int CurrentRecordNumber { get; private set; }
    public int TotalRecordCount { get; private set; }
    public LayoutSide CurrentSide { get; private set; } = LayoutSide.Unknown;
    public int RemainingIntervalSeconds { get; private set; }

    public bool IsRunning => HasStarted && !IsCompleted;

    public event Action? StateChanged;

    public void Start()
    {
        HasStarted = true;
        CancelRequested = false;
        IsCompleted = false;
        RemainingIntervalSeconds = 0;
        StateChanged?.Invoke();
    }

    public void RequestCancel()
    {
        CancelRequested = true;
        StateChanged?.Invoke();
    }

    public void ReportProgress(int currentRecordNumber, int totalRecordCount, LayoutSide currentSide)
    {
        if (currentRecordNumber < 1) throw new ArgumentOutOfRangeException(nameof(currentRecordNumber));
        if (totalRecordCount < currentRecordNumber) throw new ArgumentOutOfRangeException(nameof(totalRecordCount));
        if (currentSide is not (LayoutSide.Front or LayoutSide.Back))
            throw new ArgumentOutOfRangeException(nameof(currentSide));

        CurrentRecordNumber = currentRecordNumber;
        TotalRecordCount = totalRecordCount;
        CurrentSide = currentSide;
        RemainingIntervalSeconds = 0;
        StateChanged?.Invoke();
    }

    public void ReportCountdown(int remainingSeconds)
    {
        if (remainingSeconds < 0) throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
        RemainingIntervalSeconds = remainingSeconds;
        StateChanged?.Invoke();
    }

    public void Complete()
    {
        IsCompleted = true;
        RemainingIntervalSeconds = 0;
        StateChanged?.Invoke();
    }

    public void Reset()
    {
        HasStarted = false;
        CancelRequested = false;
        IsCompleted = false;
        CurrentRecordNumber = 0;
        TotalRecordCount = 0;
        CurrentSide = LayoutSide.Unknown;
        RemainingIntervalSeconds = 0;
        StateChanged?.Invoke();
    }
}
