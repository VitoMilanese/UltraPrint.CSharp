using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Optional compatibility adapter for the exact Microsoft Script Control family
/// shipped by the UltraPrint 2.2.115 installer (MSSCRIPT.OCX). The managed rewrite
/// does not require the OCX for normal operation; this adapter is activated only
/// when the COM component is registered and execution is explicitly requested.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsScriptControlEngine : ILegacyScriptEngine, ILegacyScriptProcedureProbe
{
    private readonly object _control;
    private readonly bool _allowUi;
    private string? _currentSourcePath;
    private bool _disposed;

    private MsScriptControlEngine(object control, bool allowUi)
    {
        _control = control;
        _allowUi = allowUi;
        ConfigureAfterReset();
    }

    public string Description => $"Microsoft Script Control ({LegacyScriptContract.ScriptControlProgId})";

    public static bool IsAvailable(out string? reason)
    {
        reason = null;
        if (!OperatingSystem.IsWindows())
        {
            reason = "Microsoft Script Control is a Windows COM component.";
            return false;
        }

        try
        {
            var type = Type.GetTypeFromProgID(LegacyScriptContract.ScriptControlProgId, throwOnError: false);
            if (type is null)
            {
                reason = $"COM ProgID '{LegacyScriptContract.ScriptControlProgId}' is not registered for this process architecture.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public static bool TryCreate(out MsScriptControlEngine? engine, out string? error, bool allowUi = false)
    {
        engine = null;
        error = null;
        try
        {
            var type = Type.GetTypeFromProgID(LegacyScriptContract.ScriptControlProgId, throwOnError: false);
            if (type is null)
            {
                error = $"COM ProgID '{LegacyScriptContract.ScriptControlProgId}' is not registered for this process architecture.";
                return false;
            }

            var instance = Activator.CreateInstance(type);
            if (instance is null)
            {
                error = "Microsoft Script Control could not be instantiated.";
                return false;
            }

            engine = new MsScriptControlEngine(instance, allowUi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Reset()
    {
        ThrowIfDisposed();
        try
        {
            InvokeMethod(_control, "Reset");
            ConfigureAfterReset();
            _currentSourcePath = null;
        }
        catch (Exception ex)
        {
            throw CreateScriptException(ex);
        }
    }

    public void AddObject(string name, object value, bool addMembers = true)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            InvokeMethod(_control, "AddObject", name.Trim(), value, addMembers);
        }
        catch (Exception ex)
        {
            throw CreateScriptException(ex);
        }
    }

    public void ExecuteStatement(string statement, string? sourcePath = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(statement);
        _currentSourcePath = sourcePath;
        try
        {
            InvokeMethod(_control, "ExecuteStatement", statement);
        }
        catch (Exception ex)
        {
            throw CreateScriptException(ex);
        }
    }

    public void AddCode(string code, string? sourcePath = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(code);
        _currentSourcePath = sourcePath;
        try
        {
            InvokeMethod(_control, "AddCode", code);
        }
        catch (Exception ex)
        {
            throw CreateScriptException(ex);
        }
    }

    public bool HasProcedures()
    {
        ThrowIfDisposed();
        object? modules = null;
        object? module = null;
        object? procedures = null;
        object? procedure = null;
        try
        {
            // Native Funzioni.Vbscript runs under On Error Resume Next and probes
            // Modules.Item(1).Procedures.Item(1), then calls rtcIsEmpty on the
            // returned Variant. It does not enumerate or compare Count values.
            modules = GetProperty(_control, "Modules");
            if (modules is null) return false;
            module = GetIndexedProperty(modules, "Item", 1);
            if (module is null) return false;
            procedures = GetProperty(module, "Procedures");
            if (procedures is null) return false;
            procedure = GetIndexedProperty(procedures, "Item", 1);
            return procedure is not null && procedure is not DBNull;
        }
        catch (TargetInvocationException)
        {
            // Missing Item(1) is exactly the condition hidden by the native
            // Resume Next path before it returns the "NO CODE" sentinel.
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (procedure is not null) ReleaseComObject(procedure);
            if (procedures is not null) ReleaseComObject(procedures);
            if (module is not null) ReleaseComObject(module);
            if (modules is not null) ReleaseComObject(modules);
        }
    }

    public object? Invoke(string procedureName, params object?[] arguments)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureName);
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            // Native Funzioni.Vbscript late-calls ScriptControl.Run (DISPID 2003)
            // directly. Keep the same boundary instead of invoking CodeObject.
            var invokeArguments = new object?[arguments.Length + 1];
            invokeArguments[0] = procedureName;
            Array.Copy(arguments, 0, invokeArguments, 1, arguments.Length);
            return InvokeMethod(_control, "Run", invokeArguments);
        }
        catch (Exception ex)
        {
            throw CreateScriptException(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseComObject(_control);
        GC.SuppressFinalize(this);
    }

    private void ConfigureAfterReset()
    {
        SetProperty(_control, "Language", LegacyScriptContract.Language);
        TrySetProperty(_control, "AllowUI", _allowUi);
    }

    private LegacyScriptException CreateScriptException(Exception exception)
    {
        Exception root = exception is TargetInvocationException { InnerException: not null } tie
            ? tie.InnerException!
            : exception;

        var description = root.Message;
        var line = 0;
        var column = 0;
        object? errorObject = null;
        try
        {
            errorObject = GetProperty(_control, "Error");
            if (errorObject is not null)
            {
                description = Convert.ToString(GetProperty(errorObject, "Description"))?.Trim() is { Length: > 0 } text
                    ? text
                    : description;
                line = ConvertToInt32(GetProperty(errorObject, "Line"));
                column = ConvertToInt32(GetProperty(errorObject, "Column"));
            }
        }
        catch
        {
            // The original program also falls back to its runtime error path when
            // ScriptControl.Error itself cannot be read.
        }
        finally
        {
            if (errorObject is not null) ReleaseComObject(errorObject);
        }

        return new LegacyScriptException(
            new LegacyScriptDiagnostic(description, line, column, _currentSourcePath),
            root);
    }

    private static object? InvokeMethod(object target, string name, params object?[] arguments) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
            binder: null,
            target: target,
            args: arguments);

    private static object? GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty,
            binder: null,
            target: target,
            args: null);

    private static object? GetIndexedProperty(object target, string name, params object?[] arguments) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty,
            binder: null,
            target: target,
            args: arguments);

    private static void SetProperty(object target, string name, object value) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty,
            binder: null,
            target: target,
            args: new[] { value });

    private static void TrySetProperty(object target, string name, object value)
    {
        try { SetProperty(target, name, value); }
        catch { }
    }

    private static int ConvertToInt32(object? value)
    {
        try { return value is null ? 0 : Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static void ReleaseComObject(object value)
    {
        if (!Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); }
        catch { }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
