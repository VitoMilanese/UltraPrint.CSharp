# UltraPrint 2.2.115 magnetic-track editor recovery

This note records the native behavior recovered for `frmTracce` and the three layout-global
magnetic-track expressions. It is intentionally limited to editor/persistence semantics; no
printer or magnetic-encoder API is enabled by this work.

## Native `frmTracce`

Recovered procedure addresses:

- `Form_Load` — `0x005E83E0`
- `Form_Unload` — `0x005E87C0`
- `Ok` — `0x005E8A50`
- `Traccia` change event — `0x005E8B60`
- `Traccia` OLE-drop event — `0x005E8DC0`

The three source globals are consecutive BSTR variables:

- `Traccia1` — `0x0062A05C`
- `Traccia2` — `0x0062A060`
- `Traccia3` — `0x0062A064`

`Form_Load` trims these globals and places them in the three `Traccia(0..2)` text controls.
When a track changes, native code rereads all three controls and synchronizes all three globals.
The OK handler therefore does not need to copy text again: it invokes the shared target method at
vtable `+0x6FC` with VB `True` (`-1`) and unloads the form.

The OLE-drop handler validates its legacy data format/source object, accepts a source whose `Name`
is `lblLabels`, reads that source's `Caption`, and produces a bracket record-field token:

`[` + `Caption` + `]`

The managed dialog reproduces this proven resulting token through an **Insert [field]** action;
it deliberately does not emulate the obsolete OLE transport/control protocol.

## `.ly` serialization

The fixed 64-record field table begins at byte `2029` and occupies `64 * 264 = 16896` bytes.
The global tail therefore begins at byte `18925` (`0x49ED`). Native sequential serialization then
accounts for the complete remaining 1223 bytes as:

`2 + 2 + (256 * 4) + (65 * 3) = 1223`

The first three fixed `String * 256` values are the recovered magnetic-track globals:

| Value | Decimal offset | Hex offset | Length |
| --- | ---: | ---: | ---: |
| Track 1 / `Traccia1` | 18929 | `0x49F1` | 256 |
| Track 2 / `Traccia2` | 19185 | `0x4AF1` | 256 |
| Track 3 / `Traccia3` | 19441 | `0x4BF1` | 256 |
| fourth global string, semantic still not assigned here | 19697 | `0x4CF1` | 256 |

The supplied `TPMFAO19.ly` fixture confirms this boundary: the first three track slots are empty,
while the fourth fixed string contains `.\\Foto`. The two 16-bit values before Track 1 and the
trailing `65 * 3` bytes remain intentionally untouched.

The managed codec decodes Track 1/2/3 into layout-global `MagneticStripe` settings. On save it
rewrites a track slot only when its decoded value changed. This preserves the fixture's original
zero-filled empty slots during an unchanged byte-for-byte round trip. A changed fixed string is
written through the current byte-preservable ANSI boundary and padded with spaces; values longer
than 256 bytes or characters outside that conservative boundary are rejected rather than silently
truncated or encoded with an unproven code page.

## Print preparation relationship

These stored expressions feed the already recovered `0x00603FC0` smartDriver preparation helper.
For each track the proven structural order is:

1. trim the source global;
2. run `Interpretariga(..., True)`;
3. remove embedded NUL characters;
4. expand persistent `+(...)` counter tokens;
5. expand `[...]` current-record tokens;
6. assign the prepared result to the corresponding `frmCarta` magnetic-track property.

The helper then returns `Len(track1) + Len(track2) + Len(track3)`, which gates whether native
magnetic encoding is attempted. This recovery does **not** activate any mutating ICE/GDI/device
operation; hardware execution remains disabled until supported printer/encoder validation exists.
