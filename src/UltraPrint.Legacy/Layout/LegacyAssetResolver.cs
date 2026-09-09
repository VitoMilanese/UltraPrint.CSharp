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

    /// <summary>
    /// Imports an image beside a legacy layout and returns the path form used by the original
    /// UltraPrint installation (normally .\LY\file.ext when the layout itself lives in LY).
    /// </summary>
    public static string ImportForLayout(string layoutPath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Image file not found.", sourcePath);

        var layoutDirectory = Path.GetDirectoryName(Path.GetFullPath(layoutPath))
                              ?? throw new InvalidOperationException("The layout path has no parent directory.");
        Directory.CreateDirectory(layoutDirectory);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var target = Path.Combine(layoutDirectory, Path.GetFileName(sourcePath));
        if (!string.Equals(sourceFullPath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(target))
            {
                if (!FilesEqual(target, sourceFullPath))
                    target = GetAvailableTarget(target);
            }

            if (!File.Exists(target))
                File.Copy(sourceFullPath, target, overwrite: false);
        }

        var fileName = Path.GetFileName(target);
        return string.Equals(Path.GetFileName(layoutDirectory), "LY", StringComparison.OrdinalIgnoreCase)
            ? $".\\LY\\{fileName}"
            : fileName;
    }

    private static string GetAvailableTarget(string requestedTarget)
    {
        var directory = Path.GetDirectoryName(requestedTarget)!;
        var stem = Path.GetFileNameWithoutExtension(requestedTarget);
        var extension = Path.GetExtension(requestedTarget);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate a unique asset file name in the layout directory.");
    }

    private static bool FilesEqual(string left, string right)
    {
        var a = new FileInfo(left);
        var b = new FileInfo(right);
        if (a.Length != b.Length) return false;

        const int bufferSize = 81920;
        using var sa = File.OpenRead(left);
        using var sb = File.OpenRead(right);
        var ba = new byte[bufferSize];
        var bb = new byte[bufferSize];
        while (true)
        {
            var ra = sa.Read(ba, 0, ba.Length);
            var rb = sb.Read(bb, 0, bb.Length);
            if (ra != rb) return false;
            if (ra == 0) return true;
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
        }
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
