# Sequenza / sheet-print compatibility

UltraPrint 2.2.115 contains a dedicated `Sequenza` form for arranging multiple card records on physical sheets. Recovered VB6 method names include `Pescarecord`, `PosizionaPagina`, `ScriviSetup`, `LeggiSetup`, `StampaPagina_Click`, `StampaTutte_Click`, front/back/page controls, and row/column/margin/pitch handlers.

The managed replacement now exposes the same workflow through **Sequence -> Sequence / Sheet printing...**.

## Implemented workflow

- configurable rows and columns;
- left/top margins in millimetres;
- horizontal/vertical pitch in millimetres;
- selectable first slot on the first sheet;
- interactive sheet preview; clicking a first-sheet slot moves record 1 there;
- previous/next/first/last logical sheet navigation;
- Front, Back, or Front + Back output;
- optional cut marks;
- Page Setup using the real Windows printer/paper settings;
- Preview/Print current logical sheet;
- Preview/Print all logical sheets;
- database-record imposition using the same `.ly.data.json`, query/table state and field bindings as Database / Records;
- template-only repeated-card printing when database records are disabled.

`SequencePrintPlanner` fills the first sheet from `StartSlot` in row-major order. Every later sheet starts at slot zero. Current-sheet printing preserves that logical sheet's record assignment rather than re-numbering it from record 1.

## Front/back behavior

When **Front + Back** is selected, the managed print service emits two consecutive physical pages for each logical sheet: front first, back second, with identical record-to-slot assignments. This is the safest currently proven representation of the legacy front/back intent.

Exact printer-specific duplex mirroring/rotation remains unclaimed until UltraPrint 2.2.115 is compared on a real duplex/card-printer workflow. Device-specific transforms must not be guessed into the generic sheet planner.

## Setup persistence

Exact native storage used by `Sequenza.LeggiSetup` / `ScriviSetup` has not yet been decoded byte-for-byte. Managed settings are therefore stored non-destructively beside the layout as:

`<layout>.sequence.json`

This keeps legacy `.ly` bytes untouched and can be migrated once the original setup format is proven.

## ScriptControl identity

Native `Funzioni.AddObjects` registers the same global `Sequenza` instance twice, under the names `Sequenza` and `Stampa`. That alias identity is proven and must be preserved when the ScriptControl facade is enabled.

The visible managed sequence workflow is implemented first. Individual script-callable `Sequenza` methods are not exposed merely from their names: their ABI/parameter semantics will be decoded before the shared `Sequenza`/`Stampa` facade is registered.

## Remaining parity work

- decode the exact `Pescarecord`, `PosizionaPagina`, `LeggiSetup` and `ScriviSetup` callable contracts;
- register one shared ScriptControl facade as both `Sequenza` and `Stampa`;
- compare paper orientation, printer hard margins and front/back transforms against the legacy executable;
- recover exact native setup persistence if production data depends on it;
- integrate device/card-printer progress/cancel behavior after the standard sheet workflow is stable.
