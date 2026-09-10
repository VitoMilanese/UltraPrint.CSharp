# Sequenza Taglio / front-back native recovery

This note records the UltraPrint 2.2.115 behavior recovered directly from `Sequenza.cmdImposta_Click`, `StampaPagina_Click` and `StampaTutte_Click`.

## Relevant controls

The VB6 form control table and generated getters identify:

| Control | Array id | Getter vtable |
| --- | ---: | ---: |
| `Fronte` | 10 | `+0x320` |
| `Retro` | 11 | `+0x324` |
| `OffsetRetroY` | 18 | `+0x340` |
| `OffsetRetroX` | 25 | `+0x35C` |
| `RetroaSpecchio` | 36 | `+0x388` |
| `FronteRetro` | 38 | `+0x390` |
| `SoloRetro` | 39 | `+0x394` |
| `SoloFronte` | 40 | `+0x398` |
| `Taglio` | 65 | `+0x3FC` |
| `Orizzontale` | 67 | `+0x404` |
| `Verticale` | 68 | `+0x408` |
| `MSFlexGrid1` | 75 | `+0x424` |

`cmdImposta_Click` stores `Righe * Colonne` in the form's record-per-page Variant and sizes the native MSFlexGrid to `2 * Righe` by `2 * Colonne`. The doubled grid alternates card cells and spacing cells.

## Taglio

`Taglio.Value` is tested independently in the horizontal and vertical population branches.

When false, the starting one-based record is:

```text
(Pagina - 1) * RecordxPagina + 1
```

and cells advance sequentially.

When true, the current slot ordinal starts at one and each cell computes:

```text
record = (slotOrdinal - 1) * Pagine + Pagina
```

The same transform occurs in both `Orizzontale` and `Verticale`; those controls only change how slot ordinals map to physical grid cells. Therefore `Taglio` is a cut-and-stack record-order mode, not crop-mark drawing and not a third fill direction.

## Print mode

`StampaPagina_Click` reads `SoloFronte` and `FronteRetro` first. If either is active, it enters the `Fronte` phase and rebuilds the grid. Otherwise it enters `Retro`, corresponding to `SoloRetro`.

After the front phase, when `SoloFronte` is not selected and back output exists, the routine switches to `Retro`, rebuilds, and prints the second phase. Thus the persistent output selector is:

```text
SoloFronte  -> front only
FronteRetro -> front, then back
SoloRetro   -> back only
```

`Fronte` and `Retro` are transient phase/view controls, not the persistent print-mode selector.

`StampaTutte_Click` loops the logical page range and delegates each page to the page-print routine. Front/back orchestration remains inside `StampaPagina_Click`.

## RetroaSpecchio

The mirror check occurs in `cmdImposta_Click` only when the `Retro` phase is active. The routine then swaps data between symmetric MSFlexGrid columns for each applicable row. Because the grid has alternating card/gap columns, the assembly uses doubled-column arithmetic around the vertical centreline.

At logical-card level the observable transform is:

```text
backColumn = Columns - 1 - frontColumn
```

Record identity is preserved. There is no evidence of a pixel-level horizontal flip of the card; the same back card is rendered normally in the mirrored slot.

## Back offsets

`StampaPagina_Click` first computes normal X/Y from `MSFlexGrid.CellLeft` / `CellTop` plus the recovered spacing arrays. When `Retro.Value` is true it reads `OffsetRetroX.Text` and `OffsetRetroY.Text`, converts them with the VB runtime numeric conversion and adds them directly to the final coordinate Variants:

```text
X_back = X_slot + OffsetRetroX
Y_back = Y_slot + OffsetRetroY
```

No absolute-value/clamp behavior was observed. Managed compatibility therefore permits signed offsets.

## Managed consequences

- `Taglio` maps to `SequencePrintSettings.CutStack`.
- `RetroaSpecchio` maps to `MirrorBack` and mirrors logical columns only for back output.
- `OffsetRetroX` / `OffsetRetroY` map to signed `BackOffsetXmm` / `BackOffsetYmm`.
- `SoloFronte` / `FronteRetro` / `SoloRetro` map to the existing managed output mode (`LayoutSide.Front` / `Unknown` / `Back`).
- managed crop marks remain an independent convenience feature and are never serialized as `Taglio`.
- `.sequence.json` version 5 persists the newly recovered state; v4 and older files migrate with Taglio/mirror disabled and offsets zero.
