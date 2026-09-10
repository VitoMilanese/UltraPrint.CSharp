# Sequenza / sheet-print compatibility

UltraPrint 2.2.115 contains a dedicated `Sequenza` form for arranging multiple card records on physical sheets. Recovered VB6 method names include `Pescarecord`, `PosizionaPagina`, `ScriviSetup`, `LeggiSetup`, `StampaPagina_Click`, `StampaTutte_Click`, front/back/page controls, and row/column/margin/pitch handlers.

The managed replacement exposes the visible workflow through **Sequence -> Sequence / Sheet printing...** and now also exposes the first proven ScriptControl-compatible `Sequenza` / `Stampa` surface.

## Implemented visible workflow

- configurable rows and columns;
- left/top managed margins in millimetres;
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

## Recovered native callable contracts

Direct disassembly now distinguishes explicit parameters from VB6 hidden return storage:

| Method | Native VA | Recovered callable shape | Managed exposure |
| --- | ---: | --- | --- |
| `Pescarecord` | `0x005C3590` | zero explicit arguments, returns `Variant` through a hidden 16-byte result pointer | not yet exposed; return semantics still unresolved |
| `PosizionaPagina` | `0x005C5250` | one explicit `Variant` argument, no hidden result observed | not yet exposed; argument meaning/side effects still unresolved |
| `ScriviSetup` | `0x005D5810` | one **Optional Variant** filename | exposed |
| `LeggiSetup` | `0x005D65B0` | one **Optional Variant** filename | exposed |

The Optional-argument check in both setup methods is the same VB runtime path. When the filename is Missing, native code constructs:

`App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`

The managed default path resolver reproduces that convention using the current layout name and its recovered legacy application root.

## Legacy `.Seq` setup format

`ScriviSetup` and `LeggiSetup` are no longer treated as an unknown binary format. Native code obtains the form's control collection, reads control `Name` plus `Value` or `Text` through late binding, and calls `File.WriteIni` / `File.GetIni` with the literal section name:

`[Sequenza]`

The new `LegacySequenceIniStore` therefore reads and writes actual `.Seq` INI files. It currently maps only controls whose meaning is unambiguous in the managed model:

- `Righe` -> rows;
- `Colonne` -> columns;
- `MargineAlto` -> top margin;
- `PassoOrizzontale` -> horizontal pitch;
- `PassoVerticale` -> vertical pitch.

Numeric values are written with the original Italian-style decimal comma and read using both Italian and invariant numeric forms.

Existing `.Seq` files are updated conservatively: unknown keys are preserved. This is important because the old form also persists controls such as `MargineDestro`, `Fronte`, `Retro`, paper/card orientation, `PaginaSingola`, `Taglio`, and other state whose exact relationship to the managed placement model has not yet been proved.

### Why `MargineDestro` is not mapped to managed `MarginLeftMm`

The old form's handler is explicitly named `MargineDestro_Change` (right margin), while the first managed planner used a left-origin margin. Until `PosizionaPagina` / print geometry proves whether the legacy sequence fills from the right edge, mirrors columns, or merely labels the control unusually, mapping it to the managed left margin would be a guess. The key is therefore preserved untouched instead of silently changing sheet geometry.

The managed `.sequence.json` sidecar remains in use for managed-only/unresolved state. Loading a legacy `.Seq` starts from the current managed settings and replaces only the five confirmed equivalents, so unresolved values are not destroyed.

## ScriptControl identity

Native `Funzioni.AddObjects` registers the exact same global `Sequenza` instance twice, under the names `Sequenza` and `Stampa`.

The managed registration now does the same: one `LegacyScriptSequenceFacade` object is registered under both names, in recovered relative order:

`... Db -> Sequenza -> Stampa -> frmDatabase ...`

The exposed subset is:

- `Sequenza.ScriviSetup([Filename])` / `Stampa.ScriviSetup([Filename])`;
- `Sequenza.LeggiSetup([Filename])` / `Stampa.LeggiSetup([Filename])`.

An application-lifetime WinForms sequence host follows the layout currently open in `MainForm`; legacy setup loads are mirrored into that layout's managed sequence state. `Pescarecord` and `PosizionaPagina` remain deliberately unavailable rather than being replaced with fake methods whose parameter semantics are not yet fully understood.

## Remaining parity work

- finish `PosizionaPagina` geometry recovery, especially `MargineDestro`, horizontal fill direction, orientation and paper-edge behavior;
- decode the value returned by `Pescarecord` and its exact interaction with the active database row;
- map the remaining `.Seq` control names only when their native meaning is proven;
- make an already-open managed Sequence workspace refresh immediately when a script calls `LeggiSetup` (the persisted state is already shared; reopening/reloading sees it now);
- compare paper orientation, printer hard margins and front/back transforms against the legacy executable;
- integrate device/card-printer progress/cancel behavior after the standard sheet workflow is stable.
