# Sequenza ScriptControl / legacy .Seq recovery note

This compatibility slice is implemented in `LegacyScriptSequenceFacade`, `LegacyScriptSequenceHostRegistry`, `LegacySequenceIniStore`, `LegacyQbeMatcher`, `WinFormsLegacySequenceHost`, and the shared WinForms database host.

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

The managed `Pescarecord` implementation now reproduces the proven observable contract: compact QBE field/operator/value input, first-match search, one-based result/zero failure shape, `Record non trovato`, and movement of the shared Database/Records `BindingSource` so subsequent `Tabella`/record-binding calls observe the found row. `LegacyQbeMatcher` keeps the recovered operator tokens while evaluating loaded `DataTable` values, which also extends the workflow to managed CSV/text sources that do not expose a DAO Recordset.

The managed `.Seq` reader/writer maps the exact control names with proven direct equivalents: `Righe`, `Colonne`, `MargineAlto`, `PassoOrizzontale`, `PassoVerticale`, and `PaginaSingola`. Existing unknown keys are preserved. In particular, `MargineDestro` is not guessed to mean the managed left-origin margin.

The managed `PosizionaPagina` implementation exposes the recovered script method, uses a pure `LegacySequencePositioning` helper for the literal native arithmetic/clamping, drives the currently open managed Sequence workspace, and mirrors the native MSFlexGrid match with a red/bold record highlight.
