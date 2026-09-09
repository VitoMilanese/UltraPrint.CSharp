namespace UltraPrint.Legacy.Startup;

public sealed record StartupPaths(
    string BaseDirectory,
    string CampoIni,
    string UpIni,
    string DbDirectory,
    string PhotoDirectory,
    string ScriptDirectory,
    string LayoutDirectory,
    string OperatorDatabase,
    string LegacyOperatorDatabase)
{
    public static StartupPaths FromBaseDirectory(string baseDirectory)
    {
        baseDirectory = Path.GetFullPath(baseDirectory);
        return new StartupPaths(
            baseDirectory,
            Path.Combine(baseDirectory, "Campo.ini"),
            Path.Combine(baseDirectory, "UP.ini"),
            Path.Combine(baseDirectory, "Db"),
            Path.Combine(baseDirectory, "Foto"),
            Path.Combine(baseDirectory, "Script"),
            Path.Combine(baseDirectory, "Ly"),
            Path.Combine(baseDirectory, "Db", "Operatori.FFM"),
            Path.Combine(baseDirectory, "Operatori.FFM"));
    }

    public void EnsureWorkingDirectories()
    {
        Directory.CreateDirectory(DbDirectory);
        Directory.CreateDirectory(PhotoDirectory);
        Directory.CreateDirectory(ScriptDirectory);
        Directory.CreateDirectory(LayoutDirectory);
    }
}
