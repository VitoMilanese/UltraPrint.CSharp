# smartDriver magnetic-track transform pipeline recovery

This note extends the recovered `0x00603FC0` contract from `SMARTDRIVER_MAGSTRIPE_PREPARATION_NATIVE.md`. It closes the structural order used to transform each global `Traccia1/2/3` value before assignment to the corresponding `frmCarta` track property. The replacement meanings inside the two private token-expansion helpers remain deliberately unclaimed.

## Per-track pipeline

Native `0x00603FC0` performs the same sequence independently for `Traccia1`, `Traccia2`, and `Traccia3`:

```text
raw TracciaN
 -> Trim(...)
 -> Funzioni.Interpretariga(..., True)
 -> remove Chr$(0)
 -> private +(...) expansion helper 0x006079D0
 -> private [...] expansion helper 0x00606910
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

For every source global the helper first copies the BSTR into a by-reference Variant and calls the imported MSVBVM60 runtime ordinal **520**, `rtcTrimVar`, i.e. VB `Trim()`.

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

Thus embedded NUL characters are removed before either private token-expansion helper runs.

## `+(...)` expansion stage — `0x006079D0`

The first private stage receives the current track plus the same raw mode propagated into `0x00603FC0`; the production smartDriver call sites use raw value `1`.

`0x006079D0` calls `Funzioni.GetInside` (`+0x7CC`) with the literal delimiters `"+("` and `")"`. When a non-empty body is found, it passes that extracted body plus the raw mode to private helper `0x005F5A90`, then uses `Funzioni.Sostituisci` to replace the complete `"+(" & body & ")"` occurrence. It repeats the extraction/replacement loop until no non-empty `+(...)` body remains.

The exact semantic meaning of the replacement Variant returned by `0x005F5A90` is not yet claimed. Therefore managed compatibility records this as a **private plus-parenthesized token expansion stage**, not as a guessed counter/variable operation.

## `[...]` expansion stage — `0x00606910`

The output of `0x006079D0` is then passed to private helper `0x00606910`.

Its opening structure again calls `Funzioni.GetInside` (`+0x7CC`), this time with literal delimiters `"["` and `"]"`, and tests the extracted Variant against an empty string before entering its deeper branch logic.

This proves that bracketed constructs are processed after the `+(...)` stage. The full resolver/replacement semantics inside `0x00606910` remain more complex and are intentionally left unresolved in this slice.

## Managed boundary

`LegacySmartDriverMagstripeTransformPipelineSemantics` records only the proven structural facts:

- stage order;
- MSVBVM60 Trim ordinal;
- `Interpretariga`, `Sostituisci`, and `GetInside` vtable offsets;
- `Interpretariga(..., True)` Boolean value;
- `Chr$(0)` removal;
- private helper addresses;
- `+(...)` and `[...]` delimiters;
- production raw expansion mode `1`;
- `0x005F5A90` as the still-unresolved `+(...)` replacement resolver.

No actual token resolver, script engine, COM facade, ICE API, GDI call, printer operation, or card mutation is executed by this compatibility layer.
