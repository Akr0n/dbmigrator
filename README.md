# Database Migrator

A Windows tool for migrating data between relational databases (SQL Server, Oracle, PostgreSQL).

## Features

- 🔄 Cross-database data migration
- 🗄️ Support for SQL Server, Oracle, PostgreSQL
- 🎨 Modern graphical interface (Avalonia UI)
- 📊 Selective table selection with search/filter
- 🔧 Automatic data type mapping
- 🔑 Primary Key and UNIQUE constraint migration
- 📈 Real-time progress bar
- ♻️ A lost connection to the source does not lose a big table: the read resumes where it stopped (100,000 rows or more, primary key required)
- 💾 Single-file executable (.exe)
- 📁 Save/Load connection configurations
- 📝 Generate script ("Genera Script" tab): exports the DDL and/or the data of tables, views, procedures, functions, triggers, sequences and indexes of the source as a runnable `.sql` file for SQL Server, PostgreSQL or Oracle
- 🔀 Three migration modes: Schema+Data, Schema Only, Data Only
- ↩️ Automatic rollback on failure (Schema+Data mode)

## Requirements

- Windows 10/11 (64-bit)
- .NET 10.0 Runtime (included in standalone exe)

## Installation

### Method 1: Standalone Executable (Recommended)
1. Download `DatabaseMigrator.exe` from the [latest release](https://github.com/Akr0n/dbmigrator/releases/latest)
2. Run the executable directly

### Method 2: Build from Source

#### Build Prerequisites:
- .NET 10.0 SDK
- PowerShell 7+ (Windows)

#### Build and Publish:

**PowerShell:**
```powershell
# Build and publish for Windows x64
.\publish.ps1

# The executable will be in: .\release\DatabaseMigrator.exe
```

**Manual with dotnet CLI:**
```powershell
dotnet publish src/DatabaseMigrator/DatabaseMigrator.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The executable is then in `.\publish\DatabaseMigrator.exe` (without `-o` it ends up in `src\DatabaseMigrator\bin\Release\net10.0\win-x64\publish\`).

## Usage

### Step 1: Connect to Databases
1. Launch the application
2. In the "Connessioni Database" tab (the window is in Italian):
   - **Source Database**: Enter connection details for the source DB
   - **Target Database**: Enter connection details for the target DB
3. Click "Connetti ai Database". Connect opens a connection to each database you named, so the **target database must already exist**: on SQL Server and PostgreSQL the migration creates tables, not databases. On an Oracle target the Database field is the service name; if no user with that name exists, "Avvia Migrazione" tries to create one (`CREATE USER`, which needs that privilege) and carries on with it as the target login

### Step 2: Select Tables
1. Select the tables to migrate in the "Selezione Tabelle" tab
2. Use the search box to filter tables by name or schema
3. "Seleziona Tutto" (select all) selects only the tables the filter currently shows, so you can select one schema at a time; "Deseleziona Tutto" clears every selection, including tables the filter hides. If you start a migration while selected tables are hidden by the filter, you are asked to confirm
4. Row counts are loaded automatically

### Step 3: Choose Migration Mode
Select one of three migration modes:
- **Schema + Data** ("Schema + Dati"): Creates tables and migrates data (tables created by the run are dropped again if the data load fails)
- **Schema Only** ("Solo Schema"): Creates only the table structure without data
- **Data Only** ("Solo Dati"): Migrates data only (tables must already exist in target)

**Data modes empty the target tables first.** With Schema + Data and Data Only, every selected table is emptied on the target before it is loaded (SQL Server `TRUNCATE`, PostgreSQL `TRUNCATE ... CASCADE`, Oracle `DELETE FROM`), so its previous rows are replaced. Tables are loaded parents-first following the target's FOREIGN KEYs; on SQL Server the keys are switched off for the load and re-validated at the end. If emptying a table would also wipe tables you did not select and they hold rows (PostgreSQL `CASCADE`, Oracle `ON DELETE CASCADE` / `SET NULL`), the migration stops and asks you: select those tables too, or empty them yourself. SQL Server asks whenever an enabled foreign key with a delete action points at the table from a table you did not select, whether or not that table holds rows. "Continua" adds the rows without emptying the table.

### Step 4: Start Migration
1. Go to the "Migrazione" tab
2. Review the status information
3. Click "Avvia Migrazione" (start migration)
4. Monitor progress with the progress bar. While a migration or a reload runs, "Connetti ai Database", "Carica Configurazione" and the migration-mode buttons are disabled. The "Log" tab shows what is happening; its "Segui" button keeps the view on the last line as it grows, and switching it off lets you scroll back and read

## Connection Configuration

### SQL Server
- **Type**: SqlServer
- **Server**: Server name or IP
- **Port**: 1433 (default)
- **Database**: Database name
- **Username**: sa or SQL user (leave empty for Windows Auth)
- **Password**: Account password

### Oracle
- **Type**: Oracle
- **Server**: host name or IP of the listener (tnsnames.ora aliases are not used)
- **Port**: 1521 (default)
- **Database**: the Oracle service name (e.g., FREEPDB1, XE); a SID that is not also a registered service name does not connect
- **Username**: Oracle user
- **Password**: Account password

### PostgreSQL
- **Type**: PostgreSQL
- **Server**: Server name or IP
- **Port**: 5432 (default)
- **Database**: Database name
- **Username**: postgres or other user
- **Password**: Account password

## Save/Load Configurations

The application supports saving and loading connection configurations:

- **Save**: File → Salva Configurazione...
- **Load**: File → Carica Configurazione...

There are no keyboard shortcuts for them.

Configurations are saved as JSON files and include both source and target connection settings.
Passwords are protected with Windows DPAPI (`passwordProtected: true`) by default.

## Connection security

- **SQL Server** connections are always encrypted. The server certificate is verified unless **"Accetta certificato server (SSL)"** is
  ticked. The box starts unticked (set `DBMIGRATOR_TRUST_SERVER_CERTIFICATE=true`, or `TrustServerCertificateByDefault` in
  `appsettings.json`, to start it ticked): tick it for a SQL Server in a container or with a self-signed certificate. A failed
  connection says so.
- **PostgreSQL** uses the driver's default (`SslMode=Prefer`): it encrypts when the server offers TLS and otherwise falls back to
  plaintext, without verifying the server. **Oracle** connects over plain TCP, which never negotiates TLS. Tick
  **"Richiedi connessione cifrata TLS"** to require it: PostgreSQL then uses `SslMode=VerifyFull` (`Require`, i.e. encrypted but
  unverified, when "Accetta certificato server" is also ticked) and Oracle connects over TCPS. If the server cannot provide that,
  the connection fails instead of falling back. For Oracle the certificate chain is checked against the
  Windows trust store or the client wallet; "Accetta certificato server" only skips the check that its name is the server's.
- Both choices are saved in the configuration file (`trustServerCertificate`, `requireEncryption`).
- Verified against real servers: PostgreSQL with and without TLS (self-signed certificate) and an Oracle listener without TLS, which
  refuses a required-encryption connection. Oracle TCPS against a TLS listener is covered only by a unit test on the connection string.

## Runtime Settings

Runtime settings can be configured with:

- an `appsettings.json` next to the executable (`src/DatabaseMigrator/appsettings.json` is the template; a build or publish copies it to the output folder, but the GitHub release holds the `.exe`, `RELEASE_NOTES.txt`, this README, `LICENSE` and `THIRD-PARTY-NOTICES.txt`, not that file), then `%LOCALAPPDATA%\DatabaseMigrator\appsettings.json`, which is read after it. For the downloaded executable use the second one
- environment variables (override file settings), for example:
  - `DBMIGRATOR_BATCH_SIZE`
  - `DBMIGRATOR_COMMAND_TIMEOUT_SECONDS`
  - `DBMIGRATOR_RETRY_COUNT`
  - `DBMIGRATOR_ENABLE_RETRIES` (`false` also turns off the resume of a lost source connection, see Known limitations)
  - `DBMIGRATOR_LOG_MAX_FILE_MB`
  - `DBMIGRATOR_TRUST_SERVER_CERTIFICATE`

## Data Type Mapping

The application automatically maps data types between different database systems:

| SQL Server | PostgreSQL | Oracle |
|------------|------------|--------|
| int | integer | NUMBER(10) |
| bigint | bigint | NUMBER(19) |
| varchar(n) | varchar(n) | VARCHAR2(n) |
| nvarchar(n) | varchar(n) | NVARCHAR2(n) |
| varchar(max) | text | CLOB |
| nvarchar(max) | text | NCLOB |
| datetime2(p) | timestamp(p) | TIMESTAMP(p) |
| bit | boolean | NUMBER(1) |
| text | text | CLOB |
| varbinary(max) | bytea | BLOB |
| uniqueidentifier | uuid | RAW(16) |

On Oracle the sizes are capped: `VARCHAR2` at 4000, `NVARCHAR2` at 2000 and `RAW` at 2000. `varbinary(n)` becomes `RAW(n)` within that cap and only `varbinary(max)` becomes `BLOB`; `datetime2(p)` becomes `TIMESTAMP(p)` (p is capped at 9 on Oracle and at 6 on PostgreSQL, and is 6 when the source reports no precision).

## Error Handling

- **Connection failures**: Clear error messages with troubleshooting hints
- **Schema creation errors**: Detailed logging of DDL operations
- **Constraint migration**: Primary Keys and UNIQUE constraints are automatically recreated
- **Data migration errors**: Automatic rollback of created tables (in Schema+Data mode)
- **Validation**: Data-only mode validates table existence before starting

## Logging

The application logs all operations to help with troubleshooting:
- Connection attempts and results
- Table discovery and row counts
- Schema DDL generation
- Data migration progress
- Error details with stack traces

Log files are automatically rotated and retained according to runtime settings.

## Known limitations

- **Text from PostgreSQL and Oracle into SQL Server is created as Unicode**: `nvarchar(n)`, or `nvarchar(max)` above 4000 characters, for `varchar`, `char`, `text`, `VARCHAR2`, `CHAR`, `CLOB` and the like, so Japanese, Cyrillic or Greek text survives a database with a Latin code page. Fixed-width `CHAR` columns become `nvarchar` too (a fixed-width `nchar` would double every row and could stop a table with many `CHAR` columns from being created). Oracle reports column sizes in bytes, so a multi-byte `CHAR(n CHAR)` becomes a wider `nvarchar`. `nvarchar` doubles the bytes of an index key: a PRIMARY KEY or UNIQUE column longer than 450 characters (clustered index, 900 bytes; 850 for a 1700-byte key) rejects longer values, and one above 4000 characters (`nvarchar(max)` cannot be indexed) cannot get its constraint, where `varchar` allowed both. Tables that already exist on the target keep their own column types: if one is `varchar`, text outside its code page still turns into `?`.
- **The target schema must already exist.** Schema + Data creates the tables but not their schema, and PostgreSQL's `public` schema cannot be created on SQL Server.
- **Each table is committed on its own.** If table N fails, tables 1 to N-1 that already existed on the target (or all of them in Data Only mode) stay replaced, while tables a Schema + Data run created are dropped again. On Oracle every statement is committed, so the failed table is not rolled back either: the rows already deleted and inserted stay.
- **A lost connection to the source does not restart a big table.** A table of **100,000 rows or more** is read in one stream that can last tens of minutes, ordered by its primary key from the first row on. If the connection to the source is cut in the middle of it, the tool waits (2 seconds, doubling up to 30), opens a new connection and carries on after the last row received (`ORDER BY key OFFSET n`), with the target transaction still open. It gives up after 5 attempts in a row without a new row, and the table fails as usual (rolled back, except on an Oracle target where every statement is already committed). What this does not cover:
  - Smaller tables are read as before, with no ordering and no resume: starting over costs seconds, and ordering by the key can make the server sort first when the key is not the physical order (a heap with a non-clustered key, any large PostgreSQL or Oracle table).
  - A table **without a usable primary key** cannot be resumed (reading again in an unknown order could skip or repeat rows): it fails as before (and, except on an Oracle target, is rolled back). Usable means an enforced, validated key: an Oracle key that is `DISABLE`d or `ENABLE NOVALIDATE`, and a PostgreSQL table that other tables inherit from (a partitioned table is fine), do not count. The log lists the possible causes.
  - Only the connection to the **source** is resumed: if the connection to the target is lost, the table fails (and, except on an Oracle target, is rolled back).
  - Only the step that fetches the next row is covered. On an Oracle source the content of a LOB column (`CLOB`, `BLOB`, `XMLTYPE`) can be read from the network a moment later, while the row is turned into values: a cut right there is not resumed, and the table fails as it did before. SQL Server and PostgreSQL read the whole row when they fetch it.
  - A statement that ran in the table's transaction is not retried after the server ended that transaction (a deadlock victim is rolled back as a whole): the retry would run outside any transaction. The table fails and can be run again.
  - The resume counts rows: it assumes the source does not change while the table is read. A row deleted or inserted *behind* the position reached, during the cut, shifts the rows after it by one (one skipped or one repeated). After a resume the log warns when the number of rows read differs from the count taken at the start, but the table is still committed.
  - Reading in key order can change the order rows reach the target. On a PostgreSQL or Oracle target the keys are not switched off for the load, so a table that references itself fails if a child sorts before its parent; set `DBMIGRATOR_ENABLE_RETRIES=false` to read in the source's own order (this turns off the ordering and the resume).
  - It needs `OFFSET` on the source (SQL Server 2012, PostgreSQL, Oracle 12c).

  Verified against a real SQL Server with the connection cut by a reset (one cut, two cuts, no key, below the threshold, a network that stays down) and, for the key lookup, against real PostgreSQL (including a read-only role, a mixed-case column and an inheritance parent) and Oracle (enabled, disabled and not validated keys).
- **A wide table is loaded with fewer rows per `INSERT` on a SQL Server target.** SQL Server cannot compile an `INSERT ... VALUES` of about 200,000 values (1000 rows of a 199-column table gave "ran out of internal resources and could not produce a query plan"), so a statement holds at most 30,000 values and never more than 1000 rows (SQL Server's limit for one `VALUES` list, whatever `DBMIGRATOR_BATCH_SIZE` says): 150 rows for 200 columns, the full batch (1000 rows by default) for a table of 30 columns or fewer. The log says it once per table. The `INSERT`s of a generated script for SQL Server follow the same rule. Statements are not limited by their size: a table with very large values in every row (hundreds of KB of binary, xml or text) can still build a statement over SQL Server's batch size limit, and for it `DBMIGRATOR_BATCH_SIZE` has to be lowered.
- **Sequence-backed columns are not recreated as auto-increment.** Only true identity columns are (SQL Server `IDENTITY`, PostgreSQL `GENERATED ... AS IDENTITY`, Oracle identity). A PostgreSQL `SERIAL` column or an Oracle `NEXTVAL` default is copied as a plain `DEFAULT` that the target usually cannot use: create such tables on the target yourself and migrate their data with Data Only.
- **Oracle checks before emptying a table** (an `ON DELETE CASCADE` / `SET NULL` key would otherwise empty or change tables the run does not load) are exact only when the migration user can read `DBA_CONSTRAINTS` (DBA, or `SELECT_CATALOG_ROLE`). Without that right they are deliberately cautious: the migration stops and asks when the migration user does not own the table, or a table the cascade passes through, or when another schema was granted `REFERENCES` on one of them (on the whole table, on some columns, or through `GRANT ALL`), because the keys of those schemas cannot be read. The keys on tables the user can access are still checked. A role such as `SELECT_CATALOG_ROLE` is picked up as soon as you press "Connetti ai Database" again (the pooled Oracle sessions are dropped then, and when the check stops and asks). Tables that only have a column nulled (`SET NULL`) and tables with no rows are not asked about: nothing passes through them. A catalog query that fails stops the load and asks too; it is never read as "no keys". What it cannot see at all is a schema that holds the `REFERENCES ANY TABLE` system privilege instead of a grant on the table. Answering "continue" loads the rows without emptying the table; a user with `SELECT_CATALOG_ROLE` reloads populated Oracle tables without questions.
- **Exported scripts** ("Genera Script") are UTF-8 without a byte-order mark: run a SQL Server script with `sqlcmd -f 65001` (the Podman fixture tests do), or its non-Latin text is read in the console code page. For Oracle, text values are written in pieces joined with `||` over several lines (SQL*Plus ignores a line of more than 4999 bytes); a text value of more than 4000 bytes still cannot be written as a literal (ORA-01489), as before. A binary value (`BLOB`, `RAW`, `bytea`) is one `hextoraw('...')` literal that cannot be split, in a script and in a direct migration alike (both write the value as text): above about 2000 bytes Oracle refuses it (ORA-01704), and in a script above about 2500 SQL*Plus skips the row without an error. Binary values that large cannot currently be loaded into Oracle.
- **XML values in a script for SQL Server**: SQL Server refuses an XML declaration that names an encoding (`<?xml ... encoding="UTF-8"?>`) in a Unicode literal, so the declaration is removed from values that go into an `xml` column. A direct migration asks the target column. A script has no target to ask: with the tables in it (schema + data) the column types it creates settle the question, but in a data-only script it judges by the source column type. Text that holds a declaration and goes into an *existing* `xml` column is then left as it is and SQL Server rejects the whole `INSERT` that holds it when the script runs (a statement is all or nothing, and a script writes up to 200 rows per `INSERT` by default); an `xml` / `XMLTYPE` value going into an existing text column loses its declaration, where a direct migration keeps it.
- **Oracle table and column names are written unquoted** in generated DDL and scripts (identifiers are upper-cased); constraint and index names are quoted. A source object whose name has characters other than letters, digits, `_`, `$` and `#`, or a line break, produces a statement Oracle rejects or reads differently: migrate from sources you trust and read an exported script before running it.
- **SQL Server to SQL Server**: a `sql_variant` value is inserted as a SQL literal, so its base type follows the literal and not the source: `varchar` becomes `nvarchar`, and `datetime`, `uniqueidentifier` and `bit` values, among others, are stored with a different base type. Do not rely on the variant's base type after a migration.
- **Oracle `INTERVAL` columns** are created as text on the other databases (`nvarchar(max)` on SQL Server, `text` on PostgreSQL): the value the driver returns (a month count, a time span) is not accepted by an interval column.

## Tests

- `dotnet test DatabaseMigrator.sln` runs the unit tests and the headless UI tests (`tests/DatabaseMigrator.UiTests`, Windows only: they drive the real window and view model without a screen).
- E2E tests (`Category=E2E`) need the database containers; see the matrix below. `FullRunE2ETests`, the SQL Server customer scenario in the UI project, runs with `$env:DBMIGRATOR_RUN_E2E = 'true'; dotnet test tests/DatabaseMigrator.UiTests` (PowerShell) against the SQL Server container.

## E2E Matrix

Cross-database E2E matrix automation is available via:

```powershell
.\scripts\run-e2e-matrix.ps1
```

See `PODMAN_E2E_TESTING.md` for prerequisites and details.

## Documentation

- [QUICKSTART.md](QUICKSTART.md): the first migration in five minutes.
- [DEPLOYMENT.md](DEPLOYMENT.md): requirements, troubleshooting and the release procedure.
- [ARCHITECTURE.md](ARCHITECTURE.md): design, services and data-type mapping.
- [PODMAN_E2E_TESTING.md](PODMAN_E2E_TESTING.md): the cross-database E2E tests with Podman.

## License

MIT License - see the [LICENSE](LICENSE) file.

The executable bundles third-party components under their own licenses. Nearly all of the NuGet packages (Avalonia, ReactiveUI, Microsoft.Data.SqlClient and most of the rest) are under the MIT license. The exceptions are Npgsql (PostgreSQL License), the ANGLE natives of Avalonia (`Avalonia.Angle.Windows.Natives`, BSD-style), `Microsoft.Data.SqlClient.SNI.runtime` (Microsoft Software License Terms) and Oracle.ManagedDataAccess.Core (Oracle Free Distribution, Hosting, and Use Terms and Conditions). The license of each package is in its NuGet metadata (the `.nuspec`) and, for some of them, in a license file inside the package. [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) lists every bundled package with its copyright notice and reproduces the license texts; it is attached to each release together with `LICENSE`, and `scripts\generate-third-party-notices.ps1` regenerates it after a dependency update.

## Contributing

Contributions are welcome! Please read the ARCHITECTURE.md file to understand the codebase structure.

`main` is the only long-lived branch and takes changes only through a pull request: its required check, `test / build` (the build and all the tests), must pass, so a direct push is rejected. From a short-lived branch:

```powershell
git switch -c fix/x
# edit, then commit your change
git push -u origin HEAD
gh pr create --title "What the change does, in one line" --fill && gh pr merge --auto --merge --delete-branch
```

The pull request merges itself once the check passes, and its branch is deleted. Give it a descriptive title: the release notes list the titles of the merged pull requests.

A release is cut by the maintainer with one manual run of the `Release` workflow, which also runs the cross-database E2E matrix and blocks the release if it fails; see "Releasing" in [DEPLOYMENT.md](DEPLOYMENT.md#releasing).
