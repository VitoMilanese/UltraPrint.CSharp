# smartDriver magnetic-track transform pipeline recovery

This note extends the recovered `0x00603FC0` contract from `SMARTDRIVER_MAGSTRIPE_PREPARATION_NATIVE.md`. It records the structural order used to transform each global `Traccia1/2/3` value before assignment to the corresponding `frmCarta` track property. Both token-expansion stages are now identified: `+(...)` is persistent `frmContatori` counter expansion and `[...]` is active-record field expansion.

## Per-track pipeline

Native `0x00603FC0` performs the same sequence independently for `Traccia1`, `Traccia2`, and `Traccia3`:

```text
raw TracciaN
 -> Trim(...)
 -> Funzioni.Interpretariga(..., True)
 -> remove Chr$(0)
 -> persistent counter expansion +(CounterName) via 0x006079D0
 -> active record-field expansion [...] via 0x00606910
 -> frmCarta track setter
```

The three final setter/getter pairs remain:

| Global | getter | setter |
| --- | ---: | ---: |
| `Traccia1` | `+0x75C` | `+0x760` |
| `Traccia2` | `+0x768` | `+0x76C` |
| `Traccia3` | `+0x774` | `+0x778` |

After all three assignments, `0x00603FC0` rereads the getters and returns the sum of their VB `Len(...)` values, as documented in the preparation-helper note.

## Trim and Interpretariga

For every source global the helper first copies the BSTR into a by-reference Variant and calls imported MSVBVM60 ordinal **520**, `rtcTrimVar`, i.e. VB `Trim()`.

The trimmed Variant is then passed to the shared `Funzioni` singleton through vtable offset `+0x840`, recovered independently as `Funzioni.Interpretariga`. The second explicit argument is a Variant Boolean containing `0xFFFF`, VB `True`.

The recovered structural call is therefore:

```text
Interpretariga(Trim(TracciaN), True)
```

The returned Variant is converted to the working BSTR for the remaining preparation stages.

## Embedded-NUL removal

Immediately after all three `Interpretariga` calls, native code constructs character code zero through MSVBVM60 `rtcBstrFromAnsi`, i.e. `Chr$(0)`, and calls `Funzioni.Sostituisci` at vtable offset `+0x7F8` for each interpreted track.

`Sostituisci` is a by-reference replacement helper, so this stage is structurally equivalent to:

```text
Sostituisci(track, Chr$(0), "")
```

Thus embedded NUL characters are removed before either token-expansion helper runs.

## `+(...)` persistent counter stage — `0x006079D0`

The first private stage receives the current track plus the same raw mode propagated into `0x00603FC0`; the production smartDriver call sites use raw value `1`.

`0x006079D0` calls `Funzioni.GetInside` (`+0x7CC`) with literal delimiters `"+("` and `")"`. A non-empty body is passed with the raw mode to `0x005F5A90`, proven to be the resolver for the persistent table edited by `frmContatori`.

For smartDriver, resolver mode `1` increments the matching counter by one **before** formatting and substitution, immediately persists the updated table to `App.Path\Contatori.dat`, and returns the updated value. The complete token is then replaced through `Funzioni.Sostituisci`.

`Sostituisci` replaces every identical exact occurrence before returning, so duplicate occurrences of the same `+(CounterName)` in one current track receive one resolved value from one increment. Different counter-token bodies are processed on later `0x006079D0` iterations.

The full counter record, `Funzioni.Zeri`, persistence, defaults, and replace-all behavior are documented in [`COUNTERS_NATIVE.md`](COUNTERS_NATIVE.md).

## `[...]` active record-field stage — `0x00606910`

The output of counter expansion is passed to `0x00606910`. It repeatedly uses `Funzioni.GetInside` (`+0x7CC`) with `"["` / `"]"`.

The helper chooses its legacy record source from form-loaded WORD flags:

- `frmDatabase` loaded (`0x0062A04C`) -> its `Data1` data control;
- `Tabella` loaded (`0x0062A04A`) -> its `datPrimaryRS` data control, overriding `frmDatabase` if both are active;
- neither loaded -> return the input unchanged without replacing brackets.

A bracket body without commas is a field token. If a comma is present, `Funzioni.Parola` (`+0x7F0`) reads at most three comma-separated arguments, while the second and third are converted through VB `Val()`:

```text
[field]
[field,start]
[field,start,length]
```

The first token is wrapped back as `"[" & field & "]"` and looked up through the selected data control's late-bound `.Recordset.Fields(...).Name` path. Lookup failure yields empty replacement. Existing fields are read again; `IsNull()` also maps Null values to empty replacement.

When `start > 0`, VB `Mid()` is applied using one-based character positions. Positive `length` selects `Mid(value,start,length)`; absent/non-positive length selects `Mid(value,start)`. Finally `Funzioni.Sostituisci` replaces the **complete original token** (`[raw body]`) everywhere in the current string, and the outer helper repeats for the next bracket token.

The full source-selection, field-lookup, error/null, substring and replace-all contract is documented in [`BRACKET_RECORD_FIELDS_NATIVE.md`](BRACKET_RECORD_FIELDS_NATIVE.md).

## Managed boundary

The managed compatibility layer splits the recovered behavior into pure contracts:

- `LegacySmartDriverMagstripeTransformPipelineSemantics` records the overall stage order;
- `LegacyCounterTokenSemantics` records persistent `+(...)` counter effects without file I/O;
- `LegacyBracketRecordFieldSemantics` records `[...]` source/field/slicing behavior without COM/database I/O.

No actual counter-file write, legacy data-control access, script engine, COM facade, ICE API, GDI call, printer operation, or card mutation is executed by these compatibility models. Remaining work is implementation/integration against managed data/counter stores while preserving the recovered native contracts.
