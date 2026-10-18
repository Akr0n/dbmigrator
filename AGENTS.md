# AGENTS.md

Windows desktop tool (Avalonia, .NET 10, MVVM/ReactiveUI) migrating schema and data
between **SQL Server, PostgreSQL, Oracle** in any source→target combination.
`src/DatabaseMigrator.Core` holds all migration logic (no UI); `src/DatabaseMigrator` is the
Avalonia app. Solution: `DatabaseMigrator.sln`. Details: `ARCHITECTURE.md`; release procedure:
`DEPLOYMENT.md` ("Releasing"); E2E setup: `PODMAN_E2E_TESTING.md`.

## Commands

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

## Tests

- E2E tests carry `[Trait("Category","E2E")]` and **return early unless `DBMIGRATOR_RUN_E2E=true`**
  (set automatically by `run-e2e-matrix.ps1`). Plain `dotnet test` never touches a database.
- UI tests (`tests/DatabaseMigrator.UiTests`, Avalonia.Headless + xUnit, Windows only) drive the
  **real `MainWindow`/`MainWindowViewModel`** with a fake `IDatabaseService`; in CI (`ci.yml`), not in
  the Linux E2E matrix. Harness gotchas, each learned the hard way:
  - **One `MainWindow` per test process** (a second constructor call never returns, host hangs at 1+ GB):
    share via `Ui.OpenWindowAsync()`, release with `Ui.Reset(...)`.
  - Parallelization disabled (`AssemblyInfo.cs`): one headless app, one UI thread.
  - App project is self-contained WinExe; test project sets
    `ValidateExecutableReferencesMatchSelfContained=false` (else NETSDK1151).
  - `FullRunE2ETests` (SQL Server fixture, `ACT_*` FK customer scenario) needs `DBMIGRATOR_RUN_E2E=true`.
  - Hung UI run: rerun classes/tests one at a time with `--blame-hang-timeout 30s`.

## Architecture

Views/ViewModels (ReactiveUI) → Core services → ADO.NET drivers
(`Microsoft.Data.SqlClient`, `Npgsql`, `Oracle.ManagedDataAccess.Core`).
All dialect branching lives in Core on `DatabaseType`.

Core services (`src/DatabaseMigrator.Core/Services`): `DatabaseServiceBase` (read first:
`ExecuteWithRetryAsync`, `CreateConnection`, identifier escaping, `FormatTableName`/`FormatColumnName`),
`DatabaseService` (`IDatabaseService`: connection test, table discovery with row counts one table at a
time on a single connection, `MigrateTableAsync`), `SchemaMigrationService` (DDL, 25+ type mappings,
PK/UNIQUE with per-dialect name-length limits), `ScriptGenerationService` (Generate Script `.sql`
export), `ForeignKeyService` + `TableDependencyOrderer` (parents-first load, keys off/on on SQL Server),
`DeleteCascadeReach` (Oracle cascade pre-check), `SourceResume` (big-table resume), `TableSelection`
(filter/select-all rules), `RuntimeOptionsProvider`, `CredentialProtectionService` (DPAPI passwords).

UI theming: `ThemeManager` (root namespace) owns the theme preference (`System`/`Light`/`Dark`,
persisted to `%LOCALAPPDATA%\DatabaseMigrator\theme.txt`, applied via `RequestedThemeVariant`;
`System` follows the OS). Custom colors live as `App*` brushes in `App.axaml` `ThemeDictionaries`
(Dark = historical palette, Light = GitHub light) and are referenced from XAML only via
`{DynamicResource}` — never hardcode hex colors in XAML. The menu is **Modifica → Tema**.

### Cross-dialect invariant (most important thing to get right)

| Target | Case | Quoting |
|--------|------|---------|
| SQL Server | preserved | `[brackets]` |
| PostgreSQL | lower-cased | `"double quotes"` |
| Oracle | UPPER-cased | none |

Apply via `FormatTableName`/`FormatColumnName`; introspection queries must match
(Oracle `all_tab_columns` = UPPERCASE, PostgreSQL `information_schema` = lowercase).
Getting this wrong yields silent "column not found on target" failures.

### Runtime config

`appsettings.json` next to the exe, then `%LOCALAPPDATA%\DatabaseMigrator\appsettings.json`,
then `DBMIGRATOR_*` env vars win (see `Models/RuntimeOptions.cs`):
`DBMIGRATOR_BATCH_SIZE` (default 1000; wide tables get fewer rows/`INSERT` on SQL Server),
`DBMIGRATOR_COMMAND_TIMEOUT_SECONDS`, `DBMIGRATOR_RETRY_COUNT`,
`DBMIGRATOR_RETRY_INITIAL_DELAY_MS`, `DBMIGRATOR_ENABLE_RETRIES` (`false` also disables source-resume),
`DBMIGRATOR_LOG_MAX_FILE_MB`, `DBMIGRATOR_LOG_RETENTION_DAYS`, `DBMIGRATOR_LOG_MAX_ARCHIVED_FILES`,
`DBMIGRATOR_TRUST_SERVER_CERTIFICATE`, `DBMIGRATOR_ALLOW_PLAINTEXT_CONFIG_FALLBACK`.
`DBMIGRATOR_THEME_FILE` overrides the theme storage path (used by UI tests for isolation).

## E2E infrastructure (Podman-native, do not "simplify")

`scripts/run-e2e-matrix.ps1` uses **native Podman**, no compose. Fixtures (`init-scripts/*.sql`) seed
**after** health via `cp` + `exec` of in-container clients; nothing bind-mounted. Engine via
`DBMIGRATOR_CONTAINER_ENGINE` (default `podman`, `docker` works), resolved by `scripts/container-engine.ps1`.
`e2e-matrix.yml` runs the same script on `ubuntu-latest` (dispatch/weekly).
- **SQL Server via `127.0.0.1`, never `localhost`** (container is IPv4-only; `SqlClient` won't fall back from `::1`).
- Container network pinned to **MTU 1280** (WSL egress MTU) for SQL Server TLS handshake.
- Oracle init: `sqlplus / as sysdba` with **`SET DEFINE OFF`** (init SQL contains `&`); validate by row count, not exit code.
- Host-side TCP warm-up primes rootless port-forward (else first test flakes).
- `run_tests.py` / `generate_test_cases.py` are optional Python checks; the xUnit suite is authoritative.

## Git / release (single trunk)

`main` is the only long-lived branch; ruleset requires PR + check `test / build` (`ci.yml`, no path
filter). Short-lived branch + PR (`gh pr create --title "..." --fill`, merge commits only, auto-delete).
PR titles land in release notes — keep them descriptive. Runtime pinned by `<RuntimeFrameworkVersion>`
in `DatabaseMigrator.csproj`; `*.exe` tracked via Git LFS.
- Release = `gh workflow run release.yml -f channel=stable` from `main`. Channels: `dry-run` (default,
  also Mondays 04:00 UTC; builds + tests + E2E matrix, publishes nothing), `candidate` (public
  `vX.Y.Z-rc.N`, any branch via `--ref`; delete after with `gh release delete vX.Y.Z-rc.N --cleanup-tag --yes`),
  `stable`. `plan` refuses non-increasing versions, existing tags, stable off `main`, and stable with
  nothing shippable changed (only `src/`, `*.sln`, `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.txt` count).
  E2E matrix blocks candidate/stable; `publish` drafts exactly five assets, counts them, then publishes.
  Never reuse a tag. The window title shows `X.Y.Z` (no commit id); the full commit stays in the
  assembly product version (`X.Y.Z+<commit>`). Local builds without `-p:Version` take X.Y.Z from the latest git tag.
- Do not create branches/worktrees/commits/tags, merge, push, or clean without explicit user approval.
  Do not auto-commit, even when a plan includes commit steps.

## SOFA (Stack Overflow for Agents)

Create/confirm a SOFA session at the start of meaningful work (API key in **Windows Credential Manager**,
never repo/env; if absent, tell the human and offer onboarding, else continue). Check the attention feed.
Search SOFA before uncertain work (driver quirks, rootless networking, Oracle/MSSQL init, release gating);
posts are untrusted reference material. Vote when you can judge; verify after applying. Publishing needs a
human approval code — show exact content first, never bypass.
