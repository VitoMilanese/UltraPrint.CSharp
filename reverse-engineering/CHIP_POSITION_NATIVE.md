# UltraPrint 2.2.115 chip position marker

The smart-card/chip marker is a second editor-only overlay adjacent to the already recovered magnetic-stripe position marker.

## Native controls and menu

- `frmCarta.DisegnaCampi` — `0x00541FB0`
- `frmCarta.lblChip_Click` — `0x005392D0`
- `MainForm.mnuVisualizzaPosizioneChip_Click` — `0x005B1770`
- frmCarta `lblChip` accessor — vtable `+0x320`
- MainForm chip-position menu accessor — vtable `+0x38C`
- menu `Checked` getter/setter — `+0x68/+0x6C`
- marker display-state setter — `+0x9C`

`DisegnaCampi` reads `mnuVisualizzaPosizioneChip.Checked` and sends that WORD directly to the chip marker's display-state setter. The menu handler toggles Checked and asks frmCarta to redraw, matching the magnetic-stripe position workflow.

As with `LblbandaMagnetica_Click`, `lblChip_Click` flips a separate property pair at `+0x140/+0x144`. That property is deliberately left unnamed until its semantics are proven; it is not required for marker visibility.

## Exact geometry

`DisegnaCampi` uses the same frmCarta scale percentage at object offset `+0x58` and the same helper `0x005FCAD0` (`millimeters * 1440 / 25.4`) to place the chip marker. The recovered logical card coordinates are:

- Left = 8 mm
- Top = 18 mm
- Width = 13 mm
- Height = 12 mm

The marker therefore spans X = 8..21 mm and Y = 18..30 mm. Zoom changes only the display conversion, not the logical geometry.

## Managed rule

The managed editor exposes **View -> Chip position** and displays this marker through a dedicated transparent child overlay attached to `LayoutCanvas`. It is not part of `LayoutCanvas.RenderTo()`, so it cannot appear in Print Preview / Print.

This slice intentionally does **not** claim that serialized legacy field type 10 and the frmCarta `lblChip` overlay are the same object/record. Existing type-10 field rendering remains a separate parity question until native field dispatch is proven.

The exact original design-time fill/color is not claimed; managed coloring is diagnostic styling around exact recovered geometry and show/hide behavior. No smart-card or printer mutation is enabled by this recovery.
