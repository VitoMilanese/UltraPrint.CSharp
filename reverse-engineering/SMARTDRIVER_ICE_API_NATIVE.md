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

A surviving public ICE API declaration independently confirms the x86 card-status ABI: packed `CARDIDTYPE` is 12 bytes (`DWORD JobId`, `DWORD CardNum`, `HANDLE hPrinter`), `CARD_INFO_1` is two `BOOL`s (8 bytes), `CARD_INFO_2` is two `DWORD`s plus `SYSTEMTIME` (24 bytes), `GetCardId(HDC, LPCARDIDTYPE)` takes 8 stack bytes, and `GetCardStatus(CARDIDTYPE by value, DWORD level, LPBYTE, DWORD, LPDWORD)` takes 28.

## `PrintWithCardStatus` card-job lifecycle

`PrintWithCardStatus_Event0` (`0x00512820`) delegates to native helper `0x00623650`. Its recovered card-job sequence is:

1. Obtain the current VB6 `Printer.hDC` through DISPID `0x10009`.
2. Call `_GetCardId@8(hDC, &CARDIDTYPE)`. A zero return takes the native `GetCardId failed` path.
3. The sample surfaces `Job #<JobId>, Card #<CardNum>`, then finishes the GDI print job.
4. Call helper `0x00623C10` with the 12-byte `CARDIDTYPE`.
5. That helper calls `_GetCardStatus@28(CARDIDTYPE by value, level=1, &CARD_INFO_1, 8, &needed)` at most **60 times**.
6. A FALSE API return is retried. A TRUE return with `CARD_INFO_1.Active != 0` is also retried.
7. The first TRUE return with `Active == 0` ends the loop and returns VB True only when `CARD_INFO_1.Success != 0`.
8. If all 60 calls fail or remain active, the helper returns VB False.

There is no `Sleep`/timer in `0x00623C10`; the 60 calls are immediate. Any blocking/wait performed inside the vendor DLL is external to UltraPrint. `LegacyIceCardJobSemantics` reproduces this bounded retry contract, while `LegacyIceCardJobMonitor` exposes the exact read-only `GetCardId -> GetCardStatus` flow for a caller that already owns the live printer HDC. It does not create a print job itself.

## `smartDriver.StampaRecord` mutation boundary

`smartDriver.StampaRecord` (`0x00512D20`) is a separate production print path from the `PrintWithCardStatus` sample. It directly performs printer-mutating ICE calls, so they remain disabled in managed code until their surrounding layout/module conditions are fully recovered and hardware-tested. Direct call-site evidence currently proves:

- `0x00513587`: `_SetInteractiveMode@8(hDC, TRUE)`; a zero return goes to the native `La stampante non accetta il modo Interactive` error path.
- `0x00513C75`: a conditional `_RotateCardSide@8(hDC, TRUE)` after the method's single explicit Variant argument compares equal to VB True. The semantic name of that argument is not yet claimed.
- `0x00513D15`: `_FeedCard@8(hDC, 0x11)`; the semantic name of raw value `0x11` is not guessed.
- `0x0051401D`, `0x00514529`, `0x00514FC1`: `_SmartCardContinue@8` with raw second arguments `1`, `0`, `1` respectively on different branches.
- `0x00515541`: cleanup calls `_SetInteractiveMode@8(hDC, FALSE)` after clearing the two transient smartDriver flags at `0x0062B680` / `0x0062B682`.

The recovered 24-export ICE surface contains **no card-job cancel export**, and neither the recovered `PrintWithCardStatus` poller nor `smartDriver.StampaRecord` calls `_GetCardStatus` as a cancellation mechanism. No `AbortDoc`, `KillDoc`, `CancelDC` or equivalent native cancellation contract has been proven in these paths. Therefore managed cancellation must remain the already-restored cooperative application-level cancellation until a real device/driver cancellation contract is recovered; this slice deliberately does not invent one.

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
- `LegacyIceCardJobMonitor` adds the proven `GetCardId -> GetCardStatus(level 1)` lifecycle for an already-created printer job, including the native maximum of 60 immediate status calls. It never feeds/rotates/submits/cancels a card.
- **Devices -> Printer / Device diagnostics...** exposes configured profiles, enabled modules, installed Windows printers, the current device and ICE_API compatibility state. **Read ICE status** is explicit; refreshing the form does not contact the printer for status/info beyond the existing API-version probe.

This preserves a hard boundary around printer mutation: diagnostics never clear errors, resume/suspend polling, clean/update firmware, feed a card, encode magnetic data, initialize a smart card or submit a print job.

## `MainForm.PrinterEscape` is raw PASSTHROUGH, not cancellation

`MainForm.PrinterEscape` (`0x005B9150`) has two explicit arguments plus a Variant return. This build never reads the first explicit argument. It reads the second argument as a BSTR, takes its length, obtains `Printer.hDC` through DISPID `0x10009`, builds a legacy byte/string frame through VB `Chr()` plus Variant concatenation, and calls the dynamically imported GDI `Escape` routine with hard-coded escape code `0x13` (decimal **19**, `PASSTHROUGH`). The GDI return value is wrapped as a VB `Long` Variant.

After the `Escape` call, the routine invokes VB Printer DISPID `0x20000`. The same DISPID is used at the native document-closing points in `PrintWithCardStatus`, `MainForm.StampaRecord` and Sequenza, so this is the current-document `Printer.EndDoc` operation. `PrinterEscape` therefore sends a low-level passthrough payload and then closes the VB Printer document; it is **not** a device/job cancel path.

The exact byte ordering of the legacy `Chr(0)`, `Chr(Len(payload))`, payload BSTR and embedded-NUL framing is not yet claimed. Managed code records the proven contract but deliberately does not emit PASSTHROUGH data until that framing is closed and tested on supported hardware.

## `smartDriver.StampaRecord` recovered preamble and gates

`smartDriver.StampaRecord` (`0x00512D20`) has one Optional Variant argument. Native `rtcIsMissing` handling defaults a missing argument to VB `False`. The source-level parameter name is not recovered, so the managed compatibility model calls it only the **optional gate**.

When the optional gate is `True`, native code first enters the magnetic-stripe preparation branch (`0x00603FC0`, `Traccia1/2/3`, `EncodeMagStripeWithApi`) and, on the successful close path, pairs raw `EndPage` / `EndDoc` with script hooks `EndPage` / `EndDoc`. The same gate is tested again immediately before `_RotateCardSide@8(hDC, TRUE)`. No semantic label such as front/back is assigned to this argument without stronger evidence.

The first production card-job preamble is now proven in this order:

1. `_SetInteractiveMode@8(hDC, TRUE)`; failure takes the embedded `La stampante non accetta il modo Interactive` path.
2. `frmCarta.InteractiveMode = True` through its recovered setter at `+0x7B8`.
3. Win32 `StartDocA` on the VB Printer HDC.
4. `Funzioni.Vbscript("StartDoc", ...)`.
5. Win32 `StartPage`.
6. `Funzioni.Vbscript("StartPage", ...)`.
7. If the Optional Variant gate is `True`, `_RotateCardSide@8(hDC, TRUE)`.
8. `_FeedCard@8(hDC, 0x11)`; `0x11` remains a raw native value rather than a guessed enum name.
9. `Funzioni.Vbscript("EncodeChip", ...)`.
10. Later smart-card branches call `_SmartCardContinue@8` with raw second arguments `1`, `0`, `1` at `0x0051401D`, `0x00514529`, `0x00514FC1` respectively, interleaved with conditional `EndPage`/`EndDoc` and their script hooks.
11. `frmCarta.HasRear` (`+0x794`) gates the later rear-side continuation. If no rear side exists, native flow goes to cleanup.
12. Cleanup clears both transient smartDriver flags (`0x0062B680`, `0x0062B682`), calls `_SetInteractiveMode@8(hDC, FALSE)`, and writes `frmCarta.InteractiveMode = False`.

This closes the high-confidence orchestration envelope without guessing the semantic names of the two transient flags, the Optional Variant parameter, raw feed/continue numeric values, or branch meanings that depend on real printer/module behavior.

## Cancellation search result

The recovered 24-export `ICE_API.DLL` surface contains no cancel-card or cancel-job export. The production `smartDriver.StampaRecord` path does not call `GetCardStatus` as a cancellation operation. `MainForm.PrinterEscape` is `PASSTHROUGH + EndDoc`, not abort. Searches of the recovered standard-print, card-status and smartDriver paths also found no `AbortDoc`, `KillDoc` or `CancelDC` contract.

Consequently the C# replacement continues to use the already-restored application-level cooperative cancellation for batch/sequence workflows. A device-level abort will only be added if a separate driver contract, escape command, vendor runtime or real-printer trace proves one.

## Managed semantic guard

`LegacySmartDriverPrintSemantics` and `LegacyPrinterEscapeContract` now encode the proven preamble, raw constants, `HasRear` gate, cleanup transition and absence of a proven device cancel path as **pure data/state semantics**. They intentionally execute no GDI `Escape`, no `StartDoc`/`FeedCard`, and no mutating ICE API export. This keeps future hardware work testable without silently activating a printer.
