# Legacy formats

## Campo.ini — confirmed

`Campo.ini` combines several roles:

1. a small `< ... >` preamble containing UI/script setup;
2. schema-like sections such as `[Generale]`, `[Testo]`, `[Aspetto]`, `[Immagine]`, `[Sfondo]`, `[Tabella]`;
3. printer/driver and hardware configuration sections;
4. a `[$]` section that stores flattened current values as `Group.Key=Value`.

The C# rewrite preserves and exposes this file through:

- `LegacyIniDocument`
- `CampoSchemaParser`
- `CampoCurrentValueStore`

The supplied file confirms, among other values, `Sfondo.Dpi=300`, `Sfondo.Altezza=54`, `Sfondo.larghezza=85`, plus current-field X/Y/width/height and image settings.

### VB6 value conventions that occur in the file

- Boolean true can be `-1`; false is `0`.
- Numeric decimal separator can be Italian comma, e.g. `8,9`.
- OLE colors appear as VB-style hex strings such as `&H80000008`.
- Relative paths such as `.\Foto` and `.\LY\...` occur.

## UP.ini — partially confirmed

Native startup reads/writes at least section `[Setup]`, key `Pw`, including `/erasepw` behavior.

More keys should be added only as their native call sites are recovered or a real `UP.ini` is provided.

## Operatori.FFM — partially confirmed

Used as the operator/login/privilege database. The startup path is `Db\Operatori.FFM`, with legacy/fallback references to `Operatori.FFM` in the application directory.

The database layer is not implemented yet.

## .ly — partially decoded binary format

The real `TPMFAO19.ly` sample is 20,148 bytes and matches the UltraPrint 2.2.115 binary loader assumptions.

### Header — confirmed for the supplied sample

| Offset | Type | Meaning | Sample |
|---:|---|---|---:|
| `0x0000` | `Single` | card height in mm | `54.0` |
| `0x0004` | `Single` | card width in mm | `85.0` |
| `0x0008` | `Int16` | DPI | `300` |

The remaining bytes before the field table are not fully decoded yet.

### Field table — confirmed

For this UltraPrint version/fixture:

- field table starts at decimal offset `2029` (`0x07ED`);
- there are 64 fixed slots;
- each slot is 264 bytes;
- unused slots are zero/blank;
- the sample has 11 active fields.

Known record layout:

| Relative offset | Size | Type | Meaning |
|---:|---:|---|---|
| `0` | 20 | ANSI fixed string | field name |
| `20` | 2 | `Int16` | legacy field type |
| `22` | 4 | `Single` | Y, VB6 twips |
| `26` | 4 | `Single` | X, VB6 twips |
| `30` | 4 | `Single` | height, VB6 twips |
| `34` | 4 | `Single` | width, VB6 twips |
| `38` | 124 | ANSI fixed string | text/image payload |
| `162..187` | 26 | mixed flags/colors | partially understood |
| `188` | 32 | ANSI fixed string | font name |
| `220` | 4 | `Single` | font size |
| `224` | 2 | VB Boolean | confirmed `Testo.Fisso` for text fields |
| `226..261` | 36 | mixed flags/strings | partially understood |
| `262` | 2 | `Int16` | level / z-order |

Coordinate conversion is the standard VB6 twip conversion:

```text
1 mm = 1440 / 25.4 = 56.692913... twips
```

This is independently confirmed by the current `Campo.ini` values. For example the `Immagine_11` record decodes to approximately:

```text
X = 3.9 mm
Y = 8.9 mm
Width = 75.7 mm
Height = 37.0 mm
Level = 1
File = .\LY\TPMFAO19_retro.jpg
Font = Arial
Font size = 9.857142
```

which matches the `[$]` state written by UltraPrint.

### Confirmed sample type codes

- `3` — text
- `5` — image
- `6` — image/photo placeholder

Other codes remain intentionally `Unknown` until another fixture proves them.

### Front / back

The sample stores both sides in the same 64-slot sequence. The record order is:

1. front background image (`..._fronte.jpg`);
2. front fields;
3. back image (`..._retro.jpg`);
4. back fields.

The C# reader currently infers a side boundary when an image record name/path contains `fronte`/`front` or `retro`/`back`. That inference is useful and correct for the supplied sample, but it is explicitly not yet claimed to be the canonical on-disk side flag.

### Safe writer strategy

`UltraPrint22115LayoutCodec.Save` does not serialize a new `.ly` from scratch. It:

1. reads the original binary;
2. patches only confirmed header/record fields;
3. preserves every other byte unchanged;
4. writes the resulting file.

This permits practical editing now without pretending the rest of the format is understood.

### Still unresolved

- remaining pre-field header structure;
- post-table/footer structures;
- full meaning of bytes `162..187` and `226..261`;
- canonical side/front/back flag if one exists;
- every field type beyond 3/5/6;
- adding/removing fields rather than editing existing slots;
- embedded/referenced script details;
- database and hardware-specific payloads;
- exact role of the additional JPEG stored with `.frm` extension in the supplied fixture.
