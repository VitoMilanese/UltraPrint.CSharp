using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraPrint.Legacy.Devices;

internal static class IceCardJobCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeCardJobContracts();
        TestNativePollingSemantics();
        TestMonitorFallback();
    }

    private static void TestNativeCardJobContracts()
    {
        AssertEqual(12, Marshal.SizeOf<LegacyIceCardId32>(), "x86 CARDIDTYPE size");
        AssertEqual(8, Marshal.SizeOf<LegacyIceCardStatus1>(), "CARD_INFO_1 size");
        AssertEqual(1u, LegacyIceCardJobSemantics.NativeStatusLevel, "GetCardStatus native level");
        AssertEqual(8, LegacyIceCardJobSemantics.NativeStatusRecordBytes, "GetCardStatus CARD_INFO_1 cbBuf");
        AssertEqual(60, LegacyIceCardJobSemantics.NativeMaximumStatusAttempts,
            "native PrintWithCardStatus maximum status calls");

        AssertExport("_GetCardId@8", 8, true);
        AssertExport("_GetCardStatus@28", 28, true);
    }

    private static void TestNativePollingSemantics()
    {
        var calls = 0;
        var success = LegacyIceCardJobSemantics.WaitForCompletion(() =>
        {
            calls++;
            return calls switch
            {
                1 => new LegacyIceCardStatusSample(ApiSucceeded: false, Active: false, Success: false),
                2 => new LegacyIceCardStatusSample(ApiSucceeded: true, Active: true, Success: false),
                _ => new LegacyIceCardStatusSample(ApiSucceeded: true, Active: false, Success: true)
            };
        });

        AssertEqual(LegacyIceCardCompletionState.Succeeded, success.State,
            "failed and active GetCardStatus calls are retried before inactive success");
        AssertEqual(3, success.Attempts, "successful card status attempt count");
        AssertTrue(success.Completed && success.Success, "successful card completion flags");

        var failure = LegacyIceCardJobSemantics.WaitForCompletion(
            () => new LegacyIceCardStatusSample(ApiSucceeded: true, Active: false, Success: false));
        AssertEqual(LegacyIceCardCompletionState.Failed, failure.State,
            "inactive CARD_INFO_1 Success=FALSE is a completed failed card");
        AssertEqual(1, failure.Attempts, "completed failure stops immediately");

        calls = 0;
        var exhausted = LegacyIceCardJobSemantics.WaitForCompletion(() =>
        {
            calls++;
            return new LegacyIceCardStatusSample(ApiSucceeded: true, Active: true, Success: false);
        });
        AssertEqual(LegacyIceCardCompletionState.AttemptsExhausted, exhausted.State,
            "active card exhausts the native polling bound");
        AssertEqual(60, calls, "native helper performs exactly 60 calls while the card remains active");
        AssertTrue(!exhausted.Completed && !exhausted.Success, "attempt exhaustion is not completion");

        calls = 0;
        var apiFailures = LegacyIceCardJobSemantics.WaitForCompletion(() =>
        {
            calls++;
            return new LegacyIceCardStatusSample(ApiSucceeded: false, Active: false, Success: false);
        });
        AssertEqual(LegacyIceCardCompletionState.AttemptsExhausted, apiFailures.State,
            "GetCardStatus FALSE is retried until the same 60-call bound");
        AssertEqual(60, calls, "API failure path uses the native 60-call bound");

        AssertThrows<ArgumentOutOfRangeException>(
            () => LegacyIceCardJobSemantics.WaitForCompletion(() => default, 0),
            "zero status attempts are rejected");
    }

    private static void TestMonitorFallback()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.IceCardJobCompatibility", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var result = LegacyIceCardJobMonitor.Track(new IntPtr(1), temp, allowSystemSearch: false);
            if (!OperatingSystem.IsWindows())
                AssertEqual(LegacyIceApiAvailability.NotWindows, result.Availability, "non-Windows card-job monitor");
            else if (IntPtr.Size != 4)
                AssertEqual(LegacyIceApiAvailability.Requires32BitProcess, result.Availability, "card-job monitor x86 guard");
            else
                AssertEqual(LegacyIceApiAvailability.Missing, result.Availability, "card-job monitor missing-DLL fallback");

            AssertEqual(LegacyIceCardJobMonitorState.Unavailable, result.State,
                "unavailable ICE runtime does not invent card-job state");
            AssertTrue(result.CardId is null && result.StatusAttempts == 0,
                "unavailable card-job monitor does not fabricate CARDIDTYPE/status calls");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void AssertExport(string name, int stackBytes, bool readOnly)
    {
        var export = LegacyIceApiProbe.Exports.SingleOrDefault(x => x.NativeName == name)
            ?? throw new InvalidOperationException("FAILED: missing recovered ICE API export " + name);
        AssertEqual(stackBytes, export.StackBytes, name + " stack bytes");
        AssertEqual(readOnly, export.ReadOnlyQuery, name + " read-only classification");
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException("FAILED: " + message);
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
