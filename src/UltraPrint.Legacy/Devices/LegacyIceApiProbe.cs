using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Devices;

public sealed record LegacyIceApiExport(string NativeName, int StackBytes, bool ReadOnlyQuery);

public enum LegacyIceApiAvailability
{
    NotWindows,
    Requires32BitProcess,
    Missing,
    LoadFailed,
    MissingExports,
    Available
}

public sealed record LegacyIceApiProbeResult(
    LegacyIceApiAvailability Availability,
    string Message,
    string? LoadedFrom,
    string? ApiVersion,
    IReadOnlyList<string> MissingExports);

/// <summary>
/// Runtime boundary for the native ICE_API.DLL used by UltraPrint 2.2.115 smartDriver.
/// This class deliberately probes only library/export compatibility and the two
/// side-effect-free API-version functions. Printing, encoding, cleaning and firmware
/// exports are catalogued but are not invoked here.
/// </summary>
public static class LegacyIceApiProbe
{
    public const string LibraryName = "ICE_API.DLL";

    public static IReadOnlyList<LegacyIceApiExport> Exports { get; } = new[]
    {
        new LegacyIceApiExport("_SetTopCoatMode@8", 8, false),
        new LegacyIceApiExport("_RotateCardSide@8", 8, false),
        new LegacyIceApiExport("_SetMagstripeFormat@12", 12, false),
        new LegacyIceApiExport("_SetCustomMagstripeFormat@36", 36, false),
        new LegacyIceApiExport("_EncodeMagstripe@20", 20, false),
        new LegacyIceApiExport("_ReadMagstripe@20", 20, true),
        new LegacyIceApiExport("_SetInteractiveMode@8", 8, false),
        new LegacyIceApiExport("_SmartCardContinue@8", 8, false),
        new LegacyIceApiExport("_FeedCard@8", 8, false),
        new LegacyIceApiExport("_GetCardId@8", 8, true),
        new LegacyIceApiExport("_GetCardStatus@28", 28, true),
        new LegacyIceApiExport("_GetCardPrinterErrorsA@24", 24, true),
        new LegacyIceApiExport("_GetCardPrinterStatusA@24", 24, true),
        new LegacyIceApiExport("_ClearAllCardErrorsA@4", 4, false),
        new LegacyIceApiExport("_DisplayCardErrorA@8", 8, false),
        new LegacyIceApiExport("_GetHelpFileNameA@16", 16, true),
        new LegacyIceApiExport("_SendPrinterCommandA@12", 12, false),
        new LegacyIceApiExport("_GetCardPrinterInfoA@20", 20, true),
        new LegacyIceApiExport("_GetCardPrinterPollingStateA@8", 8, true),
        new LegacyIceApiExport("_ResumePrinterPollingA@8", 8, false),
        new LegacyIceApiExport("_PrinterAPIMajorVersion@0", 0, true),
        new LegacyIceApiExport("_PrinterAPIMinorVersion@0", 0, true),
        new LegacyIceApiExport("_RunFirmwareUpdateUtilityA@4", 4, false),
        new LegacyIceApiExport("_CleanCardPrinterA@4", 4, false)
    };

    public static LegacyIceApiProbeResult Probe(string? applicationDirectory = null, bool allowSystemSearch = true)
    {
        if (!OperatingSystem.IsWindows())
            return Result(LegacyIceApiAvailability.NotWindows, "ICE_API is a Windows-only legacy printer API.");

        // UltraPrint.exe is 32-bit VB6 and its embedded DllFunctionCall descriptors use
        // x86 stdcall-decorated exports (_Name@N). Do not try to load that ABI in x64.
        if (IntPtr.Size != 4)
            return Result(LegacyIceApiAvailability.Requires32BitProcess,
                "Legacy ICE_API uses the 32-bit stdcall ABI. Run an x86 UltraPrint build to probe the vendor DLL.");

        var candidates = new List<(string LoadName, string DisplayName)>();
        if (!string.IsNullOrWhiteSpace(applicationDirectory))
        {
            var local = Path.Combine(Path.GetFullPath(applicationDirectory), LibraryName);
            if (File.Exists(local)) candidates.Add((local, local));
        }
        if (allowSystemSearch) candidates.Add((LibraryName, LibraryName + " (Windows DLL search path)"));
        if (candidates.Count == 0)
            return Result(LegacyIceApiAvailability.Missing, $"{LibraryName} was not found in the application directory.");

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                if (!NativeLibrary.TryLoad(candidate.LoadName, out handle) || handle == IntPtr.Zero) continue;
                var missing = Exports
                    .Where(export => !NativeLibrary.TryGetExport(handle, export.NativeName, out _))
                    .Select(export => export.NativeName)
                    .ToArray();
                if (missing.Length > 0)
                {
                    return new LegacyIceApiProbeResult(
                        LegacyIceApiAvailability.MissingExports,
                        $"{LibraryName} loaded, but {missing.Length} native UltraPrint export(s) are missing.",
                        candidate.DisplayName,
                        null,
                        missing);
                }

                var version = ReadApiVersion(handle);
                return new LegacyIceApiProbeResult(
                    LegacyIceApiAvailability.Available,
                    version is null ? "ICE_API loaded and all recovered exports are present." : $"ICE_API {version} loaded; all recovered exports are present.",
                    candidate.DisplayName,
                    version,
                    Array.Empty<string>());
            }
            catch (BadImageFormatException)
            {
                return new LegacyIceApiProbeResult(
                    LegacyIceApiAvailability.Requires32BitProcess,
                    "ICE_API was found but its architecture is incompatible with the current process.",
                    candidate.DisplayName,
                    null,
                    Array.Empty<string>());
            }
            catch (Exception ex) when (ex is DllNotFoundException or FileLoadException or EntryPointNotFoundException)
            {
                lastError = ex;
            }
            finally
            {
                if (handle != IntPtr.Zero) NativeLibrary.Free(handle);
            }
        }

        return lastError is null
            ? Result(LegacyIceApiAvailability.Missing, $"{LibraryName} was not found.")
            : Result(LegacyIceApiAvailability.LoadFailed, $"{LibraryName} could not be loaded: {lastError.Message}");
    }

    private static string? ReadApiVersion(IntPtr handle)
    {
        if (!NativeLibrary.TryGetExport(handle, "_PrinterAPIMajorVersion@0", out var majorAddress) ||
            !NativeLibrary.TryGetExport(handle, "_PrinterAPIMinorVersion@0", out var minorAddress))
            return null;
        var major = Marshal.GetDelegateForFunctionPointer<ApiVersionFunction>(majorAddress)();
        var minor = Marshal.GetDelegateForFunctionPointer<ApiVersionFunction>(minorAddress)();
        return $"{major}.{minor}";
    }

    private static LegacyIceApiProbeResult Result(LegacyIceApiAvailability availability, string message) =>
        new(availability, message, null, null, Array.Empty<string>());

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ApiVersionFunction();
}
