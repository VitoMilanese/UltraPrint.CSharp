# smartDriver / ICE_API native recovery

UltraPrint 2.2.115 contains a VB6 `smartDriver` form/class that wraps a vendor library named `ICE_API`. The supplied UltraPrint executable is 32-bit and resolves the library dynamically through VB6 `DllFunctionCall`; the supplied installer does **not** contain `ICE_API.DLL`, so the vendor runtime is an external deployment dependency.

## Recovered `smartDriver` methods

The VB6 object exposes the following recovered procedures:

- `EncodeMagStripeWithApi` `0x005114A0`
- `ChangeErrorReporting_Event0` `0x00511AD0`
- `CustomMSEncodeAndPrint_Event0` `0x00511B50`
- `GetActiveJobId_Event0` `0x00511C60`
- `GetHelpFileName_Event0` `0x00511D90`
- `GetMagstripeHeadType_Event0` `0x00511EB0`
- `GetPrinterAPIVersion_Event0` `0x00511FD0`
- `GetPrinterError_Event0` `0x00512150`
- `GetPrinterModelName_Event0` `0x00512270`
- `GetPrinterSerialNumber_Event0` `0x00512390`
- `OnInitSmartCard_Event0` `0x005124B0`
- `MagstripeReadEncodePrint_Event0` `0x00512520`
- `MSEncodeAndPrint_Event0` `0x00512630`
- `MSEncodeWithFont_Event0` `0x00512740`
- `PrintWithCardStatus_Event0` `0x00512820`
- `ReadMagstripe_Event0` `0x00512890`
- `RunCleaningCard_Event0` `0x00512B30`
- `RunFirmwareUpdate_Event0` `0x00512BA0`
- `TogglePollingState_Event0` `0x00512C10`
- `StampaRecord` `0x00512D20`

## Exact ICE_API import surface

The DllFunctionCall descriptors embedded in `UltraPrint.exe` contain these exact x86 stdcall exports. The `@N` suffix is the number of argument bytes popped by the callee and is useful ABI evidence, but does not by itself prove the C type of every argument.

| Export | Stack bytes | Managed classification |
| --- | ---: | --- |
| `_SetTopCoatMode@8` | 8 | action |
| `_RotateCardSide@8` | 8 | action |
| `_SetMagstripeFormat@12` | 12 | action |
| `_SetCustomMagstripeFormat@36` | 36 | action |
| `_EncodeMagstripe@20` | 20 | action |
| `_ReadMagstripe@20` | 20 | query/read |
| `_SetInteractiveMode@8` | 8 | action |
| `_SmartCardContinue@8` | 8 | action |
| `_FeedCard@8` | 8 | action |
| `_GetCardId@8` | 8 | query/read |
| `_GetCardStatus@28` | 28 | query/read |
| `_GetCardPrinterErrorsA@24` | 24 | query/read |
| `_GetCardPrinterStatusA@24` | 24 | query/read |
| `_ClearAllCardErrorsA@4` | 4 | action |
| `_DisplayCardErrorA@8` | 8 | action |
| `_GetHelpFileNameA@16` | 16 | query/read |
| `_SendPrinterCommandA@12` | 12 | action |
| `_GetCardPrinterInfoA@20` | 20 | query/read |
| `_GetCardPrinterPollingStateA@8` | 8 | query/read |
| `_ResumePrinterPollingA@8` | 8 | action |
| `_PrinterAPIMajorVersion@0` | 0 | query/read |
| `_PrinterAPIMinorVersion@0` | 0 | query/read |
| `_RunFirmwareUpdateUtilityA@4` | 4 | action |
| `_CleanCardPrinterA@4` | 4 | action |

The native `GetPrinterAPIVersion` event calls the two zero-argument version exports and converts their integer return values to text. That is the only ICE_API function the first managed probe invokes. Firmware, cleaning, magnetic-stripe, feed, smart-card and other printer-mutating exports are intentionally **not** called by the diagnostics layer.

## Recovered status/info flow

Native helper routines called by the smartDriver buttons prove these relationships:

- `TogglePollingState` calls `_GetCardPrinterPollingStateA@8`, distinguishes the states represented by the embedded messages `PRINTER IS RESPONDING`, `PRINTER IS NOT RESPONDING` and `PRINTER IS SUSPENDED`, and uses `_ResumePrinterPollingA@8` for the resume path.
- `GetPrinterError` uses `_GetCardPrinterErrorsA@24`.
- `GetActiveJobId` uses `_GetCardPrinterStatusA@24`.
- `GetPrinterModelName`, `GetPrinterSerialNumber` and `GetMagstripeHeadType` all use `_GetCardPrinterInfoA@20` with different native information selectors and a two-pass buffer-allocation pattern.
- `GetHelpFileName` uses `_GetHelpFileNameA@16`.

The exact pointer/structure signatures for those buffer APIs are deliberately not guessed yet. They remain behind the adapter boundary until the corresponding argument layouts are recovered or a trustworthy vendor header/runtime is supplied.

## `frmDispositivi` / Campo.ini configuration surface

`frmDispositivi.Form_Load` reads the legacy configuration through the global File/INI helper. The supplied `Campo.ini` proves the following sections are active configuration, not dead strings:

- `[Setup] Dispositivo Corrente`
- `[Moduli Hardware]`
- `[<printer>-Configurazione]`
- `[Driver-<printer>]`

The supplied hardware-module catalog is:

```ini
[Moduli Hardware]
1=Codificatore Magnetico,Mag
2=Codificatore Chip,Chip
3=Codificatore Laser,Laser
4=Stampante Termografica,Stampa
5=Flip Over,Flip
```

Configured modules use semicolon-separated rows, for example:

```ini
[Select Class-Configurazione]
1=Codificatore Magnetico; Mag;Vero
2=Stampante Termografica; Stampa;Vero
```

`cmdOK_Click` writes `[Setup] Dispositivo Corrente` back through the same legacy INI layer. The managed Devices diagnostics workspace therefore reads these profiles, lists installed Windows printers, and can update the current-device key without rewriting unrelated `Campo.ini` content.

## Managed boundary in this slice

The first managed hardware slice intentionally stops before sending real printer commands:

- `LegacyDeviceConfigurationStore` restores the proven frmDispositivi profile/current-device configuration surface.
- `LegacyIceApiProbe` catalogs all 24 recovered exports, refuses to load the x86 ABI from a 64-bit process, validates exports when an x86 vendor DLL is available, and calls only the side-effect-free major/minor version functions.
- **Devices -> Printer / Device diagnostics...** exposes configured profiles, enabled modules, installed Windows printers, the current device and ICE_API compatibility state.

This creates the adapter boundary required for subsequent card-status/cancel work without accidentally running cleaning, firmware, feed, magnetic-stripe or smart-card operations against unknown hardware.
