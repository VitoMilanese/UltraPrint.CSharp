# UltraPrint 2.2.115 functional-parity inventory

The target of this repository is **iso-functional replacement** of the supplied UltraPrint 2.2.115 installation, not merely a `.ly` viewer/editor.

Status values:

- **Implemented** — managed replacement exists and is usable.
- **Partial** — a real subset exists, but legacy behavior is not yet complete.
- **Missing** — recovered from the original binary but not implemented yet.
- **N/A** — legacy restriction/infrastructure that does not need to be reproduced to preserve useful application functionality.

## Main application and editor

| Original object/workflow | Recovered behavior | Status | Managed replacement / remaining work |
| --- | --- | --- | --- |
| `MainForm` | MDI shell, New/Open/Save/Save As, Print/Preview/Page Setup, database/devices/scripts, sequence, login/privileges, front/back, field/record management | **Partial** | Open/save, print/preview/page setup, layout properties, side/field commands, Database/Records workspace, Security/operator workflows and a legacy VBScript workspace exist. Devices/sequence, automatic script lifecycle integration and exact privilege command gating remain. |
| `frmCarta` | Card editor: create/select fields, drag, measurements, rulers, front/back, photo, record-to-card, Campo.ini sync | **Partial** | Managed canvas, select/move/resize, mm geometry, grid/snap, insert/duplicate/delete, z-order, preview and runtime Record2Card binding exist. Full style/property byte mapping, tables, rulers and photo capture remain. |
| `Dimensioni` | Card dimensions | **Implemented** | Width/height/DPI layout properties dialog. |
| `frmPrinting` | Print progress/cancel shell | **Partial** | Standard Windows PrintDocument and record/batch paths exist; progress/cancel and device-specific pipeline remain. |
| `Sequenza` | Page imposition, rows/columns, margins, pitch, front/back, page navigation, print page/all | **Missing** | Required for sheet-imposition parity; database Print All currently emits one physical card per page/side. |
| `frmApri` | Job/layout open browser | **Missing** | Current app has standard file-open dialog only. |
| `frmJob` | Job/layout management | **Missing** | Create/delete/select job workflows not ported. |

## Data, records and database design

The supplied installer contains DAO 3.5/3.6 and Jet 3.5/4.0 components. Native strings also expose the original filters `Access (*.mdb)`, `Dbf`, `Excel`, CSV and text, plus `Operatori.FFM`. The managed layer therefore treats `.mdb/.ffm` as Jet/Access-compatible sources and uses ACE 16/12 first with Jet 4.0 as the legacy x86 fallback.

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| `Funzioni` | document CRUD, DB/report conversion, SQL, dataset access, variables, formatting, scripts, login/common utilities | **Partial** | Managed dataset open/query, Record2Card and operator-login exist. Script preprocessing/dispatch and the native 1024-slot variable table are now implemented, with proven `GetVariabile`/`SetVariabile`/`IncVariabile`/`Sostituisci` exposed to scripts. Document CRUD, formatting and the remaining script object surface remain. |
| `frmDatabase` | DB selection, tables/fields, SQL query/edit/save, print all | **Partial/strong** | Database/Records workspace opens Access/FFM/DBF/Excel/CSV/text, lists tables, runs SELECT and action SQL, browses records and prints one/all. ScriptControl now receives one shared `Db`/`frmDatabase` facade with recovered `RiempiTabelle` and exact `NometipoCampo` DAO type-name mapping. Exact legacy query/database metadata persistence, schema-design tabs and script-used control properties remain. |
| `FrmDati` | data loading/database creation | **Partial** | Data loading and record browsing exist. Database creation/import flow remains. |
| `Tabella` | record/table display and photo handling | **Partial/strong** | Managed record grid, first/previous/next/last navigation, binding, card preview and printing exist. ScriptControl now has `Tabella.Trovarecord()` backed by the same database/query/table state, and when the workspace is open the script compatibility layer observes the exact selected visible row. `Tabella.Carica`, script-driven visible navigation, editing/write-back and photo acquisition remain. |
| `frmTabelle` | table/schema creation and field typing | **Missing** | Need schema editor/create/alter table workflow. |
| `FormQBE` | query-by-example / SQL parameter builder | **Missing** | Direct SQL works; original QBE builder remains. |
| `db.exe` helper | open dataset, execute SQL/file, create/fill tables, import/export helpers | **Partial** | Dataset open, table browse, SQL query/action and several legacy source formats are absorbed into the managed data layer. Create/import/export/schema utilities remain. |
| database field binding | placeholders and `Record2Card`/`RecordToReport` | **Partial/strong** | Current rows substitute text/photo fields and managed field-to-column overrides are supported. Newly created bindings are persisted non-destructively in `.ly.data.json` until the exact legacy `Campo/DataField` byte offset is confirmed from a bound legacy fixture. |
| record/batch print | `StampaRecord`, `StampaTutti` | **Partial** | Current record and all loaded records can preview/print using the same card renderer. `Sequenza` sheet imposition remains. |

## Text, scripts, counters and barcode

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| `frmText` | text/memo editor, find/link | **Missing** | Need dedicated text editor and native-compatible field-link editing. |
| `frmCodice` | script/code tree, source editor, save and error-location UI | **Partial/strong** | Managed **Tools -> Legacy VBScript...** workspace discovers/opens/edits/saves `.vbs` and now compiles through the real `SostituisciRiga -> Interpretariga -> AddProg` path, including prompt/file/folder/computer interactions and the shared legacy variable table. Native tree organization and full error-438 line remapping remain. |
| `Funzioni.Vbscript` / `AddProg` / `AddObjects` | layout script lifecycle, object injection, macro preprocessing and event dispatch including `Load` | **Partial/strong** | All 20 native `AddObject` names, lifecycle/path rules, `PreparaCodice`, `SOSTITUZ.TXT`, `@()` / `$()` / interactive macros, streaming `ExecuteStatement` -> `AddCode`, direct ScriptControl `Run`, first-module/first-procedure `NO CODE` probe, exact first-Missing argument truncation, nested dispatch depth and deferred `Chiudimi -> Form_Unload -> Reset` are recovered/implemented and regression-tested. Proven facades currently cover `Me`, `Mainform`, shared `Db`/`frmDatabase`, `Carta`, `Tabella`, shared `Fn`/`Funzioni`, `File` and `App`; remaining objects and safe automatic layout execution remain. |
| `frmContatori` | counters, digits, create/delete/reset/update | **Missing** | Need persistent counter subsystem. |
| `frmBarcode` | barcode type/size selection | **Missing** | Need barcode renderer and field type mapping. |

## Images and acquisition

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| `frmImg` | image browser, LEADTOOLS editing, brightness/contrast, zoom, crop rectangle, scan | **Partial** | Image loading/rendering and database-supplied photo paths work; editing, crop, brightness/contrast and scanner acquisition remain. |
| `frmCapture` | capture source/format/compression/display and capture | **Missing** | Need WIA/TWAIN or modern acquisition adapter. |
| `SmartFormDll.Image` | scan, device, zoom, resolution, display, append pages | **Missing** | Must be replaced by managed image/acquisition service. |

## Printing and hardware

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| standard Windows print path | `MainForm.mnuFilePrint`, preview, page setup, `StampaRecord` | **Partial/strong** | Managed PrintDocument supports layout plus current/all database records; parity testing against real printers and cancellation/progress remain. |
| `frmDispositivi` | printer/device selection, add/remove/configure, generate/edit script | **Missing** | Need printer/device configuration UI and persisted profiles. |
| `smartDriver` | printer API version/model/serial/error, magstripe encode/read/print, cleaning, firmware, smart-card init | **Missing** | Need adapters per supported current hardware. |
| `frmTracce` | magnetic track configuration | **Missing** | Need Track 1/2/3 UI and encoding pipeline. |
| `frmMostraBanda` | magnetic stripe position display | **Missing** | Need stripe overlay/preview. |
| `Chip` | chip module form | **Missing** | Need smart-card adapter boundary and UI. |
| `SmartFormDll.Report` | `PrintPage`, `PrintGrid`, layout/field/record source, tracks | **Partial** | Managed card/record renderer covers core PrintPage behavior; PrintGrid/sequence and tracks remain. |
| `ICE_API` / printer wrappers | vendor/device API calls | **Missing** | Replace or isolate behind hardware adapter interfaces. |

## Operators, security and licensing

The original binary contains `Funzioni.CheckPassword`, `Funzioni.ChekPassword`, `Funzioni.Login`, `MainForm.PuoFare` and `MainForm.SetPrivilegi`, plus direct `Operatori` SQL for password, level, privilege and group. The exact command-gating algorithm is not yet claimed because no real legacy `Operatori.FFM` was supplied for side-by-side validation.

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| `frmLogin` | operator login | **Partial/strong** | Security menu, operator enumeration, masked-password login/logout and current-session display are implemented against the Jet/ACE `Operatori.FFM` path. Exact legacy password comparison/transformation and startup enforcement still require a real FFM/runtime comparison. |
| `frmPassword` | set/check password | **Partial/strong** | `/erasepw` compatibility plus interactive current-operator password change and administrator password reset are implemented. Exact legacy password storage/case semantics remain to validate. |
| `frmPrivilegi` | operator privileges | **Partial** | Managed operator CRUD exposes raw `Livello`, `Privilegio` and `Gruppo`; an optional privilege-description grid exists. Exact `PuoFare`/`SetPrivilegi` mapping to application commands is deliberately not guessed. |
| operator management | create/update/delete/list operators and groups | **Partial/strong** | CRUD is implemented using the recovered `Operatori` columns; native-confirmed missing `Privilegio` and `Gruppo` migrations are applied conservatively. A production FFM is still needed for write-compatibility validation. |
| `frmLic` | old machine-bound registration/license checks | **N/A** | New implementation should not require the obsolete UltraPrint 2003 license mechanism. Import of any license-derived configuration can be added only if functionally required. |

## Options and shell utilities

| Original object/workflow | Recovered behavior | Status | Remaining work |
| --- | --- | --- | --- |
| `Opzioni` | large options/configuration UI plus `Chiudimi` unload helper | **Missing/partial evidence** | `Chiudimi` is mapped and its script unload flag behavior is reproduced in the scripting layer; the remaining options/configuration UI must be rebuilt incrementally. |
| `File` | INI/files/log/archive/zip/encrypt/decrypt/path helpers | **Partial/strong** | Besides INI/asset handling, the script-facing `SoloExt`, `SoloNomeFile`, `SoloPath` and `Esiste` subset is now reproduced, including wildcard existence checks. Archive/encryption and remaining file utilities are not all ported. |
| `frmAbout` | About/system info | **Partial** | Basic About dialog exists; old system-info helper is unnecessary unless a workflow depends on it. |
| `frmWait` / `frmSplash` | wait/splash UI | **Missing** | Low-priority presentation parity. |
| `cLogo` | logo/gradient drawing helper | **Missing** | Cosmetic; low priority after functional parity. |

## Compatibility formats

| Format/configuration | Status | Notes |
| --- | --- | --- |
| `Campo.ini` | **Partial/strong** | ANSI/order-preserving reader/writer and `[$]` current-value semantics recovered. More consumers still need implementation. |
| `.ly` | **Partial/strong** | Dimensions, DPI and 64x264-byte field table partially decoded. Unknown bytes are preserved; insertion/duplication/deletion reuse raw templates. Global database/query and per-field binding offsets are deliberately not written until verified. |
| `.ly.data.json` | **Managed compatibility state** | Non-destructive sidecar for database path/query/table and newly created field bindings. It exists specifically to avoid corrupting unknown legacy `.ly` bytes and can be migrated once native offsets are confirmed. |
| `.vbs` / `Script` directories | **Partial/strong** | Root/application discovery, ScriptControl ProgID, 20 exposed names, lifecycle names, syntax normalization, `SOSTITUZ.TXT`, variable/INI/interactive macros, streaming AddProg execution, direct `Run`, first-Missing optional argument semantics, `NO CODE` and deferred unload mechanics are recovered. The script workspace uses this pipeline; automatic layout execution remains disabled pending broader object-facade coverage. |
| `UP.ini` | **Partial** | `/erasepw` `[Setup] Pw` compatibility implemented. Other settings remain. |
| `Operatori.FFM` | **Partial/strong** | Recovered lookup order is `Db\Operatori.FFM` then root fallback. Existing files open through ACE16/ACE12/Jet4; `Operatore`, `Password`, `Livello` are required and only the native-confirmed `Privilegio LONG` / `Gruppo TEXT(50)` migrations are auto-added. Managed creation/CRUD/login exist, pending validation against a real legacy FFM. |
| `Contatori.dat` | **Missing** | Supplied file is empty; counter persistence behavior must be recovered from code/runtime samples. |

## Definition of done

The replacement is considered iso-functional only when the following are true:

1. Existing customer `.ly`, `Campo.ini`, database and job data needed in production open without manual conversion.
2. Layout editing covers every field type/property used by real legacy layouts and round-trips without loss.
3. Record/database workflows can create, query, edit, bind and print data like the original program.
4. Script/counter/barcode workflows used by real installations have managed equivalents.
5. Print preview, direct print, sequence/batch printing and front/back workflows match legacy geometry.
6. Required current card printers, magnetic-stripe, chip and scanner devices work through explicit adapters.
7. Each migrated workflow is verified side-by-side against UltraPrint 2.2.115 using the same inputs and expected outputs.

This file is the parity checklist. A feature is not marked Implemented merely because a menu item or placeholder UI exists.