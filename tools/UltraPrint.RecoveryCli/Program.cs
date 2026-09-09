using UltraPrint.Legacy.Configuration;
using UltraPrint.Legacy.Layout;
using UltraPrint.Legacy.Startup;

return args switch
{
    ["probe-layout", var path] => ProbeLayout(path),
    ["decode-layout", var path] => DecodeLayout(path),
    ["parse-campo", var path] => ParseCampo(path),
    ["startup-plan", var baseDirectory] => StartupPlan(baseDirectory),
    _ => Usage()
};

static int ProbeLayout(string path)
{
    Console.WriteLine(LegacyLayoutProbe.Probe(path).ToReport());
    return 0;
}

static int DecodeLayout(string path)
{
    var layout = new UltraPrint22115LayoutCodec().Load(path);
    Console.WriteLine($"Layout: {layout.Name}");
    Console.WriteLine($"Card: {layout.WidthMm:0.###} x {layout.HeightMm:0.###} mm @ {layout.Dpi} DPI");
    Console.WriteLine($"Fields: {layout.Fields.Count}");
    foreach (var field in layout.Fields)
    {
        var payload = field.Kind == UltraPrint.Core.Models.LayoutFieldKind.Image
            ? field.Image.File
            : field.Text.Content;
        Console.WriteLine(
            $"  #{field.Index:00} {field.Side,-5} type={field.LegacyTypeCode,2} " +
            $"X={field.Xmm,7:0.###} Y={field.Ymm,7:0.###} W={field.WidthMm,7:0.###} H={field.HeightMm,7:0.###} " +
            $"{field.Name} -> {payload}");
    }
    return 0;
}

static int ParseCampo(string path)
{
    var document = LegacyIniDocument.Load(path);
    var schema = CampoSchemaParser.ParseFile(path);
    var current = document.GetSection(CampoCurrentValueStore.CurrentSection);
    Console.WriteLine($"Sections: {schema.Sections.Count}");
    foreach (var section in schema.Sections) Console.WriteLine($"  [{section.Name}] {section.Entries.Count} entries");
    Console.WriteLine($"Current [$] values: {current.Count}");
    foreach (var pair in current.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"  {pair.Key}={pair.Value.TrimEnd()}");
    return 0;
}

static int StartupPlan(string baseDirectory)
{
    var p = StartupPaths.FromBaseDirectory(baseDirectory);
    Console.WriteLine($"Base: {p.BaseDirectory}");
    Console.WriteLine($"Campo.ini: {p.CampoIni}");
    Console.WriteLine($"UP.ini: {p.UpIni}");
    Console.WriteLine($"Db: {p.DbDirectory}");
    Console.WriteLine($"Foto: {p.PhotoDirectory}");
    Console.WriteLine($"Script: {p.ScriptDirectory}");
    Console.WriteLine($"Ly: {p.LayoutDirectory}");
    Console.WriteLine($"Operators DB: {p.OperatorDatabase}");
    Console.WriteLine($"Legacy operators DB: {p.LegacyOperatorDatabase}");
    return 0;
}

static int Usage()
{
    Console.WriteLine("UltraPrint.RecoveryCli");
    Console.WriteLine("  probe-layout <file.ly>");
    Console.WriteLine("  decode-layout <file.ly>");
    Console.WriteLine("  parse-campo <Campo.ini>");
    Console.WriteLine("  startup-plan <legacy-app-directory>");
    return 2;
}
