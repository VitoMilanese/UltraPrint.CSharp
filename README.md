# UltraPrint C#

C#/.NET 8 replacement of the legacy UltraPrint 2.2.115 VB6 application recovered from the supplied 2003 binaries.

The target is **iso-functional compatibility**, not merely a `.ly` viewer. The new program is being rebuilt workflow by workflow while keeping compatibility with the legacy layouts/configuration that real installations still use.

## Current status

Implemented compatibility foundation:

- .NET 8 solution split into Core / Legacy / WinForms / Recovery CLI.
- Recovered legacy startup switches: `/erasepw`, `/Layout=`, `/printform`.
- `/erasepw` compatibility for `UP.ini` `[Setup] Pw`.
- Order-preserving ANSI-era INI reader/writer.
- Parser for the schema/config structure in `Campo.ini`.
- Verified `[$]` current-value store used by native `frmCarta.CampoToIni` / `IniToCampo`.
- Partially decoded UltraPrint 2.2.115 binary `.ly` codec.
- Confirmed card header: height, width and DPI.
- Confirmed fixed 64-slot field table, 264 bytes per field.
- Confirmed field names, legacy type codes, X/Y/width/height in VB6 twips, payload, font, font size, fixed flag, OLE colors and level.
- Confirmed sample types `3 = text`, `5 = image`, `6 = image/photo placeholder`.
- Raw 264-byte legacy records are preserved per field so unknown flags survive record move/duplicate operations.
- Legacy Save/Save As rewrites only known values plus preserved raw records; unknown header/footer data stays untouched.

Implemented editor/standard-print workflow:

- Front / Back / All view.
- Real image preview and legacy `.\LY\...` asset resolution.
- Text and photo-placeholder rendering.
- Field list and PropertyGrid editing.
- Mouse drag movement and corner-handle resizing.
- Keyboard nudging: 0.1 mm, `Shift` = 1 mm, `Ctrl` = 0.01 mm.
- 1 mm grid and snap-to-grid.
- Insert text/image/photo-placeholder fields.
- Duplicate/delete fields while preserving raw legacy record bytes.
- Bring-to-front / send-to-back level editing.
- Layout width/height/DPI editor.
- Standard Windows PrintDocument Print, Print Preview and Page Setup.
- Imported images are copied using the original `LY` path convention where applicable.

Implemented database/record workflow:

- **Database -> Database / Records...** workspace integrated into the main application.
- Legacy source filters recovered from the original program: Access, DBF, Excel, CSV and text.
- `.mdb` and `Operatori.FFM` use a Jet/Access-compatible OLE DB path: ACE 16/12 first, then Jet 4.0 for legacy x86 environments.
- Table discovery and browsing.
- Direct SELECT queries plus confirmed action SQL execution for writable OLE DB sources.
- Record grid and first/previous/next/last navigation.
- Runtime `Record2Card`-style substitution for text and photo/image fields.
- Explicit card-field to database-column bindings.
- Per-record Front/Back/All card preview.
- Print/preview the current database record.
- Preview/print all loaded records using the same physical-size card renderer.
- Direct **Print all** now reproduces the separate native `frmPrinting` shell: `Inizia` starts the batch, `Annulla` raises cooperative cancellation, and live card/side/countdown state remains responsive through the VB6-style message pump.
- Recovered `UP.ini [Setup] Intervallo`: native load default is `5`; layouts with decoded legacy type-10 `SmartCara`/Chip fields (`frmCarta.HasChip`) normalize the interval to at least 30 seconds and wait only **between cards**. Non-chip layouts receive no interval delay.
- CSV/text fallback parser works without an OLE DB text driver and preserves quoted delimiters.
- Database path, SQL, selected table and newly created managed bindings are persisted beside the layout in `.ly.data.json` until the exact legacy `.ly` database/binding byte offsets are verified.

That sidecar is intentional: unverified legacy bytes are not overwritten just to make a feature appear complete. Existing bindings recovered from legacy layouts continue to work, and the managed state can be migrated into native `.ly` bytes once the exact offsets are proven from a real database-bound legacy fixture.

Implemented sequence/sheet-print workflow:

- **Sequence -> Sequence / Sheet printing...** workspace with configurable rows/columns and first-slot selection.
- Native `MargineDestro` is recovered as the left-origin X offset used by `StampaPagina`; `MargineAlto` is the top offset.
- Native `PassoOrizzontale` / `PassoVerticale` are inter-card gaps, not full slot pitch.
- Recovered `Orizzontale` row-major and `Verticale` column-major record fill.
- Recovered `Taglio` as cut-and-stack record ordering rather than crop-mark drawing.
- Recovered `Campo.ini [Formati]` keys 1..20 and `cboDimensioni` virtual sheet sizing; the supplied A4/A3/Card strings are parsed with the native bracket/x/space grammar.
- Recovered `FoglioPortrait` / `FoglioLandscape` paper orientation, kept separate from record fill direction.
- Sequence printing compensates modern `PrintDocument` hard-margin origin so UltraPrint coordinates remain measured from the physical page edge; the driver still clips physically unprintable pixels.
- Recovered `SoloFronte`, `FronteRetro` and `SoloRetro` output modes, plus horizontal back-slot mirroring via `RetroaSpecchio` and signed `OffsetRetroX` / `OffsetRetroY` corrections.
- Recovered native `Pausa` / `Riprendi` cooperative pause behavior and `cmdStop` semantics: **Print all finishes the current logical sheet and then stops**, including the matching back phase in `FronteRetro`; live sheet/side progress is shown while printing.
- Optional crop marks remain a managed-only convenience and are deliberately not serialized as legacy `Taglio`.
- Database-record imposition reuses the same query/table/binding state as Database / Records; template-only repeated-card mode is also available.
- Legacy `[Sequenza]` `.Seq` setup persistence covers the proven controls including `Taglio`, `cboDimensioni`, output mode, back mirror and back offsets while preserving unknown keys and transient `Fronte` / `Retro` state.
- Script-visible `Sequenza` / `Stampa` aliases expose recovered `Pescarecord`, `PosizionaPagina`, `ScriviSetup` and `LeggiSetup` behavior.
- Managed `.sequence.json` v5 persists the recovered sheet format, cut-and-stack and back-side geometry state; v1-v4 migrate without changing established placement/side behavior. Pause/Stop/progress remain transient job state.
- The supplied `samples/legacy/Campo.ini` is copied to the WinForms output as `Campo.ini`, matching the native `App.Path\Campo.ini` lookup convention.

Implemented operator/security compatibility layer:

- Recovered original lookup order: `Db\Operatori.FFM`, then root `Operatori.FFM`.
- Existing operator databases use ACE 16 -> ACE 12 -> Jet 4.0 provider fallback.
- Existing files are opened conservatively: `Operatore`, `Password` and `Livello` must exist; only the two native-confirmed migrations for `Privilegio LONG` and `Gruppo TEXT(50)` may be applied automatically.
- **Security** menu with Login, Logout, current-operator password change, Operators and Privileges management.
- Operator create/edit/delete plus administrator password reset.
- Raw `Livello`, `Privilegio` and `Gruppo` values are preserved and editable.
- A new `Operatori.FFM` can be created through ADOX when a compatible Jet/ACE stack is installed.
- The current session is shown in the application status bar.

Exact privilege-to-command gating is deliberately not guessed yet. The original binary contains `MainForm.PuoFare` and `MainForm.SetPrivilegi`, but a real legacy `Operatori.FFM`/runtime comparison is still required to prove the meaning of `Livello`, `Privilegio`, password storage/comparison, and when login is mandatory. See [`docs/OPERATOR_COMPATIBILITY.md`](docs/OPERATOR_COMPATIBILITY.md).

Implemented scripting compatibility foundation:

- Recovered the original `Funzioni.AddObjects -> AddProg -> Vbscript("Load")` layout-script lifecycle.
- Recovered all 20 names passed to `ScriptControl.AddObject`, including aliases that point to the same native object (`Db`/`frmDatabase`, `Fn`/`Funzioni`, `Sequenza`/`Stampa`).
- Rechecked `Fn`/`Funzioni` directly in native `AddObjects`: both resolve the same global singleton, and the managed host preserves that identity.
- Recovered the shipped engine ProgID: `MSScriptControl.ScriptControl` from `MSSCRIPT.OCX`.
- Recovered lifecycle names `OnLoad`, `Load`, `Main` and `Unload` and root/application `Script\*.VBS` path conventions.
- Recovered the directly observed `PreparaCodice` syntax-normalization rules.
- Recovered and implemented `SostituisciRiga` / `SOSTITUZ.TXT`: sequential case-sensitive search/replacement rules, positive numeric replacements as ANSI characters, and execution before other line macros.
- Recovered `Interpretariga` semantics for `@(variable)`, `$(scope.key)`, `?(prompt)`, `@GETFILE(...)`, `@DIRECTORY(...)` and `@COMPUTER(...)`, including native write-back/increment and cancellation behavior.
- The WinForms script workspace now executes through the real `SostituisciRiga -> Interpretariga -> AddProg` chain rather than bypassing the recovered macro layer.
- Recovered the native fixed 1024-slot variable table; `GetVariabile`, `SetVariabile` and `IncVariabile` share state with `@()` macro expansion.
- Recovered and wired the native `AddProg` source split into the engine: top-level lines use `ExecuteStatement`; from the first `sub`/`function` match onward the mode never resets and the accumulator is submitted through `AddCode` at EOF.
- Recovered the core `Vbscript` dispatcher: prefix/event procedure names, numeric `P_` prefixes, space removal, uppercase `X -> *`, direct ScriptControl `Run`, `NO CODE` probing and seven optional Variant slots.
- Recovered exact optional-argument semantics: the first VB Missing argument truncates that slot and every slot to its right; `Null` remains a supplied Variant.
- Recovered the native first-procedure probe shape as `Modules.Item(1).Procedures.Item(1)` under Resume Next rather than a collection-count approximation.
- Recovered the native nested dispatch-depth and `Chiudimi` unload contract: unload is deferred until the outermost Vbscript frame, then `Form_Unload` is recursively dispatched before ScriptControl `Reset`.
- Added COM-visible proven facades: `Me.Chiudimi`; shared `Fn`/`Funzioni.GetVariabile`, `SetVariabile`, `IncVariabile`, `Sostituisci`; `File.GetIni`, `WriteIni`, `SoloExt`, `SoloNomeFile`, `SoloPath`, `Esiste`; and `App.Path` / `App.EXEName`.
- Static analysis of this build's `File.ApriFile` shows its two formal arguments are unused and its filter is hard-coded to `Tutti i File|*.*`; the `@GETFILE(...)` WinForms adapter reproduces that behavior.
- Added a managed scripting session plus an optional COM adapter for the original Script Control when installed/registered for the process architecture, including direct `ExecuteStatement`, `AddCode` and `Run` calls.
- Added **Tools -> Legacy VBScript...** with script discovery, open/edit/save, prepared-source preview, compile, lifecycle-event invocation and ScriptControl line/column diagnostics.
- Legacy script execution is disabled until explicitly enabled for the current editor session; opening a layout does not silently execute arbitrary `.vbs` code.

This scripting layer remains intentionally partial. The main remaining compatibility gap is the callable member surface behind `Carta`, `Mainform`, database/table, sequence/print and device objects, plus production-safe automatic layout lifecycle integration and exact error-438 editor remapping. See [`docs/SCRIPT_COMPATIBILITY.md`](docs/SCRIPT_COMPATIBILITY.md) and [`docs/SCRIPT_OBJECT_MODEL.md`](docs/SCRIPT_OBJECT_MODEL.md).

Implemented counter compatibility workflow:

- Recovered native `Contatori.dat` as 99 logical counter slots with 20-byte serialized records and a canonical compacting save of 100 records / 2000 bytes.
- Preserves the still-unclaimed WORD and all existing one-byte name data without code-page guessing.
- Recovered `+(CounterName)` as first exact matching counter, increment-before-replacement, optional `Funzioni.Zeri` formatting and immediate persistent save; smartDriver uses increment `1`.
- Recovered `frmContatori` load/select/change/New/Delete/OK behavior, including first-match duplicate handling, `Capitalizzato`, fixed `String * 10` truncation, Delete/New slot-reuse quirks and deferred editor save.
- **Tools -> Counters...** provides the managed editor using `App.Path\Contatori.dat`, with digits 1..20, current value, leading-zero flag, New/Delete and OK-save.

Implemented device/card-printer compatibility foundation:

- Recovered the exact 24-export x86 stdcall surface dynamically loaded from external `ICE_API.DLL`.
- **Devices -> Printer / Device diagnostics...** restores the proven `Campo.ini` device/profile surface and explicitly reads ICE polling/model/serial/magnetic-head/active-job/error state when the compatible vendor runtime is available.
- Recovered `PrintWithCardStatus`: `_GetCardId@8` followed by `_GetCardStatus@28` level 1 with a maximum of 60 immediate status calls.
- Recovered the production `smartDriver.StampaRecord` orchestration envelope as non-executing compatibility semantics: Optional Variant Missing => False; optional track preparation; interactive mode enable; Win32 `StartDocA`/`StartPage` plus script hooks; optional rotation; `_FeedCard(hDC, 0x11)` result gate; `EncodeChip`; exact raw SmartCardContinue result branches; optional prepared-track magstripe encode; Front side-processing followed by Rear only when `HasRear`; FeedCard-failure close path; and interactive-mode cleanup.
- Recovered `MainForm.PrinterEscape` as GDI `PASSTHROUGH` escape 19 followed by `Printer.EndDoc`. Its first explicit argument is unused and its second is the payload. Native framing is now proven as `Chr(0) & Chr(Len(payload)) & payload & Chr(0)`, with `Len(payload)` passed as `cbInput` and `Null` as the fifth `Escape` argument; managed emission remains disabled pending ANSI/driver hardware validation.
- No ICE card-job cancel export and no `AbortDoc` / `KillDoc` / `CancelDC` path is proven, so hardware-level cancellation is deliberately not invented. Existing managed batch/sequence cancellation stays cooperative.
- All feed/rotate/magstripe/chip/cleaning/firmware printer mutations remain disabled until their remaining branch semantics and real hardware behavior are validated.

Still required for full parity includes database create/schema/import/write-back, complete `.ly` flag/type decoding, exact security privilege gating, remaining VBScript object facades, barcodes, image acquisition/editing, the exact legacy `cboDimensioni` relationship to physical printer paper size, real-printer clipping/duplex/chip-timing validation, full magstripe/smart-card execution, PrinterEscape ANSI/DBCS marshaling and hardware validation, active smartDriver mutation on supported hardware, remaining device-profile editing/module sequencing, and remaining options/job workflows.

See [`docs/FEATURE_PARITY.md`](docs/FEATURE_PARITY.md) for the authoritative parity checklist and [`docs/MIGRATION_PLAN.md`](docs/MIGRATION_PLAN.md) for implementation order.

## Build

Open `UltraPrint.sln` in Visual Studio 2022 with the .NET 8 desktop workload, or run:

```powershell
dotnet build UltraPrint.sln
```

Compatibility smoke tests:

```powershell
dotnet run --project tests/UltraPrint.CompatibilityTests/UltraPrint.CompatibilityTests.csproj -c Release
```

## Try the editor, records, sequence, security, scripts and counters

1. Build and run `UltraPrint.WinForms`.
2. Use **File -> Open layout (.ly)...** and open `samples\legacy\TPMFAO19\TPMFAO19.ly` or a real layout from an original UltraPrint `LY` directory.
3. Use **Front**, **Back** and **All** to inspect both sides.
4. Click a field to edit it in **Properties**, or drag/resize it directly on the card.
5. Use **Database -> Database / Records...** to open an `.mdb`, `.ffm`, `.dbf`, `.xls/.xlsx`, `.csv`, `.txt` or `.dat` source.
6. Open a table or run SQL, bind card fields to database columns, and navigate records while watching the card preview update.
7. Use **Print all** for direct database batch output: after choosing the Windows printer, the recovered `frmPrinting` shell waits for **Inizia** and offers **Annulla**. Chip layouts apply the persisted inter-card countdown; non-chip layouts do not.
8. Use **Sequence -> Sequence / Sheet printing...** to arrange records or template copies on sheets; choose legacy sheet format, Horizontal/Vertical fill, Taglio cut-and-stack mode, Portrait/Landscape orientation and front/back mirror/offset settings. During direct printing use **Pause/Resume**; **Stop** on Print all finishes the current logical sheet before ending the job.
9. Use **Security** to open/create `Operatori.FFM`, log in, manage operators and change passwords.
10. Use **Tools -> Legacy VBScript...** to inspect/discover legacy `.vbs` files. Execution requires explicit per-session opt-in and the legacy Script Control to be registered; interactive legacy macros use the managed prompt/file/folder/computer dialogs.
11. Use **Tools -> Counters...** to edit the legacy `Contatori.dat` table. Changes remain in-memory until **OK**, matching native `frmContatori`; runtime `+(Counter)` increments persist immediately.
12. Use **Save As** first with production legacy layouts while byte-level compatibility recovery is still in progress.

## CLI

```powershell
dotnet run --project tools/UltraPrint.RecoveryCli -- parse-campo samples/legacy/Campo.ini
dotnet run --project tools/UltraPrint.RecoveryCli -- probe-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- decode-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- startup-plan "C:\Program Files (x86)\UltraPrint"
```

## Compatibility rule

Only behavior proven from binary metadata/native flow or real legacy data is treated as a confirmed compatibility contract. Unknown `.ly` bytes remain preserved rather than guessed. A workflow is marked complete only when it actually works against legacy inputs; placeholder menu items do not count.

See `docs/LEGACY_FORMATS.md`, `docs/REVERSE_ENGINEERING.md`, `docs/DATABASE_COMPATIBILITY.md`, `docs/OPERATOR_COMPATIBILITY.md`, `docs/SCRIPT_COMPATIBILITY.md`, `docs/SCRIPT_OBJECT_MODEL.md`, `docs/SEQUENCE_COMPATIBILITY.md`, `reverse-engineering/BATCH_PRINTING_NATIVE.md`, `reverse-engineering/COUNTERS_NATIVE.md`, `reverse-engineering/COUNTER_EDITOR_NATIVE.md` and `reverse-engineering/SMARTDRIVER_ICE_API_NATIVE.md` for the recovered structures and confidence level.
