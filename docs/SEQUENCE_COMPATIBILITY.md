# Sequenza / sheet-print compatibility

UltraPrint 2.2.115 contains a dedicated `Sequenza` form for arranging multiple card records on physical sheets. Recovered VB6 method names include `Pescarecord`, `PosizionaPagina`, `ScriviSetup`, `LeggiSetup`, `StampaPagina_Click`, `StampaTutte_Click`, front/back/page controls, and row/column/margin/spacing handlers.

The managed replacement exposes the visible workflow through **Sequence -> Sequence / Sheet printing...** and also exposes the proven ScriptControl-compatible `Sequenza` / `Stampa` surface.

## Implemented visible workflow

- configurable rows and columns;
- native-compatible X offset (`MargineDestro`) and top margin in millimetres;
- native `PassoOrizzontale` / `PassoVerticale` inter-card gaps in millimetres;
- Horizontal (`Orizzontale`, row-major) or Vertical (`Verticale`, column-major) record fill;
- legacy virtual sheet formats loaded from `Campo.ini` `[Formati]` into `cboDimensioni`;
- Portrait (`FoglioPortrait`) or Landscape (`FoglioLandscape`) paper orientation;
- selectable first physical slot on the first sheet;
- interactive sheet preview; clicking a first-sheet slot moves record 1 there;
- previous/next/first/last logical sheet navigation;
- Front, Back, or Front + Back output;
- optional cut marks;
- Page Setup using the real Windows printer/paper settings and synchronizing its orientation back into Sequenza state;
- Preview/Print current logical sheet;
- Preview/Print all logical sheets;
- database-record imposition using the same `.ly.data.json`, query/table state and field bindings as Database / Records;
- template-only repeated-card printing when database records are disabled.

The first sheet starts at the selected physical slot in the selected fill order. Every later sheet starts from the first cell in that order. Current-sheet printing preserves that logical sheet's record assignment rather than re-numbering it from record 1.

## Recovered native sheet geometry

Direct disassembly of `cmdImposta_Click` and `StampaPagina_Click`, correlated with the MSFlexGrid property IDs used by the binary, proves the placement model rather than inferring it from Italian control names.

The native code uses `MSFlexGrid.CellLeft` / `CellTop` as the cumulative card-size base. It also builds two spacing arrays: the first X element is `MargineDestro * 10`, the first Y element is `MargineAlto * 10`, and later elements contain `PassoOrizzontale * 10` / `PassoVerticale * 10`. `StampaPagina_Click` sums the applicable elements and divides by 10 before adding them to the grid cell origin.

Consequently the managed formula is:

```text
X = MargineDestro + column * (cardWidth + PassoOrizzontale)
Y = MargineAlto  + row    * (cardHeight + PassoVerticale)
```

This resolves two earlier ambiguities:

- despite its name, `MargineDestro` is observably a **left-origin X offset** in the print path, so it maps to managed `MarginLeftMm`;
- `PassoOrizzontale` and `PassoVerticale` are **gaps between cards**, not the complete slot pitch.

The used sheet extent therefore includes all card widths/heights plus only `Columns - 1` / `Rows - 1` gaps.

The `Orizzontale` and `Verticale` controls do not rotate the card. Their branches change the MSFlexGrid record traversal: `Orizzontale` fills rows first (row-major), while `Verticale` fills columns first (column-major). The managed planner preserves the selected physical first slot in either traversal.

## Legacy sheet formats from `Campo.ini`

Native `SubMain` builds the global configuration path as `App.Path + "\\Campo.ini"`. `Sequenza.Form_Load` clears `cboDimensioni`, loops keys **1 through 20** in the `[Formati]` section, adds every non-empty value, and finally selects list index 0.

The supplied UltraPrint `Campo.ini` contains:

```ini
[Formati]
1=A4 [21x29,7 cm]
2=A3 [29,7x42 cm]
3=Card [8,5x5,4 cm]
```

`cboDimensioni_Click` parses the first number between `[` and `x`, parses the second between `x` and the following space, converts the centimetre values to the native grid scale, and resizes the sheet-preview MSFlexGrid. Portrait keeps the parsed width/height order; Landscape swaps them. The equivalent managed preview sizes are therefore 210 x 297 mm for A4, 297 x 420 mm for A3, and 85 x 54 mm for Card before orientation swapping.

`LegacySequencePaperFormatStore` reproduces the proven lookup boundary and parsing grammar. Non-empty entries with an unrecognized dimension string remain visible as raw legacy text rather than being discarded. The managed Sequence form uses the parsed size for its virtual sheet preview and extent warning. If no usable format exists, it falls back to the selected Windows printer page size for managed preview only.

This recovery **does not equate `cboDimensioni` with a Windows `PaperKind`**. Native evidence currently proves virtual sheet/grid sizing, not a direct `Printer.PaperSize` assignment. Page Setup and the actual printer driver remain separate until a direct native paper-device mapping is recovered.

`cboDimensioni` is also a proven `.Seq` setup control: generic `ScriviSetup` / `LeggiSetup` persist ComboBox `Text`, so the managed legacy setup layer reads and writes the complete selected format string.

## Paper orientation and coordinate origin

`FoglioPortrait` / `FoglioLandscape` are a second, independent OptionButton pair. Native `FoglioPortrait_Click` stores orientation state `0`, `FoglioLandscape_Click` stores `1`, and both route through `cboDimensioni_Click`. That routine swaps the selected sheet width/height before rebuilding the MSFlexGrid, proving these controls represent **paper orientation**, not record fill direction or card rotation.

The managed Sequence workspace therefore exposes paper orientation separately and applies it directly to `PageSettings.Landscape`. Legacy `.Seq` files read/write `FoglioPortrait` and `FoglioLandscape` as mutually exclusive numeric `1`/`0` values. Windows Page Setup is initialized from the Sequenza orientation and, when accepted, writes its resulting orientation back to the managed setting.

The native `SmartFormDll.Report.PrintPage` callable accepts the printer/picture target plus `MargineDx` and `MargineTop`. VB6 Printer coordinates are page coordinates: `(0,0)` is the physical page's upper-left edge. By contrast, .NET `PrintDocument` with `OriginAtMargins = false` supplies a `Graphics` origin at the printer's **printable-area** upper-left corner. The managed sequence print path compensates `PageSettings.HardMarginX` / `HardMarginY` so recovered UltraPrint millimetre coordinates continue to mean positions from the physical page edge. The printer driver still performs the real clipping of physically unprintable pixels.

This does not claim that every printer exposes identical hard-margin values to the 2003 driver stack. Exact device clipping remains a real-printer parity item, but the managed coordinate system now matches the native page-origin contract instead of silently adding the modern printer hard margin to every placement.

Managed `.sequence.json` state is now version 4. Version 4 persists the raw `cboDimensioni` text. Version 3 already has paper orientation and migrates with no saved paper format, causing the Sequence form to use the native first-`[Formati]` fallback. Version 2 already has correct gap/fill semantics and migrates to Portrait; version 1 additionally migrates old full-pitch values to native `Passo` gaps by subtracting card width/height.

## Front/back behavior

When **Front + Back** is selected, the managed print service emits two consecutive physical pages for each logical sheet: front first, back second, with identical record-to-slot assignments. This is the safest currently proven representation of the legacy front/back intent.

Exact printer-specific duplex mirroring/rotation remains unclaimed until UltraPrint 2.2.115 is compared on a real duplex/card-printer workflow. Device-specific transforms must not be guessed into the generic sheet planner.

## Recovered native callable contracts

Direct disassembly distinguishes explicit parameters from VB6 hidden return storage:

| Method | Native VA | Recovered callable shape | Managed exposure |
| --- | ---: | --- | --- |
| `Pescarecord` | `0x005C3590` | zero explicit arguments; modal `FormQBE` -> DAO `FindFirst`; returns `AbsolutePosition + 1` as `Variant`, or `0` when cancelled/not found | exposed |
| `PosizionaPagina` | `0x005C5250` | one explicit `Variant` `NumRecord`; computes the legacy `Pagina`, refreshes it through `cmdImposta_Click`, then scans/highlights the matching `MSFlexGrid1` cell | exposed |
| `ScriviSetup` | `0x005D5810` | one **Optional Variant** filename | exposed |
| `LeggiSetup` | `0x005D65B0` | one **Optional Variant** filename | exposed |

### `Pescarecord`

Native `Pescarecord` starts with a zero Variant result and `On Error Resume Next`. It builds FormQBE field metadata from the current DAO Recordset, shows the compact QBE dialog, then runs `Recordset.FindFirst`. A failed `NoMatch` shows the literal message `Record non trovato`; success leaves the recordset on the matching row and returns its one-based number via `AbsolutePosition + 1`.

The managed `Sequenza.Pescarecord()` / `Stampa.Pescarecord()` preserves those observable semantics. Its QBE dialog uses the recovered condition tokens (`*..`, `.*.`, `..*`, `=`, `<>`, range, comparisons, `x--x`, `Vero`, `Falso`). Search is performed against the same loaded rows used by Database / Records, and a successful match moves that shared `BindingSource`.

The managed matcher evaluates `DataTable` values instead of issuing DAO `FindFirst` directly. This preserves the workflow across Access/FFM as well as managed DBF/Excel/CSV/text sources while keeping the recovered one-based return contract.

### `PosizionaPagina`

The caller contract is `PosizionaPagina NumRecord`: the legacy `cmdPosiziona_Click` handler calls `Pescarecord`, rejects a zero result, and passes the returned one-based record number directly to `PosizionaPagina`. `RecordxPagina` is `Righe * Colonne`; `Pagine` is the ceiling of total records divided by that capacity.

The native page formula is intentionally preserved exactly, including its boundary quirk. With `PaginaSingola = 0`, UltraPrint assigns `Fix(NumRecord / RecordxPagina) + 1`; with `PaginaSingola <> 0`, it assigns `NumRecord Mod Pagine`. `Pagina_Change` then normalizes any non-empty result outside `1..Pagine` back to page `1`. Consequently an exact capacity multiple can select the following page, and a modulo result of zero becomes page 1.

After assigning the page, native code calls `cmdImposta_Click`, walks `MSFlexGrid1` data cells (excluding row/column headers), compares each cell `Text` with `NumRecord`, and marks a match with `QBColor(12)` plus bold text. The managed Sequence preview mirrors that visible selection with a red/bold record cell. If the native page formula places the record on a page that does not contain it, the managed workspace preserves that page choice and leaves the record unhighlighted rather than silently relocating it.

## Legacy `.Seq` setup format

When the Optional filename is Missing, native `ScriviSetup` and `LeggiSetup` build:

`App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`

They enumerate the form controls and persist `Name` plus `Value`/`Text` as INI values under:

`[Sequenza]`

`LegacySequenceIniStore` now maps the controls whose native behavior is proven:

- `Righe` -> rows;
- `Colonne` -> columns;
- `MargineDestro` -> left-origin X offset;
- `MargineAlto` -> top margin;
- `PassoOrizzontale` -> horizontal inter-card gap;
- `PassoVerticale` -> vertical inter-card gap;
- `Orizzontale` -> row-major fill;
- `Verticale` -> column-major fill;
- `cboDimensioni` -> raw legacy `[Formati]` sheet-format text;
- `FoglioPortrait` -> portrait paper orientation;
- `FoglioLandscape` -> landscape paper orientation;
- `PaginaSingola` -> recovered single-page positioning mode.

Numeric dimensions are written with the original Italian-style decimal comma and read using both Italian and invariant numeric forms. OptionButton values are emitted as numeric `1`/`0`, which is locale-independent for the legacy VB6 setter. Boolean text, including Italian `Vero`/`Falso`, is accepted when reading existing files.

Existing `.Seq` files are updated conservatively: unknown keys are preserved. `Fronte`/`Retro`, `Taglio`, and other device/print-specific controls remain unmapped until their exact output behavior is proven.

## ScriptControl identity

Native `Funzioni.AddObjects` registers the exact same global `Sequenza` instance twice, under the names `Sequenza` and `Stampa`.

The managed registration does the same: one `LegacyScriptSequenceFacade` object is registered under both names, in recovered relative order:

`... Db -> Sequenza -> Stampa -> frmDatabase ...`

The exposed subset is:

- `Sequenza.Pescarecord()` / `Stampa.Pescarecord()`;
- `Sequenza.PosizionaPagina(NumRecord)` / `Stampa.PosizionaPagina(NumRecord)`;
- `Sequenza.ScriviSetup([Filename])` / `Stampa.ScriviSetup([Filename])`;
- `Sequenza.LeggiSetup([Filename])` / `Stampa.LeggiSetup([Filename])`.

An application-lifetime WinForms sequence host follows the layout currently open in `MainForm`; record search shares the application-lifetime database host, and `PosizionaPagina` drives the live Sequence workspace.

## Remaining parity work

- determine whether and how the selected legacy `cboDimensioni` value is applied to the actual VB6 Printer/device paper size; do not infer `PaperKind` from display text;
- compare hard-margin values and clipping against the actual legacy printer/driver combinations even though the page-origin coordinate contract is now matched;
- recover exact front/back duplex mirroring/rotation and remaining `.Seq` controls such as `Fronte`, `Retro` and `Taglio`;
- integrate device/card-printer progress/cancel behavior after the standard sheet workflow is stable.
