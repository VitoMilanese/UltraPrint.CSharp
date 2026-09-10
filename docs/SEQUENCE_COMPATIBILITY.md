# Sequenza / sheet-print compatibility

UltraPrint 2.2.115 contains a dedicated `Sequenza` form for arranging multiple card records on physical sheets. Recovered VB6 methods include `Pescarecord`, `PosizionaPagina`, `ScriviSetup`, `LeggiSetup`, `StampaPagina_Click`, `StampaTutte_Click`, the front/back phase controls, print-mode controls, page controls and row/column/margin/spacing handlers.

The managed replacement exposes the visible workflow through **Sequence -> Sequence / Sheet printing...** and exposes the proven ScriptControl-compatible `Sequenza` / `Stampa` surface.

## Implemented visible workflow

- configurable rows and columns;
- native-compatible X offset (`MargineDestro`) and top margin in millimetres;
- native `PassoOrizzontale` / `PassoVerticale` inter-card gaps in millimetres;
- Horizontal (`Orizzontale`, row-major) or Vertical (`Verticale`, column-major) record fill;
- native `Taglio` cut-and-stack ordering;
- legacy virtual sheet formats loaded from `Campo.ini` `[Formati]` into `cboDimensioni`;
- Portrait (`FoglioPortrait`) or Landscape (`FoglioLandscape`) paper orientation;
- selectable first physical slot on the first sheet;
- interactive sheet preview and logical-sheet navigation;
- native output modes `SoloFronte`, `FronteRetro`, `SoloRetro`;
- native `RetroaSpecchio` horizontal back-slot mirroring;
- native signed `OffsetRetroX` / `OffsetRetroY` back-coordinate corrections;
- optional managed crop marks, explicitly separate from legacy `Taglio`;
- Page Setup using the real Windows printer/paper settings and synchronizing orientation back into Sequenza state;
- Preview/Print current logical sheet and Preview/Print all logical sheets;
- database-record imposition using the same `.ly.data.json`, query/table state and field bindings as Database / Records;
- template-only repeated-card printing when database records are disabled.

The first sheet starts at the selected physical slot in the selected fill order. Every later sheet uses the normal grid. The native `Taglio` formula is reproduced exactly when the first slot is the native/default first cell; the managed non-zero first-slot extension rotates the cut-and-stack sequence so record 1 still begins at the explicitly selected first-sheet cell.

## Recovered native sheet geometry

Direct disassembly of `cmdImposta_Click` and `StampaPagina_Click`, correlated with the MSFlexGrid property IDs used by the binary, proves the placement model rather than inferring it from Italian control names.

The native code uses `MSFlexGrid.CellLeft` / `CellTop` as the cumulative card-size base. It also builds two spacing arrays: the first X element is `MargineDestro * 10`, the first Y element is `MargineAlto * 10`, and later elements contain `PassoOrizzontale * 10` / `PassoVerticale * 10`. `StampaPagina_Click` sums the applicable elements and divides by 10 before adding them to the grid cell origin.

Consequently the managed formula is:

```text
X = MargineDestro + column * (cardWidth + PassoOrizzontale)
Y = MargineAlto  + row    * (cardHeight + PassoVerticale)
```

Despite its name, `MargineDestro` is observably a **left-origin X offset**. `PassoOrizzontale` and `PassoVerticale` are **gaps between cards**, not complete slot pitches.

Native `cmdImposta_Click` internally constructs an MSFlexGrid with `2 * Righe` rows and `2 * Colonne` columns. Card cells and gap cells alternate. The managed planner does not reproduce that implementation detail literally; it produces the equivalent logical card geometry above.

`Orizzontale` and `Verticale` change record traversal, not card rotation: `Orizzontale` is row-major and `Verticale` is column-major.

## `Taglio`: cut-and-stack ordering

`Taglio` is not a crop-mark switch. Native `cmdImposta_Click` tests it independently inside both the horizontal and vertical fill paths.

Without `Taglio`, one-based record numbering on one-based page `p` is page-major:

```text
start = (p - 1) * capacity + 1
record(slotOrdinal) = start + slotOrdinal - 1
```

With `Taglio`, the initial slot ordinal starts at 1 and each cell computes:

```text
record = (slotOrdinal - 1) * Pagine + Pagina
```

Example with capacity 6, 10 records and therefore 2 pages:

```text
Page 1: 1, 3, 5, 7, 9
Page 2: 2, 4, 6, 8, 10
```

`Orizzontale`/`Verticale` then decide where those slot ordinals appear physically. This is classic cut-and-stack ordering: after printing and cutting equal slot stacks, records remain sequential when the stacks are combined.

The previous managed checkbox named `Draw cut marks` was an unrelated convenience feature. It remains available as **Managed crop marks (non-legacy)** and is deliberately not serialized as `Taglio`.

## Back-side behavior

### Print mode

The native output mode is selected by three OptionButtons:

- `SoloFronte` — front only;
- `FronteRetro` — front followed by back for the same logical page;
- `SoloRetro` — back only.

`Fronte` and `Retro` are separate transient phase/view controls used while `StampaPagina_Click` executes. They are not the persistent output-mode selector. The managed `LayoutSide.Front`, `LayoutSide.Unknown` and `LayoutSide.Back` values encode `SoloFronte`, `FronteRetro` and `SoloRetro` respectively to preserve existing sidecar compatibility.

`StampaTutte_Click` loops the logical `Pagina` range and delegates each page to the page-print routine; front/back phase orchestration stays inside `StampaPagina_Click`. Managed Print All follows the same conceptual shape.

### `RetroaSpecchio`

`RetroaSpecchio` is applied while `cmdImposta_Click` builds the grid for the `Retro` phase. Native code swaps symmetric MSFlexGrid columns around the vertical centreline. Because the native grid alternates card and gap columns, its arithmetic uses the doubled grid dimensions; the equivalent logical-card transform is:

```text
backColumn = Columns - 1 - frontColumn
```

This mirrors **record-to-slot assignment** horizontally. It does not flip the pixels inside each card. The managed printer therefore moves the same record/card to the mirrored back slot and renders the back side normally.

### `OffsetRetroX` / `OffsetRetroY`

After the normal cell geometry has been calculated, `StampaPagina_Click` checks the `Retro` phase and adds the two text values directly to the final coordinates:

```text
X_back = X_slot + OffsetRetroX
Y_back = Y_slot + OffsetRetroY
```

The offsets accept signed values and are represented in managed code as millimetres. They affect only back output.

## Legacy sheet formats from `Campo.ini`

Native `SubMain` builds the global configuration path as `App.Path + "\\Campo.ini"`. `Sequenza.Form_Load` clears `cboDimensioni`, loops keys **1 through 20** in `[Formati]`, adds every non-empty value, then selects list index 0.

The supplied UltraPrint configuration contains:

```ini
[Formati]
1=A4 [21x29,7 cm]
2=A3 [29,7x42 cm]
3=Card [8,5x5,4 cm]
```

`cboDimensioni_Click` parses the first number between `[` and `x` and the second between `x` and the following space. Portrait keeps width/height; Landscape swaps them. Managed preview sizes are therefore 210 x 297 mm for A4, 297 x 420 mm for A3 and 85 x 54 mm for Card before orientation swapping.

The recovered path does **not** prove a direct mapping from `cboDimensioni` text to Windows `PaperKind`. `StampaPagina_Click` consumes the rebuilt MSFlexGrid geometry rather than rereading `cboDimensioni`, so the managed code keeps virtual Sequenza sheet size separate from the actual printer driver's paper choice.

## Paper orientation and coordinate origin

`FoglioPortrait` / `FoglioLandscape` are independent of record fill. Their click handlers route through `cboDimensioni_Click`, which swaps the selected virtual sheet width/height before rebuilding the grid.

The managed Sequence workspace applies orientation to `PageSettings.Landscape`. Windows Page Setup is initialized from Sequenza orientation and writes its resulting orientation back when accepted.

The native `SmartFormDll.Report.PrintPage` callable accepts the target plus `MargineDx` and `MargineTop`. VB6 Printer coordinates are page coordinates from the physical page's upper-left edge. .NET `PrintDocument` with `OriginAtMargins = false` exposes `Graphics` at the printable-area origin, so the managed sequence print path compensates `PageSettings.HardMarginX` / `HardMarginY` to restore the legacy physical-page coordinate system. Real-driver clipping still remains a real-printer validation item.

## Legacy `.Seq` setup format

When the Optional filename is Missing, native `ScriviSetup` and `LeggiSetup` build:

`App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`

They enumerate form controls and persist `Name` plus `Value`/`Text` under `[Sequenza]`.

`LegacySequenceIniStore` maps the controls with proven managed equivalents:

- `Righe`, `Colonne`;
- `MargineDestro`, `MargineAlto`;
- `PassoOrizzontale`, `PassoVerticale`;
- `OffsetRetroX`, `OffsetRetroY`;
- `Orizzontale`, `Verticale`;
- `Taglio`;
- `cboDimensioni`;
- `FoglioPortrait`, `FoglioLandscape`;
- `RetroaSpecchio`;
- `SoloFronte`, `FronteRetro`, `SoloRetro`;
- `PaginaSingola`.

Numeric dimensions are written with the original Italian decimal comma and read using both Italian and invariant forms. OptionButton/checkbox values are emitted as numeric `1`/`0`; boolean text including Italian `Vero`/`Falso` is accepted when reading existing files. Existing `.Seq` files are updated conservatively and unknown keys, including transient `Fronte`/`Retro`, remain untouched.

Managed `.sequence.json` is version 5. Version 4 already contains `cboDimensioni` and orientation and migrates the newly recovered Taglio/back controls to disabled/zero defaults. Earlier migrations retain the existing gap/orientation compatibility rules. The managed-only crop-mark setting remains sidecar-only.

## Recovered native callable contracts

| Method | Native VA | Recovered callable shape | Managed exposure |
| --- | ---: | --- | --- |
| `Pescarecord` | `0x005C3590` | zero explicit arguments; modal `FormQBE` -> DAO `FindFirst`; returns `AbsolutePosition + 1` as `Variant`, or `0` when cancelled/not found | exposed |
| `PosizionaPagina` | `0x005C5250` | one explicit `Variant` `NumRecord`; computes legacy `Pagina`, refreshes through `cmdImposta_Click`, scans/highlights matching grid cell | exposed |
| `ScriviSetup` | `0x005D5810` | one **Optional Variant** filename | exposed |
| `LeggiSetup` | `0x005D65B0` | one **Optional Variant** filename | exposed |

`Pescarecord` reproduces the observable QBE/FindFirst workflow, one-based successful result and `0` failure/cancel shape while sharing the current managed database row.

`PosizionaPagina` preserves the native arithmetic, including the exact-capacity boundary quirk. In normal mode it computes `Fix(NumRecord / RecordxPagina) + 1`; in `PaginaSingola` mode it computes `NumRecord Mod Pagine`; `Pagina_Change` normalizes values outside `1..Pagine` back to page 1. The matching grid cell is shown with the native `QBColor(12)`/bold cue in managed preview.

## ScriptControl identity

Native `Funzioni.AddObjects` registers the same global `Sequenza` instance under both `Sequenza` and `Stampa`. Managed registration does the same and exposes the recovered `Pescarecord`, `PosizionaPagina`, `ScriviSetup` and `LeggiSetup` subset through one facade instance.

## Remaining parity work

- determine whether and how legacy `cboDimensioni` influenced the physical printer driver's paper selection outside the recovered grid path;
- compare hard margins, clipping, `RetroaSpecchio` and `OffsetRetro*` against real legacy printer/driver combinations;
- recover device/card-printer progress, cancellation and hardware-specific duplex behavior beyond the generic Sequenza slot mirror;
- keep expanding the script/device surface only where native behavior is proven.
