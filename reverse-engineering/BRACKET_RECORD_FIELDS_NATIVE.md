# Legacy `[...]` record-field token recovery

UltraPrint 2.2.115 uses native helper `0x00606910` to expand square-bracket expressions after `Interpretariga`, NUL removal, and persistent `+(CounterName)` expansion in the magnetic-track preparation pipeline.

The helper is a legacy record-field resolver backed by the currently loaded `frmDatabase` or `Tabella` data control. This note records the directly recovered selection, lookup, optional substring, error/null, and replacement semantics without executing COM/database operations.

## Active record-source selection

Two global WORD Booleans are maintained directly by form lifecycle events:

- `0x0062A04C` is set to VB True by `frmDatabase.Form_Load` and cleared by `frmDatabase.Form_Unload`;
- `0x0062A04A` is set to VB True by `Tabella.Form_Load` and cleared by `Tabella.Form_Unload`.

`0x00606910` checks them in this order:

1. if `frmDatabase` is loaded, obtain its data-control object through form vtable `+0x2FC`;
2. if `Tabella` is loaded, obtain its data-control object through the same `+0x2FC` accessor and overwrite the previously selected object.

Object/control metadata identifies the relevant controls as:

- `frmDatabase.Data1`;
- `Tabella.datPrimaryRS`.

Therefore `Tabella` has deterministic priority when both forms are loaded. If neither form is loaded, the helper returns the original input text unchanged and does not consume/replace any `[...]` expression.

## Token extraction and argument grammar

The outer loop uses `Funzioni.GetInside` (`+0x7CC`) with literal delimiters `"["` and `"]"`. A non-empty extracted body is then processed.

The helper tests whether the body contains a comma. Without a comma, the entire body is the field token and both optional numeric arguments remain zero.

When a comma is present, `Funzioni.Parola` (`+0x7F0`) is used sequentially with comma as the separator to obtain at most three values:

```text
[field]
[field,start]
[field,start,length]
```

The second and third tokens are converted with MSVBVM60 ordinal **581**, `rtcR8ValFromBstr` / VB `Val()`. Before a substring operation, the numeric value is converted to I4. Values that are absent or not positive do not activate the corresponding optional branch.

## Recordset field lookup

The first parsed token is wrapped back in square brackets before recordset access:

```text
fieldLookupKey = "[" & fieldToken & "]"
```

The selected data-control object is accessed late-bound through members embedded in the native binary:

```text
.Recordset
.Fields
.Name
```

The first access effectively validates the requested field through the `Recordset.Fields(fieldLookupKey).Name` path under VB error handling. If that lookup raises an error, the replacement value becomes an empty string.

When the field exists, the helper accesses `Recordset.Fields(fieldLookupKey)` again. MSVBVM60 ordinal **560**, `rtcIsNull` / `IsNull()`, checks the value; a Null value also becomes an empty string. A non-Null value is converted to text for optional slicing and replacement.

The managed compatibility model deliberately records this late-bound shape rather than selecting a modern ADO/DAO provider or activating a COM data control.

## Optional `Mid` slicing

When parsed `start > 0`, the helper uses MSVBVM60 ordinal **632**, `rtcMidCharVar` / VB `Mid()`.

The recovered behavior is:

```text
[field]                 -> full field text
[field,start]           -> Mid(fieldText, start)
[field,start,length]    -> Mid(fieldText, start, length) when length > 0
```

`start` is the ordinary VB one-based character position. A non-positive `start` leaves the full field value unchanged even if a positive third token is present. If `start > 0` but `length <= 0` or is absent, native code passes a Missing Variant as the third Mid argument, producing the to-end form.

## Replacement and iteration

After lookup/null handling and optional Mid slicing, the helper reconstructs the **complete original token body**:

```text
"[" & rawExtractedBody & "]"
```

It then calls `Funzioni.Sostituisci` (`+0x7F8`). That helper performs binary replacement repeatedly until no exact occurrence remains, so all identical exact bracket tokens in the current string receive the same already-resolved value.

The outer `0x00606910` loop then calls `GetInside("[", "]")` again and continues until no non-empty bracket body remains.

Consequences:

- an active source plus a missing field removes that token (replacement is empty);
- an active source plus a Null field also removes that token;
- no active record source preserves the original input and every bracket expression unchanged;
- replacement matches the complete raw body, so `[Name]`, `[Name,2]`, and `[Name,2,3]` are distinct source tokens even though all use `Name` as the lookup token.

## Managed boundary

`LegacyBracketRecordFieldSemantics` captures the proven behavior as pure state/string semantics:

- loaded-form flag addresses and source priority;
- data-control names and accessor offset;
- `Recordset` / `Fields` / `Name` late-bound member names;
- bracket/comma grammar and three-argument maximum;
- `Parola`, `Val`, `Mid`, and `IsNull` runtime contracts;
- lookup-error/Null -> empty behavior;
- one-based Mid slicing;
- exact bracket-token replace-all behavior;
- source-none -> unchanged input.

It does not instantiate `frmDatabase`, `Tabella`, `Data1`, `datPrimaryRS`, any DAO/ADO object, or any printer/hardware API.
