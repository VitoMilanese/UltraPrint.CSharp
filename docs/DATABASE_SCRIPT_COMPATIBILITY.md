# Database / Tabella ScriptControl compatibility

This slice covers the database objects injected by native `Funzioni.AddObjects` in UltraPrint 2.2.115.

## Native object identity

The original application registers the same global `frmDatabase` object twice:

- `Db`
- `frmDatabase`

`Tabella` is a different global form, but it participates in the same record/database workflow. The managed ScriptControl registration therefore uses **one** `LegacyScriptDatabaseFacade` instance for both `Db` and `frmDatabase`, plus a separate `LegacyScriptTableFacade` backed by the same host state.

The relative order among currently implemented AddObjects entries is preserved:

`Me -> Mainform -> Db -> frmDatabase -> Carta -> Tabella -> Fn -> Funzioni -> File -> App`

Unavailable legacy objects that belong between those names (`Preview`, `Sequenza`, `Stampa`, `Chip`, etc.) are skipped rather than replaced with empty stubs.

## `frmDatabase.RiempiTabelle`

Native entry: `0x00568260`.

The method ends with `ret 4`, so it is a zero-explicit-argument VB6 instance Sub. Its native body enumerates table/query information and contains the literal pairs `t` / `Table` and `Q` / `Query`.

The managed implementation refreshes table/view names from the active `ILegacyRecordSource` selected for the current layout.

## `frmDatabase.NometipoCampo`

Native entry: `0x0056E4A0`.

The native function reads one caller argument and writes a BSTR result through the hidden return slot. The recovered switch is exact for this build:

| DAO value | UltraPrint result |
| ---: | --- |
| 1 | `YESNO` |
| 2 | `BYTE` |
| 3 | `INTEGER` |
| 4 | `LONG` |
| 5 | `CURRENCY` |
| 6 | `SINGLE` |
| 7 | `DOUBLE` |
| 8 | `DATE` |
| 10 | `TEXT` |
| 11 | `LONGBINARY` |
| 12 | `MEMO` |
| 16 | `AUTOINCRFIELD` |

Other values leave the function's initially empty result unchanged. `LegacyDaoFieldTypeCatalog` reproduces this behavior.

## `Tabella.Trovarecord`

Native entry: `0x00523850`.

The method ends with `ret 4`, confirming no explicit arguments. Native string references show that it constructs record SQL from fragments including `Select`, `From`, `where` and `order by`, then refreshes the table form's recordset.

The managed `Tabella.Trovarecord()` re-executes the current layout database/query/table state. That state is the same state persisted by the normal **Database / Records** workspace in `.ly.data.json`, with the decoded `CardLayout.DatabasePath` / `Sql` values used as fallback.

## Shared managed runtime

`WinFormsLegacyDatabaseHost` is registered for the lifetime of `MainForm`. It follows whichever layout is currently displayed, opens the same Access/FFM/DBF/Excel/CSV/text provider abstraction used by the database workspace, refreshes table names, and materializes the current query/table recordset for ScriptControl calls.

When the normal **Database / Records** workspace is open for that same layout, `DatabaseWorkspaceRuntimeBridge` reads the exact selected `DataRowView` from its visible grid. That visible row, row index and row count take precedence over the host's fallback recordset. This means the compatibility layer now has the same current record the operator is looking at, which is the state required by the later `Carta.Record2Card` bridge.

If the workspace is not open, the host falls back to its own materialized recordset using the same persisted database/query/table settings. This intentionally avoids a second script-only database configuration.

## Still unresolved: `Tabella.Carica`

Native entry: `0x0051C820`.

The body demonstrably consumes caller-provided data and passes it through `Funzioni.Parola`; however the complete source-level signature and parameter meaning have not yet been proven. The native epilogue alone is not enough to distinguish the remaining VB6 calling-shape details safely.

For that reason `Carica` is documented in `LegacyScriptDatabaseContract` but is **not** exposed as a guessed COM method yet.

## Remaining parity work

- decode `Tabella.Carica` exactly;
- map the old Tabella form's field-filter controls so `Trovarecord` can reproduce its QBE-style search UI rather than only the already-built SQL/table state;
- make script-driven record navigation update the visible Database / Records selection when that behavior is proven/required (visible selection -> script state already works);
- recover any script-used `frmDatabase` control properties from real production `.vbs` files;
- wire `Carta.Record2Card` to the now-shared current-record state once its native parameter semantics are confirmed.
