# Migration plan

The project target is **iso-functional replacement of UltraPrint 2.2.115**, not a standalone `.ly` recovery utility. The authoritative implementation checklist is [`FEATURE_PARITY.md`](FEATURE_PARITY.md).

## Phase 1 — compatibility foundation

- [x] identify VB6 Native code and object graph
- [x] recover startup switches/paths
- [x] recover `Campo.ini` current-value semantics
- [x] create C# domain/legacy separation
- [x] extract and inspect `CadBox.ocx`
- [x] obtain/probe a real `.ly` sample
- [x] implement conservative `.ly` load/save with unknown-byte preservation
- [x] preserve raw legacy field records so record reorder/duplicate/delete does not reconstruct unknown flags
- [~] finish byte-level `.ly` codec (remaining flags/types/header/footer still unresolved)

## Phase 2 — card editor and standard printing

- [x] managed WinForms card canvas
- [x] field selection, mouse move and resize handles
- [x] precise keyboard movement
- [x] grid and snap-to-grid
- [x] text/image/photo-placeholder rendering
- [x] front/back/all view for recovered layouts
- [x] field insert, duplicate and delete using preserved legacy record templates
- [x] z-order editing through legacy level values
- [x] image import into legacy `LY` layout directory conventions
- [x] width/height/DPI layout properties
- [x] standard Windows PrintDocument print, preview and page setup path
- [~] appearance rendering (confirmed OLE colors implemented; remaining style flags still need byte mapping)
- [ ] full text style persistence (bold/italic/strike/alignment/rotation/border/opaque flags)
- [ ] table field record-byte mapping and full data rendering
- [ ] barcode record/property mapping and actual barcode rendering
- [ ] ruler/marker behavior parity with `frmCarta`
- [ ] completely decoded side/front/back persistence independent of current filename heuristics
- [ ] legacy New layout creation without requiring a source template

## Phase 3 — data, records and database design

- [x] recover original DAO/Jet dependency family and supported source filters from installer/native strings
- [x] implement managed database/data-source abstraction for Access/FFM, DBF, Excel, CSV and text
- [x] implement table discovery and direct SELECT/action SQL execution for writable OLE DB sources
- [x] implement managed record browser/navigation and card preview (`Tabella` core workflow)
- [x] implement runtime database-field binding and `Record2Card` substitution
- [x] implement current-record and all-record print/preview pipeline
- [x] persist new managed DB/query/table/binding state without modifying unverified `.ly` bytes
- [~] absorb `frmDatabase` core workflow (source/table/query/records/print implemented; schema designer and exact legacy persistence remain)
- [~] absorb required `db.exe` functions (open/query/action implemented; create/import/export/schema utilities remain)
- [ ] confirm global `.ly` Database/Sql and per-field `Campo/DataField` offsets using a real legacy database-bound layout
- [ ] implement record edit/write-back semantics
- [ ] implement database creation and schema editor (`FrmDati`, `frmTabelle`)
- [ ] implement query-by-example builder (`FormQBE`)
- [ ] implement import/export helpers used by production workflows

## Phase 4 — scripting, counters and barcode

- [~] recover script object model exposed by `Funzioni.AddObjects` / `AddProg`: all 20 native `AddObject` names are mapped; proven managed facades now exist for `Me`, `Carta`, shared `Fn`/`Funzioni`, `File` and `App`, while the remaining form/database/print/device objects still need script-callable member recovery
- [x] recover confirmed preprocessing pipeline through `SostituisciRiga`, `PreparaCodice` and `Interpretariga`: `SOSTITUZ.TXT`, `@()`, `$()`, `?()`, `@GETFILE()`, `@DIRECTORY()` and `@COMPUTER()` semantics are implemented
- [x] wire the recovered preprocessing into the actual WinForms script workspace, including the shared legacy variable table and prompt/file/folder/computer interaction adapter
- [x] recover the native 1024-slot `GetVariabile` / `SetVariabile` / `IncVariabile` store and expose it through the same managed object registered as both `Fn` and `Funzioni`
- [x] recover and wire the native `AddProg` execution split: top-level prepared lines use ScriptControl `ExecuteStatement`, the first line containing `sub`/`function` permanently switches to the CRLF accumulator, and final code is sent once through `AddCode`
- [x] recover the core `Vbscript` dispatcher: prefix/event naming, numeric `P_` handling, space removal, uppercase `X -> *`, direct ScriptControl `Run`, exact first-module/first-procedure `NO CODE` probing, first-Missing truncation of zero-to-seven optional Variant arguments, nested dispatch depth and deferred `Chiudimi -> Form_Unload -> Reset`
- [~] implement constrained VBScript compatibility strategy: `ILegacyScriptEngine`, streaming `LegacyScriptSession.LoadProgram`, optional `MSScriptControl.ScriptControl` COM adapter, direct `Run`, error line/column mapping and lifecycle names exist; automatic layout execution remains deliberately disabled until broader injected-object facade coverage is proven
- [~] implement script editor (`frmCodice`): managed **Tools -> Legacy VBScript...** workspace discovers/opens/edits/saves scripts and executes through the recovered preprocessing/AddProg pipeline; native code-tree formatting and exact error-438 source-line mapping remain
- [x] recover the first core helper facades: `Me.Chiudimi`, shared `Fn`/`Funzioni.GetVariabile/SetVariabile/IncVariabile/Sostituisci`, `File.GetIni/WriteIni/SoloExt/SoloNomeFile/SoloPath/Esiste` and `App.Path/EXEName`
- [~] recover `Carta` script facade: confirmed type switch and one-based field numbers are implemented; live editor injection plus `NuovoCampo`, `CampoToIni`, `IniToCampo`, `SetupCampo`, `DisegnaCampi`, `AggiornaMisure` and `AggiornaBottoni` are implemented; ambiguous `SelectField`, `LabelToCampo`, `SetButton`, `AggiornaMarcatori`, `FaiFoto` and `Record2Card` remain
- [x] preserve recovered `NuovoCampo` type codes across managed `.ly` load/save: `2=Rettangolo`, `3=Testo`, `4=Barcode`, `5=Immagine`, `7=Twain`, `8=Telecamera`, `9=BandaMagnetica`, `10=SmartCara`, `13=Tabella`, plus fixture-proven image/photo type `6`
- [ ] recover required `Mainform` script members
- [ ] bridge `Db`/`frmDatabase` and `Tabella` while preserving alias/state identity
- [ ] bridge `Sequenza`/`Stampa` as the same managed object
- [ ] add the actually-used intrinsic/print/device facades (`Printer`, `Screen`, `ClipBoard`, `SmartDriver`, `Dispositivi`, `Chip`)
- [ ] integrate trusted legacy script lifecycle execution with normal layout open/close once the required object facade surface is sufficient
- [ ] reproduce remaining ScriptControl error-438/source-line remapping and host logging where functionally relevant
- [ ] implement counters (`frmContatori`, `Contatori.dat`)
- [ ] implement barcode field/rendering workflow (`frmBarcode`)

## Phase 5 — image acquisition

- [ ] replace LEADTOOLS editor operations used by `frmImg`
- [ ] brightness/contrast/crop/zoom parity
- [ ] scanner/capture adapter (WIA/TWAIN or device-specific bridge)
- [ ] replace required `SmartFormDll.Image` API surface

## Phase 6 — sequence printing and devices

- [ ] implement `Sequenza` sheet/page imposition, rows/columns, margins, pitch and front/back pages
- [ ] printer/device profiles (`frmDispositivi`)
- [ ] card-printer adapter interfaces
- [ ] magnetic Track 1/2/3 configuration and encoding (`frmTracce`, `frmMostraBanda`)
- [ ] smart-card/chip adapter (`Chip`)
- [ ] replace required `smartDriver`, `ICE_API` and remaining `SmartFormDll.Report` behavior

## Phase 7 — operators/options/shell parity

- [x] recover `Operatori.FFM` startup lookup order (`Db\Operatori.FFM`, then root fallback)
- [x] implement Jet/ACE operator-database open path and native-confirmed `Privilegio`/`Gruppo` schema migrations
- [x] implement managed `Operatori.FFM` creation when ADOX/Jet/ACE is installed
- [x] implement operator enumeration and login UI (`frmLogin` core workflow)
- [x] implement operator create/edit/delete and password change/reset workflows
- [~] implement raw `Livello` / `Privilegio` / `Gruppo` management and privilege-description UI
- [ ] validate password comparison/storage against a real legacy `Operatori.FFM`
- [ ] decode exact `MainForm.PuoFare` / `SetPrivilegi` privilege-to-action mapping before gating application commands
- [ ] confirm when the original application forces login at startup versus allowing an unauthenticated session
- [ ] validate operator CRUD/schema behavior side-by-side against a production legacy database
- [ ] options UI (`Opzioni`)
- [ ] job/layout manager (`frmJob`, `frmApri`)
- [ ] remaining shell/help/wait behavior where functionally relevant
- [x] obsolete machine-bound `frmLic` mechanism is not required by the new application

## Phase 8 — parity verification

For every migrated workflow, compare the managed implementation against UltraPrint 2.2.115 with the same legacy files, database rows, printer settings and rendered/printed geometry. Features are marked complete only when behavior works; placeholder menu items do not count.
