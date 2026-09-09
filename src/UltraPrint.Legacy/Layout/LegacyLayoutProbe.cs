using System.Security.Cryptography;
using System.Text;

namespace UltraPrint.Legacy.Layout;

public enum LegacyLayoutContentKind
{
    Empty,
    TextLike,
    Binary,
    OleCompoundDocument,
    Zip,
    Unknown
}

public sealed record LegacyLayoutProbeResult(
    string Path,
    long Length,
    string Sha256,
    LegacyLayoutContentKind Kind,
    double PrintableRatio,
    string HeaderHex,
    IReadOnlyList<string> AsciiStrings,
    IReadOnlyList<string> Utf16Strings)
{
    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Path: {Path}");
        sb.AppendLine($"Size: {Length:N0} bytes");
        sb.AppendLine($"SHA-256: {Sha256}");
        sb.AppendLine($"Assessment: {Kind}");
        sb.AppendLine($"Printable byte ratio: {PrintableRatio:P1}");
        sb.AppendLine($"Header: {HeaderHex}");
        sb.AppendLine();
        sb.AppendLine("ASCII strings:");
        foreach (var value in AsciiStrings.Take(80)) sb.AppendLine("  " + value);
        sb.AppendLine();
        sb.AppendLine("UTF-16LE strings:");
        foreach (var value in Utf16Strings.Take(80)) sb.AppendLine("  " + value);
        return sb.ToString();
    }
}

/// <summary>
/// Safe format probe. It intentionally does not claim to decode .ly until a real legacy
/// sample/format contract is available. This prevents locking the rewrite to a guessed format.
/// </summary>
public static class LegacyLayoutProbe
{
    public static LegacyLayoutProbeResult Probe(string path)
    {
        var data = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var printable = data.Length == 0 ? 0 : data.Count(IsPrintable) / (double)data.Length;
        var kind = Classify(data, printable);
        var header = Convert.ToHexString(data.AsSpan(0, Math.Min(32, data.Length))).ToLowerInvariant();
        return new LegacyLayoutProbeResult(
            Path.GetFullPath(path), data.LongLength, hash, kind, printable, header,
            ExtractAscii(data), ExtractUtf16(data));
    }

    private static LegacyLayoutContentKind Classify(byte[] data, double printable)
    {
        if (data.Length == 0) return LegacyLayoutContentKind.Empty;
        ReadOnlySpan<byte> span = data;
        if (span.Length >= 8 && span[..8].SequenceEqual(new byte[] { 0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1 }))
            return LegacyLayoutContentKind.OleCompoundDocument;
        if (span.Length >= 4 && span[0] == (byte)'P' && span[1] == (byte)'K' && span[2] is 3 or 5 or 7 && span[3] is 4 or 6 or 8)
            return LegacyLayoutContentKind.Zip;
        if (printable >= 0.82) return LegacyLayoutContentKind.TextLike;
        if (printable <= 0.45 || data.Contains((byte)0)) return LegacyLayoutContentKind.Binary;
        return LegacyLayoutContentKind.Unknown;
    }

    private static bool IsPrintable(byte b) => b is 9 or 10 or 13 || b >= 32;

    private static IReadOnlyList<string> ExtractAscii(byte[] data)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            if (b is >= 32 and <= 126 || b >= 160) sb.Append((char)b);
            else
            {
                if (sb.Length >= 4) result.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length >= 4) result.Add(sb.ToString());
        return result.Distinct().Take(500).ToArray();
    }

    private static IReadOnlyList<string> ExtractUtf16(byte[] data)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var c = (char)(data[i] | data[i + 1] << 8);
            if (c is >= ' ' and <= '~' || c >= '\u00A0') sb.Append(c);
            else
            {
                if (sb.Length >= 4) result.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length >= 4) result.Add(sb.ToString());
        return result.Distinct().Take(500).ToArray();
    }
}
