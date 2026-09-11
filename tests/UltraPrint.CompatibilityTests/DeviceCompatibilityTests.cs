using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Devices;

internal static class DeviceCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestDeviceConfiguration();
        TestCurrentDevicePersistence();
        TestIceApiCatalog();
        TestIceApiReadContracts();
        TestIceApiProbeFallback();
        TestIceApiReaderFallback();
    }

    private static void TestDeviceConfiguration()
    {
        var temp = CreateTempDirectory();
        try
        {
            var path = Path.Combine(temp, "Campo.ini");
            File.WriteAllText(path,
                "[Setup]\r\nDispositivo Corrente=Select Class\r\n\r\n" +
                "[Moduli Hardware]\r\n1=Codificatore Magnetico,Mag\r\n2=Codificatore Chip,Chip\r\n\r\n" +
                "[Select Class-Configurazione]\r\n1=Codificatore Magnetico; Mag;Vero\r\n2=Stampante Termografica; Stampa;Falso\r\n3=\r\nActiveXDll=Legacy.Device.Control\r\n\r\n" +
                "[Driver-Select Class]\r\nAbilitato=1\r\nT1Font=Track 1 - Magnetic stripe\r\n\r\n" +
                "[Other]\r\nKeep=Yes\r\n");

            var configuration = LegacyDeviceConfigurationStore.Load(path);
            AssertEqual("Select Class", configuration.CurrentDevice!, "current device");
            AssertEqual(2, configuration.HardwareModules.Count, "hardware module definitions");
            AssertEqual("Mag", configuration.HardwareModules[0].Key, "module key");
            AssertEqual(1, configuration.Devices.Count, "device profile count");
            var profile = configuration.Devices[0];
            AssertEqual("Select Class", profile.Name, "device profile name");
            AssertEqual("Legacy.Device.Control", profile.ActiveXDll!, "device ActiveX DLL");
            AssertTrue(profile.DriverEnabled, "Driver-<device> Abilitato");
            AssertEqual(2, profile.Modules.Count, "configured module count");
            AssertTrue(profile.Modules[0].Enabled, "Vero module state");
            AssertTrue(!profile.Modules[1].Enabled, "Falso module state");
        }
        finally { TryDelete(temp); }
    }

    private static void TestCurrentDevicePersistence()
    {
        var temp = CreateTempDirectory();
        try
        {
            var path = Path.Combine(temp, "Campo.ini");
            File.WriteAllText(path, "[Setup]\r\nDispositivo Corrente=Old Printer\r\n\r\n[Other]\r\nKeep=Yes\r\n");
            LegacyDeviceConfigurationStore.SaveCurrentDevice(path, "New Printer");
            var configuration = LegacyDeviceConfigurationStore.Load(path);
            AssertEqual("New Printer", configuration.CurrentDevice!, "frmDispositivi current-device persistence");
            var text = File.ReadAllText(path);
            AssertTrue(text.Contains("[Other]", StringComparison.Ordinal) && text.Contains("Keep=Yes", StringComparison.Ordinal),
                "current-device save preserves unrelated Campo.ini content");
        }
        finally { TryDelete(temp); }
    }

    private static void TestIceApiCatalog()
    {
        AssertEqual("ICE_API.DLL", LegacyIceApiProbe.LibraryName, "ICE API DLL name");
        AssertEqual(24, LegacyIceApiProbe.Exports.Count, "recovered ICE API export count");
        AssertExport("_GetCardPrinterStatusA@24", 24, true);
        AssertExport("_GetCardPrinterInfoA@20", 20, true);
        AssertExport("_PrinterAPIMajorVersion@0", 0, true);
        AssertExport("_PrinterAPIMinorVersion@0", 0, true);
        AssertExport("_RunFirmwareUpdateUtilityA@4", 4, false);
        AssertExport("_CleanCardPrinterA@4", 4, false);
    }

    private static void TestIceApiReadContracts()
    {
        AssertEqual(1u, LegacyIceApiContracts.PrinterModelInfoLevel, "printer model info level");
        AssertEqual(2u, LegacyIceApiContracts.PrinterSerialInfoLevel, "printer serial info level");
        AssertEqual(4u, LegacyIceApiContracts.MagstripeHeadInfoLevel, "magstripe head info level");
        AssertEqual(1u, LegacyIceApiContracts.PrinterStatusLevel, "printer status info level");
        AssertEqual(1u, LegacyIceApiContracts.PrinterErrorsLevel, "printer errors info level");

        AssertEqual(12, LegacyIceApiContracts.PrinterModelInfoPrefixSize, "native model prefix size");
        AssertEqual(32, LegacyIceApiContracts.PrinterSerialInfoPrefixSize, "native serial prefix size");
        AssertEqual(28, LegacyIceApiContracts.MagstripeHeadInfoPrefixSize, "native magstripe prefix size");
        AssertEqual(16, LegacyIceApiContracts.PrinterStatusRecordSize, "native status record size");
        AssertEqual(16, LegacyIceApiContracts.PrinterErrorRecordSize, "native error record size");

        AssertEqual(12, Marshal.SizeOf<LegacyIceCardId32>(), "x86 CARDIDTYPE size");
        AssertEqual(8, Marshal.SizeOf<LegacyIceCardStatus1>(), "CARD_INFO_1 size");
        AssertEqual(16, Marshal.SizeOf<LegacyIceSystemTime>(), "SYSTEMTIME size");
        AssertEqual(24, Marshal.SizeOf<LegacyIceCardStatus2>(), "CARD_INFO_2 size");

        AssertTrue(LegacyIceApiContracts.DecodePollingState(0) == LegacyIcePrinterPollingState.Responding,
            "polling state 0 is responding");
        AssertTrue(LegacyIceApiContracts.DecodePollingState(1) == LegacyIcePrinterPollingState.NotResponding,
            "polling state 1 is not responding");
        AssertTrue(LegacyIceApiContracts.DecodePollingState(2) == LegacyIcePrinterPollingState.Suspended,
            "polling state 2 is suspended");
        AssertTrue(LegacyIceApiContracts.DecodePollingState(3) is null, "unknown polling state remains unknown");

        AssertEqual("not installed", LegacyIceApiContracts.DecodeMagstripeHeadType(0, 1, 1, 1),
            "missing first magstripe presence flag");
        AssertEqual("not installed", LegacyIceApiContracts.DecodeMagstripeHeadType(1, 0, 1, 1),
            "missing second magstripe presence flag");
        AssertEqual("not enabled", LegacyIceApiContracts.DecodeMagstripeHeadType(1, 1, 0, 1),
            "disabled magstripe head");
        AssertEqual("IAT", LegacyIceApiContracts.DecodeMagstripeHeadType(1, 1, 1, 1),
            "magstripe head type 1");
        AssertEqual("NTT", LegacyIceApiContracts.DecodeMagstripeHeadType(1, 1, 1, 2),
            "magstripe head type 2");
        AssertEqual("Unknown", LegacyIceApiContracts.DecodeMagstripeHeadType(1, 1, 1, 9),
            "unknown magstripe head type");
    }

    private static void TestIceApiProbeFallback()
    {
        var temp = CreateTempDirectory();
        try
        {
            var result = LegacyIceApiProbe.Probe(temp, allowSystemSearch: false);
            if (!OperatingSystem.IsWindows())
                AssertEqual(LegacyIceApiAvailability.NotWindows, result.Availability, "non-Windows ICE API probe");
            else if (IntPtr.Size != 4)
                AssertEqual(LegacyIceApiAvailability.Requires32BitProcess, result.Availability, "legacy x86 ICE API process guard");
            else
                AssertEqual(LegacyIceApiAvailability.Missing, result.Availability, "missing local ICE API probe");
        }
        finally { TryDelete(temp); }
    }

    private static void TestIceApiReaderFallback()
    {
        var temp = CreateTempDirectory();
        try
        {
            var result = LegacyIceApiReader.ReadPrinter("Test Printer", temp, allowSystemSearch: false);
            if (!OperatingSystem.IsWindows())
                AssertEqual(LegacyIceApiAvailability.NotWindows, result.Availability, "non-Windows read-only ICE API reader");
            else if (IntPtr.Size != 4)
                AssertEqual(LegacyIceApiAvailability.Requires32BitProcess, result.Availability, "read-only ICE API reader x86 guard");
            else
                AssertEqual(LegacyIceApiAvailability.Missing, result.Availability, "read-only ICE API reader missing-DLL fallback");

            AssertEqual("Test Printer", result.PrinterName, "read-only ICE API reader preserves printer name");
            AssertTrue(result.ModelName is null && result.SerialNumber is null && result.ActiveJobId is null,
                "reader fallback does not invent printer data");
        }
        finally { TryDelete(temp); }
    }

    private static void AssertExport(string name, int stackBytes, bool readOnly)
    {
        var export = LegacyIceApiProbe.Exports.SingleOrDefault(x => x.NativeName == name)
            ?? throw new InvalidOperationException("FAILED: missing recovered ICE API export " + name);
        AssertEqual(stackBytes, export.StackBytes, name + " stack bytes");
        AssertEqual(readOnly, export.ReadOnlyQuery, name + " query/action classification");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UltraPrint.DeviceCompatibility", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
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
