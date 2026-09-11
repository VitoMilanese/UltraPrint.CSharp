# MainForm native ABI notes

The script-visible `Mainform` object is the original VB6 MDI `MainForm` singleton. Native procedure cleanup and result-storage patterns recover the following ABI facts without guessing parameter meanings:

- `StampaRecord` `0x005B3830`: Sub, 0 explicit arguments (`ret 4`).
- `CaricaSfondo` `0x005B5650`: Sub, 0 explicit arguments (`ret 4`).
- `SetButton` `0x005B8490`: Sub, 2 explicit arguments (`ret 12`, both inputs consumed).
- `SetCoordinate` `0x005B8850`: Sub, 1 explicit argument (`ret 8`).
- `AdattaCarta` `0x005B8B50`: Sub, 0 explicit arguments (`ret 4`).
- `PrinterEscape` `0x005B9150`: Function, 2 explicit arguments plus hidden result (`ret 16`).
- `Termina` `0x005B9450`: Sub, 0 explicit arguments (`ret 4`).
- `PuoFare` `0x005B9AB0`: Function, 1 explicit argument plus hidden result (`ret 12`).
- `SetPrivilegi` `0x005BA8B0`: Sub, 0 explicit arguments (`ret 4`).
- `GestioneRecord` `0x005BC580`: Function, 0 explicit arguments; writes a 16-byte Variant result (`ret 8`).
- `GestioneSequenza` `0x005BDF00`: Function, 0 explicit arguments; writes a 16-byte Variant result (`ret 8`).
- `GestioneLato` `0x005BECE0`: Sub, 6 explicit arguments (`ret 28`); exact parameter semantics remain unresolved.
- `GestioneCampo` `0x005BFDB0`: Sub, 6 explicit arguments (`ret 28`) and forwards all six to helper `0x0060A090`.
- `VersioneDemo` `0x005BFE30`: Sub, 0 explicit arguments (`ret 4`).

These facts are encoded in `LegacyScriptMainFormContract`. Only `StampaRecord`, `CaricaSfondo`, and `Termina` are currently exposed in `LegacyScriptMainFormFacade`; the rest remain deliberately unavailable until their behavior is decoded sufficiently for iso-functional execution.
