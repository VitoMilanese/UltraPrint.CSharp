# Legacy counter / `+(...)` token recovery

UltraPrint 2.2.115 uses the `+(...)` stage in magnetic-track preparation as a persistent counter expansion. The resolver is native helper `0x005F5A90`; its table is the same data edited by `frmContatori`, and matched updates are saved immediately to `App.Path\Contatori.dat`.

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

## Recovered in-memory counter record

The resolver iterates logical counter indices **1 through 99**. Each in-memory entry has a 32-byte stride. Proven fields are:

| Memory offset | Size | Recovered meaning |
| ---: | ---: | --- |
| `+0x00` | fixed `String * 10` = 20 bytes in VB6 memory | counter name |
| `+0x14` | WORD | configured number of digits (`cbocifre`) |
| `+0x16` | WORD Boolean | enable `Funzioni.Zeri` formatting (`Check1`) |
| `+0x18` | WORD | still-unclaimed field; must be preserved |
| `+0x1C` | Long | current counter value (`txtValore`) |

`cmdNuovo` initializes the proven defaults:

- digits = `6`;
- zero-format Boolean = VB True (`0xFFFF`).

## Exact `Contatori.dat` serialized record

The VB6 UDT descriptor at `0x0042ACFC` proves that the 32-byte in-memory record is serialized without its memory alignment and with the fixed `String * 10` occupying **10 bytes on disk**. The exact serialized record is therefore **20 bytes**:

| File offset | Size | Recovered meaning |
| ---: | ---: | --- |
| `+0x00` | 10 | fixed counter name bytes |
| `+0x0A` | 2 | digits |
| `+0x0C` | 2 | raw zero-padding Boolean WORD |
| `+0x0E` | 2 | unknown WORD, preserved byte-for-byte |
| `+0x10` | 4 | current counter Long |

All numeric values are native little-endian x86 values.

### Load helper `0x005F5E70`

The helper opens `Contatori.dat` **For Binary** and performs sequential `Get` operations for logical indices **1..99**. It therefore consumes the first 99 serialized records, i.e. 1980 bytes. A canonical native file contains one additional trailing blank record that the load helper never reads.

A missing/empty file is treated as an empty counter table by the managed compatibility layer. A non-empty file shorter than 99 complete records is rejected as damaged rather than silently synthesizing partially read legacy state.

### Save helper `0x005F60C0`

The native save path is not a direct dump of the 99 in-memory slots:

1. it deletes the previous file and opens a new Binary file;
2. loops logical indices 1..99;
3. writes only records whose exact predicate is `Left(Name, 1) > " "`;
4. therefore compacts active records while preserving their logical order;
5. counts how many active records were written;
6. runs a second loop `For i = activeCount To 99` **inclusive** and writes a blank UDT each time.

The second pass writes `100 - activeCount` blank records. Consequently every canonical native save contains exactly **100 serialized records = 2000 bytes**, including at least one trailing blank record even when all 99 logical counters are active.

The blank local UDT is zero-initialized by VB6, including the fixed string, so its canonical disk representation is twenty zero bytes.

`LegacyCounterFileStore` reproduces this compaction and fixed 2000-byte save shape. Existing name bytes are mapped losslessly through U+0000..U+00FF so every byte survives round-trip. New names containing wider Unicode are rejected for now rather than guessing the original Windows ANSI/DBCS code page.

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

`LegacyPersistentCounterResolver` now reproduces this load/match/increment/format/immediate-save boundary against `LegacyCounterFileStore`. It preserves the unclaimed serialized WORD while changing only the current counter value before the native-shaped compacting save.

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

This unusual overflow presentation is preserved in the compatibility semantics rather than silently modernized.

## `0x006079D0` / `+(...)` expansion

Helper `0x006079D0` repeatedly uses `Funzioni.GetInside` with delimiters `"+("` and `")"`. For a non-empty body it calls the counter resolver above, constructs the complete token `"+(" & body & ")"`, and passes it plus the resolved text to `Funzioni.Sostituisci`.

`Funzioni.Sostituisci` (`0x004A36F0`) performs binary `InStr`-style replacement in a loop until no exact match remains. The counter resolver runs once before that replacement call. Consequently, if the same exact token occurs multiple times in the current track string, one counter increment produces one replacement value and all identical occurrences receive that same value.

Different `+(...)` bodies are processed by subsequent iterations of `0x006079D0`.

## Managed boundary

The counter subsystem is no longer semantics-only:

- `LegacyCounterTokenSemantics` preserves the proven increment, formatting and replacement rules;
- `LegacyCounterFileStore` reads/writes the recovered 20-byte UDT representation and exact native 100-record save shape;
- `LegacyPersistentCounterResolver` performs the recovered immediate persistent increment for a matched counter;
- unknown serialized field `+0x0E` is retained unchanged;
- corrupted short non-empty files are rejected instead of being rewritten.

Still not implemented in this slice: the `frmContatori` UI for list/create/delete/edit/reset, automatic hookup of persistent counter expansion to live hardware printing, and exact system-code-page interpretation for newly entered characters outside the lossless one-byte mapping. Those remain separate work so persistence can be validated without silently activating printer paths.
