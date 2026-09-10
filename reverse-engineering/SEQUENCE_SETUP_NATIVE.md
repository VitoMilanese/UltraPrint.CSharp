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
- `PosizionaPagina` at `0x005C5250` consumes one explicit Variant. It reads `PaginaSingola`, updates `Retro`, invokes another Sequenza refresh method, and walks `TabStrip1` to update page/tab state. The exact page-number normalization is still intentionally unexposed until every branch is proved; this avoids an off-by-one compatibility bug.
- `ScriviSetup` at `0x005D5810` and `LeggiSetup` at `0x005D65B0` each consume one Optional Variant filename.
- when that filename is Missing, both build `App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`.
- setup persistence is INI-style, section `[Sequenza]`; native code enumerates controls and uses their `Name` plus `Value`/`Text` with `File.WriteIni` / `File.GetIni`.
- the same global `Sequenza` object is injected into ScriptControl as both `Sequenza` and `Stampa`.

The managed `Pescarecord` implementation now reproduces the proven observable contract: compact QBE field/operator/value input, first-match search, one-based result/zero failure shape, `Record non trovato`, and movement of the shared Database/Records `BindingSource` so subsequent `Tabella`/record-binding calls observe the found row. `LegacyQbeMatcher` keeps the recovered operator tokens while evaluating loaded `DataTable` values, which also extends the workflow to managed CSV/text sources that do not expose a DAO Recordset.

The managed `.Seq` reader/writer currently maps only the exact control names with proven direct equivalents: `Righe`, `Colonne`, `MargineAlto`, `PassoOrizzontale`, and `PassoVerticale`. Existing unknown keys are preserved. In particular, `MargineDestro` is not guessed to mean the managed left-origin margin.
