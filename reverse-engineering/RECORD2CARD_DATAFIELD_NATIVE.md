# `frmCarta.Record2Card` native DataField recovery

This note closes the per-field database-binding slot used by UltraPrint 2.2.115 and ties it
directly to the managed record-binding path, including barcode fields.

## Native procedure and field UDT

`frmCarta.Record2Card` is native procedure `0x00553FA0`.

The in-memory field table is addressed with a **0x1D8 / 472-byte stride**. Within each
record the relevant recovered offsets are:

| RAM offset | Meaning |
| ---: | --- |
| `+0x28` | field type WORD; zero means unused |
| `+0x3C` | fixed payload string (`String * 124`) |
| `+0x19A` | fixed `DataField` string (`String * 28`) |
| `+0x1D6` | final WORD already correlated with the serialized field-level slot |

The `+0x19A` access is explicit: Record2Card repeatedly calls `__vbaStrFixstr` with length
`0x1C` / 28 and the address `field + 0x19A`, trims the result, and writes the normalized
fixed string back with `__vbaLsetFixstr`.

The executable also carries the literal member name `DataField`, matching this slot's
source-level role.

## `.ly` serialized offset

VB6 fixed-length Unicode strings occupy two bytes per character in the native UDT but are
written as fixed ANSI strings by the `.ly` binary UDT serializer. Correlating the already
proven field type, payload, font and final level locations yields the exact serialized map:

| `.ly` offset | Length | Meaning |
| ---: | ---: | --- |
| `20` | 2 | field type |
| `38` | 124 | field payload |
| `230` | **28** | **DataField** |
| `262` | 2 | final field-level WORD |

This mapping is also internally consistent with RAM `+0x19A`: the size reduction from the
preceding fixed strings (`String * 20`, `String * 124`, `String * 32`) plus native alignment
accounts for the RAM/file offset difference, and the following `String * 28` reduction lands
the final RAM `+0x1D6` WORD at file byte 262.

Managed compatibility therefore reads `DataField` from the preserved 264-byte raw field
template instead of inventing another sidecar-only field format. Existing raw bytes are not
rewritten by this slice.

## Record2Card resolution modes

After the active-field/side gate, Record2Card trims the fixed `DataField` and proceeds only
when it is non-empty.

Two resolution modes are directly visible in the native flow:

1. **No `[` in DataField** — use the current data source's `Recordset.Fields(DataField)`
   value directly.
2. **`[` present** — pass the **whole DataField expression** through private helper
   `0x00606910`, whose bracket-field semantics are recovered separately in
   `BRACKET_RECORD_FIELDS_NATIVE.md`.

The bracket path therefore supports expressions such as:

```text
[Name]
ID-[Code,2]
[Code,2,3]
```

using the already recovered one-based VB `Mid` semantics for optional start/length arguments.
Missing and Null bracket fields resolve to empty text. The managed direct-field path also clears
stale payload when a non-empty binding cannot be resolved.

The resolved value is then copied into the field's fixed payload at RAM `+0x3C`. Importantly,
the loop is gated by **field type != 0**, not by a special text/image type test. This is why a
legacy type-4 Barcode field uses the same DataField-to-payload binding mechanism.

## Managed behavior

`LegacyNativeDataFieldSemantics` records the native offsets and resolves direct/bracket
DataField values against the current managed record without opening COM data controls.

`LegacyRecordBinder` now applies the same binding path to:

- text payload/content;
- image payload/path;
- barcode raw payload.

A user-selected Database / Records override intentionally takes precedence for that managed
session. Otherwise the preserved native DataField is used first. The older `%COLUMN%` name
fallback remains only as managed compatibility for layouts created/handled before this offset
was recovered.

Barcode rendering remains separate: after Record2Card supplies the raw barcode value,
`LegacyBarcodeFormatter` converts it to the legacy font glyph string and `LayoutCanvas` draws it
with the selected barcode font.

Compatibility tests cover the recovered RAM/file offsets, raw DataField extraction, direct
lookup, bracket expansion with one-based slicing, Null/missing behavior, managed-override
precedence, `%COLUMN%` fallback and type-4 barcode payload binding without mutating the template.
