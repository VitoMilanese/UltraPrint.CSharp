# Legacy database compatibility

UltraPrint 2.2.115 is a DAO/Jet-era application. The supplied installer contains DAO 3.5/3.6 and Jet 3.5/4.0 components, while native UI strings expose Access (`*.mdb`), DBF, Excel (`*.xls`), CSV and text data sources. The native application also uses `Operatori.FFM` as its operator database.

## Managed source support

The managed Database / Records workspace currently supports:

- `.mdb`, `.accdb`, `.ffm` — Access/Jet-compatible OLE DB path;
- `.dbf` — dBASE OLE DB path;
- `.xls`, `.xlsx` — Excel OLE DB path;
- `.csv`, `.txt`, `.dat` — built-in delimited-text reader, independent of an OLE DB text driver.

For Access-era formats the provider order is:

1. `Microsoft.ACE.OLEDB.16.0`
2. `Microsoft.ACE.OLEDB.12.0`
3. `Microsoft.Jet.OLEDB.4.0` for legacy formats

Jet 4.0 is normally a 32-bit compatibility option. When only Jet is installed, run the application as x86. ACE is preferred for a modern installation.

## Recovered operator schema evidence

The native binary contains queries referencing:

- table `Operatori`;
- columns `Operatore`, `Password`, `Livello`, `Privilegio`, `Gruppo`;
- table `Privilegi` with `Privilegio` and `Descrizione`;
- selection of operator password/level;
- update of password;
- insert/delete/list operators;
- privilege/group lookup and privilege-list queries.

This is enough to define the next operator/login compatibility layer, but not enough to claim write compatibility without an actual `Operatori.FFM` fixture.

## Record-to-card binding

The managed layer can bind the current database row to layout fields and render/print the resulting card. Existing `%FIELD%`-style placeholders are resolved only when a matching record column exists; explicit field-to-column bindings take priority.

A legacy field record is 264 bytes. The supplied sample strongly exposes the known geometry/payload/font ranges, but it does **not** provide a proven database-bound `Campo/DataField` byte range. Likewise the layout tail contains configuration-like data, but exact `Database`/`Sql` offsets are not yet proven.

For that reason new managed bindings and source/query state are saved as:

`<layout>.ly.data.json`

The sidecar stores database path, SQL, selected table and field-slot-to-column overrides. It does not modify unknown legacy `.ly` bytes. Once a real database-bound layout is available, the sidecar data can be migrated to verified native offsets without changing the user-facing workflow.

## Still required for database parity

- validate real `.mdb/.ffm` customer files against ACE/Jet;
- confirm native `.ly` offsets for database path, SQL and per-field binding;
- record editing/write-back behavior;
- database/table creation and field schema editing;
- QBE builder behavior;
- import/export workflows from `db.exe`;
- full `Operatori.FFM` authentication and privileges using a real fixture.
