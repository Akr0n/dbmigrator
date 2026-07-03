# E2E Testing Setup with Podman

This setup starts 3 containers (PostgreSQL, Oracle `gvenzl/oracle-free`, and SQL Server),
preloaded with test data, for testing the DatabaseMigrator application. Orchestration is
**native Podman** — no `docker-compose` and no external compose provider are required.

## Prerequisites

- **Podman** installed, with the Podman machine running.
  On Windows the machine is a WSL2 VM: `podman machine start` (it inherits ~50% of host
  RAM from WSL2 automatically, which is enough for all three databases).
- **PowerShell 7+** (for the scripts).
- ~4 GB of disk for the three container images (pulled automatically on first run).

Check the engine is reachable:

```powershell
podman info
podman machine list   # the machine must be "Currently running"
```

> Podman is frequently **not on PATH** right after install on Windows. The E2E tooling
> resolves it automatically (see *Container engine selection* below), so the scripts work
> even when `podman` is not on your PATH.

## Automated Cross-DB Matrix

Run the automated E2E matrix (SQL Server <-> PostgreSQL <-> Oracle):

```powershell
# Starts containers, waits for health checks, seeds fixtures, runs E2E tests, tears down
.\scripts\run-e2e-matrix.ps1

# Keep containers running after tests (useful for debugging / manual checks)
.\scripts\run-e2e-matrix.ps1 -KeepContainers

# Assume the containers are already running (skip startup, still seeds missing fixtures)
.\scripts\run-e2e-matrix.ps1 -SkipStartup
```

The script:

1. Resolves the container engine (Podman by default) and exports it via
   `DBMIGRATOR_CONTAINER_ENGINE` so the tests use the same one.
2. Creates the `dbmigrator-network` network and runs the three DB containers with explicit
   `podman run` commands — same names, ports and healthchecks as the retired
   `docker-compose.yml`, with **named volumes** (no host bind-mounts).
3. Waits for each container to become healthy.
4. Seeds the shared fixtures by copying the init `.sql` into the container (`podman cp`) and
   running it through the native client (`psql` / `sqlcmd` / `sqlplus`). Each step is
   idempotent and gated on an existence check, so re-runs are safe.
5. Executes the xUnit tests marked `Category=E2E` with `DBMIGRATOR_RUN_E2E=true`.
6. Tears everything down (containers, volumes, network) unless `-KeepContainers` is set.

## Container engine selection

The engine is chosen via the `DBMIGRATOR_CONTAINER_ENGINE` environment variable, resolved by
`scripts/container-engine.ps1` and mirrored by the E2E test code:

| Value                                   | Behaviour                                              |
| --------------------------------------- | ------------------------------------------------------ |
| *unset*                                 | `podman` (the default going forward)                   |
| `podman`                                | looked up on PATH, else the standard Windows install dir |
| `docker`                                | looked up on PATH (transition-period fallback)          |
| absolute path to an executable          | used verbatim                                           |

Podman and Docker share the CLI surface used here (`run` / `exec` / `cp` / `inspect` /
`logs` / `rm` / `network` / `volume`), so `docker` still works during the transition:

```powershell
$env:DBMIGRATOR_CONTAINER_ENGINE = 'docker'
.\scripts\run-e2e-matrix.ps1
```

## E2E Test Suite

All E2E tests carry the `Category=E2E` trait and run only when `DBMIGRATOR_RUN_E2E=true`.

### `CrossDatabaseDataMigrationMatrixTests`
Migrates a sample table across every source/target database pair and verifies the row
count round-trips.

### `ScriptGenerationE2ETests`
End-to-end tests for the **"Generate Script"** feature (`ScriptGenerationService` — the
tab that exports DDL + data of selected objects to a `.sql` file).

- **`GenerateScript_SameDialect_RoundTripsSchemaAndData`** (per dialect) — generates a
  script for a subset of objects (the `users`/`products`/`orders` tables plus fixture
  view/index/sequence), **executes the generated `.sql` against the database via the
  native client** (`psql` / `sqlcmd` / `sqlplus`, invoked through `<engine> exec` on the
  E2E containers) and verifies that schema and data are recreated faithfully. This proves
  the generated script is valid and re-runnable.
- **`GenerateScript_CrossDialect_ProducesTargetDialectScript`** — generates a script in a
  *different* target dialect than the source and checks the DDL was translated. Not
  executed: cross-dialect translation of some constructs has documented limits.
- **`GenerateScript_HonoursIncludeFlags`** — verifies the `IncludeSchema` / `IncludeData`
  options.

The test class creates its own fixture objects idempotently (a view `vw_user_orders`, an
index `idx_e2e_orders_user`, a sequence `seq_e2e_demo`) inside the `migration_test`
schema; it does not modify the shared init scripts. Running the generated scripts requires
the containers to be up (the native SQL clients live inside them).

## Connection Credentials

> Use **`127.0.0.1`**, not `localhost`, as the host. Under Podman the SQL Server container
> listens only on IPv4; `localhost` resolves to IPv6 `::1` first and Microsoft.Data.SqlClient
> does not fall back to IPv4, so the connection hangs until timeout. `127.0.0.1` is
> unambiguous and works for all three databases.

### PostgreSQL
- **Host**: `127.0.0.1`
- **Port**: `5432`
- **Database**: `testdb`
- **User**: `pguser`
- **Password**: `pgpass123`
- **Schema**: `migration_test`

### Oracle
- **Host**: `127.0.0.1`
- **Port**: `1521`
- **Database/Service**: `FREEPDB1`
- **User**: `migration_test`
- **Password**: `oraclepass123`

### SQL Server
- **Host**: `127.0.0.1`
- **Port**: `1433`
- **Database**: `TestDB`
- **User**: `sa`
- **Password**: `SqlServer@123`
- **Schema**: `migration_test`

## Available Tables

Each database contains the following tables with test data:

| Table | Records | Special Columns |
|-------|---------|-----------------|
| users | 4 | password_hash (binary) |
| products | 5 | image, thumbnail (binary) |
| orders | 8 | references users, products |
| audit_log | 4 | change_data (binary) |

(Plus a set of edge-case tables for type/NULL/reserved-word/FK-chain coverage.)

## Verifying Test Data

### PostgreSQL
```powershell
podman exec dbmigrator-postgres psql -U pguser -d testdb -c "SELECT COUNT(1) FROM migration_test.users;"
```

### SQL Server
```powershell
podman exec dbmigrator-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P SqlServer@123 -C -Q "SELECT COUNT(1) FROM migration_test.users;"
```

### Oracle
```powershell
"SELECT COUNT(1) FROM users;" | podman exec -i dbmigrator-oracle sqlplus -S migration_test/oraclepass123@//localhost:1521/FREEPDB1
```

## Stopping / Resetting

`run-e2e-matrix.ps1` tears down automatically. To do it manually:

```powershell
# Remove the containers
podman container rm --force dbmigrator-postgres dbmigrator-oracle dbmigrator-sqlserver

# Remove the data volumes (complete reset)
podman volume rm postgres_data oracle_data sqlserver_data

# Remove the network
podman network rm dbmigrator-network
```

## Troubleshooting

### A container doesn't become healthy
```powershell
podman ps -a
podman logs --tail 100 dbmigrator-oracle     # Oracle takes 1-3 minutes to initialize
```
Wait for Oracle's "DATABASE IS READY TO USE" message.

### Port already in use
```powershell
netstat -ano | findstr :1433
netstat -ano | findstr :5432
netstat -ano | findstr :1521
```
Stop the conflicting service (e.g. a leftover Docker container) or free the port.

### Podman machine not running
```powershell
podman machine list
podman machine start
```

## Notes

- **No host bind-mounts**: init scripts are copied into the container with `podman cp` and
  run via the native client after the container is healthy. This avoids the fragility of
  bind-mounting Windows host paths into a rootless Podman/WSL machine.
- **Binary Data**: test data uses ASCII-encoded mock data for simplicity.
- **Foreign Keys**: orders reference users and products. Migrate in order: users →
  products → orders.
- **Schema Names**: PostgreSQL and SQL Server use the `migration_test` schema; Oracle uses
  the `migration_test` user as schema.
