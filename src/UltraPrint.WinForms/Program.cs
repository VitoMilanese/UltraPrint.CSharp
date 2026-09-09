using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = LegacyStartupOptions.Parse(args);
        Application.Run(new MainForm(options));
    }
}
