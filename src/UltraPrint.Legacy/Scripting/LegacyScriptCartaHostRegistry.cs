namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Process-local bridge for the current managed frmCarta equivalent. UltraPrint VB6 uses a
/// global frmCarta singleton, so the optional ScriptControl compatibility workspace needs the
/// same identity boundary when it is opened from the live editor. Explicit hosts passed to
/// LegacyScriptCoreFacadeRegistration still take precedence in tests and isolated consumers.
/// </summary>
public static class LegacyScriptCartaHostRegistry
{
    private static readonly object Sync = new();
    private static ILegacyScriptCartaHost? _current;

    public static ILegacyScriptCartaHost? Current
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

    public static void ClearIfCurrent(ILegacyScriptCartaHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Sync)
        {
            if (ReferenceEquals(_current, host)) _current = null;
        }
    }
}
