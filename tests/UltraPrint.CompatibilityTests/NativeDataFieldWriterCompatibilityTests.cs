using System.Runtime.CompilerServices;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;
using UltraPrint.Legacy.Layout;

internal static class NativeDataFieldWriterCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestWriterTouchesOnlyDataFieldBytes();
        TestModelHydration();
        TestLayoutRoundTrip();
        TestValidation();
    }

    private static void TestWriterTouchesOnlyDataFieldBytes()
    {
        var original = Enumerable.Range(0, 264).Select(index => (byte)(index % 251)).ToArray();
        var field = new LayoutField
        {
            Kind = LayoutFieldKind.Barcode,
            LegacyRecordTemplate = original.ToArray()
        };

        LegacyNativeDataFieldWriter.Write(field, "CARD_NO");

        for (var index = 0; index < field.LegacyRecordTemplate.Length; index++)
        {
            var inside = index >= LegacyNativeDataFieldSemantics.SerializedDataFieldOffset &&
                         index < LegacyNativeDataFieldSemantics.SerializedDataFieldOffset +
                         LegacyNativeDataFieldSemantics.SerializedDataFieldLength;
            if (!inside)
                AssertEqual(original[index], field.LegacyRecordTemplate[index],
                    $"native DataField writer preserves byte {index}");
        }

        AssertEqual("CARD_NO", LegacyNativeDataFieldSemantics.ReadDataField(field),
            "written DataField can be read from raw record");
        AssertEqual("CARD_NO", field.Text.DatabaseField,
            "barcode runtime binding mirrors written native DataField");
    }

    private static void TestModelHydration()
    {
        var image = new LayoutField
        {
            Kind = LayoutFieldKind.Image,
            LegacyRecordTemplate = new byte[264]
        };
        LegacyNativeDataFieldWriter.Write(image, "PHOTO");
        AssertEqual("PHOTO", image.Image.DatabaseField,
            "image native DataField mirrors into image binding model");
        AssertEqual(string.Empty, image.Text.DatabaseField,
            "image binding does not leak into text settings");
    }

    private static void TestLayoutRoundTrip()
    {
        var codec = new UltraPrint22115LayoutCodec();
        var layout = codec.CreateNewLayout("NativeDataField");
        var barcode = codec.CreateField(layout, LayoutFieldKind.Barcode, LayoutSide.Front, legacyTypeCode: 4);
        barcode.LegacyPayload = "123456";
        barcode.Text.FontName = "3 OF 9";
        LegacyNativeDataFieldWriter.Write(barcode, "CARD_NO");

        var path = Path.Combine(Path.GetTempPath(), "UltraPrint.DataField." + Guid.NewGuid().ToString("N") + ".ly");
        try
        {
            codec.Save(layout, path);
            var reloaded = codec.Load(path);
            var recovered = reloaded.Fields.Single(field => field.Kind == LayoutFieldKind.Barcode);
            AssertEqual("CARD_NO", LegacyNativeDataFieldSemantics.ReadDataField(recovered),
                "native DataField survives actual .ly save/load");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestValidation()
    {
        var field = new LayoutField
        {
            Kind = LayoutFieldKind.Text,
            LegacyRecordTemplate = new byte[264]
        };
        LegacyNativeDataFieldWriter.Write(field, new string('X', 28));
        AssertEqual(new string('X', 28), LegacyNativeDataFieldSemantics.ReadDataField(field),
            "28-byte DataField is accepted");

        AssertThrows<ArgumentOutOfRangeException>(
            () => LegacyNativeDataFieldWriter.Write(field, new string('X', 29)),
            "DataField longer than 28 bytes is rejected");
        AssertThrows<ArgumentException>(
            () => LegacyNativeDataFieldWriter.Write(field, "名前"),
            "unproven non-ANSI DataField encoding is rejected");
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

    private static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAILED: {message}. Expected '{expected}', actual '{actual}'.");
    }
}
