# Sequenza ScriptControl / legacy .Seq recovery note

This compatibility slice is implemented in `LegacyScriptSequenceFacade`, `LegacyScriptSequenceHostRegistry`, `LegacySequenceIniStore`, `LegacySequencePaperFormatStore`, `LegacyQbeMatcher`, `WinFormsLegacySequenceHost`, the managed `SequencePrintPlanner`, and the shared WinForms database host.

Native facts used by the implementation:

- `Pescarecord` at `0x005C3590` returns a VB6 `Variant` through a hidden result pointer and has zero explicit arguments.
  - it initializes that Variant to integer `0` and enters `On Error Resume Next`;
  - it enumerates the current DAO Recordset fields and builds the field/type description consumed by `FormQBE`;
  - `FormQBE` is modal and contains the recovered `lblCampo`, `ComboTpric`, `Contenuto`, `Ok`, and `Annulla` controls;
  - after QBE closes, native code calls DAO `Recordset.FindFirst` with the generated criteria;
  - `Recordset.NoMatch` shows `Record non trovato` and leaves the result at `0`;
  - a successful search returns `Recordset.AbsolutePosition + 1`, i.e. the one-based matching record number, while the Recordset remains positioned on that row.
- recovered FormQBE condition labels include starts-with (`*..`), contains (`.*.`), ends-with (`..*`), `=`, `<>`, between, `>`, `>=`, `<`, `<=`, numeric range (`x--x`), `Vero`, and `Falso`.
- `PosizionaPagina` at `0x005C5250` consumes one explicit Variant `NumRecord`. `cmdPosiziona_Click` passes the non-zero one-based result of `Pescarecord` directly to it.
  - `cmdImposta_Click` establishes `RecordxPagina = Righe * Colonne` and `Pagine = Ceil(Record / RecordxPagina)`.
  - with `PaginaSingola = 0`, `PosizionaPagina` assigns `Pagina = Fix(NumRecord / RecordxPagina) + 1`; with `PaginaSingola <> 0`, it assigns `Pagina = NumRecord Mod Pagine`.
  - `Pagina_Change` clamps any non-empty page outside `1..Pagine` to `1`, so modulo zero becomes page 1 and an out-of-range quotient page also resets to 1.
  - the exact-multiple behavior is a native quirk: for example record 10 with capacity 10 selects page 2, even though `cmdImposta_Click` fills page 2 from record 11. The managed helper intentionally preserves this instead of correcting it.
  - after setting `Pagina`, the routine calls `cmdImposta_Click`, scans `MSFlexGrid1` over columns `1..Cols-1` and rows `1..Rows-1`, compares cell `Text` with `NumRecord`, and on a match sets `CellBackColor = QBColor(12)` and `CellFontBold = True`; it leaves the grid cursor at `(0,0)` on match and `(1,1)` on no match.
  - `TabStrip1` is not part of `PosizionaPagina`; the earlier association was incorrect and came from the separate `TabStrip1_Click` routine.
- `ScriviSetup` at `0x005D5810` and `LeggiSetup` at `0x005D65B0` each consume one Optional Variant filename.
- when that filename is Missing, both build `App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`.
- setup persistence is INI-style, section `[Sequenza]`; native code enumerates controls and uses their `Name` plus `Value`/`Text` with `File.WriteIni` / `File.GetIni`.
- the same global `Sequenza` object is injected into ScriptControl as both `Sequenza` and `Stampa`.

## Recovered sheet geometry

The control IDs and native object getters correlate the relevant form controls as follows:

- `MargineDestro` -> control id 21 / getter vtable `+0x34C`;
- `MargineAlto` -> id 22 / `+0x350`;
- `PassoOrizzontale` -> id 23 / `+0x354`;
- `PassoVerticale` -> id 24 / `+0x358`;
- `Orizzontale` -> id 67 / `+0x404`;
- `Verticale` -> id 68 / `+0x408`;
- `FoglioPortrait` -> id 70 / `+0x410`;
- `FoglioLandscape` -> id 71 / `+0x414`;
- `cboDimensioni` -> id 72 / `+0x418`;
- `MSFlexGrid1` -> id 75 / `+0x424`.

The MSFlexGrid DISPIDs observed by the binary match the control typelib: `Rows=4`, `Cols=5`, `Row=10`, `Col=11`, `CellLeft=29`, `CellTop=30`, `CellWidth=31`, `CellHeight=32`, `ColWidth=57`, `RowHeight=58`.

`cmdImposta_Click` sizes the grid cells from the card dimensions. It then builds separate X/Y spacing arrays. The first X element is `MargineDestro * 10` and the first Y element is `MargineAlto * 10`; later X/Y elements are `PassoOrizzontale * 10` / `PassoVerticale * 10`. `StampaPagina_Click` sums those arrays up to the current grid cell, divides by 10, and adds the result to `MSFlexGrid.CellLeft` / `CellTop` before passing the final coordinates to the print/draw helper.

Therefore the native placement is equivalent to:

```text
X = MargineDestro + column * (cardWidth + PassoOrizzontale)
Y = MargineAlto  + row    * (cardHeight + PassoVerticale)
```

This proves that `MargineDestro`, despite its label, behaves as a left-origin X offset in the actual print path. It also proves that `PassoOrizzontale` / `PassoVerticale` are inter-card gaps, not full pitch values.

The `Orizzontale` and `Verticale` branches in `cmdImposta_Click` select traversal order rather than card rotation: `Orizzontale` is row-major and `Verticale` is column-major.

## `Campo.ini [Formati]` and `cboDimensioni`

Native `SubMain` constructs the configuration pathname at startup as `App.Path + "\\Campo.ini"`. `Sequenza.Form_Load` obtains `cboDimensioni` through getter `+0x418`, clears it, then iterates integer keys **1..20** from section `Formati` through the global `File` helper. Every non-empty returned string is added to the ComboBox; after the loop `ListIndex` is set to `0`.

The supplied fixture confirms the deployed string grammar:

```ini
[Formati]
1=A4 [21x29,7 cm]
2=A3 [29,7x42 cm]
3=Card [8,5x5,4 cm]
```

`cboDimensioni_Click` at `0x005C5A50` reads the selected ComboBox Text and extracts two values with the recovered `GetInside` pattern:

- first token: delimiter `[` to delimiter `x`;
- second token: delimiter `x` to the following space.

Each Variant is converted to `Single` through VB runtime `__vbaR4ErrVar` and multiplied by `100`. With the supplied centimetre strings this produces the form/grid scale used to resize the sheet preview. `FoglioPortrait.Value` selects original width/height order; the Landscape branch swaps them. The routine then calls `cmdImposta_Click` to rebuild the imposition grid.

This proves that `cboDimensioni` defines the **virtual Sequenza sheet/grid size**. It does **not** by itself prove any assignment to VB6 `Printer.PaperSize` or a Windows `PaperKind`; no such mapping is claimed until a direct native call path is found.

Because native `ScriviSetup` / `LeggiSetup` generically persist ComboBox `Text`, `cboDimensioni` is also a proven `[Sequenza]` `.Seq` key. The managed model therefore stores its raw text and parses dimensions conservatively with the same delimiters while preserving non-empty unrecognized strings.

## Paper orientation and page origin

`FoglioLandscape_Click` stores Variant value `1` in the form orientation state and calls `cboDimensioni_Click`; `FoglioPortrait_Click` stores `0` and follows the same rebuild path. `cboDimensioni_Click` tests `FoglioPortrait.Value`. Portrait uses the parsed width/height in their original order; Landscape swaps them, then the routine calls `cmdImposta_Click`.

This proves `FoglioPortrait` / `FoglioLandscape` are the **paper/grid orientation** pair and are independent of `Orizzontale` / `Verticale` record traversal.

The recovered `SmartFormDll.Report.PrintPage` public parameter names are `Pic`, `nzoom`, `Lato`, `MargineDx`, and `MargineTop`, confirming that UltraPrint supplies its computed X/top offsets to the printer/picture drawing path rather than deriving a new margin from the printer driver.

VB6 Printer page coordinates define `(0,0)` at the physical page's upper-left edge. Modern `System.Drawing.Printing.PrintDocument`, when `OriginAtMargins` is false, exposes `PrintPageEventArgs.Graphics` at the upper-left corner of the printer's printable area. The managed sequence path therefore subtracts `PageSettings.HardMarginX` / `HardMarginY` (converted from hundredths of an inch to the current pixel PageUnit) before rendering. This restores the native physical-page coordinate system while leaving physically impossible output to driver clipping.

The coordinate-system recovery does not prove that a current driver reports exactly the same hard-margin rectangle as the 2003 printer stack. That remains a real-device parity check rather than a reason to keep the known systematic printable-origin offset in managed output.

## Managed consequences

The managed `Pescarecord` implementation reproduces the proven observable contract: compact QBE field/operator/value input, first-match search, one-based result/zero failure shape, `Record non trovato`, and movement of the shared Database/Records `BindingSource` so subsequent `Tabella`/record-binding calls observe the found row. `LegacyQbeMatcher` keeps the recovered operator tokens while evaluating loaded `DataTable` values, which also extends the workflow to managed CSV/text sources that do not expose a DAO Recordset.

The managed `PosizionaPagina` implementation exposes the recovered script method, uses a pure `LegacySequencePositioning` helper for the literal native arithmetic/clamping, drives the currently open managed Sequence workspace, and mirrors the native MSFlexGrid match with a red/bold record highlight.

`LegacySequenceIniStore` maps the exact control names with proven direct equivalents: `Righe`, `Colonne`, `MargineDestro`, `MargineAlto`, `PassoOrizzontale`, `PassoVerticale`, `Orizzontale`, `Verticale`, `cboDimensioni`, `FoglioPortrait`, `FoglioLandscape`, and `PaginaSingola`. Unknown keys remain preserved. `Fronte`/`Retro`, `Taglio` and remaining device-specific setup stay unresolved until their exact output effects are proven.

`LegacySequencePaperFormatStore` reproduces Form_Load's 1..20 `[Formati]` lookup and the recovered bracket/x/space dimension grammar. The managed Sequence preview uses the resulting virtual sheet dimensions, while `SequencePrintService` continues to use actual driver `PageSettings` for Windows output because no direct native `cboDimensioni -> Printer.PaperSize` contract has yet been proved.

The managed planner uses `MargineDestro` as the X offset, treats `Passo*` as gaps, and supports both recovered fill orders. `SequencePrintService` applies recovered paper orientation to `PageSettings.Landscape` and translates the .NET printable-area origin back to the physical-page origin before rendering. Managed `.sequence.json` version 4 persists raw `cboDimensioni` text; v3 retains orientation with native first-format fallback, v2 migrates to Portrait, and v1 additionally migrates full-pitch values to native gaps.
