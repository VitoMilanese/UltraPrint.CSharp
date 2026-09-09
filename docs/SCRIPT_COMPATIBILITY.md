# Legacy VBScript compatibility

UltraPrint 2.2.115 embeds a real VBScript subsystem. This document separates what is directly recovered from the native VB6 executable from the managed compatibility behavior that has now been implemented.

## Native evidence

The installer ships `MSSCRIPT.OCX`. Its embedded registration strings expose the ProgID `MSScriptControl.ScriptControl`, matching the `ScriptControl1` member referenced by the UltraPrint executable.

Recovered `Funzioni` methods:

| Method | Native address | vtable |
| --- | ---: | ---: |
| `LoadCodicePerTipo` | `0x004990E0` | `0x7A0` |
| `AddObjects` | `0x0049EE60` | `0x7C0` |
| `Interpretariga` | `0x004B1DF0` | `0x840` |
| `PreparaCodice` | `0x004B5800` | `0x844` |
| `AddProg` | `0x004B6420` | `0x848` |
| `Vbscript` | `0x004B7D00` | `0x84C` |

The recovered layout-loading path calls `AddObjects`, then `AddProg`, then `Vbscript("Load")`, before `frmCarta.DisegnaCampi`. `LoadCodicePerTipo` also contains the lifecycle names `OnLoad`, `Load` and `Main`; the generic `Vbscript` dispatcher contains special `Unload`/`Form_Unload` handling.

### Objects exposed by `AddObjects`

The executable makes twenty `ScriptControl.AddObject(name, object, True)` calls. The recovered names, in native order, are:

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

The names are confirmed. The complete callable member surface behind each legacy object is **not** yet considered recovered, so the managed rewrite does not fabricate fake members simply to make scripts appear to run.

### Script paths

`LoadCodicePerTipo` constructs paths from the literal fragments:

- `\Script\`
- `.VBS`
- `\App\`
- `(Applicazione)`
- `(Server)`

and verifies candidates through the recovered `File.Esiste` method. The directly supported managed path contract is therefore:

- `<UltraPrint root>\Script\<type>.VBS`
- `<UltraPrint root>\App\<application>\Script\<type>.VBS` when an application name is known
- the equivalent server-scoped form only when a server name is actually available

The supplied `Campo.ini` contains `Sfondo.Applicazione` / “Nome dell'applicazione VBScript”, but the supplied sample has that value empty.

A literal fallback `\Script\File.VBS` and `#endfile#` marker also exist in native file/script helper code. Their exact role in every job format is still being recovered.

### `PreparaCodice` normalization

Native `Funzioni.PreparaCodice` repeatedly calls the recovered string replacement helper. The following transformations are directly visible and are now reproduced by `LegacyScriptCodePreprocessor`:

- remove `Private ` and `Public `
- remove VB6 type suffixes `As Integer`, `Long`, `Variant`, `Date`, `Double`, `Currency`, `Boolean`, `Object`, `Any`
- remove `As MSComctlLib.Node` and `As MSComctlLib.ListItem`
- ` Form_Unload(Cancel)` -> ` Form_Unload()`
- ` Unload Me` -> ` Chiudimi`
- `'Me.'` -> `Me.`
- `'Fn.'` -> `Fn.`
- remove the literal marker `'-'`

The leading spaces shown above are part of the native search literals. The managed compatibility test therefore preserves that spacing rather than silently broadening the rule.

`PreparaCodice` then calls `Interpretariga`. Those additional transformations are not fully decoded yet, so the managed preprocessor deliberately labels itself as a confirmed subset rather than claiming perfect source translation.

### `Interpretariga` token evidence

The full native method spans `0x004B1DF0` to `0x004B57E9` and returns through a Variant-style output slot. Direct BSTR references inside it expose the following parser tokens:

- `@(`
- `$(`
- `?(`
- `@GETFILE(`
- `@DIRECTORY(`
- `@COMPUTER(`
- closing `)`
- separators `,;` and `;,`
- path/config fragments `.`, `\`, `.Ini`, `\App\`, `$`

The method calls recovered helpers including `GetInside`, `Parola`, `GetVariabile`, `SetVariabile`, `Sostituisci`, `File.GetIni` and `File.WriteIni`. The `@GETFILE` / `@DIRECTORY` / `@COMPUTER` branches are additionally guarded by the method's second Boolean-like argument. These facts prove that `Interpretariga` is more than cosmetic VB6 syntax stripping: it expands/interprets UltraPrint-specific script/config macros.

The exact input grammar and output/side effects for each token are **not** yet claimed. No managed macro expansion is enabled until the surrounding branches are decoded well enough to avoid changing production script meaning.

### `AddProg`

Static analysis confirms that `AddProg`:

- detects `sub` and `function` text;
- calls `PreparaCodice`;
- uses Script Control `ExecuteStatement` and `AddCode`;
- reads `ScriptControl.Error.Description`, `.Line` and `.Column`;
- builds the native message prefix `Errore nello script `;
- drives `frmCodice`/RichTextBox selection properties (`SelStart`, `Find`, `GetLineFromChar`, `SelBold`, `SelColor`, `SelItalic`, `SelLength`) to locate a failing source line.

The exact split between top-level `ExecuteStatement` lines and blocks sent to `AddCode` is still being mapped.

## Managed implementation

The current branch adds:

- `LegacyScriptContract` with the recovered object and lifecycle names;
- `LegacyScriptPathResolver` for the confirmed global/application script-directory convention;
- `LegacyScriptCodePreprocessor` containing only directly observed transformations;
- `ILegacyScriptEngine` / `LegacyScriptSession` as the managed execution boundary, with registered objects re-injected after reset and before code loading to preserve `AddObjects -> AddProg` ordering;
- optional `MsScriptControlEngine`, using the exact legacy `MSScriptControl.ScriptControl` COM ProgID when registered;
- a WinForms **Tools -> Legacy VBScript...** workspace with discovery, source editing, save/save-as, prepared-code preview, compile and explicit lifecycle invocation;
- Script Control error description/line/column mapped back to the editor caret.

Normal UltraPrint functionality does **not** depend on `MSSCRIPT.OCX`. If the COM component is absent or registered only for the other process architecture, the script editor and recovery tools still work and report the engine as unavailable.

## Execution boundary

The original application executes legacy scripts as part of layout lifecycle. The rewrite currently does **not** auto-run arbitrary `.vbs` files merely because a layout was opened.

Execution must be enabled explicitly for the current script-workspace session, and the first execution request displays a trust warning. This is intentional while the object model is incomplete and while production legacy scripts have not yet been validated side-by-side.

The twenty `AddObjects` names are displayed in the workspace, but managed facade objects are not registered yet. That avoids silently presenting incorrect APIs. A script that only uses intrinsic VBScript can be compiled/run when Script Control is installed; a script depending on UltraPrint objects may fail until the corresponding facade contract is recovered.

## Remaining work

1. Recover `Interpretariga` completely, including the exact `@(` / `$(` / `?(` and `@GETFILE` / `@DIRECTORY` / `@COMPUTER` macro semantics.
2. Finish the exact `AddProg` top-level statement / procedure-block loading algorithm.
3. Recover `Vbscript` argument mapping and event-name normalization for event families beyond the confirmed lifecycle names.
4. Map each of the twenty `AddObjects` names to the exact callable members actually used by production scripts.
5. Implement managed facade objects incrementally from proven behavior.
6. Validate `OnLoad -> Load -> Main` and unload behavior with a real customer `.vbs`/layout pair.
7. Decide whether a 32-bit compatibility host is needed for installations where the legacy OCX exists only in the x86 COM registry.

Until those points are proven, scripting is correctly marked **Partial**, not Implemented.
