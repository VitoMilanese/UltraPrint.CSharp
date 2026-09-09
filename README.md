# UltraPrint C# Recovery

C#/.NET 8 migration of the legacy UltraPrint 2.2.115 VB6 application recovered from the supplied 2003 binaries.

## Current status

This is still an incremental compatibility rewrite, but the supplied `TPMFAO19.ly` sample allowed the first real editor/preview path to replace the earlier binary-only probe.

Implemented now:

- .NET 8 solution split into Core / Legacy / WinForms / Recovery CLI.
- Recovered legacy startup paths and switches: `/erasepw`, `/Layout=`, `/printform`.
- Order-preserving ANSI-era INI reader/writer.
- Parser for the schema/config structure in `Campo.ini`.
- Verified `[$]` current-value store used by native `frmCarta.CampoToIni` / `IniToCampo`.
- Partially decoded UltraPrint 2.2.115 binary `.ly` codec.
- Confirmed card header: height, width and DPI.
- Confirmed fixed 64-slot field table, 264 bytes per field.
- Confirmed field names, legacy type codes, X/Y/width/height in VB6 twips, payload, font, font size, fixed flag and level.
- Confirmed sample types `3 = text`, `5 = image`, `6 = image/photo placeholder`.
- Front/back grouping inferred from the legacy image names/paths (`fronte` / `retro`) and record order.
- WinForms layout editor with:
  - Front / Back / All view;
  - real image preview;
  - text and photo-placeholder rendering;
  - field list;
  - PropertyGrid editing;
  - mouse drag to reposition fields;
  - Save / Save As.
- Legacy save is conservative: it patches only decoded values into a copy of the original binary and preserves every unknown byte.
- Diagnostics tab and CLI remain available for continuing reverse engineering.

## Build

Open `UltraPrint.sln` in Visual Studio 2022 with the .NET 8 desktop workload, or run:

```powershell
dotnet build UltraPrint.sln
```

The analysis environment used to generate this revision does not contain the .NET SDK, so the solution could not be compiled here. The source/project structure was statically checked and the recovered binary offsets were independently validated against the supplied layout.

## Try the recovered editor

1. Build and run `UltraPrint.WinForms`.
2. Use **File -> Open layout (.ly)...**.
3. Open `samples\legacy\TPMFAO19\TPMFAO19.ly` or the original file from the UltraPrint `LY` directory.
4. Switch between **Front** and **Back** in the toolbar.
5. Click a field to inspect it in **Properties**.
6. Drag a field directly on the card to move it.
7. Use **Save As** first when testing. Unknown parts of the original binary are preserved.

Image lookup understands old paths such as `.\LY\TPMFAO19_fronte.jpg` and also tries the layout directory itself, which is useful when working from an extracted fixture directory.

## CLI

```powershell
dotnet run --project tools/UltraPrint.RecoveryCli -- parse-campo samples/legacy/Campo.ini
dotnet run --project tools/UltraPrint.RecoveryCli -- probe-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- decode-layout samples/legacy/TPMFAO19/TPMFAO19.ly
dotnet run --project tools/UltraPrint.RecoveryCli -- startup-plan "C:\Program Files (x86)\UltraPrint"
```

## Important limitation

The complete `.ly` format is not claimed to be decoded yet. The current writer updates only fields verified from the real sample and leaves unknown bytes intact. Adding/removing legacy fields, all field-type-specific flags, VBScript payloads, database metadata, magnetic/chip data and several tail/header structures still require more samples and/or native-code recovery.

See `docs/LEGACY_FORMATS.md` and `docs/REVERSE_ENGINEERING.md` for the recovered structure and confidence level.
