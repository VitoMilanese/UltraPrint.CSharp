using System.Runtime.CompilerServices;
using System.Text;
using UltraPrint.Legacy.Layout;

internal static class MagneticTrackCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(18_925, UltraPrint22115LayoutCodec.KnownLayoutTailOffset, "layout tail offset");
        AssertEqual(256, UltraPrint22115LayoutCodec.MagneticTrackStringLength, "fixed magnetic-track String length");
        AssertEqual(18_929, UltraPrint22115LayoutCodec.KnownMagneticTrack1Offset, "Track 1 offset");
        AssertEqual(19_185, UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset, "Track 2 offset");
        AssertEqual(19_441, UltraPrint22115LayoutCodec.KnownMagneticTrack3Offset, "Track 3 offset");
        AssertEqual(19_697, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset, "fourth fixed global string offset");

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TPMFAO19.ly");
        if (!File.Exists(fixture))
            throw new FileNotFoundException("Compatibility fixture not copied to output.", fixture);

        var codec = new UltraPrint22115LayoutCodec();
        var originalBytes = File.ReadAllBytes(fixture);
        var layout = codec.Load(fixture);
        AssertEqual(string.Empty, layout.MagneticStripe.Track1, "fixture Track 1 starts empty");
        AssertEqual(string.Empty, layout.MagneticStripe.Track2, "fixture Track 2 starts empty");
        AssertEqual(string.Empty, layout.MagneticStripe.Track3, "fixture Track 3 starts empty");
        AssertEqual(@".\Foto", ReadFixedString(originalBytes, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset),
            "fourth String * 256 remains the recovered photo-path slot");

        var temp = Path.Combine(Path.GetTempPath(), "UltraPrint.MagneticTrackTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var unchanged = Path.Combine(temp, "unchanged.ly");
            codec.Save(layout, unchanged);
            AssertTrue(originalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(unchanged)),
                "loading magnetic-track globals must not break byte-identical unchanged round-trip");

            layout = codec.Load(fixture);
            layout.MagneticStripe.Track1 = "123456789";
            layout.MagneticStripe.Track2 = "[HOLDER_LAST_NAME]";
            layout.MagneticStripe.Track3 = "+(SerialCounter)-[SERIAL_NUMBER,2,4]";

            var changedPath = Path.Combine(temp, "tracks.ly");
            codec.Save(layout, changedPath);
            var changedBytes = File.ReadAllBytes(changedPath);
            AssertEqual(originalBytes.Length, changedBytes.Length, "magnetic-track write preserves file length");
            AssertOnlyTrackSlotsChanged(originalBytes, changedBytes);
            AssertEqual(@".\Foto", ReadFixedString(changedBytes, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset),
                "magnetic-track write leaves fourth String * 256 untouched");

            var reload = codec.Load(changedPath);
            AssertEqual("123456789", reload.MagneticStripe.Track1, "Track 1 save/load");
            AssertEqual("[HOLDER_LAST_NAME]", reload.MagneticStripe.Track2, "Track 2 save/load");
            AssertEqual("+(SerialCounter)-[SERIAL_NUMBER,2,4]", reload.MagneticStripe.Track3, "Track 3 save/load");

            AssertTrackPrefix(changedBytes, UltraPrint22115LayoutCodec.KnownMagneticTrack1Offset, "123456789");
            AssertTrackPrefix(changedBytes, UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset, "[HOLDER_LAST_NAME]");
            AssertTrackPrefix(changedBytes, UltraPrint22115LayoutCodec.KnownMagneticTrack3Offset, "+(SerialCounter)-[SERIAL_NUMBER,2,4]");

            reload.MagneticStripe.Track2 = string.Empty;
            var clearedPath = Path.Combine(temp, "track-cleared.ly");
            codec.Save(reload, clearedPath);
            var cleared = File.ReadAllBytes(clearedPath);
            var clearedTrack2 = cleared.AsSpan(
                UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset,
                UltraPrint22115LayoutCodec.MagneticTrackStringLength);
            AssertTrue(clearedTrack2.ToArray().All(value => value == 0x20),
                "explicitly clearing a populated fixed String * 256 produces VB6-style space padding");

            var tooLong = codec.Load(fixture);
            tooLong.MagneticStripe.Track1 = new string('A', UltraPrint22115LayoutCodec.MagneticTrackStringLength + 1);
            AssertThrows<InvalidDataException>(() => codec.Save(tooLong, Path.Combine(temp, "too-long.ly")),
                "257-byte track expression is rejected instead of truncated");

            var nonAnsi = codec.Load(fixture);
            nonAnsi.MagneticStripe.Track1 = "SERIAL-Ж";
            AssertThrows<InvalidDataException>(() => codec.Save(nonAnsi, Path.Combine(temp, "non-ansi.ly")),
                "unproven non-byte-preservable track text is rejected");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void AssertOnlyTrackSlotsChanged(byte[] expected, byte[] actual)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            if (IsTrackByte(index)) continue;
            if (expected[index] != actual[index])
                throw new InvalidOperationException(
                    $"FAILED: magnetic-track save changed unrelated byte {index} (0x{index:X}). " +
                    $"Expected 0x{expected[index]:X2}, actual 0x{actual[index]:X2}.");
        }
    }

    private static bool IsTrackByte(int offset)
    {
        var length = UltraPrint22115LayoutCodec.MagneticTrackStringLength;
        return IsInside(offset, UltraPrint22115LayoutCodec.KnownMagneticTrack1Offset, length) ||
               IsInside(offset, UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset, length) ||
               IsInside(offset, UltraPrint22115LayoutCodec.KnownMagneticTrack3Offset, length);
    }

    private static bool IsInside(int value, int start, int length) => value >= start && value < start + length;

    private static string ReadFixedString(byte[] data, int offset) =>
        Encoding.Latin1.GetString(data, offset, UltraPrint22115LayoutCodec.MagneticTrackStringLength)
            .TrimEnd('\0', ' ');

    private static void AssertTrackPrefix(byte[] data, int offset, string expected)
    {
        var bytes = Encoding.Latin1.GetBytes(expected);
        AssertTrue(data.AsSpan(offset, bytes.Length).SequenceEqual(bytes), $"{expected} bytes are written at 0x{offset:X}");
        AssertEqual((byte)0x20, data[offset + bytes.Length], "changed fixed String slot is padded with spaces");
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("FAILED: " + message);
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
