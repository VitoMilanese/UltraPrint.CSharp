namespace UltraPrint.Legacy.Scripting;

/// <summary>
/// Managed persistence boundary for the recovered +(CounterName) resolver. It reproduces
/// native load -> first matching trimmed fixed name -> increment/format -> immediate save.
/// It does not execute VBScript, magnetic-stripe code, or printer APIs.
/// </summary>
public sealed class LegacyPersistentCounterResolver
{
    private readonly LegacyCounterFileStore _store;

    public LegacyPersistentCounterResolver(LegacyCounterFileStore? store = null)
    {
        _store = store ?? new LegacyCounterFileStore();
    }

    public LegacyCounterTokenResolution ResolveSmartDriverCounter(string counterFilePath, string counterName) =>
        ResolveAndPersist(counterFilePath, counterName, LegacyCounterTokenSemantics.SmartDriverIncrement);

    public LegacyCounterTokenResolution ResolveAndPersist(
        string counterFilePath,
        string counterName,
        int increment)
    {
        ArgumentException.ThrowIfNullOrEmpty(counterFilePath);
        ArgumentException.ThrowIfNullOrEmpty(counterName);

        var records = _store.Load(counterFilePath).ToArray();
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (!string.Equals(record.NativeResolverName, counterName, StringComparison.Ordinal))
                continue;

            var resolution = LegacyCounterTokenSemantics.ResolveMatchedCounter(
                record.CurrentValue,
                increment,
                record.Digits,
                record.ZeroPadding);

            records[index] = record.WithCurrentValue(resolution.UpdatedValue!.Value);
            _store.Save(counterFilePath, records);
            return resolution;
        }

        return LegacyCounterTokenSemantics.NoMatch;
    }
}
