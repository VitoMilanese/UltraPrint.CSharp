namespace UltraPrint.Legacy.Layout;

public static class LegacyAssetResolver
{
    public static string? Resolve(string layoutPath, string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var raw = storedPath.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(raw) && File.Exists(raw)) return Path.GetFullPath(raw);

        var layoutDirectory = Path.GetDirectoryName(Path.GetFullPath(layoutPath))!;
        var parent = Directory.GetParent(layoutDirectory)?.FullName;
        var fileName = SafeFileName(raw);
        var candidates = new List<string>();

        if (fileName.Length > 0)
            candidates.Add(Path.Combine(layoutDirectory, fileName));

        var trimmed = raw;
        while (trimmed.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            trimmed = trimmed[2..];

        if (!Path.IsPathRooted(trimmed))
        {
            candidates.Add(Path.Combine(layoutDirectory, trimmed));
            if (parent is not null) candidates.Add(Path.Combine(parent, trimmed));
        }

        if (parent is not null && fileName.Length > 0)
            candidates.Add(Path.Combine(parent, "LY", fileName));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = ResolveCaseInsensitive(candidate);
            if (resolved is not null) return resolved;
        }

        return null;
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return string.Empty; }
    }

    private static string? ResolveCaseInsensitive(string candidate)
    {
        if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        var directory = Path.GetDirectoryName(candidate);
        var fileName = Path.GetFileName(candidate);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            return null;

        try
        {
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
