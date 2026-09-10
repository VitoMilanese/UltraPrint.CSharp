# Sequenza ScriptControl / legacy .Seq recovery note

This compatibility slice is implemented in `LegacyScriptSequenceFacade`, `LegacyScriptSequenceHostRegistry`, `LegacySequenceIniStore`, and `WinFormsLegacySequenceHost`.

Native facts used by the implementation:

- `Pescarecord` at `0x005C3590` returns a VB6 `Variant` through a hidden result pointer and has zero explicit arguments. Its returned value semantics are still intentionally unimplemented.
- `PosizionaPagina` at `0x005C5250` consumes one explicit Variant. The parameter meaning and right-edge geometry are still intentionally unimplemented.
- `ScriviSetup` at `0x005D5810` and `LeggiSetup` at `0x005D65B0` each consume one Optional Variant filename.
- when that filename is Missing, both build `App.Path + "\\ly\\" + frmCarta.Caption + ".Seq"`.
- setup persistence is INI-style, section `[Sequenza]`; native code enumerates controls and uses their `Name` plus `Value`/`Text` with `File.WriteIni` / `File.GetIni`.
- the same global `Sequenza` object is injected into ScriptControl as both `Sequenza` and `Stampa`.

The managed `.Seq` reader/writer currently maps only the exact control names with proven direct equivalents: `Righe`, `Colonne`, `MargineAlto`, `PassoOrizzontale`, and `PassoVerticale`. Existing unknown keys are preserved. In particular, `MargineDestro` is not guessed to mean the managed left-origin margin.
