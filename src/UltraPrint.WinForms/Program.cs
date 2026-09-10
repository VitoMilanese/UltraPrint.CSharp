using UltraPrint.Legacy.Startup;

namespace UltraPrint.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = LegacyStartupOptions.Parse(args);
        var mainForm = new MainForm(options);
        DatabaseWorkspaceIntegration.Attach(mainForm);
        SequenceIntegration.Attach(mainForm);
        SecurityIntegration.Attach(mainForm);
        ScriptIntegration.Attach(mainForm);
        Application.Run(mainForm);
    }
}
