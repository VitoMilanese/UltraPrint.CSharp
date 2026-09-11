using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Devices;

public enum LegacyIceCardCompletionState
{
    Succeeded,
    Failed,
    AttemptsExhausted
}

public readonly record struct LegacyIceCardStatusSample(
    bool ApiSucceeded,
    bool Active,
    bool Success);

public sealed record LegacyIceCardWaitResult(
    LegacyIceCardCompletionState State,
    int Attempts,
    LegacyIceCardStatusSample LastSample)
{
    public bool Completed => State is LegacyIceCardCompletionState.Succeeded or LegacyIceCardCompletionState.Failed;
    public bool Success => State == LegacyIceCardCompletionState.Succeeded;
}

/// <summary>
/// Pure implementation of the status loop used by native PrintWithCardStatus.
/// GetCardStatus failures and active cards are both retried; the first inactive
/// status ends the loop and its Success flag becomes the result. The native
/// helper performs at most 60 immediate calls and adds no delay of its own.
/// </summary>
public static class LegacyIceCardJobSemantics
{
    public const uint NativeStatusLevel = 1;
    public const int NativeStatusRecordBytes = 8;
    public const int NativeMaximumStatusAttempts = 60;

    public static LegacyIceCardWaitResult WaitForCompletion(
        Func<LegacyIceCardStatusSample> readStatus,
        int maxAttempts = NativeMaximumStatusAttempts)
    {
        ArgumentNullException.ThrowIfNull(readStatus);
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        var last = default(LegacyIceCardStatusSample);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            last = readStatus();
            if (!last.ApiSucceeded || last.Active)
                continue;

            return new LegacyIceCardWaitResult(
                last.Success ? LegacyIceCardCompletionState.Succeeded : LegacyIceCardCompletionState.Failed,
                attempt,
                last);
        }

        return new LegacyIceCardWaitResult(
            LegacyIceCardCompletionState.AttemptsExhausted,
            maxAttempts,
            last);
    }
}

public enum LegacyIceCardJobMonitorState
{
    Unavailable,
    CardIdQueryFailed,
    CompletedSuccess,
    CompletedFailure,
    AttemptsExhausted
}

public sealed record LegacyIceCardJobMonitorResult(
    LegacyIceCardJobMonitorState State,
    LegacyIceApiAvailability Availability,
    string Message,
    LegacyIceCardId32? CardId,
    int StatusAttempts)
{
    public bool Completed => State is LegacyIceCardJobMonitorState.CompletedSuccess or LegacyIceCardJobMonitorState.CompletedFailure;
    public bool Success => State == LegacyIceCardJobMonitorState.CompletedSuccess;
}

/// <summary>
/// Read-only monitor for a card job already submitted through a Windows printer HDC.
/// It reproduces the native GetCardId -> GetCardStatus(level 1) flow and never
/// submits, feeds, rotates, cancels or otherwise mutates a printer/card job.
/// </summary>
public static class LegacyIceCardJobMonitor
{
    public static LegacyIceCardJobMonitorResult Track(
        IntPtr printerHdc,
        string? applicationDirectory = null,
        bool allowSystemSearch = true,
        int maxStatusAttempts = LegacyIceCardJobSemantics.NativeMaximumStatusAttempts)
    {
        if (printerHdc == IntPtr.Zero) throw new ArgumentException("A printer HDC is required.", nameof(printerHdc));
        if (maxStatusAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxStatusAttempts));

        var probe = LegacyIceApiProbe.Probe(applicationDirectory, allowSystemSearch);
        if (probe.Availability != LegacyIceApiAvailability.Available)
        {
            return new LegacyIceCardJobMonitorResult(
                LegacyIceCardJobMonitorState.Unavailable,
                probe.Availability,
                probe.Message,
                null,
                0);
        }

        var loadName = ResolveLoadName(applicationDirectory);
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(loadName, out handle) || handle == IntPtr.Zero)
            {
                return new LegacyIceCardJobMonitorResult(
                    LegacyIceCardJobMonitorState.Unavailable,
                    LegacyIceApiAvailability.LoadFailed,
                    $"{LegacyIceApiProbe.LibraryName} passed the compatibility probe but could not be reopened for card-job monitoring.",
                    null,
                    0);
            }

            var getCardId = GetDelegate<GetCardIdFunction>(handle, "_GetCardId@8");
            if (getCardId(printerHdc, out var cardId) == 0)
            {
                return new LegacyIceCardJobMonitorResult(
                    LegacyIceCardJobMonitorState.CardIdQueryFailed,
                    LegacyIceApiAvailability.Available,
                    "GetCardId failed.",
                    null,
                    0);
            }

            var getCardStatus = GetDelegate<GetCardStatusFunction>(handle, "_GetCardStatus@28");
            var wait = LegacyIceCardJobSemantics.WaitForCompletion(
                () => ReadStatus(getCardStatus, cardId),
                maxStatusAttempts);

            var state = wait.State switch
            {
                LegacyIceCardCompletionState.Succeeded => LegacyIceCardJobMonitorState.CompletedSuccess,
                LegacyIceCardCompletionState.Failed => LegacyIceCardJobMonitorState.CompletedFailure,
                _ => LegacyIceCardJobMonitorState.AttemptsExhausted
            };

            var message = state switch
            {
                LegacyIceCardJobMonitorState.CompletedSuccess =>
                    $"Card job {cardId.JobId}, card {cardId.CardNumber} completed successfully.",
                LegacyIceCardJobMonitorState.CompletedFailure =>
                    $"Card job {cardId.JobId}, card {cardId.CardNumber} completed with failure.",
                _ =>
                    $"Card job {cardId.JobId}, card {cardId.CardNumber} did not become inactive within {wait.Attempts} status calls."
            };

            return new LegacyIceCardJobMonitorResult(
                state,
                LegacyIceApiAvailability.Available,
                message,
                cardId,
                wait.Attempts);
        }
        catch (BadImageFormatException ex)
        {
            return new LegacyIceCardJobMonitorResult(
                LegacyIceCardJobMonitorState.Unavailable,
                LegacyIceApiAvailability.Requires32BitProcess,
                "ICE_API architecture is incompatible with the current process: " + ex.Message,
                null,
                0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or FileLoadException or EntryPointNotFoundException)
        {
            return new LegacyIceCardJobMonitorResult(
                LegacyIceCardJobMonitorState.Unavailable,
                LegacyIceApiAvailability.LoadFailed,
                "ICE_API card-job monitoring could not start: " + ex.Message,
                null,
                0);
        }
        finally
        {
            if (handle != IntPtr.Zero) NativeLibrary.Free(handle);
        }
    }

    private static LegacyIceCardStatusSample ReadStatus(GetCardStatusFunction getCardStatus, LegacyIceCardId32 cardId)
    {
        var success = getCardStatus(
            cardId,
            LegacyIceCardJobSemantics.NativeStatusLevel,
            out var status,
            LegacyIceCardJobSemantics.NativeStatusRecordBytes,
            out _);

        return new LegacyIceCardStatusSample(
            success != 0,
            status.Active != 0,
            status.Success != 0);
    }

    private static string ResolveLoadName(string? applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(applicationDirectory))
        {
            var local = Path.Combine(Path.GetFullPath(applicationDirectory), LegacyIceApiProbe.LibraryName);
            if (File.Exists(local)) return local;
        }

        return LegacyIceApiProbe.LibraryName;
    }

    private static T GetDelegate<T>(IntPtr handle, string exportName) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(handle, exportName, out var address))
            throw new EntryPointNotFoundException(exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCardIdFunction(
        IntPtr printerHdc,
        out LegacyIceCardId32 cardId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCardStatusFunction(
        LegacyIceCardId32 cardId,
        uint level,
        out LegacyIceCardStatus1 status,
        uint bufferBytes,
        out uint bytesNeeded);
}
