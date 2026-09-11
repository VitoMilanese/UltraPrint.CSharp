using System.Runtime.CompilerServices;
using System.Text;
using UltraPrint.Core.Models;
using UltraPrint.Legacy.Data;

internal static class Record2CardDataFieldCompatibilityTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestNativeOffsets();
        TestDataFieldRead();
        TestDirectAndBracketResolution();
        TestBarcodeRecordBinding();
        TestManagedOverridePrecedence();
        TestPlaceholderFallback();
        TestMagneticTracksSurviveBindingClone();
    }

    private static void TestNativeOffsets()
    {
        AssertEqual(0x553FA0, LegacyNativeDataFieldSemantics.Record2CardNativeAddress,
            "frmCarta.Record2Card native address");
        AssertEqual(0x1D8, LegacyNativeDataFieldSemantics.NativeFieldRecordStrideBytes,
            "native field UDT stride");
        AssertEqual(0x28, LegacyNativeDataFieldSemantics.NativeFieldTypeOffset,
            "native field type offset");
        AssertEqual(0x3C, LegacyNativeDataFieldSemantics.NativePayloadOffset,
            "native payload offset");
        AssertEqual(0x19A, LegacyNativeDataFieldSemantics.NativeDataFieldOffset,
            "native DataField offset");
        AssertEqual(0x1D6, LegacyNativeDataFieldSemantics.NativeFieldLevelOffset,
            "native level offset");

        AssertEqual(20, LegacyNativeDataFieldSemantics.SerializedFieldTypeOffset,
            "serialized field type offset");
        AssertEqual(38, LegacyNativeDataFieldSemantics.SerializedPayloadOffset,
            "serialized payload offset");
        AssertEqual(124, LegacyNativeDataFieldSemantics.SerializedPayloadLength,
            "serialized payload length");
        AssertEqual(230, LegacyNativeDataFieldSemantics.SerializedDataFieldOffset,
            "serialized DataField offset");
        AssertEqual(28, LegacyNativeDataFieldSemantics.SerializedDataFieldLength,
            "serialized DataField length");
        AssertEqual(262, LegacyNativeDataFieldSemantics.SerializedFieldLevelOffset,
            "serialized field level offset");
    }

    private static void TestDataFieldRead()
    {
        var field = FieldWithNativeDataField(LayoutFieldKind.Text, "Name", "  CUSTOMER_NAME  ");
        AssertEqual("CUSTOMER_NAME", LegacyNativeDataFieldSemantics.ReadDataField(field),
            "fixed DataField is trimmed like Record2Card");
    }

    private static void TestDirectAndBracketResolution()
    {
        var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CUSTOMER_NAME"] = "Rossi",
            ["CODE"] = "ABCDEFG",
            ["NULL_VALUE"] = null
        };

        AssertTrue(LegacyNativeDataFieldSemantics.TryResolve(record, "CUSTOMER_NAME", out var direct),
            "non-empty direct DataField is handled");
        AssertEqual("Rossi", direct, "direct Recordset.Fields(DataField) lookup");

        AssertTrue(LegacyNativeDataFieldSemantics.TryResolve(record, "ID-[CODE,3,3]", out var expression),
            "bracket DataField expression is handled");
        AssertEqual("ID-CDE", expression, "bracket expression uses one-based Mid slicing");

        AssertTrue(LegacyNativeDataFieldSemantics.TryResolve(record, "X[NULL_VALUE]Y", out var nullExpression),
            "Null bracket field is still a handled DataField expression");
        AssertEqual("XY", nullExpression, "Null bracket field becomes empty text");

        AssertTrue(LegacyNativeDataFieldSemantics.TryResolve(record, "MISSING", out var missing),
            "missing direct field still represents a non-empty native binding");
        AssertEqual(string.Empty, missing, "missing direct field clears stale payload");

        AssertTrue(!LegacyNativeDataFieldSemantics.TryResolve(record, "   ", out _),
            "blank DataField means no binding");
    }

    private static void TestBarcodeRecordBinding()
    {
        var layout = new CardLayout { Name = "BarcodeBinding", WidthMm = 85, HeightMm = 54, Dpi = 300 };
        var barcode = FieldWithNativeDataField(LayoutFieldKind.Barcode, "Barcode_1", "CARD_NO");
        barcode.LegacyTypeCode = 4;
        barcode.LegacyPayload = "OLD";
        barcode.Text.FontName = "3 OF 9";
        layout.Fields.Add(barcode);

        var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CARD_NO"] = "987654"
        };
        var bound = LegacyRecordBinder.CreateBoundLayout(layout, record);
        var boundBarcode = bound.Fields.Single();

        AssertEqual("987654", boundBarcode.LegacyPayload,
            "type-4 barcode receives Record2Card DataField value in its payload");
        AssertEqual("OLD", barcode.LegacyPayload,
            "binding clone does not mutate the legacy template field");
        AssertEqual("3 OF 9", boundBarcode.Text.FontName,
            "barcode binding preserves recovered barcode font");
    }

    private static void TestManagedOverridePrecedence()
    {
        var layout = new CardLayout { Name = "Override", WidthMm = 85, HeightMm = 54, Dpi = 300 };
        var barcode = FieldWithNativeDataField(LayoutFieldKind.Barcode, "Barcode_1", "NATIVE_VALUE");
        barcode.LegacyTypeCode = 4;
        layout.Fields.Add(barcode);

        var record = new Dictionary<string, object?>
        {
            ["NATIVE_VALUE"] = "native",
            ["MANAGED_VALUE"] = "managed"
        };
        var overrides = new Dictionary<int, string> { [barcode.Index] = "MANAGED_VALUE" };
        var bound = LegacyRecordBinder.CreateBoundLayout(layout, record, overrides);

        AssertEqual("managed", bound.Fields.Single().LegacyPayload,
            "explicit Database / Records override wins over persisted native DataField");
    }

    private static void TestPlaceholderFallback()
    {
        var layout = new CardLayout { Name = "Fallback", WidthMm = 85, HeightMm = 54, Dpi = 300 };
        var barcode = FieldWithNativeDataField(LayoutFieldKind.Barcode, "%CARD_NO%", string.Empty);
        barcode.LegacyTypeCode = 4;
        barcode.Text.DatabaseField = "%CARD_NO%"; // shape produced by older managed codec builds
        layout.Fields.Add(barcode);

        var record = new Dictionary<string, object?> { ["CARD_NO"] = "1234" };
        var bound = LegacyRecordBinder.CreateBoundLayout(layout, record);
        AssertEqual("1234", bound.Fields.Single().LegacyPayload,
            "%COLUMN% compatibility fallback remains available when native DataField is empty");
    }

    private static void TestMagneticTracksSurviveBindingClone()
    {
        var layout = new CardLayout { Name = "MagstripeClone", WidthMm = 85, HeightMm = 54, Dpi = 300 };
        layout.MagneticStripe.Track1 = "TRACK-1";
        layout.MagneticStripe.Track2 = "[CARD_NO]";
        layout.MagneticStripe.Track3 = "+(SerialCounter)";

        var bound = LegacyRecordBinder.CreateBoundLayout(
            layout,
            new Dictionary<string, object?> { ["CARD_NO"] = "1234" });

        AssertEqual("TRACK-1", bound.MagneticStripe.Track1,
            "Record2Card clone preserves layout-global magnetic Track 1 expression");
        AssertEqual("[CARD_NO]", bound.MagneticStripe.Track2,
            "Record2Card clone preserves layout-global magnetic Track 2 expression");
        AssertEqual("+(SerialCounter)", bound.MagneticStripe.Track3,
            "Record2Card clone preserves layout-global magnetic Track 3 expression");

        bound.MagneticStripe.Track1 = "CHANGED";
        AssertEqual("TRACK-1", layout.MagneticStripe.Track1,
            "bound layout owns an independent magnetic-track settings object");
    }

    private static LayoutField FieldWithNativeDataField(
        LayoutFieldKind kind,
        string name,
        string dataField)
    {
        var record = new byte[264];
        var bytes = Encoding.Latin1.GetBytes(dataField);
        if (bytes.Length > LegacyNativeDataFieldSemantics.SerializedDataFieldLength)
            throw new ArgumentOutOfRangeException(nameof(dataField));
        Array.Fill(record, (byte)' ',
            LegacyNativeDataFieldSemantics.SerializedDataFieldOffset,
            LegacyNativeDataFieldSemantics.SerializedDataFieldLength);
        bytes.CopyTo(record,
            LegacyNativeDataFieldSemantics.SerializedDataFieldOffset);

        return new LayoutField
        {
            Index = 0,
            Name = name,
            Kind = kind,
            Side = LayoutSide.Front,
            LegacyRecordTemplate = record,
            WidthMm = 30,
            HeightMm = 10
        };
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
