namespace UltraPrint.Legacy.Configuration;

public sealed record CampoSchema(
    IReadOnlyList<string> Preamble,
    IReadOnlyList<CampoSchemaSection> Sections);

public sealed record CampoSchemaSection(string Name, IReadOnlyList<CampoSchemaEntry> Entries);

public enum CampoSchemaEntryKind { Property, GroupHeading, Comment, Raw, Blank }

public sealed record CampoSchemaEntry(
    CampoSchemaEntryKind Kind,
    string Raw,
    string? Name = null,
    string? Descriptor = null);

/// <summary>
/// Parses the schema-like prefix and all named sections of Campo.ini without
/// pretending that later driver/config sections use the same mini-language.
/// </summary>
public static class CampoSchemaParser
{
    public static CampoSchema ParseFile(string path) => Parse(File.ReadAllText(path, System.Text.Encoding.Latin1));

    public static CampoSchema Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var preamble = new List<string>();
        var sections = new List<CampoSchemaSection>();
        var currentName = (string?)null;
        var current = new List<CampoSchemaEntry>();
        var inPreamble = false;

        void Flush()
        {
            if (currentName is not null)
                sections.Add(new CampoSchemaSection(currentName, current.ToArray()));
            currentName = null;
            current = new List<CampoSchemaEntry>();
        }

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed == "<") { inPreamble = true; preamble.Add(raw); continue; }
            if (inPreamble)
            {
                preamble.Add(raw);
                if (trimmed == ">") inPreamble = false;
                continue;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                Flush();
                currentName = trimmed[1..^1].Trim();
                continue;
            }

            if (currentName is null) continue;
            if (trimmed.Length == 0)
            {
                current.Add(new(CampoSchemaEntryKind.Blank, raw));
            }
            else if (trimmed.StartsWith("'", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                current.Add(new(CampoSchemaEntryKind.Comment, raw));
            }
            else if (trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                current.Add(new(CampoSchemaEntryKind.GroupHeading, raw, trimmed[1..].Trim()));
            }
            else
            {
                var equals = raw.IndexOf('=');
                if (equals >= 0)
                    current.Add(new(CampoSchemaEntryKind.Property, raw, raw[..equals].Trim(), raw[(equals + 1)..].Trim()));
                else
                    current.Add(new(CampoSchemaEntryKind.Raw, raw));
            }
        }

        Flush();
        return new CampoSchema(preamble, sections);
    }
}
