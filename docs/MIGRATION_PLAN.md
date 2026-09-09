# Migration plan

## Phase 1 — compatibility foundation (started)

- [x] identify VB6 Native code and object graph
- [x] recover startup switches/paths
- [x] recover `Campo.ini` current-value semantics
- [x] create C# domain/legacy separation
- [x] create `.ly` probe without guessing serialization
- [x] extract and inspect `CadBox.ocx`
- [x] obtain/probe a real `.ly` sample
- [~] finish byte-level `.ly` codec (safe partial reader/writer implemented; unknown ranges preserved)

## Phase 2 — card editor (started)

- [x] replace the diagnostic-only view with a managed WinForms canvas
- [x] field selection and mouse move
- [ ] resize handles and z-order editing
- [x] text/image/photo-placeholder rendering
- [ ] table rendering
- [x] front/back view for the supplied fixture
- [ ] rulers/grid/snapping
- [x] preserve mm/DPI/twip conversion behavior
- [x] safe load/save of decoded legacy `.ly` values while preserving unknown bytes

## Phase 3 — data and scripting

- recover DAO/Jet schema usage
- implement operator/login database compatibility
- implement database-field binding and SQL flows
- define a safe VBScript compatibility strategy

## Phase 4 — printing and devices

- normal Windows print pipeline
- card printer adapters
- magnetic stripe / smart-card module boundaries
- image acquisition/scanner replacement

## Phase 5 — parity verification

For each migrated feature, compare the managed implementation against the old application using the same legacy files, database rows, printer settings, and expected rendered geometry.
