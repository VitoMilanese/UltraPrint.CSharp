# UltraPrint C#

C#/.NET 8 replacement of the legacy UltraPrint 2.2.115 VB6 application recovered from the supplied 2003 binaries.

The target is **iso-functional compatibility**, not merely a `.ly` viewer. The new program is being rebuilt workflow by workflow while keeping compatibility with the legacy layouts/configuration that real installations still use.

## Current status

Implemented compatibility foundation:

- .NET 8 solution split into Core / Legacy / WinForms / Recovery CLI.
- Recovered legacy startup switches: `/erasepw`, `/Layout=`, `/printform`.
- `/erasepw` compatibility for `UP.ini` `[Setup] Pw`.
- Order-preserving ANSI-era INI reader/writer.
- Parser for the schema/config structure in `Campo.ini`.
- Verified `[$]` current-value store used by native `frmCarta.CampoToIni` / `IniToCampo`.
- Partially decoded UltraPrint 2.2.115 binary `.ly` codec.
- Confirmed card header: height, width and DPI.
- Confirmed fixed 64-slot field table, 264 bytes per field.
- Confirmed field names, legacy type codes, X/Y/width/height in VB6 twips, payload, font, font size, fixed flag, OLE colors and level.
- Confirmed sample types `3 = text`, `5 = image`, `6 = image/photo placeholder`.
- Raw 264-byte legacy records are preserved per field so unknown flags survive record move/duplicate operations.
- Legacy Save/Save As rewrites only known values plus preserved raw records; unknown header/footer data stays untouched.

Implemented editor/standard-print workflow:

- Front / Back / All view.
- Real image preview and legacy `.\LY\...` asset resolution.
- Text and photo-placeholder rendering.
- Field list and PropertyGrid editing.
- Mouse drag movement and corner-handle resizing.
- Keyboard nudging: 0.1 mm, `Shift` = 1 mm, `Ctrl` = 0.01 mm.
- 1 mm grid and snap-to-grid.
- Insert text/image/photo-placeholder fields.
- Duplicate/delete fields while preserving raw legacy record bytes.
- Bring-to-front / send-to-back level editing.
- Layout width/height/DPI editor.
- Standard Windows PrintDocument Print, Print Preview and Page Setup.
- Imported images are copied using the original `LY` path convention where applicable.

Implemented database/record workflow:

- **Database -> Database / Records...** workspace integrated into the main application.
- Legacy source filters recovered from the original program: Access, DBF, Excel, CSV and text.
- `.mdb` and `Operatori.FFM` use a Jet/Access-compatible OLE DB path: ACE 16/12 first, then Jet 4.0 for legacy x86 environments.
- Table discovery and browsing.
- Direct SELECT queries plus confirmed action SQL execution for writable OLE DB sources.
- Record grid and first/previous/next/last navigation.
- Runtime `Record2Card`-style substitution for text and photo/image fields.
- Explicit card-field to database-column bindings.
- Per-record Front/Back/All card preview.
- Print/preview the current database record.
- Preview/print all loaded records using the same physical-size card renderer.
- CSV/text fallback parser works without an OLE DB text driver and preserves quoted delimiters.
- Database path, SQL, selected table and newly created managed bindings are persisted beside the layout in `.ly.data.json` until the exact legacy `.ly` database/binding byte offsets are verified.

That sidecar is intentional: unverified legacy bytes are not overwritten just to make a feature appear complete. Existing bindings recovered from legacy layouts continue to work, and the managed state can be migrated into native `.ly` bytes once the exact offsets are proven from a real database-bound legacy fixture.

Implemented operator/security compatibility layer:

- Recovered original lookup order: `Db\Operatori.FFM`, then root `Operatori.FFM`.
- Existing operator databases use ACE 16 -> ACE 12 -> Jet 4.0 provider fallback.
- Existing files are opened conservatively: `Operatore`, `Password` and `Livello` must exist; only the two native-confirmed migrations for `Privilegio LONG` and `Gruppo TEXT(50)` may be applied automatically.
- **Security** menu with Login, Logout, current-operator password change, Operators and Privileges management.
- Operator create/edit/delete plus administrator password reset.
- Raw `Livello`, `Privilegio` and `Gruppo` values are preserved and editable.
- A new `Operatori.FFM` can be created through ADOX when a compatible Jet/ACE stack is installed.
- The current session is shown in the application status bar.

Exact privilege-to-command gating is deliberately not guessed yet. The original binary contains `MainForm.PuoFare` and `MainForm.SetPrivilegi`, but a real legacy `Operatori.FFM`/runtime comparison is still required to prove the meaning of `Livello`, `Privilegio`, password storage/comparison, and when login is mandatory. See [`docs/OPERATOR_COMPATIBILITY.md`](docs/OPERATOR_COMPATIBILITY.md).

Still required for full parity includes database create/schema/import/write-back, complete `.ly` flag/type decoding, exact security privilege gating, VBScript lifecycle, counters/barcodes, image acquisition/editing, `Sequenza` sheet imposition, device profiles, card-printer APIs, magnetic stripe, smart-card/chip and remaining options/job workflows.

See [`docs/FEATURE_PARITY.md`](docs/FEATURE_PARITY.md) for the authoritative parity checklist and [`docs/MIGRATION_PLAN.md`](docs/MIGRATION_PLAN.md) for implementation order.

## Build

Open `UltraPrint.sln` in Visual Studio 2022 with the .NET 8 desktop workload, or run:

```powershell
dotnet build UltraPrint.sln
```

Compatibility smoke tests:

```powershell
dotnet run --project tests/UltraPrint.CompatibilityTests/UltraPrint.CompatibilityTests.csproj -c Release
```

## Try the editor, records and security

1. Build and run `UltraPrint.WinForms`.
2. Use **File -> Open layout (.ly)...** and open `samples\legacy\TPMFAO19\TPMFAO19.ly` or a real layout from an original UltraPrint `LY` directory.
3. Use **Front**, **Back** and **All** to inspect both sides.
4. Click a field to edit it in **Properties**, or drag/resize it directly on the card.
5. Use **Database -> Database / Records...** to open an `.mdb`, `.ffm`, `.dbf`, `.xls/.xlsx`, `.csv`, `.txt` or `.dat` source.
6. Open a table or run SQL, bind card fields to database columns, and navigate records while watching the card preview update.
7. Print/preview the current record or all loaded records.
8. Use **Security** to open/create `Operatori.FFM`, log in, manage operators and change passwords.
9. Use **Save As** first with production legacy layouts while byte-level compatibility recovery is still in progress.

## CLI

```powershell
dotnet run --project tools/UltraPrint.RecoveryCli -- parse-campo samples/legacy/Campo.ini
dotnet run --project tools/UltraPrint.RecoveryCli -- probe-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- decode-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- startup-plan "C:\Program Files (x86)\UltraPrint"
```

## Compatibility rule

Only behavior proven from binary metadata/native flow or real legacy data is treated as a confirmed compatibility contract. Unknown `.ly` bytes remain preserved rather than guessed. A workflow is marked complete only when it actually works against legacy inputs; placeholder menu items do not count.

See `docs/LEGACY_FORMATS.md`, `docs/REVERSE_ENGINEERING.md`, `docs/DATABASE_COMPATIBILITY.md` and `docs/OPERATOR_COMPATIBILITY.md` for the recovered structures and confidence level.
