namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Process-local identity bridge for the live managed Sequenza replacement. Explicit
/// script workspaces must receive the same application sequence state rather than a
/// detached compatibility-only object.
/// </summary>
public static class LegacyScriptSequenceHostRegistry
{
    private static readonly object Sync = new();
    private static ILegacyScriptSequenceHost? _current;

    public static ILegacyScriptSequenceHost? Current
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

    public static void ClearIfCurrent(ILegacyScriptSequenceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Sync)
        {
            if (ReferenceEquals(_current, host)) _current = null;
        }
    }
}
