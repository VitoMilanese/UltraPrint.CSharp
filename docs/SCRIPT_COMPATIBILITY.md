# Legacy VBScript compatibility

UltraPrint 2.2.115 embeds a real VBScript subsystem. This document separates behavior recovered directly from the native VB6 executable from managed compatibility behavior already implemented.

## Native evidence

The installer ships `MSSCRIPT.OCX`; the recovered ProgID is `MSScriptControl.ScriptControl`.

Key native methods:

| Method | Native address | vtable |
| --- | ---: | ---: |
| `LoadCodicePerTipo` | `0x004990E0` | `0x7A0` |
| `AddObjects` | `0x0049EE60` | `0x7C0` |
| `GetInside` | `0x004A0810` | `0x7CC` |
| `Parola` | `0x004A3110` | `0x7F0` |
| `Sostituisci` | `0x004A36F0` | `0x7F8` |
| `SostituisciRiga` | `0x004A3F50` | `0x800` |
| `GetVariabile` | `0x004AFEF0` | `0x82C` |
| `SetVariabile` | `0x004B0220` | `0x830` |
| `IncVariabile` | `0x004B0560` | `0x834` |
| `Interpretariga` | `0x004B1DF0` | `0x840` |
| `PreparaCodice` | `0x004B5800` | `0x844` |
| `AddProg` | `0x004B6420` | `0x848` |
| `Vbscript` | `0x004B7D00` | `0x84C` |

The recovered layout path is `AddObjects -> AddProg -> Vbscript("Load") -> frmCarta.DisegnaCampi`.

## Objects exposed by `AddObjects`

Native order:

1. `Me`
2. `Mainform`
3. `Preview`
4. `Db`
5. `Sequenza`
6. `Stampa`
7. `frmDatabase`
8. `Carta`
9. `Chip`
10. `Tabella`
11. `frmlogin`
12. `Fn`
13. `Funzioni`
14. `File`
15. `SmartDriver`
16. `Dispositivi`
17. `Printer`
18. `Screen`
19. `ClipBoard`
20. `App`

`AddObject(..., True)` is used for all twenty names.

Alias identity is part of the compatibility contract:

- `Db` and `frmDatabase` are the same VB6 instance;
- `Sequenza` and `Stampa` are the same VB6 instance;
- `Fn` and `Funzioni` are the same exact `Funzioni` singleton. Both native AddObjects blocks resolve global `0x0062B2C8` and object info `0x0041751C`.

The managed host therefore registers one `LegacyScriptFunctionsFacade` instance under both `Fn` and `Funzioni`.

## Script paths

Recovered candidates include:

- `<UltraPrint root>\Script\<type>.VBS`
- `<UltraPrint root>\App\<application>\Script\<type>.VBS`
- corresponding server-scoped candidates when a server name exists.

Native code also references `\Script\File.VBS` and `#endfile#`; their complete job-format role is still being recovered.

## `PreparaCodice`

Confirmed transformations include:

- remove `Private ` and `Public `;
- remove VB6 type clauses for Integer, Long, Variant, Date, Double, Currency, Boolean, Object and Any;
- remove `As MSComctlLib.Node` and `As MSComctlLib.ListItem`;
- ` Form_Unload(Cancel)` -> ` Form_Unload()`;
- ` Unload Me` -> `Chiudimi`;
- `'Me.'` -> `Me.`;
- `'Fn.'` -> `Fn.`;
- remove the literal marker `'-'`.

Space-sensitive native literals are preserved rather than generalized.

## `SostituisciRiga` / `SOSTITUZ.TXT`

`Interpretariga` first calls `SostituisciRiga`.

Recovered behavior:

- path `App.Path + "\\SOSTITUZ.TXT"`;
- lazily loaded/cached table;
- fixed legacy table of about 100 entries;
- comma-delimited search/replacement pairs;
- empty rules ignored;
- `Val(replacement) > 0` converts to an ANSI character, e.g. `13` -> CR, `10` -> LF, `9` -> TAB;
- sequential binary/case-sensitive replacement.

`LegacyScriptSubstitutionTable` implements that behavior while safely ignoring overflow instead of reproducing a VB6 bounds crash.

## `Interpretariga`

Recovered processing order:

1. `SostituisciRiga`
2. `@(`
3. `$(`
4. `?(`
5. `@GETFILE(`
6. `@DIRECTORY(`
7. `@COMPUTER(`

### `@(variable)`

`GetVariabile` supplies the stored Variant; `Parola(..., "·")` takes the first token and trims it. A stored trailing `+` is consumed as a one-time numeric increment and written back with `SetVariabile`. Missing variables expand to empty when a variable store is available.

The native variable store consists of two parallel Variant arrays with **1024 slots**. Names are compared after trim/case normalization. `SetVariabile` and `IncVariabile` update only existing slots. `IncVariabile` performs VB Variant `+ 1` coercion. `LegacyScriptVariableTable` reproduces this; its `Define` method is only a managed host initialization helper.

### `$(scope.key)`

Section is always `$`.

- empty scope -> `<App.Path>\<App.EXEName>.Ini`;
- non-empty scope -> `<App.Path>\App\<scope>\<scope>.Ini`;
- trailing `+` on the key increments the persisted numeric value before substitution.

### `?(prompt)`

Uses the input-box path. Empty/cancelled result aborts the current interpretation path.

### `@GETFILE(...)`

The macro body is tokenized into two arguments and passed to `File.ApriFile`. Static analysis of this exact UltraPrint 2.2.115 build shows that `ApriFile` does not use either formal argument and hard-codes `Tutti i File|*.*`. The WinForms adapter therefore uses an all-files dialog instead of inventing meaning for those arguments.

### `@DIRECTORY(prompt)`

Returns the selected folder; cancel aborts the interpretation path.

### `@COMPUTER(prompt)`

Native code uses the shell/network browser and returns the host component of the selected UNC path. The managed adapter currently uses a simpler computer/server prompt and applies the same UNC-to-host normalization.

## `AddProg`

Recovered execution is intentionally unusual:

1. read source line by line;
2. trim/lowercase only for detection;
3. before the first line containing `sub` or `function`, pad the original line, preprocess/interpret it, and immediately call ScriptControl `ExecuteStatement`;
4. the first `sub`/`function` match permanently switches a Boolean mode flag;
5. from then through EOF every prepared line is accumulated with CRLF;
6. EOF calls ScriptControl `AddCode` once, including the native empty-accumulator case.

`LegacyScriptSession.LoadProgram` uses this real path; it is no longer only a diagnostic planner.

## `Vbscript`

Recovered dispatcher contract:

- prefix + event form is `prefix_event`;
- empty prefix uses only the event;
- `Val(prefix) > 0` changes it to `P_<prefix>`;
- spaces are removed;
- uppercase `X` is replaced by literal `*` using the case-sensitive helper;
- the resulting string is passed directly to ScriptControl `Run`; UltraPrint does not perform its own wildcard lookup.

There are seven optional Variant slots. Native code tests them left-to-right with `rtcIsMissing`. The **first Missing truncates that argument and every argument to its right**, even if a later slot contains a value. `Null` is a supplied Variant and is not Missing. `LegacyScriptMissing` and `LegacyScriptInvocation.GetSuppliedOptionalArguments` reproduce this distinction.

### `NO CODE` probe

Before `Run`, native code probes:

`ScriptControl.Modules.Item(1).Procedures.Item(1)`

under Resume Next and tests the returned Variant for Empty. It does not rely on collection Count. When no usable first procedure exists, `Vbscript` returns the literal `NO CODE` sentinel. `MsScriptControlEngine` now mirrors this exact probe shape.

### Error 438

After `Run`, native code checks VB `Err.Number`. Error `438` enters the ScriptControl diagnostic path and reads `Error.Description`, `Text`, `Line` and `Column`; the error text is then tokenized by CRLF for code-editor localization. Managed diagnostics already preserve Description/Line/Column, but the exact legacy `Text`-to-editor-line mapping is still pending.

### `Chiudimi` / `Form_Unload`

`Unload Me` is rewritten to `Chiudimi`. Native `Chiudimi` sets a shared unload flag. `Vbscript` tracks nested dispatch depth and defers unload until the outermost frame.

At that point native code:

1. recursively dispatches `Form_Unload` with all seven optionals Missing;
2. hides the legacy utility form;
3. resets ScriptControl;
4. logs the unload;
5. clears the unload flag.

`LegacyScriptSession` implements the functional defer -> `Form_Unload` -> Reset state machine.

## Proven managed facade surface

Currently registered from direct native evidence:

- `Me.Chiudimi`;
- `Carta` when a live managed card is available:
  - `NuovoCampo`
  - `CampoToIni`
  - `IniToCampo`
  - `SetupCampo`
  - `DisegnaCampi`
  - `AggiornaMisure`
  - `AggiornaBottoni`;
- shared `Fn` / `Funzioni` object:
  - `GetVariabile`
  - `SetVariabile`
  - `IncVariabile`
  - `Sostituisci`;
- `File`:
  - `GetIni`
  - `WriteIni`
  - `SoloExt`
  - `SoloNomeFile`
  - `SoloPath`
  - `Esiste`;
- `App.Path`;
- `App.EXEName`.

The `Carta` facade uses the actual open `CardLayout` from the WinForms canvas. Its script-facing field number is one-based, matching native `frmCarta`, while the managed `.ly` slot remains zero-based. `CampoToIni` / `IniToCampo` synchronize the recovered `[$]` current-field keys without replacing unrelated `Campo.ini` content. The native `NuovoCampo` type switch is now represented by `LegacyFieldTypeCatalog`: `2=Rettangolo`, `3=Testo`, `4=Barcode`, `5=Immagine`, `7=Twain`, `8=Telecamera`, `9=BandaMagnetica`, `10=SmartCara`, `13=Tabella`; supplied fixture type `6` remains the proven photo/image-style record.

Methods whose argument meanings are still not sufficiently decoded (`SelectField`, `LabelToCampo`, `SetButton`, `AggiornaMarcatori`, `FaiFoto`, `Record2Card`) are intentionally absent from the COM facade rather than implemented by guesswork.

`SoloExt`, `SoloNomeFile` and `SoloPath` preserve the old literal Windows-string behavior rather than silently modernizing it through `System.IO.Path` semantics. `Esiste` supports both normal and Dir-style wildcard existence checks.

## Managed script workspace

**Tools -> Legacy VBScript...** now compiles through the recovered pipeline instead of bypassing it:

`SostituisciRiga -> Interpretariga -> AddProg -> ScriptControl`

It supplies:

- the shared `LegacyScriptVariableTable` used by both `@()` and `Fn`/`Funzioni`;
- the live `Carta` facade when a layout is open;
- input-box interaction;
- generic legacy file picker;
- folder picker;
- computer/server input with UNC host normalization;
- the proven COM-visible facade objects before code loading.

Script execution is still explicit per workspace session. Opening a layout does not silently execute arbitrary legacy `.vbs` yet.

## Remaining work

1. Recover the required `Mainform` members.
2. Bridge `Db`/`frmDatabase` and `Tabella` while preserving alias/state identity.
3. Bridge `Sequenza`/`Stampa` as one shared object.
4. Finish the remaining `Carta` methods only as their argument semantics are proved.
5. Add only the actually-used `Printer`, `Screen`, `ClipBoard`, `SmartDriver`, `Dispositivi` and `Chip` members.
6. Finish exact error-438 source-line remapping and relevant host logging.
7. Enable trusted normal layout lifecycle execution only after facade coverage is sufficient.
8. Validate `OnLoad -> Load -> Main -> Form_Unload` against representative production scripts.
9. Decide whether a 32-bit compatibility host is needed on machines where MSSCRIPT is only registered x86.

Until those points are complete, scripting remains **Partial**, not Implemented.
