# Native `.ly` DataField write-back

After `frmCarta.Record2Card` recovery proved that the per-field `DataField` is the fixed
`String * 28` at RAM `+0x19A` and serialized `.ly` bytes **230..257**, managed binding no
longer needs to be sidecar-only.

`LegacyNativeDataFieldWriter` mutates only that 28-byte region in the field's preserved
264-byte raw record. It uses the same conservative byte-preservable ANSI boundary as the
current `.ly` codec; values longer than 28 bytes or requiring an unproven wider code page are
rejected rather than truncated/corrupted. Every byte outside the DataField span is left intact.

The writer updates the in-memory Text/Image binding property at the same time. The actual `.ly`
file is still changed only by the existing main-editor Save command, so normal dirty-state and
save/discard behavior remain in control.

The Database / Records integration now mirrors **Bind** into the native DataField record and
marks the main layout dirty. **Clear binding** removes both the managed override and the native
DataField. The `.data.json` file remains useful for database/query/table state and as an unsaved
session compatibility copy, but is no longer the only representation of per-field binding.

Regression coverage verifies:

- only bytes 230..257 change;
- non-DataField bytes remain byte-identical;
- Text/Image runtime binding properties stay synchronized;
- exactly 28 bytes are accepted and 29 are rejected;
- unproven non-ANSI field names are rejected;
- a new type-4 Barcode binding survives an actual `.ly` Save/Load cycle through the raw record.
