# Verification

The migration uses temporary Windows CI only while a compatibility block is being developed. Temporary workflow files are removed from the final PR history.

## Current verified scenarios

On Windows with .NET 8:

- solution restore;
- Release build of the full solution;
- byte-identical load/save of an unchanged `TPMFAO19.ly`;
- insert/duplicate/delete field save+reload;
- layout dimensions/DPI and front/back fixture decoding;
- `.ffm`, DBF and Excel source-kind detection;
- CSV/text parsing, including quoted delimiters;
- `%FIELD%` record-to-card substitution;
- explicit managed field-to-column binding override;
- record binding does not mutate the source layout template;
- non-destructive `.ly.data.json` database/query/table/binding state round-trip;
- recovered `Operatori.FFM` lookup order: `Db\Operatori.FFM` first, root fallback second;
- operator-database locator fallback and precedence behavior.

The Security/operator UI and Jet/ACE store compile as part of the full Windows solution. Real Access/Jet operator CRUD/authentication is deliberately **not** reported as fixture-verified yet because no representative legacy `Operatori.FFM` was supplied and provider availability is machine-dependent (ACE 16/12 or legacy Jet 4.0 x86). Exact password and privilege semantics therefore remain side-by-side validation items rather than inferred tests.
