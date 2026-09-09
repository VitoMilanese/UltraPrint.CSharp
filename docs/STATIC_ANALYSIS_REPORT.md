# UltraPrint 2.2.115 — static reverse-engineering report

## Scope and safety
The supplied EXE/DLL/OCX files were inspected statically. No supplied executable or ActiveX component was run.

## Identification
- `UltraPrint.exe`: Visual Basic 6, Native Code, Win32/x86.
- Version: `2.2.0.115` (`UltraPrint - 2.2.115`).
- Installer entry timestamp: 2003-05-16 18:55:04.
- `db.exe`: Visual Basic 6 Native Code database/helper utility.
- `SmartFormDll.dll`: Visual Basic 6 Native ActiveX/COM DLL with embedded `TYPELIB`.
- `CadBox.ocx` 2.0.0.0: Visual Basic 6 Native ActiveX control, recovered by decoding the installer CAB.

## Main executable structure
Recovered from VB6 metadata:
- 47 compiled VB objects.
- 34 forms.
- 281 external API declarations.
- Native `Sub Main` at VA `0x005F7380`.

Representative objects:
- `File` — INI/files/logging/archive helpers.
- `Funzioni` — documents, DB/report logic, SQL, VBScript, login/common utilities.
- `MainForm` — main MDI UI, open/save/print/layout lifecycle.
- `frmCarta` — card/layout editor.
- `Sequenza` — page/print sequence settings.
- `frmDatabase`, `frmDispositivi`, `frmImg`, `frmLic`.
- `ICE_API`, `VBCryptoAPI`, `WinAPI` and other Win32 wrappers.

## Startup flow recovered from native `Sub Main`
Confirmed startup behavior includes:
1. Build paths relative to `App.Path` for `Db\\Operatori.FFM`, legacy `Operatori.FFM`, and `Campo.ini`.
2. Log/create/check the operators database.
3. Ensure working directories including `Db`, `Foto`, `Script`, and `Ly`.
4. Run licensing verification through `frmLic`.
5. Handle `UP.ini`, including `[Setup] Pw` and `/erasepw` behavior.
6. Recognize `/Layout=` and `/printform` automation switches.

## Important correction: Campo.ini is not the .ly file
A deeper native-code pass disproved the early hypothesis that `frmCarta.CampoToIni` / `IniToCampo` directly serialize `.ly`.

Confirmed native methods:
- `frmCarta.CampoToIni` — `0x0053D790`, vtable `0x7D4`.
- `frmCarta.IniToCampo` — `0x0053F7E0`, vtable `0x7D8`.
- `frmCarta.SetupCampo` — `0x0054B050`, vtable `0x7E4`.

The helper used by `CampoToIni` calls the recovered `File.WriteIni` method (vtable `0x744`) effectively as:

```text
WriteIni("$", Group + "." + Key, Value, App.Path + "\\Campo.ini")
```

`Sub Main` initializes the filename global to `App.Path\\Campo.ini`. `SetupCampo(CampoCorrente)` logs `Campo su Ini <index>` and synchronizes the selected field through `CampoToIni` / `IniToCampo`.

Thus the `[$]` section in `Campo.ini` is a flattened **current editor/field state store**, not proof of the `.ly` on-disk format.

## Campo.ini — confirmed roles
The supplied `Campo.ini` contains:
- `< ... >` UI/script preamble;
- schema-like field/background/table sections;
- current flattened values in `[$]` (`Group.Key=Value`);
- card background dimensions and DPI;
- DB/script settings;
- hardware module definitions;
- magnetic-stripe driver strings and per-driver configuration.

Native field sync confirms groups/keys including:
- `Generale`: X, Y, Larghezza, Altezza, Nome, Livello, Campo.
- `Testo`: Contenuto, Fisso, Campo, Font, Dimensione, Grassetto, Corsivo, Barrato, alignment.
- `Aspetto`: Opaco, Colore, Sfondo, Bordo, Spessore, Rotazione, ColoreBordo.
- `Immagine`: File, Campo, Proporzioni, Originali, Estensione.
- `Tabella`: Righe, Colonne, Sql and related state.

## .ly layout lifecycle and binary format — partially recovered
A real `TPMFAO19.ly` fixture was later supplied separately from the original application/installer archives.

A central native routine at `0x00601EA0` is called by MainForm Open/Save/SaveAs and several job/layout workflows. Confirmed behavior includes:
- normalize/copy the chosen layout path;
- derive layout basename via `File.SoloNomeFile` (vtable `0x73C`);
- interact with `MainForm`, `frmCarta`, and `Funzioni`;
- `Funzioni.AddObjects` (vtable `0x7C0`);
- `Funzioni.AddProg` (vtable `0x848`);
- `Funzioni.Vbscript` (vtable `0x84C`) with literal event name `Load`;
- `frmCarta.DisegnaCampi` (vtable `0x7DC`).

On save, sequence support can additionally call `Sequenza.ScriviSetup` (vtable `0x7B4`).

This proves layout lifecycle includes object/script state and is not just a coordinate INI. The supplied fixture also allowed a first byte-level decode: header `Single` height/width plus `Int16` DPI; a 64-slot field table at offset 2029; 264 bytes per slot; and confirmed name/type/twip geometry/payload/font/font-size/fixed/level fields. The remaining binary ranges are still unresolved and are preserved byte-for-byte by the C# writer.

## Selected recovered native methods
### File
- `GetIni` `0x0048DDF0` / vtable `0x720` — params: `Sezione, Variabile, Default, Filename`.
- `SoloNomeFile` `0x00490540` / `0x73C`.
- `WriteIni` `0x00490BF0` / `0x744` — params: `Appname, KeyName, keydefault, Filename`.

### Funzioni
- `AddObjects` `0x0049EE60` / `0x7C0`.
- `CaricaDocumento` `0x004A0600` / `0x7C4`.
- `ExecuteSql` `0x004A23D0` / `0x7E8`.
- `Opendataset` `0x004A65C0` / `0x814`.
- `AddProg` `0x004B6420` / `0x848`.
- `Vbscript` `0x004B7D00` / `0x84C`.

### frmCarta
- `LabelToCampo` `0x0053A670` / `0x7CC`.
- `CampoToLabel` `0x0053BA20` / `0x7D0`.
- `CampoToIni` `0x0053D790` / `0x7D4`.
- `IniToCampo` `0x0053F7E0` / `0x7D8`.
- `DisegnaCampi` `0x00541FB0` / `0x7DC`.
- `SetupCampo` `0x0054B050` / `0x7E4`.
- `Record2Card` `0x00553FA0` / `0x7FC`.

## CadBox.ocx
The MSZIP cabinet was decoded and all 73 installer payload files extracted for analysis.

`CadBox.ocx` is VB6 Native. Its recovered objects include:

### CadBox
- `BoxOn`
- `DrawRectangle`
- `StepGrid` property pair
- `Tipo` property pair
- `GetObject`

### PropertyTool
- `AddProprieta`, `RemoveProprieta`
- `BackColor`, `ForeColor`, `Enabled`, `Font`, `BackStyle`, `BorderStyle`
- `Id`, `Table`, `Databasename`
- `GetExtraProprierties`, `SetMember`, `SetExtraProprierties`
- `Setup`, `GetObject`, `ClearProprieta`, `GetProprieta`
- `SaveObject`, `LoadObject`, `RefreshAll`, `Loadproprieta`

UltraPrint `frmCarta` statically contains `CadBox1` but not `PropertyTool`, so `PropertyTool.SaveObject/LoadObject` alone is insufficient evidence that `.ly` is a CadBox file.

## Important installer dependencies
- LEADTOOLS 10 / `LTOCX10N.OCX`.
- `CadBox.ocx` 2.0.
- classic VB6 controls (`MSCOMCTL`, `MSCOMCT2`, `COMCT332`, `MSFLXGRD`, `DBGRID32`, `COMDLG32`).
- `MSSCRIPT.OCX`.
- DAO 3.5/3.6 + Jet 3.5/4.0 and ISAM drivers.
- card-printer / WinSpool / GDI / TWAIN / hardware APIs.

## C# migration started
A first .NET 8 solution has been generated with:
- `UltraPrint.Core` — managed domain models.
- `UltraPrint.Legacy` — `Campo.ini`, startup compatibility, `.ly` probe, and safe partial UltraPrint 2.2.115 codec.
- `UltraPrint.WinForms` — front/back layout editor/preview with field selection, properties and drag-move.
- `UltraPrint.RecoveryCli` — repeatable command-line probes.

The `.ly` codec is now deliberately partial rather than unsupported: it patches only byte ranges confirmed from the real fixture and leaves all unresolved bytes unchanged.

## Hashes
- `UltraPrint.exe`: `cc79cfce8a2d002b2ecc93e38cc5acf8915ed3ed82171a6cdc1f267d32963b22`
- `db.exe`: `403f0e9e25672164fb2dddeba964f8bb33f33818c725e29d4a16ed2acaa48f2a`
- `SmartFormDll.dll`: `c351f593e4336b4a7631c54d0fe746e4bb73ad18945be2fe43d7aad7d243322b`
- `UltraPrint.CAB`: `0db04053189b6c49e1a0734f8bf7a0577e82e263f9653c0b03a7f918dbc90a31`
