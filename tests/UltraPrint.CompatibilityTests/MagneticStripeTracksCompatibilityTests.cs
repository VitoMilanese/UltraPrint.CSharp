using System.Runtime.CompilerServices;
using UltraPrint.Legacy.Layout;

internal static class MagneticStripeTracksCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AssertEqual(18_925, UltraPrint22115LayoutCodec.KnownLayoutTailOffset, "layout tail offset");
        AssertEqual(256, UltraPrint22115LayoutCodec.MagneticTrackStringLength, "magnetic track slot length");
        AssertEqual(18_929, UltraPrint22115LayoutCodec.KnownMagneticTrack1Offset, "track 1 offset");
        AssertEqual(19_185, UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset, "track 2 offset");
        AssertEqual(19_441, UltraPrint22115LayoutCodec.KnownMagneticTrack3Offset, "track 3 offset");
        AssertEqual(19_697, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset, "fourth String*256 offset");

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TPMFAO19.ly");
        if (!File.Exists(fixture))
            throw new FileNotFoundException("Compatibility fixture not copied to output.", fixture);

        var original = File.ReadAllBytes(fixture);
        AssertSlotIsZeroFilled(original, UltraPrint22115LayoutCodec.KnownMagneticTrack1Offset, "fixture track 1");
        AssertSlotIsZeroFilled(original, UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset, "fixture track 2");
        AssertSlotIsZeroFilled(original, UltraPrint22115LayoutCodec.KnownMagneticTrack3Offset, "fixture track 3");

        var unknownFourth = System.Text.Encoding.Latin1
            .GetString(original, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset,
                UltraPrint22115LayoutCodec.MagneticTrackStringLength)
            .TrimEnd('\0', ' ');
        AssertEqual(".\\Foto", unknownFourth, "fixture fourth String*256 remains independently identified");

        var codec = new UltraPrint22115LayoutCodec();
        var layout = codec.Load(fixture);
        AssertEqual(string.Empty, layout.MagneticStripe.Track1, "fixture track 1 decoded empty");
        AssertEqual(string.Empty, layout.MagneticStripe.Track2, "fixture track 2 decoded empty");
        AssertEqual(string.Empty, layout.MagneticStripe.Track3, "fixture track 3 decoded empty");

        var tempDir = Path.Combine(Path.GetTempPath(), "UltraPrint.CompatibilityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var changedPath = Path.Combine(tempDir, "tracks.ly");
            layout.MagneticStripe.Track2 = "ABC[HOLDER_LAST_NAME]+(1)";
            codec.Save(layout, changedPath);

            var changed = File.ReadAllBytes(changedPath);
            AssertEqual(original.Length, changed.Length, "track edit preserves .ly file length");

            var start = UltraPrint22115LayoutCodec.KnownMagneticTrack2Offset;
            var end = start + UltraPrint22115LayoutCodec.MagneticTrackStringLength;
            for (var i = 0; i < original.Length; i++)
            {
                if (i >= start && i < end) continue;
                if (original[i] != changed[i])
                    throw new InvalidOperationException($"FAILED: Track 2 edit changed unrelated byte 0x{i:X}.");
            }

            var stored = System.Text.Encoding.Latin1.GetString(changed, start,
                    UltraPrint22115LayoutCodec.MagneticTrackStringLength)
                .TrimEnd('\0', ' ');
            AssertEqual("ABC[HOLDER_LAST_NAME]+(1)", stored, "track 2 native slot content");

            var reloaded = codec.Load(changedPath);
            AssertEqual("ABC[HOLDER_LAST_NAME]+(1)", reloaded.MagneticStripe.Track2,
                "track 2 save/load round-trip");
            AssertEqual(string.Empty, reloaded.MagneticStripe.Track1, "track 1 remains unchanged");
            AssertEqual(string.Empty, reloaded.MagneticStripe.Track3, "track 3 remains unchanged");

            var changedFourth = System.Text.Encoding.Latin1
                .GetString(changed, UltraPrint22115LayoutCodec.KnownUnknownGlobalStringOffset,
                    UltraPrint22115LayoutCodec.MagneticTrackStringLength)
                .TrimEnd('\0', ' ');
            AssertEqual(".\\Foto", changedFourth, "track edit preserves fourth unknown String*256");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { }
        }
    }

    private static void AssertSlotIsZeroFilled(byte[] data, int offset, string message)
    {
        for (var i = 0; i < UltraPrint22115LayoutCodec.MagneticTrackStringLength; i++)
        {
            if (data[offset + i] != 0)
                throw new InvalidOperationException($"FAILED: {message} expected zero at +{i}, actual {data[offset + i]}.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }
}
