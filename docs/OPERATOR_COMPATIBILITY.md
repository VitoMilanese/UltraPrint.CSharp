# Operator / `Operatori.FFM` compatibility

This document records the compatibility boundary for the UltraPrint 2.2.115 operator subsystem.

## Recovered native contract

The original VB6 executable contains direct SQL/schema strings for the `Operatori` table:

- `Operatore`
- `Password`
- `Livello`
- `Privilegio`
- `Gruppo`

Recovered queries include selecting password/level/privilege/group by operator, inserting and deleting operators, and updating passwords/operators. Two startup migrations are visible directly in the native strings:

```sql
ALTER TABLE Operatori ADD COLUMN Privilegio Long;
ALTER TABLE Operatori ADD COLUMN Gruppo TEXT(50);
```

`Sub Main` also probes the operator database in this order:

1. `Db\Operatori.FFM`
2. `Operatori.FFM`

The supplied installer contains DAO 3.5/3.6 and Jet 3.5/4.0, so `.FFM` is routed through the same Jet/Access compatibility path as legacy `.mdb` files.

## Managed replacement

The WinForms application now exposes **Security** with:

- Open/Create operator database
- Login / Logout
- Change current operator password
- operator list
- create/edit/delete operator
- operator password reset/change
- raw `Livello`, `Privilegio`, `Gruppo` editing
- optional managed privilege-description table UI

`LegacyOperatorStore` uses ACE 16, ACE 12 and then Jet 4.0 provider fallback. Existing databases are opened conservatively: `Operatore`, `Password` and `Livello` must already exist. Only the two native-confirmed `Privilegio` and `Gruppo` migrations are automatically added when missing.

A newly created `Operatori.FFM` is created through ADOX and receives a managed schema based on the recovered fields. Creation therefore requires an installed Jet/ACE/ADOX stack; opening an existing database only requires a compatible OLE DB provider.

## Deliberately unresolved

A real legacy `Operatori.FFM` was not present in the supplied application directory, so these points are not claimed as exact yet:

- whether the stored password is always plain text or transformed by another routine before comparison;
- password case-sensitivity in every original workflow;
- the exact meaning/range of `Livello`;
- whether `Privilegio` is an ID, level, mask, or another numeric policy value;
- the exact privilege-to-menu/action mapping used by `MainForm.PuoFare` / `SetPrivilegi`;
- whether login is mandatory at every startup or only when enabled by configuration.

For that reason the managed application preserves the raw numeric values and does **not** invent privilege meanings. Operator-management actions require an authenticated operator once the database contains at least one account, but application menu gating is not enabled until the native mapping is recovered.

## Validation needed

For exact parity, test with a real production `Operatori.FFM` and compare UltraPrint 2.2.115 and the managed replacement for:

1. operator enumeration;
2. successful and failed password login;
3. password change;
4. create/edit/delete operator;
5. `Livello`, `Privilegio`, `Gruppo` persistence;
6. menu/action availability for several privilege combinations.

Until that side-by-side test exists, the subsystem is marked **Partial/strong**, not fully Implemented.
