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

The pointer/buffer contracts for the read-only status helpers are now recovered closely enough to reproduce the native queries without calling the mutating printer functions.

## Exact read-only status/info ABIs

Native call-site stack reconstruction proves these x86 stdcall signatures:

```c
BOOL GetCardPrinterInfoA(
    LPCSTR printerName, DWORD level, LPBYTE pData, DWORD cbBuf, LPDWORD pcbNeeded);

BOOL GetCardPrinterStatusA(
    LPCSTR printerName, DWORD level, LPBYTE pData, DWORD cbBuf,
    LPDWORD pcbNeeded, LPDWORD pcReturned);

BOOL GetCardPrinterErrorsA(
    LPCSTR printerName, DWORD level, LPBYTE pData, DWORD cbBuf,
    LPDWORD pcbNeeded, LPDWORD pcReturned);

BOOL GetCardPrinterPollingStateA(LPCSTR printerName, LPDWORD state);
```

The `pcReturned` semantic name is based on the classic array-query pattern; UltraPrint passes a second `DWORD*` and does not consume it. All three variable-buffer helpers use a two-pass call: first with `cbBuf=0` to obtain `pcbNeeded`, then with an allocated buffer of that size.

Recovered levels/record prefixes are:

| Query | Level | Native bytes consumed by UltraPrint | Recovered value |
| --- | ---: | ---: | --- |
| printer model | 1 | 12-byte prefix | first DWORD is ANSI model-name pointer |
| printer serial | 2 | 32-byte prefix | first DWORD is ANSI serial-number pointer |
| magnetic head | 4 | 28-byte prefix | first four DWORDs encode installed/enabled/head type |
| printer status | 1 | 16-byte record | first DWORD is active job id |
| printer errors | 1 | 16-byte record | first DWORD is ANSI error-text pointer |

`GetMagstripeHeadType` interprets the level-4 first four DWORDs literally: either of the first two zero means `not installed`; third zero means `not enabled`; fourth value 1 means `IAT`, value 2 means `NTT`, and other values remain `Unknown`.

`GetCardPrinterPollingStateA` returns the states used by the native messages: `0 = PRINTER IS RESPONDING`, `1 = PRINTER IS NOT RESPONDING`, `2 = PRINTER IS SUSPENDED`. `TogglePollingState` can subsequently call `_ResumePrinterPollingA@8`, but the managed diagnostics reader deliberately does not expose that mutating operation.

The native `GetPrinterError` button reads `_GetCardPrinterErrorsA@24` and then calls `_ClearAllCardErrorsA@4`. The managed **Read ICE status** command intentionally stops after the read, so inspecting errors cannot clear the printer's error queue.

A surviving public ICE API declaration independently confirms the x86 card-status ABI used by later `PrintWithCardStatus` work: packed `CARDIDTYPE` is 12 bytes (`DWORD JobId`, `DWORD CardNum`, `HANDLE hPrinter`), `CARD_INFO_1` is two `BOOL`s (8 bytes), `CARD_INFO_2` is two `DWORD`s plus `SYSTEMTIME` (24 bytes), `GetCardId(HDC, LPCARDIDTYPE)` takes 8 stack bytes, and `GetCardStatus(CARDIDTYPE by value, DWORD level, LPBYTE, DWORD, LPDWORD)` takes 28. The managed types are data-only in this slice; those card/job calls are not invoked yet.

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
- `LegacyIceApiReader` adds explicit read-only polling/model/serial/magnetic-head/active-job/first-error queries using the recovered two-pass buffer contracts. Each query is isolated so a vendor failure becomes a diagnostic warning rather than silently enabling another hardware action.
- **Devices -> Printer / Device diagnostics...** exposes configured profiles, enabled modules, installed Windows printers, the current device and ICE_API compatibility state. **Read ICE status** is explicit; refreshing the form does not contact the printer for status/info beyond the existing API-version probe.

This preserves a hard boundary around printer mutation: diagnostics never clear errors, resume/suspend polling, clean/update firmware, feed a card, encode magnetic data, initialize a smart card or submit a print job.
