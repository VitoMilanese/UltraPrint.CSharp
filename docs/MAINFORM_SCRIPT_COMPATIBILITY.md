# MainForm script compatibility

UltraPrint 2.2.115 exposes the global VB6 `MainForm` instance to Microsoft Script Control under the name `Mainform` with `AddMembers=True`. This document records the native ABI recovered from the original executable and the subset that is currently safe to expose in the C# replacement.

## Recovered public ABI

The cleanup size of each native VB6 procedure was checked against known functions/subs. For VB6 instance methods the hidden object pointer consumes 4 bytes; functions also carry a hidden result pointer. This gives the following explicit argument counts.

| Member | Native VA | Explicit args | Return | Managed status |
| --- | ---: | ---: | --- | --- |
| `StampaRecord` | `0x005B3830` | 0 | Sub | exposed |
| `CaricaSfondo` | `0x005B5650` | 0 | Sub | exposed |
| `SetButton` | `0x005B8490` | 2 | Sub | ABI only; argument meaning still unresolved |
| `SetCoordinate` | `0x005B8850` | 1 | Sub | ABI only; argument meaning still unresolved |
| `AdattaCarta` | `0x005B8B50` | 0 | Sub | behavior still being decoded |
| `PrinterEscape` | `0x005B9150` | 2 | Function | ABI only; return type/semantics still unresolved |
| `Termina` | `0x005B9450` | 0 | Sub | exposed |
| `PuoFare` | `0x005B9AB0` | 1 | Function | ABI confirmed; privilege semantics deliberately not guessed |
| `SetPrivilegi` | `0x005BA8B0` | 0 | Sub | privilege-action mapping still being decoded |
| `GestioneRecord` | `0x005BC580` | 0 | Variant Function | ABI confirmed; script-state return semantics unresolved |
| `GestioneSequenza` | `0x005BDF00` | 0 | Variant Function | ABI confirmed; script-state return semantics unresolved |
| `GestioneLato` | `0x005BECE0` | 6 | Sub | strong ABI; parameter semantics unresolved |
| `GestioneCampo` | `0x005BFDB0` | 6 | Sub | strong ABI; delegates six arguments to the central field helper |
| `VersioneDemo` | `0x005BFE30` | 0 | Sub | behavior not required for compatibility licensing |

Additional native evidence:

- `PuoFare` zeroes the hidden result storage at entry and accepts one explicit input. Its body queries `Privilegi` (`Privilegio`, `Descrizione`) and is therefore a real permission-check function, not a two-argument Sub.
- `GestioneRecord` and `GestioneSequenza` copy a full 16-byte Variant into hidden result storage before returning. Both are zero-argument functions.
- `GestioneCampo` reads six explicit arguments and forwards all six to the common native helper at `0x0060A090`.
- `GestioneLato` also has six explicit arguments and contains the `Sfondo` / `Fronte` / `Retro` side workflow before invoking `GestioneCampo`.
- `SetPrivilegi` references the original action names `Smartcrd`, `Print`, `Modificare`, `Save`, `Editare`, `Fotografare`, `Disegna`, `Database`, `Tablewiz`, `Portrait`, `Landscape`, `Disegnare`, `Properties`, `PrintList`, `PrintForm`, `Amministrare`, `Programmare`, `Privilegi`, and `Stampare`. Their numeric privilege mapping still needs production DB/runtime evidence before command gating is enabled.

## Current managed facade

`LegacyScriptMainFormFacade` is COM-visible and is registered under exactly `Mainform`, immediately after `Me` in the relative recovered `AddObjects` order. It currently exposes only behavior that can be mapped without inventing argument semantics:

- `Mainform.StampaRecord()` — performs a direct print of the current managed card through the standard `PrintDocument` renderer without opening the normal Print dialog. Device-specific smart-card/magnetic/card-printer behavior remains a later compatibility layer.
- `Mainform.CaricaSfondo()` — invalidates the live card image/background cache and redraws the current card. The managed layout already owns decoded front/back background state, so no detached VB6 control state is created.
- `Mainform.Termina()` — defers closing the live main window until the current ScriptControl callback has unwound.

The facade is backed by the same live `MainForm`/`LayoutCanvas` opened by the user. It is not a disconnected script-only model.

## Deliberately not exposed yet

ABI recovery alone is not treated as functional parity. `SetButton`, `SetCoordinate`, `AdattaCarta`, `PrinterEscape`, `PuoFare`, `SetPrivilegi`, `GestioneRecord`, `GestioneSequenza`, `GestioneLato`, `GestioneCampo`, and `VersioneDemo` stay unavailable through the COM facade until their parameter meanings and side effects are sufficiently decoded. This avoids making old scripts appear to work while silently doing the wrong thing.

The next script-object target is `Db` / `frmDatabase` together with `Tabella`, because those objects must share the live record/query state already implemented by the managed database workspace.
