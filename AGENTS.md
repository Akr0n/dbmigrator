# AGENTS.md

Windows desktop tool (Avalonia, .NET 10, MVVM/ReactiveUI) migrating schema and data
between **SQL Server, PostgreSQL, Oracle** in any source→target combination.
`src/DatabaseMigrator.Core` holds all migration logic (no UI); `src/DatabaseMigrator` is the
Avalonia app. Solution: `DatabaseMigrator.sln`.

## Key Commands

```powershell
dotnet build DatabaseMigrator.sln -c Release          # build everything (Roslyn/Avalonia analyzers run here; no separate lint)
dotnet run --project src/DatabaseMigrator              # run the app
.\publish.ps1                                          # self-contained single-file exe -> .\release\DatabaseMigrator.exe
dotnet test DatabaseMigrator.sln -c Release            # unit + headless UI tests only; E2E self-skip (see below)
dotnet test tests/DatabaseMigrator.Tests/DatabaseMigrator.Tests.csproj --filter "FullyQualifiedName~TC001"
.\scripts\run-e2e-matrix.ps1                           # full cross-DB E2E: spins up 3 DB containers, seeds, runs Category=E2E, tears down
.\scripts\run-e2e-matrix.ps1 -KeepContainers           # leave containers up afterwards
.\scripts\run-e2e-matrix.ps1 -SkipStartup              # reuse already-running containers
```

## Testing

- E2E tests carry `[Trait("Category","E2E")]` and **return early unless `DBMIGRATOR_RUN_E2E=true`** (set automatically by `run-e2e-matrix.ps1`)
- UI tests (`tests/DatabaseMigrator.UiTests`) use Avalonia.Headless + xUnit, Windows only; drive **real `MainWindow`/`MainWindowViewModel`** with fake `IDatabaseService` (parallelization disabled in `AssemblyInfo.cs`)
- `FullRunE2ETests` (SQL Server fixture, `ACT_*` FK customer scenario) needs `DBMIGRATOR_RUN_E2E=true`

## Architecture

**Core services** (`src/DatabaseMigrator.Core/Services`): `DatabaseServiceBase` (retry logic, connection factory, identifier escaping), `DatabaseService` (connection test, table discovery, data migration), `SchemaMigrationService` (DDL, 25+ type mappings), `ScriptGenerationService` (Generate Script `.sql` export), `ForeignKeyService` + `TableDependencyOrderer` (parents-first load, keys off/on on SQL Server), `DeleteCascadeReach` (Oracle cascade pre-check), `SourceResume` (big-table resume), `TableSelection` (filter/select-all rules), `RuntimeOptionsProvider`, `CredentialProtectionService` (DPAPI passwords).

**Cross-dialect invariant** (most important thing to get right):
| Target | Case | Quoting |
|--------|------|---------|
| SQL Server | preserved | `[brackets]` |
| PostgreSQL | lower-cased | `"double quotes"` |
| Oracle | UPPER-cased | none |

Apply via `FormatTableName`/`FormatColumnName`; introspection queries must match (Oracle `all_tab_columns` = UPPERCASE, PostgreSQL `information_schema` = lowercase). Getting this wrong yields silent "column not found on target" failures. All dialect branching lives in Core on `DatabaseType`.

## Runtime Configuration

`appsettings.json` next to the exe, then `%LOCALAPPDATA%\DatabaseMigrator\appsettings.json`, then `DBMIGRATOR_*` env vars win (see `Models/RuntimeOptions.cs`):
- `DBMIGRATOR_BATCH_SIZE` (default 1000; wide tables get fewer rows/`INSERT` on SQL Server)
- `DBMIGRATOR_COMMAND_TIMEOUT_SECONDS`, `DBMIGRATOR_RETRY_COUNT`, `DBMIGRATOR_RETRY_INITIAL_DELAY_MS`, `DBMIGRATOR_ENABLE_RETRIES` (`false` also disables source-resume)
- `DBMIGRATOR_LOG_MAX_FILE_MB`, `DBMIGRATOR_LOG_RETENTION_DAYS`, `DBMIGRATOR_LOG_MAX_ARCHIVED_FILES`, `DBMIGRATOR_TRUST_SERVER_CERTIFICATE`, `DBMIGRATOR_ALLOW_PLAINTEXT_CONFIG_FALLBACK`

## E2E Infrastructure (Podman-native, do not "simplify")

- `scripts/run-e2e-matrix.ps1` uses **native Podman**, no compose (fixtures seed after health via `cp` + `exec` of in-container clients; nothing bind-mounted)
- Engine via `DBMIGRATOR_CONTAINER_ENGINE` (default `podman`, `docker` works), resolved by `scripts/container-engine.ps1`)
- **SQL Server via `127.0.0.1`, never `localhost`** (container is IPv4-only; `SqlClient` won't fall back from `::1`)
- Container network pinned to **MTU 1280** (WSL egress MTU) for SQL Server TLS handshake
- Oracle init: `sqlplus / as sysdba` with **`SET DEFINE OFF`** (init SQL contains `&`); validate by row count, not exit code

## Registro in Obsidian

A fine lavoro, aggiungi una voce sotto "## Registro modifiche AI" nel file
C:\Users\feder\OneDrive\Lavoro\_progetti_personali\_obsidian\Progetti\dbmigrator.md
con data, cosa è stato chiesto, cosa è stato modificato e come è stato verificato,
in italiano, massimo 5 righe.

## Uso di CodeGraph
Per capire il codice usa PRIMA gli strumenti codegraph (codegraph_context, codegraph_explore) invece di leggere molti file. Leggi un file intero solo quando devi modificarlo.

## Documentazione (leggere solo quando serve)
- Struttura o nuovi moduli: docs/ARCHITECTURE.md
- Rete, file, input esterni, dati: docs/SECURITY.md
- Interfaccia grafica: docs/DESIGN.md
- Stato del lavoro: STATO.md (leggere a inizio sessione, aggiornare a fine passo)