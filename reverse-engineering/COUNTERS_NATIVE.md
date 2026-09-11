# Legacy counter / `+(...)` token recovery

UltraPrint 2.2.115 uses the `+(...)` stage in magnetic-track preparation as a persistent counter expansion. The resolver is native helper `0x005F5A90`; its table is the same data edited by `frmContatori`, and matched updates are saved immediately to `App.Path\Contatori.dat`.

This note records the proven binary contract without enabling automatic writes to a user's legacy counter file.

## `frmContatori` ownership

VB6 metadata identifies the UI object `frmContatori`. Its recovered controls/events include:

- `ListaContatori`
- `cmdNuovo`
- `CmdElimina`
- `cbocifre`
- `txtValore`
- `Check1`
- `Ok`

The form and resolver share the table pointer at `0x0062B26C`.

`frmContatori.Form_Load` calls helper `0x005F5E70`; `frmContatori.Ok` calls `0x005F60C0`. Both helpers build the literal path:

```text
App.Path\Contatori.dat
```

The first loads the counter table and the second writes it. Resolver `0x005F5A90` calls that same save helper after every successful counter update.

## Recovered counter record

The resolver iterates logical counter indices **1 through 99**. Each in-memory entry has a 32-byte stride. Proven fields are:

| Offset | Size | Recovered meaning |
| ---: | ---: | --- |
| `+0x00` | fixed 10-character string | counter name |
| `+0x14` | WORD | configured number of digits (`cbocifre`) |
| `+0x16` | WORD Boolean | enable `Funzioni.Zeri` formatting (`Check1`) |
| `+0x1C` | Long | current counter value (`txtValore`) |

Bytes not listed above remain intentionally unclaimed.

`cmdNuovo` initializes the proven defaults:

- digits = `6`;
- zero-format Boolean = VB True (`0xFFFF`).

## `0x005F5A90` resolution

The helper initializes its return Variant to an empty string, then loops indices 1..99. For each record it:

1. converts the fixed 10-character name to a Variant;
2. applies VB `Trim()`;
3. compares that name with the token body supplied by the `+(...)` expansion helper.

If no record matches, it returns empty text and does not save the table.

On a matching record it:

1. reads the Long at `+0x1C`;
2. adds the resolver's numeric increment argument;
3. converts the result back to a Long and stores it at `+0x1C`;
4. converts the updated value to text;
5. if `+0x16` is true, formats that text through `Funzioni.Zeri` using `+0x14` as the configured width;
6. calls counter-table save helper `0x005F60C0`;
7. returns the formatted updated value.

The production smartDriver path passes raw increment **1**, so `+(CounterName)` increments its matching counter before substitution.

## `Funzioni.Zeri`

`Funzioni.Zeri` is native method `0x004A1D40`, vtable `+0x7DC`. Its recovered formatting expression is structurally:

```text
Right(String(width, "0") + Trim(value), width)
```

MSVBVM60 ordinal 607 supplies `String()` and ordinal 619 supplies `Right()` in this build. Therefore a shorter value is left-padded with zeroes. A value longer than the configured width follows the literal legacy `Right(..., width)` behavior and loses leading characters rather than expanding the width.

Example with width 6:

```text
42      -> 000042
1000000 -> 000000
```

This unusual overflow presentation is preserved in the pure compatibility semantics rather than silently modernized.

## `0x006079D0` / `+(...)` expansion

Helper `0x006079D0` repeatedly uses `Funzioni.GetInside` with delimiters `"+("` and `")"`. For a non-empty body it calls the counter resolver above, constructs the complete token `"+(" & body & ")"`, and passes it plus the resolved text to `Funzioni.Sostituisci`.

`Funzioni.Sostituisci` (`0x004A36F0`) performs binary `InStr`-style replacement in a loop until no exact match remains. The counter resolver runs once before that replacement call. Consequently, if the same exact token occurs multiple times in the current track string, one counter increment produces one replacement value and all identical occurrences receive that same value.

Different `+(...)` bodies are processed by subsequent iterations of `0x006079D0`.

## Managed boundary

`LegacyCounterTokenSemantics` encodes the proven behavior as pure state:

- helper addresses;
- `Contatori.dat` filename;
- 1..99 search range;
- 32-byte record stride and known field offsets;
- new-counter defaults;
- increment-before-substitution;
- immediate-save contract for a match;
- exact `Zeri` width behavior;
- replace-all behavior for identical exact tokens.

It does **not** currently open, create, or rewrite `Contatori.dat`, because remaining record bytes and write-compatibility should be preserved before enabling persistent managed mutation. The `frmContatori` UI itself also remains to be rebuilt.
