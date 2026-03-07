# Refresh Regression Checklist

## Scope

Regression coverage for the table refresh hotfix in `MainWindowViewModel`:

- `Refresh`
- `Select All -> Refresh`
- `Deselect All -> Refresh`
- Active filter + `Refresh`

## Manual Validation Protocol

1. Connect source and target DBs.
2. Open the table list tab and trigger `Refresh` 5 times consecutively.
3. Click `Select All`, then `Refresh` 5 times.
4. Click `Deselect All`, then `Refresh` 5 times.
5. Apply a filter (`users`, `migration_test`, or partial schema/table name), then `Refresh` 5 times.
6. While refresh is running, trigger refresh again to verify re-entrancy guard.

Expected result for all steps:

- no crash / no UI freeze;
- selected tables are preserved when still present after refresh;
- counters (`SelectedTablesCount`, `TotalRowsToMigrate`) remain consistent;
- filter result lists remain coherent after each refresh.

## Automated Regression Executed

The following checks were executed in this implementation cycle:

- `dotnet build DatabaseMigrator.sln --configuration Release`
- `dotnet test DatabaseMigrator.sln --configuration Release --verbosity minimal`

Automated tests now include:

- secure config round-trip (`ConnectionConfigSecurityTests`)
- runtime option safety bounds (`RuntimeOptionsProviderTests`)
- cross-DB E2E matrix harness (`CrossDatabaseDataMigrationMatrixTests`, gated by `DBMIGRATOR_RUN_E2E=true`)
