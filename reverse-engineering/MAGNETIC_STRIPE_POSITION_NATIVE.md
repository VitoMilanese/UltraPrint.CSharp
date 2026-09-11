# UltraPrint 2.2.115 magnetic-stripe position marker

This note separates two legacy UI features that are easy to confuse:

- `MainForm.mnuMostraBanda_Click` (`0x005B0D50`) shows/hides the separate floating `frmMostraBanda` window.
- `MainForm.mnuVisualizzaPosizioneBanda_Click` (`0x005B1620`) toggles the magnetic-stripe **position marker inside frmCarta** and asks frmCarta to redraw.

This recovery slice implements only the second feature. It is editor UI, not magnetic encoding.

## Display-state contract

`frmCarta.DisegnaCampi` is at `0x00541FB0`. Near the end of the routine it obtains the magnetic
band label through frmCarta vtable `+0x324`, obtains `mnuVisualizzaPosizioneBanda` through MainForm
vtable `+0x388`, reads the menu's `Checked` WORD through `+0x68`, and passes that value directly to
the band label's display-state setter at `+0x9C`.

The menu handler at `0x005B1620` toggles Checked using getter `+0x68` / setter `+0x6C`, then calls
frmCarta's editor redraw/update path. A following block does the same thing for the chip marker,
which confirms that this is an editor overlay mechanism rather than printable card content.

`LblbandaMagnetica_Click` is at `0x00539190`. It flips a separate property pair at `+0x140/+0x144`.
Because visibility/display is already controlled independently through `+0x9C`, this property is
**not** named or used by the managed implementation until its meaning is proven.

## Exact logical geometry

Inside `DisegnaCampi`, the band-label geometry is rebuilt from the current frmCarta zoom value:

- Left setter `+0x74` receives converted `0`.
- Top setter `+0x7C` receives converted `(scalePercent * 5 / 100)`.
- Height setter `+0x8C` receives converted `(scalePercent * 12 / 100)`.
- Width setter `+0x84` receives the width of the card drawing control.

Helper `0x005FCAD0` performs the exact conversion `millimeters * 1440 / 25.4`, i.e. mm to twips.
The scale percentage therefore affects only display scaling. In logical card coordinates the marker
is fixed at:

- X = 0 mm
- Y = 5 mm
- Width = full card width
- Height = 12 mm

So the physical stripe-position band spans Y = 5..17 mm.

## Managed rendering rule

The managed `LayoutCanvas` reproduces this as an **editor-only position overlay** controlled by a
checkable View-menu item. `RenderTo()` already renders with editor overlays disabled, so the marker
cannot leak into Print Preview / Print. Legacy field type 9 (`BandaMagnetica`) is also no longer
rendered as a generic orange unknown field: the recovered fixed position marker is the meaningful
editor visualization.

The exact legacy design-time color/fill of `LblbandaMagnetica` has not been claimed here. Managed
coloring is only diagnostic styling around the exact recovered geometry and show/hide behavior.
No ICE_API, GDI passthrough, magnetic encoder, or printer mutation is enabled by this work.
