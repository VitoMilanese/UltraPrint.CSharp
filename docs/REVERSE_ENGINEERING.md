# Reverse-engineering notes

## Identification

- `UltraPrint.exe`: Visual Basic 6, Native Code, Win32/x86.
- Product version: 2.2.0.115 / UltraPrint 2.2.115.
- Native `Sub Main`: `0x005F7380`.
- 47 compiled VB objects, including 34 forms.
- 281 external API declarations recovered from VB6 metadata.
- `db.exe`: separate VB6 Native helper.
- `SmartFormDll.dll`: VB6 ActiveX/COM DLL with embedded type library.
- `CadBox.ocx` 2.0.0.0: VB6 ActiveX control recovered from the installer CAB.

All supplied executables were inspected statically; none was executed.

## Startup recovered from Sub Main

The native startup sequence visibly builds/uses:

- `Db\Operatori.FFM`
- legacy fallback `Operatori.FFM`
- `Campo.ini`
- `UP.ini`
- working directories `Db`, `Foto`, `Script`, `Ly`

It also performs the license check and recognizes:

- `/erasepw`
- `/Layout=<path>`
- `/printform`

## Corrected Campo.ini finding

`frmCarta.CampoToIni` is at `0x0053D790` and `IniToCampo` at `0x0053F7E0`.

The native helper used by `CampoToIni` calls the recovered `File.WriteIni` method (vtable `0x744`) with the effective arguments:

```text
section = "$"
key     = Group + "." + Key
value   = current field/editor value
file    = App.Path + "\\Campo.ini"
```

`Sub Main` initializes the corresponding global BSTR to `App.Path\Campo.ini`.

Therefore these methods persist the *current editor/field state* to `Campo.ini`; this is not evidence that `.ly` itself is INI.

## Campo.ini groups confirmed in native field synchronization

Observed names include:

- `Generale`: X, Y, Larghezza, Altezza, Nome, Livello, Campo
- `Testo`: Contenuto, Fisso, Campo, Font, Dimensione, Grassetto, Corsivo, Barrato and alignment state
- `Aspetto`: Opaco, Colore, Sfondo, Bordo, Spessore, Rotazione, ColoreBordo
- `Immagine`: File, Campo, Proporzioni, Originali, Estensione
- `Tabella`: Righe, Colonne, Sql and related state

The supplied `Campo.ini` also contains background/card size, DPI, DB/script settings, hardware modules, magnetic-stripe driver definitions, and per-driver configuration.

## Layout lifecycle routine

A large native routine at `0x00601EA0` is referenced by Open/Save/Save As and several layout/job forms. It receives the selected layout path and an optional second argument.

Confirmed behavior inside it includes:

1. normalize/copy current layout path;
2. obtain the layout basename using `File.SoloNomeFile` (vtable `0x73C`);
3. interact with `MainForm`, `frmCarta`, and `Funzioni`;
4. call `Funzioni.AddObjects` (vtable `0x7C0`);
5. call `Funzioni.AddProg` (vtable `0x848`);
6. call `Funzioni.Vbscript` (vtable `0x84C`) with the literal event name `Load`;
7. call `frmCarta.DisegnaCampi` (vtable `0x7DC`).

This shows a layout is tied to object construction and script lifecycle, not only static coordinates.

A real `TPMFAO19.ly` fixture was subsequently supplied. It proves the file is a fixed-layout binary rather than INI/XML. For UltraPrint 2.2.115 the sample exposes a 54 x 85 mm / 300 DPI header and a 64-slot field table at offset 2029 with 264-byte records. Confirmed record fields include name, legacy type, X/Y/width/height in VB6 twips, text/image payload, font, font size, `Testo.Fisso`, and level. Type codes 3/5/6 are confirmed as text/image/photo-placeholder for this fixture. The remaining header/footer and several flag ranges are still intentionally unclaimed.

## VBScript subsystem

The installer ships `MSSCRIPT.OCX`. Static strings in the supplied OCX expose the COM ProgID `MSScriptControl.ScriptControl`, matching the `ScriptControl1` member referenced by UltraPrint.

Recovered native `Funzioni` entries are:

- `LoadCodicePerTipo` — `0x004990E0`, vtable `0x7A0`
- `AddObjects` — `0x0049EE60`, vtable `0x7C0`
- `Interpretariga` — `0x004B1DF0`, vtable `0x840`
- `PreparaCodice` — `0x004B5800`, vtable `0x844`
- `AddProg` — `0x004B6420`, vtable `0x848`
- `Vbscript` — `0x004B7D00`, vtable `0x84C`

### AddObjects registry

`AddObjects` contains twenty calls equivalent to `ScriptControl.AddObject(name, object, True)`. The exact recovered names, in native order, are:

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

The object names are confirmed; their full callable member contracts are not. The managed rewrite therefore does not manufacture broad fake objects merely to suppress script errors.

### Script discovery and lifecycle names

`LoadCodicePerTipo` contains the literal path fragments `\Script\`, `.VBS`, `\App\`, `(Applicazione)` and `(Server)` and checks candidates through recovered `File.Esiste`. This supports a global `Script\<type>.VBS` path plus application/server-scoped variants under `App\<scope>\Script\...` when the relevant scope is available.

The directly observed lifecycle names include `OnLoad`, `Load`, `Main` and `Unload`. The global layout routine explicitly dispatches `Vbscript("Load")` before drawing fields.

The executable also contains `\Script\File.VBS` and `#endfile#`; their complete relationship to every legacy job/file workflow remains unresolved.

### PreparaCodice

`PreparaCodice` repeatedly invokes the recovered replacement helper before calling `Interpretariga`. Direct static argument recovery confirms transformations that strip `Private`/`Public`, several VB6 `As <type>` declarations, `MSComctlLib.Node/ListItem` declarations, normalize `Form_Unload(Cancel)`, replace the native-spaced `Unload Me` form with `Chiudimi`, restore commented `Me.`/`Fn.` prefixes, and remove the literal `'-'` marker.

The native replacement literals include their original spacing. The managed compatibility preprocessor preserves those literals instead of broadening the rules beyond what was observed. `Interpretariga` performs further processing but is not fully decoded yet.

### AddProg and error reporting

`AddProg` visibly references Script Control `ExecuteStatement`, `AddCode`, `Error`, `Description`, `Line` and `Column`. It detects `sub`/`function` text, calls `PreparaCodice`, and uses `frmCodice` RichTextBox members including `SelStart`, `Find`, `GetLineFromChar`, `SelBold`, `SelColor`, `SelItalic` and `SelLength` while reporting `Errore nello script ...`.

This is sufficient to reproduce a conservative script editor/diagnostics boundary, but not yet enough to claim the exact native division of top-level statements versus procedure blocks.

The managed implementation and its deliberate execution restrictions are documented in `SCRIPT_COMPATIBILITY.md`.

## CadBox.ocx

The installer CAB was successfully decoded (MSZIP CAB) and `CadBox.ocx` extracted. It is itself VB6 Native Code and exposes two user controls/objects in recovered metadata:

### CadBox

Recovered named members include:

- `BoxOn`
- `DrawRectangle`
- `StepGrid` (property pair)
- `Tipo` (property pair)
- `GetObject`

### PropertyTool

Recovered named members include:

- `AddProprieta`
- `RemoveProprieta`
- `BackColor`, `ForeColor`, `Enabled`, `Font`, `BackStyle`, `BorderStyle`
- `Id`, `Table`, `Databasename`
- `GetExtraProprierties`
- `SetMember`
- `SetExtraProprierties`
- `Setup`
- `GetObject`
- `ClearProprieta`
- `GetProprieta`
- `SaveObject`
- `LoadObject`
- `RefreshAll`
- `Loadproprieta`

The UltraPrint `frmCarta` contains `CadBox1` but no statically declared `PropertyTool` control, so the existence of `SaveObject`/`LoadObject` in the second control alone is not sufficient to label `.ly` as a CadBox format.

## Migration rule

Only behavior proven from binary metadata/native flow or from a real legacy data sample should be encoded as compatibility behavior. Unproven format assumptions stay behind explicit interfaces until verified.
