# frmPrinting / database batch native recovery

This note documents the separate UltraPrint 2.2.115 `frmPrinting` workflow used by `frmDatabase.StampaTutti`. It is distinct from `Sequenza.Pausa` / `cmdStop`.

## Form surface

Recovered `frmPrinting` procedures:

- `Command1_Click` at `0x005C0A10`;
- `Form_Load` at `0x005C0BE0`;
- `Form_Unload` at `0x005C0E20`.

Relevant controls include `txtIntervallo`, `lblAncora`, `Command1` and the frame captioned `Intervallo tra 2 Carte`.

`Command1_Click` compares the button Caption with `Inizia`. The first click changes it to `Annulla`. A subsequent click sets the shared `MainForm.CancelOp` flag and disables the button. `frmDatabase.StampaTutti` checks that flag cooperatively between print phases/records rather than performing a printer-driver hard abort.

## `[Setup] Intervallo`

`Form_Load` first clears `MainForm.CancelOp`, then reads key `Intervallo` from section `Setup` through the legacy INI helper. The default Variant passed by the native code is an integer **5**.

`Form_Unload` reads the current `txtIntervallo.Text` and writes it back to `[Setup] Intervallo` in the common UltraPrint INI file (`UP.ini`).

The managed store therefore preserves the native default of 5 and uses the existing order-preserving Latin-1 INI layer.

## The interval gate is `frmCarta.HasChip`

Two interval/countdown branches inside `frmDatabase.StampaTutti` acquire the global `frmCarta` instance (`0x62B588`, class info `0x41B528`) and call its Boolean getter at vtable offset `+0x79C`. False skips the interval path; true enters it.

The embedded `frmCarta` public-member metadata contains the adjacent capability members:

```text
HasFront
HasRear
HasChip
HasMag
HasError
```

Correlating that metadata with the late frmCarta getter slots identifies `+0x79C` as **`HasChip`**. An unrelated call near `0x004D29BF` uses the `Funzioni` singleton rather than `frmCarta`; it is deliberately not used as evidence for this mapping.

The layout codec already recovers native field type `10` (`SmartCara`) as `LayoutFieldKind.Chip`, so the managed `HasChip` equivalent derives from decoded chip fields rather than introducing a new guessed `.ly` flag.

## Minimum interval and countdown

When `HasChip` is true, `StampaTutti` reads `frmPrinting.txtIntervallo.Text`, coerces it numerically and compares it with the native constant **30.0**. Values below 30 are replaced with `30` in the textbox before the countdown proceeds. `lblAncora` is updated while the code pumps VB6 `DoEvents` during the wait.

Therefore:

- layouts without a chip field do not receive the inter-card delay;
- chip layouts use `max(30, Intervallo)` seconds;
- the delay is between logical cards, not an extra delay between front/back sides of one card;
- `Annulla` remains responsive during the countdown because the wait is cooperative;
- no delay is needed after the final card.

The managed replacement keeps the same synchronous/cooperative shape: a modeless `frmPrinting` replacement waits for `Inizia`, changes the command to `Annulla`, reports card/side/countdown state, pumps WinForms messages, and honors cancellation after the current logical card completes.
