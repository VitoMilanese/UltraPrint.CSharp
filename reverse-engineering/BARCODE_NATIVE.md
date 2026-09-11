# UltraPrint 2.2.115 barcode recovery

This note records barcode behavior recovered from the original VB6 `UltraPrint.exe`.
Only native-observed behavior is treated as authoritative.

## `frmBarcode`

Recovered procedures include `Form_Load` at `0x00529B70`, `ListaBarcode` selection at
`0x0052A640`, size-list selection at `0x0052AD30`, the size textbox event at
`0x0052AE60`, and the OK handler at `0x00528D20`.

`Form_Load` enumerates installed font names and groups/matches them by these five fixed
prefixes, in this order:

1. `UPC`
2. `3 OF 9`
3. `2 OF 5`
4. `CODE 128`
5. `CODABAR`

The current field's fixed 32-character font name is read from native field offset
`+0x150` and matched back to the list. The OK path writes the selected font name back to
that same current-field font slot. The size textbox converts text to a floating-point
font size and applies it through the ordinary font-size property path. Barcode therefore
remains a font-backed text field; UltraPrint does not render barcode bars itself.

`ListaBarcode` preview uses the fixed sample `01234567` and calls the same formatter used
by print rendering.

## Formatter `0x005FCC60`

The formatter receives `(barcode-font-name, raw-value)` and returns a glyph string for
the selected legacy barcode font.

### 2 OF 5

`"(" & data & ")"`

### 2 OF 5 INTERLEAVED

If the input length is odd, a `0` is prefixed. Each two-character pair is converted by
`Chr(Val(pair) + 48)`, then the result is wrapped in `(` and `)`.

### 3 OF 9

`"*" & data & "*"`

### CODABAR

`"A" & data & "B"`

### CODE 128

The routine constructs start candidates 103, 104 and 105 and scans whether all source
characters are digits, but the scan result is dead: emitted output always begins with
`Chr(104)`.

Checksum behavior contains an important compatibility quirk. For each source character,
the routine searches it with binary `InStr` in a string containing `Chr(0)` through
`Chr(32)`, subtracts one from the 1-based result, multiplies that code by the 1-based
source position, and adds it to a checksum initialized to 104. Printable characters not
present in that control-character table therefore contribute `-1 * position`. It then
appends `Chr(checksum Mod 103)` and `Chr(128)`. This is intentionally reproduced rather
than silently replaced with a standards-correct Code 128 implementation.

### UPCH / UPC-font path

The branch is selected when `Left(fontName, 4) = "UPCH"`.

Native constants recover these glyph tables:

- A: characters 33..42, `!"#$%&'()*`
- B: characters 107..116, `klmnopqrst`
- C/right: characters 97..106, `abcdefghij`
- guard: `<`
- center: `=`
- leading digit 0..9 for 12-digit input: `u` through `~`

Parity rows are:

`AAAAAA`, `AABABB`, `AABBAB`, `AABBBA`, `ABAABB`, `ABBAAB`, `ABBBAA`,
`ABABAB`, `ABABBA`, `ABBABA`.

A trimmed 7-digit input uses four A-side digits and no leading prefix. A trimmed 12-digit
input uses six A/B-side digits, selects parity from its first digit, and represents that
first digit with the `u`..`~` prefix. Other lengths are normalized through the same
legacy zero-padding helper: lengths below 7 target 7; all other non-12 lengths target 12.

The check digit is computed from right to left: digits in positions 1,3,5... from the
right are summed and multiplied by 3; the alternating digits are added once; check digit
is `(10 - total Mod 10) Mod 10`. The check digit is appended before glyph encoding.

## Managed editor/rendering boundary

The managed editor now keeps barcode type `4` on the existing legacy field record rather
than introducing a new sidecar or new `.ly` offsets. The raw barcode value remains the
recovered 124-byte payload, while the selected barcode font and size reuse the already
recovered fixed font-name and font-size slots.

`BarcodeIntegration` adds **Insert -> Barcode Field...**, **Edit -> Edit Barcode...**, a
Barcode toolbar action and double-click editing for selected barcode fields. The editor
enumerates installed fonts using the five native prefix groups above and uses the native
`01234567` sample for its preview. Editing the raw field value is exposed as a managed
convenience because the dedicated legacy text/link editor is not yet restored.

`LayoutCanvas` renders `LayoutFieldKind.Barcode` through `LegacyBarcodeFormatter` and the
selected font instead of drawing a synthetic standards-based barcode. The same
`LayoutCanvas.RenderTo` path is used by Print Preview and Print, so screen and printed
output share the recovered formatter. If the required legacy barcode font is not
installed, Windows font substitution may occur; the application deliberately does not
replace that with a different barcode symbology.

Compatibility tests additionally round-trip a type-4 barcode field through a real managed
`.ly` save/load and verify payload, font and font size preservation.
