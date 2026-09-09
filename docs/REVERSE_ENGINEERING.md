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
