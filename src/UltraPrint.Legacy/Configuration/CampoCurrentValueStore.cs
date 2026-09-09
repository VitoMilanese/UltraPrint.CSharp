using System.Globalization;

namespace UltraPrint.Legacy.Configuration;

/// <summary>
/// Accessor for UltraPrint's verified flattened current-value section: [$].
/// Native CampoToIni writes keys as "$" / "Group.Key" / value / Campo.ini.
/// </summary>
public sealed class CampoCurrentValueStore
{
    public const string CurrentSection = "$";
    private readonly LegacyIniDocument _document;

    public CampoCurrentValueStore(LegacyIniDocument document) => _document = document;

    public string? Get(string group, string key, string? defaultValue = null) =>
        _document.Get(CurrentSection, $"{group}.{key}", defaultValue);

    public void Set(string group, string key, string value) =>
        _document.Set(CurrentSection, $"{group}.{key}", value);

    public IReadOnlyDictionary<string, string> GetGroup(string group)
    {
        var prefix = group + ".";
        return _document.GetSection(CurrentSection)
            .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key[prefix.Length..], x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public double? GetItalianDouble(string group, string key)
    {
        var raw = Get(group, key)?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.GetCultureInfo("it-IT"), out var value)
            ? value
            : null;
    }

    public int? GetInteger(string group, string key)
    {
        var raw = Get(group, key)?.Trim();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public bool? GetVbBoolean(string group, string key)
    {
        var raw = Get(group, key)?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (string.Equals(raw, "True", StringComparison.OrdinalIgnoreCase) || raw == "-1") return true;
        if (string.Equals(raw, "False", StringComparison.OrdinalIgnoreCase) || raw == "0") return false;
        return null;
    }
}
