using System.Collections.Concurrent;
using System.Text;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Compatibility implementation of Funzioni.SostituisciRiga (0x004A3F50,
/// vtable 0x800). The original lazily loads App.Path\SOSTITUZ.TXT into two
/// parallel Variant arrays and applies the rules before Interpretariga macros.
/// </summary>
public static class LegacyScriptSubstitutionTable
{
    public const string FileName = "SOSTITUZ.TXT";
    public const int LegacyRuleCapacity = 100;

    private static readonly ConcurrentDictionary<string, IReadOnlyList<Rule>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Apply(string line, string applicationDirectory)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var path = Path.GetFullPath(Path.Combine(applicationDirectory, FileName));
        var rules = GetRules(path);
        var result = line;
        foreach (var rule in rules)
        {
            // Native SostituisciRiga skips entries when either side is empty.
            if (rule.Search.Length == 0 || rule.Replacement.Length == 0) continue;
            result = ReplaceAllOrdinal(result, rule.Search, rule.Replacement);
        }
        return result;
    }

    public static IReadOnlyList<(string Search, string Replacement)> ReadRules(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var path = Path.GetFullPath(Path.Combine(applicationDirectory, FileName));
        return GetRules(path).Select(x => (x.Search, x.Replacement)).ToArray();
    }

    internal static void ClearCacheForTests() => Cache.Clear();

    private static IReadOnlyList<Rule> GetRules(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        var loaded = Load(path);

        // Native code retries loading while its rule count is zero. Mirror that by
        // caching only a non-empty table; an absent/empty file may appear later.
        if (loaded.Count > 0) Cache.TryAdd(path, loaded);
        return loaded;
    }

    private static IReadOnlyList<Rule> Load(string path)
    {
        if (!File.Exists(path)) return Array.Empty<Rule>();

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var result = new List<Rule>(Math.Min(LegacyRuleCapacity, 16));
        using var reader = new StringReader(text);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            if (rawLine.Length == 0) continue;
            if (result.Count >= LegacyRuleCapacity)
            {
                // The VB6 implementation has a fixed 100-slot table and would reach
                // an array-bounds error beyond it. The rewrite keeps the legacy limit
                // but safely ignores excess rows instead of crashing the application.
                break;
            }

            var tokenizer = new LegacyWordTokenizer(rawLine);
            var search = tokenizer.Next(",");
            var replacementToken = tokenizer.Next(",").Trim();
            var numeric = LegacyVb6ValueParser.Val(replacementToken);
            var replacement = numeric > 0
                ? LegacyVb6ValueParser.Chr(numeric)
                : replacementToken;
            result.Add(new Rule(search, replacement));
        }

        return result;
    }

    private static string ReplaceAllOrdinal(string source, string search, string replacement)
    {
        var first = source.IndexOf(search, StringComparison.Ordinal);
        if (first < 0) return source;

        var builder = new StringBuilder(source.Length);
        var sourceIndex = 0;
        while (first >= 0)
        {
            builder.Append(source, sourceIndex, first - sourceIndex);
            builder.Append(replacement);
            sourceIndex = first + search.Length;
            first = source.IndexOf(search, sourceIndex, StringComparison.Ordinal);
        }
        builder.Append(source, sourceIndex, source.Length - sourceIndex);
        return builder.ToString();
    }

    private sealed record Rule(string Search, string Replacement);
}
