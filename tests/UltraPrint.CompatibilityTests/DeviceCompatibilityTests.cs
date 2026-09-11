using System.Runtime.CompilerServices;
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
        TestIceApiProbeFallback();
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
