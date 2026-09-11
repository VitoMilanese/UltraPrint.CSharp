# `frmContatori` editor workflow recovery

This note records the user-interface semantics of UltraPrint 2.2.115 `frmContatori`, complementing `COUNTERS_NATIVE.md`, which covers `Contatori.dat` and runtime `+(...)` expansion.

## Native event map

| Event | Address |
| --- | ---: |
| `cbocifre` change | `0x0055B9F0` |
| `Check1` change | `0x0055BBA0` |
| `CmdElimina_Click` | `0x0055BD40` |
| `cmdNuovo_Click` | `0x0055BF30` |
| `Form_Load` | `0x0055C4C0` |
| `ListaContatori` selection | `0x0055CC30` |
| `Ok` | `0x0055D100` |
| `txtValore` change | `0x0055D1E0` |

The form keeps the current logical table index in a Variant at form offset `+0x34`. Valid records use one-based indices **1..99**. Form load ends with current index `-1`; Delete changes it to `0`. All three field-change handlers require `currentIndex > 0`, so both sentinel values suppress writes.

## Form_Load

`Form_Load` first calls native counter loader `0x005F5E70`.

It clears `cbocifre` and adds the decimal choices **1 through 20**.

It then clears `ListaContatori` and loops logical records 1..99. For each record it:

1. converts fixed `String * 10` name to a Variant;
2. applies `Trim()` (`rtcTrimVar`);
3. LSets the trimmed value back into the fixed 10-character field;
4. calls `Funzioni.Capitalizzato` (`0x004A5710`, vtable `+0x80C`);
5. evaluates `Left(displayName, 1) > " "`;
6. if true, adds `displayName` to the list.

After the loop it stores `-1` into current index and disables `CmdElimina`.

### `Funzioni.Capitalizzato`

The native method is exactly the familiar first-letter capitalization shape:

```text
UCase(Left(value, 1)) + LCase(Mid(value, 2))
```

The imported VB runtime functions used by the routine are `rtcUpperCaseVar` (ordinal 528), `rtcMidCharVar` (632), and `rtcLowerCaseVar` (518). The managed model uses the current process culture, matching the locale-sensitive VB6 behavior rather than hard-coding English casing.

## List selection

`ListaContatori` does not store a direct logical record index in each ListBox item. On selection it scans records 1..99 and reconstructs `Capitalizzato(Trim(name))` for each record, comparing it with the selected ListBox text.

The **first** exact display-text match wins. This means duplicate names that collapse to the same capitalization bind to the first logical slot even when a later duplicate ListBox row was selected.

When a match is found, the form temporarily sets current index to `-1`, then programmatically loads:

- `cbocifre` from record `+0x14`;
- `txtValore` from record `+0x1C`;
- `Check1` from record `+0x16`.

Only after those control updates does it set current index to the real one-based logical slot and enable Delete. The temporary `-1` prevents the three Change handlers from feeding the displayed values back into the table while the selection is being loaded.

## Change handlers

All three handlers first evaluate `currentIndex > 0`.

- `cbocifre` reads the selected combo text/value, converts it to I2, and writes record `+0x14`.
- `Check1` normalizes the checkbox value to VB Boolean `0 / -1` and writes record `+0x16`.
- `txtValore` converts Text to I4 and writes record `+0x1C`.

None of these handlers calls the save helper. Editing remains in memory until OK.

## New counter

`cmdNuovo_Click` opens a VB InputBox with literal prompt:

```text
Nome ?
```

An empty result cancels creation. Otherwise it scans logical indices 1..99 for the first free slot. During that scan each fixed name is normalized with the same `Trim + LSet + Capitalizzato` pipeline, then the free predicate is:

```text
Left(displayName, 1) < " "
```

When the first free slot is found, native code:

1. adds the **raw InputBox text** to `ListaContatori`;
2. LSets the raw input into the fixed `String * 10` name (therefore truncating values longer than 10 characters and padding shorter values);
3. writes digits = `6`;
4. writes `Check1 = True` (`0xFFFF`);
5. selects the last ListBox item, causing normal selection logic to run.

Two legacy quirks fall directly out of this code:

- the handler does **not** clear the current-value Long or the still-unclaimed WORD when reusing a deleted slot; Delete followed by New can therefore inherit those values;
- the ListBox receives the raw untruncated/uncapitalized InputBox text, while selection reconstructs `Capitalizzato` from the fixed 10-character backing name. A lower-case or over-10-character new name can therefore fail to map immediately back to a logical record. The managed compatibility model preserves this rather than silently repairing it.

If all 99 slots are occupied, the native handler simply exits without creating a record.

## Delete

`CmdElimina_Click` requires current index > 0. It:

1. LSets a single-space BSTR into the fixed `String * 10` name, resulting in ten spaces;
2. removes the current ListBox item;
3. stores current index `0`;
4. disables Delete.

It does **not** clear digits, checkbox state, the unknown WORD, or current value. This is the reason those fields survive a subsequent New operation that reuses the same logical slot.

## OK

`Ok` directly calls save helper `0x005F60C0` and then unloads/closes the form through the VB forms collection/object path. Thus the ordinary editor has **deferred persistence**: field edits/New/Delete remain in memory until OK.

This differs from runtime `+(Counter)` resolution, which persists immediately after every successful increment.

## Managed boundary

`LegacyCounterEditorModel` reproduces the recovered non-visual behavior:

- 1..20 digit choices;
- Form_Load `Trim + LSet + Capitalizzato` normalization and active filtering;
- one-based 1..99 logical record selection with `-1`/`0` sentinels;
- first-match behavior for duplicate display names;
- New raw-list-text and fixed-10-character quirks;
- Delete clearing only the name;
- delayed field updates and explicit Save.

The next layer is the WinForms dialog itself. It can use this model without duplicating or weakening the legacy compatibility rules.
