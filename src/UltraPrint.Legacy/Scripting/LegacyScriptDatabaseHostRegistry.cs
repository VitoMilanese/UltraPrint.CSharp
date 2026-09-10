namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Process-local identity bridge for the managed database/record workflow. UltraPrint VB6 keeps
/// one global frmDatabase instance (also injected as Db) and one global Tabella instance; both
/// operate on shared application database state.
/// </summary>
public static class LegacyScriptDatabaseHostRegistry
{
    private static readonly object Sync = new();
    private static ILegacyScriptDatabaseHost? _current;

    public static ILegacyScriptDatabaseHost? Current
    {
        get
        {
            lock (Sync) return _current;
        }
        set
        {
            lock (Sync) _current = value;
        }
    }

    public static void ClearIfCurrent(ILegacyScriptDatabaseHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Sync)
        {
            if (ReferenceEquals(_current, host)) _current = null;
        }
    }
}
