namespace UltraPrint.Legacy.Configuration;

public sealed record LegacyHardwareModuleDefinition(int Slot, string DisplayName, string Key);

public sealed record LegacyConfiguredHardwareModule(
    int Slot,
    string DisplayName,
    string Key,
    bool Enabled,
    string RawValue);

public sealed record LegacyDeviceProfile(
    string Name,
    string? ActiveXDll,
    IReadOnlyList<LegacyConfiguredHardwareModule> Modules,
    IReadOnlyDictionary<string, string> DriverSettings)
{
    public bool DriverEnabled => TryParseBoolean(DriverSettings.TryGetValue("Abilitato", out var value) ? value : null);

    private static bool TryParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            var text when text.Equals("Vero", StringComparison.OrdinalIgnoreCase) => true,
            var text when text.Equals("Falso", StringComparison.OrdinalIgnoreCase) => false,
            var text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }
}

public sealed record LegacyDeviceConfiguration(
    string? CurrentDevice,
    IReadOnlyList<LegacyHardwareModuleDefinition> HardwareModules,
    IReadOnlyList<LegacyDeviceProfile> Devices);

/// <summary>
/// Reads the device/profile surface used by native frmDispositivi from Campo.ini.
/// The original form reads [Setup] Dispositivo Corrente, [Moduli Hardware],
/// &lt;printer&gt;-Configurazione and Driver-&lt;printer&gt; sections.
/// </summary>
public static class LegacyDeviceConfigurationStore
{
    public const string SetupSection = "Setup";
    public const string CurrentDeviceKey = "Dispositivo Corrente";
    public const string HardwareModulesSection = "Moduli Hardware";
    public const string ConfigurationSuffix = "-Configurazione";
    public const string DriverPrefix = "Driver-";

    public static LegacyDeviceConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return new LegacyDeviceConfiguration(null, Array.Empty<LegacyHardwareModuleDefinition>(), Array.Empty<LegacyDeviceProfile>());

        var document = LegacyIniDocument.Load(path);
        var current = document.Get(SetupSection, CurrentDeviceKey)?.Trim();
        if (string.IsNullOrWhiteSpace(current)) current = null;

        var moduleDefinitions = ParseHardwareModules(document.GetSection(HardwareModulesSection));
        var devices = new List<LegacyDeviceProfile>();
        foreach (var section in document.Sections.Where(x => x.EndsWith(ConfigurationSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            var name = section[..^ConfigurationSuffix.Length].Trim();
            if (name.Length == 0) continue;
            var configuration = document.GetSection(section);
            configuration.TryGetValue("ActiveXDll", out var activeXDll);
            activeXDll = string.IsNullOrWhiteSpace(activeXDll) ? null : activeXDll.Trim();

            var modules = ParseConfiguredModules(configuration);
            var driverSettings = document.GetSection(DriverPrefix + name);
            devices.Add(new LegacyDeviceProfile(name, activeXDll, modules, driverSettings));
        }

        devices.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        return new LegacyDeviceConfiguration(current, moduleDefinitions, devices);
    }

    public static void SaveCurrentDevice(string path, string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var document = File.Exists(fullPath) ? LegacyIniDocument.Load(fullPath) : LegacyIniDocument.Parse(string.Empty);
        document.Set(SetupSection, CurrentDeviceKey, deviceName.Trim());
        document.Save(fullPath);
    }

    private static IReadOnlyList<LegacyHardwareModuleDefinition> ParseHardwareModules(IReadOnlyDictionary<string, string> section)
    {
        var result = new List<LegacyHardwareModuleDefinition>();
        foreach (var pair in OrderedNumericEntries(section))
        {
            var parts = pair.Value.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            result.Add(new LegacyHardwareModuleDefinition(pair.Slot, parts[0], parts[1]));
        }
        return result;
    }

    private static IReadOnlyList<LegacyConfiguredHardwareModule> ParseConfiguredModules(IReadOnlyDictionary<string, string> section)
    {
        var result = new List<LegacyConfiguredHardwareModule>();
        foreach (var pair in OrderedNumericEntries(section))
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            var parts = pair.Value.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            var enabled = parts.Length < 3 || ParseBoolean(parts[2], defaultValue: true);
            result.Add(new LegacyConfiguredHardwareModule(pair.Slot, parts[0], parts[1], enabled, pair.Value));
        }
        return result;
    }

    private static IEnumerable<(int Slot, string Value)> OrderedNumericEntries(IReadOnlyDictionary<string, string> section)
    {
        return section
            .Select(pair => int.TryParse(pair.Key, out var slot) ? (Valid: true, Slot: slot, pair.Value) : (Valid: false, Slot: 0, pair.Value))
            .Where(x => x.Valid)
            .OrderBy(x => x.Slot)
            .Select(x => (x.Slot, x.Value));
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var text = value.Trim();
        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) || text.Equals("Vero", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) || text.Equals("Falso", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }
}
