using System.Globalization;

namespace UltraPrint.Legacy.Configuration;

/// <summary>
/// Recovered frmPrinting persistence for UP.ini [Setup] Intervallo.
/// Form_Load requests a default value of 5; the chip-card wait path later
/// raises values below 30 seconds to 30 before starting its countdown.
/// </summary>
public static class LegacyBatchPrintSettingsStore
{
    public const int DefaultIntervalSeconds = 5;
    public const int MinimumChipIntervalSeconds = 30;

    public static int LoadIntervalSeconds(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return DefaultIntervalSeconds;

        var value = LegacyIniDocument.Load(path).Get("Setup", "Intervallo", DefaultIntervalSeconds.ToString(CultureInfo.InvariantCulture));
        if (TryParseInteger(value, out var seconds)) return seconds;
        return DefaultIntervalSeconds;
    }

    public static void SaveIntervalSeconds(string path, int seconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var document = File.Exists(path) ? LegacyIniDocument.Load(path) : LegacyIniDocument.Parse(string.Empty);
        document.Set("Setup", "Intervallo", seconds.ToString(CultureInfo.InvariantCulture));
        document.Save(path);
    }

    public static int NormalizeChipInterval(int seconds) => Math.Max(MinimumChipIntervalSeconds, seconds);

    private static bool TryParseInteger(string? value, out int result)
    {
        if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return true;
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
    }
}
