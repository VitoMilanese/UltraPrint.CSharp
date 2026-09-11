# Sequenza print-job control native recovery

This note separates two legacy workflows that were easy to conflate: the `Sequenza` pause/stop controls and the independent `frmPrinting` batch interval/start/cancel shell.

## Sequenza `Pausa`

Recovered procedure:

- `Sequenza.Pausa_Click` at `0x005D4EB0`.

The button uses its Caption as the native state flag:

- `&Pausa` = printing is running;
- `&Riprendi` = printing is paused.

`Pausa_Click` reads the current caption and toggles it between those two strings.

`StampaPagina_Click` contains explicit pause checkpoints. At `0x005D87F8` it reads `Pausa.Caption`, compares it with `&Pausa`, and when they differ it calls the VB runtime message pump (`DoEvents`) and branches back to the same test. A second equivalent checkpoint begins at `0x005D99A7`. The Pausa control is enabled when page printing starts and disabled again during cleanup.

The managed implementation therefore keeps printing synchronous and cooperative rather than inventing a background worker: it pumps WinForms messages at safe physical-page/side boundaries and, while paused, keeps pumping messages until Resume is selected.

## Sequenza `cmdStop`

Recovered procedure:

- `Sequenza.cmdStop_Click` at `0x005CFE60`.

Control metadata identifies:

- `FinoAPagina` getter at `+0x31C`;
- `Pagina` getter at `+0x328`;
- `cmdStop` getter at `+0x304`.

The observable assignment in `cmdStop_Click` is:

```text
FinoAPagina.Text = Pagina.Text
```

`StampaTutte_Click` iterates logical `Pagina` values and uses `FinoAPagina` as the upper bound. Consequently Stop is not an immediate mid-card cancellation. It truncates **Print All after the currently executing logical page finishes**. For `FronteRetro`, the logical page includes both front and back phases, so a Stop request during the front phase still permits the corresponding back phase before the job terminates.

The managed `SequencePrintJobController` models this as `StopAfterCurrentSheetRequested`; `SequencePrintService` checks it only after all requested sides of the current logical sheet have completed.

## Progress state

Native `StampaTutte_Click` drives the visible `Pagina` / `Pagine` controls while iterating pages and enables `Pausa` for the duration of the job. The managed controller reports the current one-based logical sheet, total sheet count and active Front/Back phase so the Sequence status bar can expose equivalent progress while retaining the existing interactive preview page selector.

## `frmPrinting` is a different form

`frmPrinting` has only three recovered procedures:

- `Command1_Click` at `0x005C0A10`;
- `Form_Load` at `0x005C0BE0`;
- `Form_Unload` at `0x005C0E20`.

Its controls include `txtIntervallo`, `lblAncora`, `Command1`, labels/picture and a frame captioned `Intervallo tra 2 Carte`. There is **no Pausa or cmdStop control on frmPrinting**.

Recovered behavior:

- `Form_Load` clears the shared MainForm cooperative-cancel flag and loads `[Setup] Intervallo` with native default `5`;
- `Command1` toggles from `Inizia` to `Annulla`; pressing it again sets the shared MainForm cancel flag and disables the button;
- `Form_Unload` writes the current `Intervallo` value back to `[Setup]` in `UP.ini`;
- `frmDatabase.StampaTutti` shows this shell and pumps events until `Inizia` has been pressed;
- its interval/countdown path is gated by `frmCarta.HasChip` at vtable `+0x79C`;
- when `HasChip` is true, values below 30 are replaced with `30` before the cooperative inter-card countdown; non-chip layouts skip that wait.

The formerly unidentified `+0x79C` gate is now recovered and the managed database Print All path implements this separate shell. See `BATCH_PRINTING_NATIVE.md` for the receiver/member correlation, type-10 SmartCara mapping, interval persistence and cancellation details.
