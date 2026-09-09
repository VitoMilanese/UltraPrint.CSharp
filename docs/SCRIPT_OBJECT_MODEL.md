# Recovered ScriptControl object model

This file records the object identities behind native `Funzioni.AddObjects` in UltraPrint 2.2.115. The twenty injected names are known and most names are correlated with the exact VB6 instance supplied to `ScriptControl.AddObject`.

The goal is to build managed facades from proven callable contracts rather than registering broad placeholders that only hide missing behavior.

## Native AddObjects source mapping

| Injected name | Native source | Notes |
| --- | --- | --- |
| `Me` | caller object passed to `Funzioni.AddObjects` | not a fixed singleton; native AddObjects receives it as an argument |
| `Mainform` | global `MainForm` | object info `0x0041D418` |
| `Preview` | global `frmPrinter` | object info `0x0040E1A8` |
| `Db` | global `frmDatabase` | aliases the same instance also injected as `frmDatabase` |
| `Sequenza` | global `Sequenza` | object info `0x00422D00` |
| `Stampa` | global `Sequenza` | native code injects the same `Sequenza` instance under a second name |
| `frmDatabase` | global `frmDatabase` | same instance as `Db` |
| `Carta` | global `frmCarta` | object info `0x0041B528` |
| `Chip` | global `Chip` | object info `0x004103B0` |
| `Tabella` | global `Tabella` | object info `0x004128E8` |
| `frmlogin` | global `frmLogin` | object info `0x00412E8C` |
| `Fn` | global `Funzioni` | global instance pointer `0x0062B2C8`, object info `0x0041751C` |
| `Funzioni` | global `Funzioni` | same exact singleton as `Fn` |
| `File` | global `File` | object info `0x00411DE4` |
| `SmartDriver` | global `smartDriver` | object info `0x00418BD4` |
| `Dispositivi` | global `frmDispositivi` | object info recovered from AddObjects allocation path |
| `Printer` | VB6 intrinsic Printer object | obtained through VB runtime rather than a project class |
| `Screen` | VB6 intrinsic Screen object | obtained through VB runtime rather than a project class |
| `ClipBoard` | VB6 intrinsic Clipboard object | obtained through VB runtime rather than a project class |
| `App` | VB6 intrinsic App object | obtained through VB runtime rather than a project class |

`AddObject(..., True)` is used for all twenty names, so members are also made available through Script Control's AddMembers behavior. Alias identity matters: `Db`/`frmDatabase`, `Fn`/`Funzioni`, and `Sequenza`/`Stampa` must not silently become independent managed state objects.

The `Fn`/`Funzioni` alias has been rechecked directly in the native `AddObjects` body: both name blocks resolve global `0x0062B2C8` and allocate from object info `0x0041751C`. The managed registration therefore deliberately injects the same `LegacyScriptFunctionsFacade` instance under both names.

## Public member inventory recovered from VB6 metadata

These names are evidence of the original callable surface, not a claim that every signature/side effect is decoded.

### `Mainform` / `MainForm`

Recovered public names include `StampaRecord`, `CaricaSfondo`, `SetButton`, `SetCoordinate`, `AdattaCarta`, `PrinterEscape`, `Termina`, `PuoFare`, `SetPrivilegi`, `GestioneRecord`, `GestioneSequenza`, `GestioneLato`, `GestioneCampo` and `VersioneDemo`, plus menu/toolbar handlers.

### `Preview` / `frmPrinter`

No trustworthy project-public method names were recovered from the VB object metadata. Its standard form/control properties may still be used by scripts and must be validated from real script samples or native call sites.

### `Db` / `frmDatabase`

Recovered names include `RiempiTabelle`, `NometipoCampo`, `cmdQuery_Click`, `txtSql_DblClick` and form events.

### `Sequenza` / `Stampa`

Recovered names include `Pescarecord`, `PosizionaPagina`, `ScriviSetup`, `LeggiSetup`, `StampaPagina_Click`, `StampaTutte_Click`, front/back/page controls, row/column/margin/pitch handlers and sequence form events.

### `Carta` / `frmCarta`

Recovered public names are `Marcatutti`, `SelectField`, `NuovoCampo`, `LabelToCampo`, `CampoToLabel`, `CampoToIni`, `IniToCampo`, `DisegnaCampi`, `AggiornaMisure`, `SetupCampo`, `SetButton`, `DisegnaRighello`, `AggiornaMarcatori`, `FaiFoto`, `AggiornaBottoni` and `Record2Card`.

Native stdcall cleanup confirms the following explicit argument counts:

| Method | Native VA | Explicit args |
| --- | ---: | ---: |
| `Marcatutti` | `0x0052BB40` | 0 |
| `SelectField` | `0x0052CC50` | 2 |
| `NuovoCampo` | `0x0052E5D0` | 1 |
| `LabelToCampo` | `0x0053A670` | 2 |
| `CampoToLabel` | `0x0053BA20` | 1 |
| `CampoToIni` | `0x0053D790` | 1 |
| `IniToCampo` | `0x0053F7E0` | 1 |
| `DisegnaCampi` | `0x00541FB0` | 0 |
| `AggiornaMisure` | `0x00549930` | 0 |
| `SetupCampo` | `0x0054B050` | 1 |
| `SetButton` | `0x0054E660` | 2 |
| `DisegnaRighello` | `0x0054EA20` | 0 |
| `AggiornaMarcatori` | `0x00550A30` | 4 |
| `FaiFoto` | `0x00551B20` | 1 |
| `AggiornaBottoni` | `0x005534A0` | 0 |
| `Record2Card` | `0x00553FA0` | 1 |

`NuovoCampo` also exposes a native field-type switch. Confirmed values in this build are `2=Rettangolo`, `3=Testo`, `4=Barcode`, `5=Immagine`, `7=Twain`, `8=Telecamera`, `9=BandaMagnetica`, `10=SmartCara` (literal spelling in the executable) and `13=Tabella`. The supplied production `.ly` fixture additionally proves type `6` is an image/photo-style record. Other numeric values remain unknown rather than being guessed.

The native field number used by these `frmCarta` methods is one-based. `NuovoCampo` scans a zero-based free slot, then increments it before storing/using the public current-field number. The managed `.ly` model stays zero-based, so script-facing `Carta` calls translate `1 -> managed slot 0`, `2 -> slot 1`, etc.

The managed `LegacyScriptCartaFacade` now exposes the strongly decoded subset `NuovoCampo`, `CampoToIni`, `IniToCampo`, `SetupCampo`, `DisegnaCampi`, `AggiornaMisure` and `AggiornaBottoni`. `CampoToIni` / `IniToCampo` exchange the recovered `[$]` flattened `Campo.ini` values for geometry, name, text/font/alignment, image, appearance and table settings. The live WinForms script workspace injects this facade against the currently open card canvas, so explicit legacy script execution can mutate the same `CardLayout` the editor is showing.

Methods with still-uncertain argument meaning (`SelectField`, `LabelToCampo`, `SetButton`, `AggiornaMarcatori`, `FaiFoto`, `Record2Card`) are deliberately not exposed yet.

### `Chip`

No trustworthy project-public names were recovered beyond form events. The hardware behavior must be derived from native calls and real device workflows.

### `Tabella`

Recovered project-public names are `Carica` and `Trovarecord`.

### `frmlogin`

No trustworthy project-public method names were recovered. Standard form/control members remain possible.

### `Fn` / `Funzioni`

Recovered surface includes document/database helpers, `GetInside`, `Parola`, `Sostituisci`, `GetVariabile`, `SetVariabile`, `IncVariabile`, `Interpretariga`, `PreparaCodice`, `AddProg`, `Vbscript`, `GetComputerName`, `GetUserName`, conversion helpers, login/common UI helpers and 3D drawing helpers.

The managed facade currently exposes only the strongly decoded subset: `GetVariabile`, `SetVariabile`, `IncVariabile` and binary/case-sensitive `Sostituisci`. Both injected names point to this one shared facade object and one shared 1024-slot legacy variable table.

### `File`

Recovered surface includes `Appendi`, `Log`, `Scrivi`, `ApriFile`, `GetIni`, `GetFile`, `Compatta`, `LogErrori`, `StartDocument`, `SoloExt`, `SoloNomeFile`, `SoloPath`, `WriteIni`, `Esiste`, file operations, archive/encryption helpers and list/file conversion helpers.

Current managed facade exposes the native-confirmed `GetIni`, `WriteIni`, `SoloExt`, `SoloNomeFile`, `SoloPath` and `Esiste` subset. `Esiste` includes normal and Dir-style wildcard existence checks. Static analysis of this build's `ApriFile` shows its two formal arguments are unused and its filter is hard-coded to `Tutti i File|*.*`; that behavior is currently implemented at the WinForms interaction boundary used by `@GETFILE(...)` rather than exposed as a general File facade method.

### `SmartDriver`

Recovered public names are `EncodeMagStripeWithApi` and `StampaRecord`.

### `Dispositivi`

Recovered names include `Comando` and printer/device configuration event handlers. Exact script-facing semantics are not yet claimed.

## Current managed facade slice

The script workspace registers the following proven objects before loading code, preserving their native relative order:

1. `Me` -> `LegacyScriptHostFacade`, exposing `Chiudimi()` so `Unload Me -> Chiudimi` reaches the deferred native-style unload state machine;
2. `Carta` -> `LegacyScriptCartaFacade` when a live managed layout is available;
3. `Fn` -> shared `LegacyScriptFunctionsFacade`;
4. `Funzioni` -> the same shared `LegacyScriptFunctionsFacade` instance;
5. `File` -> `LegacyScriptFileFacade`;
6. `App` -> `LegacyScriptAppFacade`.

The `Funzioni` facade uses the same `LegacyScriptVariableTable` instance passed into `Interpretariga`, so `@()` macro expansion and script calls to `Fn.GetVariabile` / `Funzioni.SetVariabile` observe one state just as the VB6 singleton did.

These facade classes are COM-visible AutoDispatch classes for compatibility with Microsoft Script Control. Unproven names are intentionally left unavailable instead of being populated with empty or misleading stubs.

## Next facade order

The most useful next targets are now:

1. `Mainform` — expose only proven workflow actions and properties;
2. `Db` / `frmDatabase` and `Tabella` — bridge record/database state while preserving alias identity;
3. `Sequenza` / `Stampa` — bridge the page-imposition/print workflow while sharing identity;
4. `Printer`, `Screen`, `ClipBoard` — replace only intrinsic members observed in real workflows;
5. device-specific `SmartDriver`, `Dispositivi`, `Chip` after the standard workflow is stable;
6. finish the remaining `Carta` methods only as their argument semantics are proved.

A representative production `.vbs` remains the best evidence for deciding which members from the large metadata surface are actually required.
