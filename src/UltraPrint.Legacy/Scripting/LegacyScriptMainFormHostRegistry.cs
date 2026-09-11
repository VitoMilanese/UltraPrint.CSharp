namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Process-local identity bridge for the live managed MainForm. The VB6 program exposes one global
/// MainForm instance to ScriptControl, so an explicitly opened script workspace must see the same
/// application object rather than a detached replacement.
/// </summary>
public static class LegacyScriptMainFormHostRegistry
{
    private static readonly object Sync = new();
    private static ILegacyScriptMainFormHost? _current;

    public static ILegacyScriptMainFormHost? Current
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

    public static void ClearIfCurrent(ILegacyScriptMainFormHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Sync)
        {
            if (ReferenceEquals(_current, host)) _current = null;
        }
    }
}
