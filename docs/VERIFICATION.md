# Verification

The migration uses temporary Windows CI only while a compatibility block is being developed. Temporary workflow files are removed from the final PR history.

## Current verified scenarios

On Windows with .NET 8:

- solution restore;
- Release build of the full solution;
- byte-identical load/save of an unchanged `TPMFAO19.ly`;
- insert/duplicate/delete field save+reload;
- layout dimensions/DPI and front/back fixture decoding;
- `.ffm`, DBF and Excel source-kind detection;
- CSV/text parsing, including quoted delimiters;
- `%FIELD%` record-to-card substitution;
- explicit managed field-to-column binding override;
- record binding does not mutate the source layout template;
- non-destructive `.ly.data.json` database/query/table/binding state round-trip;
- recovered `Operatori.FFM` lookup order: `Db\Operatori.FFM` first, root fallback second;
- operator-database locator fallback and precedence behavior;
- recovered script contract contains all 20 native `AddObject` names and the `OnLoad` / `Load` / `Main` / `Unload` lifecycle names;
- directly observed `PreparaCodice` transformations, including native literal spacing, are regression-tested;
- global and application-scoped `Script\*.VBS` discovery/candidate precedence is regression-tested;
- managed `LegacyScriptSession` resets the engine and injects registered objects before the recovered AddProg execution path;
- `SOSTITUZ.TXT` literal and numeric-character substitutions are regression-tested for sequential, case-sensitive behavior, empty-rule handling and execution before `@()` expansion;
- `@(variable)` expands the first middle-dot-delimited variable token, treats a stored trailing `+` as the native one-time increment marker, writes the incremented value back and expands missing variables to empty when a variable store is present;
- `$()` root and application-scoped INI paths, fixed `$` section, normal reads and `+` increment/write-back semantics are regression-tested;
- `?()`, `@GETFILE()`, `@DIRECTORY()` and `@COMPUTER()` use explicit interaction adapters, substitute successful results and expose the native empty-result cancellation path;
- the WinForms script workspace now passes its real interaction adapter and shared variable store into `SostituisciRiga -> Interpretariga -> AddProg` instead of bypassing the recovered macro layer;
- the recovered `AddProg` split is regression-tested structurally and through the engine boundary: pre-procedure lines call `ExecuteStatement` immediately, the first line containing `sub` or `function` permanently switches to the CRLF accumulator, and the final accumulator is passed to `AddCode`;
- `Vbscript` direct procedure-name normalization is regression-tested for empty/non-empty prefixes, positive numeric `P_` prefixes, literal-space removal and case-sensitive uppercase `X -> *`;
- the seven optional Variant slots now reproduce native `rtcIsMissing` behavior: the first Missing truncates that slot and all later slots, while `Null` remains a supplied argument;
- the normalized name is forwarded directly to the script-engine `Run` boundary without a managed wildcard-resolution layer;
- the native `NO CODE` sentinel path is regression-tested through an explicit ScriptControl procedure-presence probe, and the COM adapter mirrors `Modules.Item(1).Procedures.Item(1)` rather than relying on collection Count;
- nested Vbscript dispatch depth is balanced on normal and no-code paths;
- `Chiudimi` unload is regression-tested as deferred until the outermost dispatch frame, then recursive `Form_Unload` is invoked before ScriptControl `Reset`;
- repeated unload requests from inside `Form_Unload` do not recursively drain again;
- after unload/reset the managed session is no longer marked loaded and must load code again before dispatch;
- the fixed-capacity legacy variable table is regression-tested for trim/case lookup, no-create `SetVariabile`, Variant-style `IncVariabile` and missing-name behavior;
- proven `Funzioni` facade methods `GetVariabile`, `SetVariabile`, `IncVariabile` and case-sensitive `Sostituisci` are regression-tested;
- proven `File` facade helpers `SoloExt`, `SoloNomeFile`, `SoloPath` and ordinary/wildcard `Esiste` are regression-tested alongside `GetIni` / `WriteIni`;
- recovered `frmCarta.NuovoCampo` type codes are regression-tested: `2=Rettangolo`, `3=Testo`, `4=Barcode`, `5=Immagine`, `6=image/photo fixture`, `7=Twain`, `8=Telecamera`, `9=BandaMagnetica`, `10=SmartCara`, `13=Tabella`;
- the one-based native `Carta` field number is regression-tested against the managed zero-based `.ly` slot index;
- `Carta.CampoToIni` / `IniToCampo` are regression-tested for Italian numeric formatting, VB Boolean values, OLE colors, geometry/text/alignment round-trip and live selected-field synchronization;
- `Carta.NuovoCampo(4)` is regression-tested through real `.ly` save/reload so type `4` remains `Barcode` instead of degrading to `Unknown`;
- the managed script facade registration is regression-tested in native relative order as `Me -> Carta -> Fn -> Funzioni -> File -> App` when a live Carta host is available;
- proven facade registration without a Carta host remains supported for isolated/non-editor script sessions.

The latest `Carta` compatibility block passed temporary Windows CI with solution restore, a full Release build and the compatibility executable, including the live-script facade tests. The tests do not require `MSSCRIPT.OCX`; the COM adapter is optional and the recovered non-COM contracts are testable on a clean runner.

The Security/operator UI and Jet/ACE store compile as part of the full Windows solution. Real Access/Jet operator CRUD/authentication is deliberately **not** reported as fixture-verified yet because no representative legacy `Operatori.FFM` was supplied and provider availability is machine-dependent (ACE 16/12 or legacy Jet 4.0 x86). Exact password and privilege semantics therefore remain side-by-side validation items rather than inferred tests.

Likewise, full VBScript application behavior is not reported as production-verified yet. The workspace now runs the recovered preprocessing/interaction/variable/AddProg chain and proven `Me`, `Carta`, `Fn`/`Funzioni`, `File` and `App` facades exist, but most of the 20 injected objects plus the remaining ambiguous `Carta` methods and exact legacy error-438/source-line presentation still require additional reverse engineering or representative production scripts.
