using System.Globalization;
using Microsoft.VisualBasic.CompilerServices;

namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Managed counterpart of the two parallel Variant arrays used by
/// Funzioni.GetVariabile / SetVariabile / IncVariabile. Native code scans slots
/// 1..1024 and compares names after Trim + case normalization. SetVariabile only
/// updates an existing name; creation is performed by other UltraPrint workflows.
/// </summary>
public sealed class LegacyScriptVariableTable : ILegacyScriptVariableStore
{
    public const int Capacity = 1024;

    private readonly string?[] _names = new string?[Capacity];
    private readonly object?[] _values = new object?[Capacity];

    public int Count => _names.Count(x => x is not null);

    /// <summary>
    /// Host-side initialization helper. This is deliberately separate from the
    /// native SetVariabile contract because SetVariabile does not create slots.
    /// </summary>
    public void Define(string name, object? value)
    {
        var normalized = NormalizeName(name);
        var existing = FindIndex(normalized);
        if (existing >= 0)
        {
            _values[existing] = value;
            return;
        }

        var free = Array.FindIndex(_names, x => x is null);
        if (free < 0) throw new InvalidOperationException($"UltraPrint variable table is full ({Capacity} entries).");
        _names[free] = normalized;
        _values[free] = value;
    }

    public bool Contains(string name) => FindIndex(NormalizeName(name)) >= 0;

    public object? GetVariabile(string name)
    {
        var index = FindIndex(NormalizeName(name));
        return index < 0 ? null : _values[index];
    }

    /// <summary>
    /// Matches the recovered SetVariabile behavior: update an existing slot and do
    /// nothing when the variable name is absent.
    /// </summary>
    public bool SetVariabile(string name, object? value)
    {
        var index = FindIndex(NormalizeName(name));
        if (index < 0) return false;
        _values[index] = value;
        return true;
    }

    /// <summary>
    /// Native IncVariabile performs VB Variant addition with the integer 1 on the
    /// existing value. Missing names are ignored.
    /// </summary>
    public bool IncVariabile(string name)
    {
        var index = FindIndex(NormalizeName(name));
        if (index < 0) return false;
        _values[index] = Operators.AddObject(_values[index] ?? 0, 1);
        return true;
    }

    public IReadOnlyList<KeyValuePair<string, object?>> Snapshot()
    {
        var result = new List<KeyValuePair<string, object?>>(Count);
        for (var i = 0; i < _names.Length; i++)
        {
            if (_names[i] is { } name)
                result.Add(new KeyValuePair<string, object?>(name, _values[i]));
        }
        return result;
    }

    string? ILegacyScriptVariableStore.Get(string name)
    {
        var value = GetVariabile(name);
        return value is null ? null : Convert.ToString(value, CultureInfo.CurrentCulture);
    }

    void ILegacyScriptVariableStore.Set(string name, string value) => SetVariabile(name, value);

    private int FindIndex(string normalizedName)
    {
        for (var i = 0; i < _names.Length; i++)
        {
            if (string.Equals(_names[i], normalizedName, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpper(CultureInfo.CurrentCulture);
    }
}
