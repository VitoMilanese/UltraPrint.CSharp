using System.Text;

namespace UltraPrint.Legacy.Configuration;

/// <summary>
/// Small, order-preserving INI reader/writer for the ANSI-era files used by UltraPrint.
/// The original files are byte-oriented; Latin-1 keeps all bytes round-trippable.
/// </summary>
public sealed class LegacyIniDocument
{
    private readonly List<Line> _lines = new();

    public IReadOnlyList<string> Sections => _lines
        .Where(x => x.Kind == LineKind.Section)
        .Select(x => x.Section!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static LegacyIniDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        return Parse(Encoding.Latin1.GetString(bytes));
    }

    public static LegacyIniDocument Parse(string text)
    {
        var result = new LegacyIniDocument();
        var currentSection = string.Empty;
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                currentSection = trimmed[1..^1].Trim();
                result._lines.Add(new Line(LineKind.Section, raw, currentSection, null, null));
                continue;
            }

            if (trimmed.Length == 0)
            {
                result._lines.Add(new Line(LineKind.Blank, raw, currentSection, null, null));
                continue;
            }

            if (trimmed.StartsWith("'", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                result._lines.Add(new Line(LineKind.Comment, raw, currentSection, null, null));
                continue;
            }

            var equals = raw.IndexOf('=');
            if (equals >= 0)
            {
                var key = raw[..equals].Trim();
                var value = raw[(equals + 1)..];
                result._lines.Add(new Line(LineKind.KeyValue, raw, currentSection, key, value));
                continue;
            }

            result._lines.Add(new Line(LineKind.Raw, raw, currentSection, null, null));
        }

        return result;
    }

    public string? Get(string section, string key, string? defaultValue = null)
    {
        var line = _lines.LastOrDefault(x =>
            x.Kind == LineKind.KeyValue &&
            string.Equals(x.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return line?.Value ?? defaultValue;
    }

    public IReadOnlyDictionary<string, string> GetSection(string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in _lines.Where(x =>
                     x.Kind == LineKind.KeyValue &&
                     string.Equals(x.Section, section, StringComparison.OrdinalIgnoreCase)))
        {
            result[line.Key!] = line.Value ?? string.Empty;
        }
        return result;
    }

    public void Set(string section, string key, string value)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (line.Kind == LineKind.KeyValue &&
                string.Equals(line.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = line with { Raw = $"{line.Key}={value}", Value = value };
                return;
            }
        }

        var sectionHeader = _lines.FindLastIndex(x =>
            x.Kind == LineKind.Section && string.Equals(x.Section, section, StringComparison.OrdinalIgnoreCase));

        if (sectionHeader < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Kind != LineKind.Blank)
                _lines.Add(new Line(LineKind.Blank, string.Empty, string.Empty, null, null));
            _lines.Add(new Line(LineKind.Section, $"[{section}]", section, null, null));
            _lines.Add(new Line(LineKind.KeyValue, $"{key}={value}", section, key, value));
            return;
        }

        var insertAt = sectionHeader + 1;
        while (insertAt < _lines.Count && _lines[insertAt].Kind != LineKind.Section)
            insertAt++;
        _lines.Insert(insertAt, new Line(LineKind.KeyValue, $"{key}={value}", section, key, value));
    }

    public void Save(string path)
    {
        var text = string.Join("\r\n", _lines.Select(x => x.Raw)) + "\r\n";
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text));
    }

    public override string ToString() => string.Join(Environment.NewLine, _lines.Select(x => x.Raw));

    private enum LineKind { Blank, Comment, Section, KeyValue, Raw }
    private sealed record Line(LineKind Kind, string Raw, string? Section, string? Key, string? Value);
}
