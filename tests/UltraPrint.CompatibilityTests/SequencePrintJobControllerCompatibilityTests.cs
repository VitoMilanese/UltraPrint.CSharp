using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;

internal static class SequencePrintJobControllerCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var job = new SequencePrintJobController();
        AssertTrue(!job.IsPaused, "sequence print job starts running");
        AssertTrue(!job.StopAfterCurrentSheetRequested, "sequence print job starts without stop request");
        AssertEqual(0, job.CurrentSheetNumber, "sequence print job starts without progress");
        AssertEqual(LayoutSide.Unknown, job.CurrentSide, "sequence print job starts without active side");

        AssertTrue(job.TogglePause(), "first native Pausa toggle enters paused state");
        AssertTrue(job.IsPaused, "paused state is retained");
        AssertTrue(!job.TogglePause(), "second native Pausa toggle resumes");
        AssertTrue(!job.IsPaused, "resumed state is retained");

        job.ReportProgress(2, 5, LayoutSide.Back);
        AssertEqual(2, job.CurrentSheetNumber, "sequence print job records current logical sheet");
        AssertEqual(5, job.TotalSheetCount, "sequence print job records native Pagine total");
        AssertEqual(LayoutSide.Back, job.CurrentSide, "sequence print job records Fronte/Retro phase");

        job.RequestStopAfterCurrentSheet();
        AssertTrue(job.StopAfterCurrentSheetRequested, "cmdStop equivalent requests stop after current sheet");

        job.Reset();
        AssertTrue(!job.IsPaused, "sequence print job reset clears pause");
        AssertTrue(!job.StopAfterCurrentSheetRequested, "sequence print job reset clears stop request");
        AssertEqual(0, job.CurrentSheetNumber, "sequence print job reset clears progress");
        AssertEqual(0, job.TotalSheetCount, "sequence print job reset clears page total");
        AssertEqual(LayoutSide.Unknown, job.CurrentSide, "sequence print job reset clears phase");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }
}
