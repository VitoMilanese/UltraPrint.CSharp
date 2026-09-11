# UltraPrint 2.2.115 magnetic-track editor recovery

This note records the recovered native behavior of `frmTracce` and the corresponding layout-tail persistence used by the managed replacement.

## Native `frmTracce`

Recovered procedures:

- `Form_Load` at `0x005E83E0`
- `Form_Unload` at `0x005E87C0`
- `Ok_Click` at `0x005E8A50`
- `Traccia_Event0` at `0x005E8B60`
- `Traccia_Event1` at `0x005E8DC0`

`Form_Load` reads the three global BSTRs at `0x0062A05C`, `0x0062A060`, and `0x0062A064`, trims them, and assigns them to `Traccia(0)`, `Traccia(1)`, and `Traccia(2)` respectively.

`Traccia_Event0` reads the Text of all three track controls and synchronizes the corresponding three globals. The values are therefore live-updated before the OK handler runs.

`Ok_Click` does not separately copy the text. It calls the shared helper through vtable offset `+0x6FC` with VB True and then unloads the form.

`Traccia_Event1` is the OLE drag/drop path. It accepts the legacy source label control (`Name == "lblLabels"`) and inserts the source caption as a bracket token, effectively `[Caption]`, into the selected track text box. No separate native length/charset validation branch was recovered from this event.

## Track preparation at print time

The editor stores expressions rather than already encoded magnetic data. The print-preparation helper recovered separately at `0x00603FC0` processes each global track through the legacy pipeline:

`Trim -> Interpretariga -> remove NUL -> +(Counter) expansion -> [RecordField] expansion`

The prepared strings are written into the three `frmCarta` magnetic-track properties. The helper returns the sum of their prepared lengths, and only a non-zero result reaches `EncodeMagStripeWithApi`.

## `.ly` persistence

The legacy field table begins at byte `2029` and contains `64 * 264` bytes. Therefore the fixed tail begins at:

`2029 + (64 * 264) = 18925 / 0x49ED`

Native sequential serialization places two 16-bit values first, followed by four fixed `String * 256` values. The first three are the magnetic-track expressions:

- Track 1: byte `18929 / 0x49F1`, length `256`
- Track 2: byte `19185 / 0x4AF1`, length `256`
- Track 3: byte `19441 / 0x4BF1`, length `256`
- fourth global string: byte `19697 / 0x4CF1`, length `256` — semantic purpose intentionally left unresolved

The supplied `TPMFAO19.ly` fixture confirms the boundary: all three track slots are zero-filled, while the fourth 256-byte string contains `.\\Foto`. The remaining bytes after that fourth string are preserved as unknown layout tail data.

The managed codec decodes Track1/2/3 into layout-global `CardLayout.MagneticStripe` state and writes only a track slot whose decoded value actually changed. This keeps a normal load/save byte-identical and prevents a track edit from touching the fourth global string or any unrelated tail bytes.

## Managed editor

`Tools -> Magnetic Stripe Tracks...` exposes the three recovered expressions. It also provides an `Insert [field]` convenience matching the native drag/drop outcome while still allowing arbitrary expressions to be typed directly.

The dialog intentionally does not invent magnetic-device validation. Its current persistence boundary is the recovered byte-oriented `String * 256` representation; wider Unicode input is rejected until the original ANSI/DBCS runtime behavior is validated.
