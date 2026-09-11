using System.Runtime.InteropServices;

namespace UltraPrint.Legacy.Devices;

public enum LegacyIcePrinterPollingState : uint
{
    Responding = 0,
    NotResponding = 1,
    Suspended = 2
}

/// <summary>
/// Native x86 structure/level facts recovered from UltraPrint 2.2.115 and the
/// historical ICE API CARDIDTYPE/GetCardStatus declarations. These contracts are
/// data-only; no printer-mutating operation is exposed here.
/// </summary>
public static class LegacyIceApiContracts
{
    public const uint PrinterModelInfoLevel = 1;
    public const uint PrinterSerialInfoLevel = 2;
    public const uint MagstripeHeadInfoLevel = 4;
    public const uint PrinterStatusLevel = 1;
    public const uint PrinterErrorsLevel = 1;

    public const int PrinterModelInfoPrefixSize = 12;
    public const int PrinterSerialInfoPrefixSize = 32;
    public const int MagstripeHeadInfoPrefixSize = 28;
    public const int PrinterStatusRecordSize = 16;
    public const int PrinterErrorRecordSize = 16;

    public static LegacyIcePrinterPollingState? DecodePollingState(uint value) => value switch
    {
        0 => LegacyIcePrinterPollingState.Responding,
        1 => LegacyIcePrinterPollingState.NotResponding,
        2 => LegacyIcePrinterPollingState.Suspended,
        _ => null
    };

    /// <summary>
    /// Mirrors native GetMagstripeHeadType: the level-4 record is 28 bytes, and
    /// UltraPrint examines its first four DWORDs in this exact order.
    /// </summary>
    public static string DecodeMagstripeHeadType(
        uint presenceFlag1,
        uint presenceFlag2,
        uint enabledFlag,
        uint headType)
    {
        if (presenceFlag1 == 0 || presenceFlag2 == 0) return "not installed";
        if (enabledFlag == 0) return "not enabled";
        return headType switch
        {
            1 => "IAT",
            2 => "NTT",
            _ => "Unknown"
        };
    }
}

/// <summary>
/// Exact 32-bit CARDIDTYPE layout from the historical ICE API declaration.
/// HANDLE is deliberately represented as a 32-bit value because the recovered
/// UltraPrint ABI is x86-only.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LegacyIceCardId32
{
    public uint JobId;
    public uint CardNumber;
    public uint PrinterHandle;
}

[StructLayout(LayoutKind.Sequential)]
public struct LegacyIceCardStatus1
{
    public int Active;
    public int Success;
}

[StructLayout(LayoutKind.Sequential)]
public struct LegacyIceSystemTime
{
    public ushort Year;
    public ushort Month;
    public ushort DayOfWeek;
    public ushort Day;
    public ushort Hour;
    public ushort Minute;
    public ushort Second;
    public ushort Milliseconds;
}

[StructLayout(LayoutKind.Sequential)]
public struct LegacyIceCardStatus2
{
    public uint CopiesPrinted;
    public uint RemakeAttempts;
    public LegacyIceSystemTime TimeCompleted;
}

public sealed record LegacyIcePrinterSnapshot(
    string PrinterName,
    LegacyIceApiAvailability Availability,
    string Message,
    string? ApiVersion,
    LegacyIcePrinterPollingState? PollingState,
    string? ModelName,
    string? SerialNumber,
    string? MagstripeHeadType,
    uint? ActiveJobId,
    string? FirstError,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Read-only ICE_API diagnostics recovered from the native smartDriver helpers.
/// This layer never clears errors, resumes/suspends polling, feeds cards, prints,
/// encodes magnetic data, initializes smart cards, cleans printers or updates firmware.
/// </summary>
public static class LegacyIceApiReader
{
    private const uint MaxDiagnosticBufferBytes = 16 * 1024 * 1024;

    public static LegacyIcePrinterSnapshot ReadPrinter(
        string printerName,
        string? applicationDirectory = null,
        bool allowSystemSearch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        printerName = printerName.Trim();

        var probe = LegacyIceApiProbe.Probe(applicationDirectory, allowSystemSearch);
        if (probe.Availability != LegacyIceApiAvailability.Available)
            return Empty(printerName, probe);

        var loadName = ResolveLoadName(applicationDirectory);
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(loadName, out handle) || handle == IntPtr.Zero)
            {
                return new LegacyIcePrinterSnapshot(
                    printerName,
                    LegacyIceApiAvailability.LoadFailed,
                    $"{LegacyIceApiProbe.LibraryName} passed the compatibility probe but could not be reopened for read-only diagnostics.",
                    probe.ApiVersion,
                    null, null, null, null, null, null,
                    Array.Empty<string>());
            }

            var warnings = new List<string>();
            var polling = Capture<LegacyIcePrinterPollingState?>(
                warnings, "printer polling state", () => ReadPollingState(handle, printerName));
            var model = Capture<string?>(warnings, "printer model", () => ReadInfoString(
                handle, printerName, LegacyIceApiContracts.PrinterModelInfoLevel,
                LegacyIceApiContracts.PrinterModelInfoPrefixSize));
            var serial = Capture<string?>(warnings, "printer serial number", () => ReadInfoString(
                handle, printerName, LegacyIceApiContracts.PrinterSerialInfoLevel,
                LegacyIceApiContracts.PrinterSerialInfoPrefixSize));
            var magstripe = Capture<string?>(
                warnings, "magstripe head type", () => ReadMagstripeHeadType(handle, printerName));
            var activeJob = Capture<uint?>(
                warnings, "active card job id", () => ReadActiveJobId(handle, printerName));
            var firstError = Capture<string?>(
                warnings, "printer errors", () => ReadFirstPrinterError(handle, printerName));

            var message = warnings.Count == 0
                ? "Read-only ICE_API printer diagnostics completed."
                : $"Read-only ICE_API diagnostics completed with {warnings.Count} warning(s).";
            return new LegacyIcePrinterSnapshot(
                printerName,
                LegacyIceApiAvailability.Available,
                message,
                probe.ApiVersion,
                polling,
                model,
                serial,
                magstripe,
                activeJob,
                firstError,
                warnings);
        }
        catch (BadImageFormatException ex)
        {
            return new LegacyIcePrinterSnapshot(
                printerName,
                LegacyIceApiAvailability.Requires32BitProcess,
                "ICE_API architecture is incompatible with the current process: " + ex.Message,
                probe.ApiVersion,
                null, null, null, null, null, null,
                Array.Empty<string>());
        }
        catch (Exception ex) when (ex is DllNotFoundException or FileLoadException or EntryPointNotFoundException)
        {
            return new LegacyIcePrinterSnapshot(
                printerName,
                LegacyIceApiAvailability.LoadFailed,
                "ICE_API read-only diagnostics could not start: " + ex.Message,
                probe.ApiVersion,
                null, null, null, null, null, null,
                Array.Empty<string>());
        }
        finally
        {
            if (handle != IntPtr.Zero) NativeLibrary.Free(handle);
        }
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

    private static LegacyIcePrinterPollingState? ReadPollingState(IntPtr handle, string printerName)
    {
        var function = GetDelegate<GetCardPrinterPollingStateFunction>(handle, "_GetCardPrinterPollingStateA@8");
        if (function(printerName, out var state) == 0)
            throw new InvalidOperationException("_GetCardPrinterPollingStateA@8 returned FALSE.");
        return LegacyIceApiContracts.DecodePollingState(state);
    }

    private static string? ReadInfoString(IntPtr handle, string printerName, uint level, int minimumPrefixBytes)
    {
        var function = GetDelegate<GetCardPrinterInfoFunction>(handle, "_GetCardPrinterInfoA@20");
        return WithInfoBuffer(function, printerName, level, minimumPrefixBytes, buffer =>
        {
            var textPointer = Marshal.ReadIntPtr(buffer, 0);
            return textPointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(textPointer);
        }, emptyValue: null);
    }

    private static string? ReadMagstripeHeadType(IntPtr handle, string printerName)
    {
        var function = GetDelegate<GetCardPrinterInfoFunction>(handle, "_GetCardPrinterInfoA@20");
        return WithInfoBuffer(
            function,
            printerName,
            LegacyIceApiContracts.MagstripeHeadInfoLevel,
            LegacyIceApiContracts.MagstripeHeadInfoPrefixSize,
            buffer => LegacyIceApiContracts.DecodeMagstripeHeadType(
                ReadUInt32(buffer, 0),
                ReadUInt32(buffer, 4),
                ReadUInt32(buffer, 8),
                ReadUInt32(buffer, 12)),
            emptyValue: null);
    }

    private static uint ReadActiveJobId(IntPtr handle, string printerName)
    {
        var function = GetDelegate<GetCardPrinterArrayFunction>(handle, "_GetCardPrinterStatusA@24");
        return WithArrayBuffer(
            function,
            printerName,
            LegacyIceApiContracts.PrinterStatusLevel,
            LegacyIceApiContracts.PrinterStatusRecordSize,
            buffer => ReadUInt32(buffer, 0),
            emptyValue: 0u);
    }

    private static string? ReadFirstPrinterError(IntPtr handle, string printerName)
    {
        var function = GetDelegate<GetCardPrinterArrayFunction>(handle, "_GetCardPrinterErrorsA@24");
        return WithArrayBuffer(
            function,
            printerName,
            LegacyIceApiContracts.PrinterErrorsLevel,
            LegacyIceApiContracts.PrinterErrorRecordSize,
            buffer =>
            {
                var textPointer = Marshal.ReadIntPtr(buffer, 0);
                return textPointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(textPointer);
            },
            emptyValue: null);
    }

    private static T WithInfoBuffer<T>(
        GetCardPrinterInfoFunction function,
        string printerName,
        uint level,
        int minimumPrefixBytes,
        Func<IntPtr, T> decode,
        T emptyValue)
    {
        IntPtr dummy = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            dummy = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(dummy, 0);
            _ = function(printerName, level, dummy, 0, out var needed);
            if (needed == 0) return emptyValue;
            ValidateBufferSize(needed, minimumPrefixBytes);

            buffer = AllocateZeroed(needed);
            var capacity = needed;
            if (function(printerName, level, buffer, capacity, out needed) == 0)
                throw new InvalidOperationException($"_GetCardPrinterInfoA@20 level {level} returned FALSE.");
            if (needed > capacity)
                throw new InvalidOperationException($"_GetCardPrinterInfoA@20 level {level} grew its required buffer from {capacity} to {needed} bytes.");
            return decode(buffer);
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (dummy != IntPtr.Zero) Marshal.FreeHGlobal(dummy);
        }
    }

    private static T WithArrayBuffer<T>(
        GetCardPrinterArrayFunction function,
        string printerName,
        uint level,
        int minimumRecordBytes,
        Func<IntPtr, T> decodeFirstRecord,
        T emptyValue)
    {
        IntPtr dummy = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            dummy = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(dummy, 0);
            _ = function(printerName, level, dummy, 0, out var needed, out _);
            if (needed == 0) return emptyValue;
            ValidateBufferSize(needed, minimumRecordBytes);

            buffer = AllocateZeroed(needed);
            var capacity = needed;
            if (function(printerName, level, buffer, capacity, out needed, out _) == 0)
                throw new InvalidOperationException($"ICE_API level-{level} array query returned FALSE.");
            if (needed > capacity)
                throw new InvalidOperationException($"ICE_API array query grew its required buffer from {capacity} to {needed} bytes.");
            return decodeFirstRecord(buffer);
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (dummy != IntPtr.Zero) Marshal.FreeHGlobal(dummy);
        }
    }

    private static void ValidateBufferSize(uint needed, int minimumBytes)
    {
        if (needed < minimumBytes)
            throw new InvalidOperationException($"ICE_API reported a {needed}-byte buffer; native UltraPrint expects at least {minimumBytes} bytes for this level.");
        if (needed > MaxDiagnosticBufferBytes)
            throw new InvalidOperationException($"ICE_API requested an unexpectedly large {needed}-byte diagnostic buffer.");
    }

    private static IntPtr AllocateZeroed(uint length)
    {
        var size = checked((int)length);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var zeroes = new byte[size];
            Marshal.Copy(zeroes, 0, buffer, size);
            return buffer;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }

    private static uint ReadUInt32(IntPtr pointer, int offset) => unchecked((uint)Marshal.ReadInt32(pointer, offset));

    private static T GetDelegate<T>(IntPtr handle, string exportName) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(handle, exportName, out var address))
            throw new EntryPointNotFoundException(exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static T Capture<T>(List<string> warnings, string operation, Func<T> action)
    {
        try { return action(); }
        catch (Exception ex)
        {
            warnings.Add(operation + ": " + ex.Message);
            return default!;
        }
    }

    private static LegacyIcePrinterSnapshot Empty(string printerName, LegacyIceApiProbeResult probe) =>
        new(
            printerName,
            probe.Availability,
            probe.Message,
            probe.ApiVersion,
            null, null, null, null, null, null,
            Array.Empty<string>());

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetCardPrinterInfoFunction(
        [MarshalAs(UnmanagedType.LPStr)] string printerName,
        uint level,
        IntPtr data,
        uint bufferBytes,
        out uint bytesNeeded);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetCardPrinterArrayFunction(
        [MarshalAs(UnmanagedType.LPStr)] string printerName,
        uint level,
        IntPtr data,
        uint bufferBytes,
        out uint bytesNeeded,
        out uint itemsReturned);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetCardPrinterPollingStateFunction(
        [MarshalAs(UnmanagedType.LPStr)] string printerName,
        out uint state);
}
