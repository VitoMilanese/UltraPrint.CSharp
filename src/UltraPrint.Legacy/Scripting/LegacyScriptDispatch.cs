namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Optional engine capability matching the procedure-presence probe performed by
/// native Funzioni.Vbscript before ScriptControl.Run. Engines that cannot expose
/// this information simply omit the interface and dispatch proceeds normally.
/// </summary>
public interface ILegacyScriptProcedureProbe
{
    bool HasProcedures();
}

public sealed partial class LegacyScriptSession
{
    private int _dispatchDepth;
    private bool _unloadRequested;
    private bool _drainingUnload;

    /// <summary>
    /// Current nested Funzioni.Vbscript dispatch depth. Native code stores the same
    /// state in a Variant member and increments/decrements it on every invocation.
    /// </summary>
    public int DispatchDepth => _dispatchDepth;

    /// <summary>
    /// Managed counterpart of the global VB Boolean set by Opzioni.Chiudimi.
    /// The original PreparaCodice rewrites "Unload Me" to "Chiudimi".
    /// </summary>
    public bool UnloadRequested => _unloadRequested;

    public bool IsLoaded => _loaded;

    public void RequestUnload() => _unloadRequested = true;

    /// <summary>
    /// Reproduces the recovered Funzioni.Vbscript dispatch contract: normalize the
    /// procedure name, optionally return the native "NO CODE" sentinel, call Run
    /// with zero to seven optional arguments (stopping at the first Missing), and
    /// defer Form_Unload/Reset until the outermost nested dispatch frame when
    /// Chiudimi requested an unload.
    /// </summary>
    public object? InvokeLegacy(string? prefix, string eventName, params object?[] arguments)
    {
        if (!_loaded) throw new InvalidOperationException("No VBScript code has been loaded.");
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(arguments);
        LegacyScriptInvocation.ValidateOptionalArguments(arguments);

        var procedureName = LegacyScriptInvocation.BuildProcedureName(prefix, eventName);
        var suppliedArguments = LegacyScriptInvocation.GetSuppliedOptionalArguments(arguments);
        _dispatchDepth++;
        try
        {
            if (_engine is ILegacyScriptProcedureProbe probe && !probe.HasProcedures())
                return LegacyScriptInvocation.NoCodeSentinel;

            // Native UltraPrint passes the normalized string directly to
            // ScriptControl.Run (DISPID 2003). It does not resolve '*' itself.
            return _engine.Invoke(procedureName, suppliedArguments);
        }
        finally
        {
            try
            {
                if (_unloadRequested && _dispatchDepth == 1 && !_drainingUnload)
                    DrainRequestedUnload();
            }
            finally
            {
                _dispatchDepth--;
            }
        }
    }

    private void DrainRequestedUnload()
    {
        _drainingUnload = true;
        try
        {
            try
            {
                // Native code recursively calls Vbscript with prefix "Form" and
                // event "Unload" while all seven optional Variants are Missing.
                InvokeLegacy("Form", "Unload");
            }
            finally
            {
                try
                {
                    // The VB6 host also sets Funzioni.Visible = False immediately
                    // before this Reset. The managed session has no equivalent
                    // visible utility form, so only the functional engine reset is
                    // represented here.
                    _engine.Reset();
                }
                finally
                {
                    _loaded = false;
                    _unloadRequested = false;
                }
            }
        }
        finally
        {
            _drainingUnload = false;
        }
    }

    private void ResetDispatchState()
    {
        _dispatchDepth = 0;
        _unloadRequested = false;
        _drainingUnload = false;
    }
}
