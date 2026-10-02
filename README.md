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
- ♻️ A lost connection to the source does not lose a table: the read resumes where it stopped (primary key required)
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
1. Download `DatabaseMigrator.exe` from Releases
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
2. In the "Database Connections" tab:
   - **Source Database**: Enter connection details for the source DB
   - **Target Database**: Enter connection details for the target DB
3. Click "Connect to Databases". Connect opens a connection to each database you named, so the **target database must already exist** (the migration creates tables, not databases)

### Step 2: Select Tables
1. Select the tables to migrate in the "Table Selection" tab
2. Use the search box to filter tables by name or schema
3. "Select All" selects only the tables the filter currently shows, so you can select one schema at a time; "Deselect All" clears every selection, including tables the filter hides. If you start a migration while selected tables are hidden by the filter, you are asked to confirm
4. Row counts are loaded automatically

### Step 3: Choose Migration Mode
Select one of three migration modes:
- **Schema + Data**: Creates tables and migrates data (tables created by the run are dropped again if the data load fails)
- **Schema Only**: Creates only the table structure without data
- **Data Only**: Migrates data only (tables must already exist in target)

**Data modes empty the target tables first.** With Schema + Data and Data Only, every selected table is emptied on the target before it is loaded (SQL Server `TRUNCATE`, PostgreSQL `TRUNCATE ... CASCADE`, Oracle `DELETE FROM`), so its previous rows are replaced. Tables are loaded parents-first following the target's FOREIGN KEYs; on SQL Server the keys are switched off for the load and re-validated at the end. If emptying a table would also wipe tables you did not select (PostgreSQL `CASCADE`, Oracle `ON DELETE CASCADE` / `SET NULL`, SQL Server an enabled foreign key with a delete action from a table you did not select) and they hold rows, the migration stops and asks you: select those tables too, or empty them yourself. "Continue" adds the rows without emptying the table.

### Step 4: Start Migration
1. Go to the "Migration" tab
2. Review the status information
3. Click "Start Migration"
4. Monitor progress with the progress bar. While a migration or a reload runs, Connect, Load Configuration and the migration-mode buttons are disabled. The "Log" tab shows what is happening; its "Segui" button keeps the view on the last line as it grows, and switching it off lets you scroll back and read

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

- an `appsettings.json` next to the executable (`src/DatabaseMigrator/appsettings.json` is the template; a build or publish copies it to the output folder, but the GitHub release holds only the `.exe`), then `%LOCALAPPDATA%\DatabaseMigrator\appsettings.json`, which is read after it. For the downloaded executable use the second one
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
| datetime2 | timestamp | TIMESTAMP(6) |
| bit | boolean | NUMBER(1) |
| text | text | CLOB |
| varbinary | bytea | BLOB |
| uniqueidentifier | uuid | RAW(16) |

On Oracle the sizes are capped: `VARCHAR2` at 4000, `NVARCHAR2` at 2000 and `RAW` at 2000. `varbinary(n)` becomes `RAW(n)` within that cap and only `varbinary(max)` becomes `BLOB`; `datetime2(p)` becomes `TIMESTAMP(p)`.

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
- **A lost connection to the source does not restart a table.** Each table is read in one stream that can last tens of minutes. The read is ordered by the primary key from the first row on; if the connection to the source is cut in the middle of it, the tool waits (2 seconds, doubling up to 30), opens a new connection and carries on after the last row received, with the target transaction still open, so nothing is loaded twice or skipped. It gives up after 5 attempts in a row without a new row, and the table fails and is rolled back as usual. A table **without a primary key** cannot be resumed (reading again in an unknown order could skip or repeat rows): it fails and is rolled back as before. Only the connection to the **source** is resumed: if the connection to the target is lost, the table is rolled back. The resume relies on the source rows not changing while the table is read: the log warns when the number of rows read differs from the count taken at the start. It needs `OFFSET` on the source (SQL Server 2012, PostgreSQL, Oracle 12c) and is turned off, together with the ordering, by `DBMIGRATOR_ENABLE_RETRIES=false`. Resuming was verified against a real SQL Server with the connection cut by a reset (one cut, two cuts, no key, a network that stays down); the same queries were run by hand on PostgreSQL and Oracle.
- **Sequence-backed columns are not recreated as auto-increment.** Only true identity columns are (SQL Server `IDENTITY`, PostgreSQL `GENERATED ... AS IDENTITY`, Oracle identity). A PostgreSQL `SERIAL` column or an Oracle `NEXTVAL` default is copied as a plain `DEFAULT` that the target usually cannot use: create such tables on the target yourself and migrate their data with Data Only.
- **Oracle checks before emptying a table** (an `ON DELETE CASCADE` / `SET NULL` key would otherwise empty or change tables the run does not load) are exact only when the migration user can read `DBA_CONSTRAINTS` (DBA, or `SELECT_CATALOG_ROLE`). Without that right they are deliberately cautious: the migration stops and asks when the migration user does not own the table, or a table the cascade passes through, or when another schema was granted `REFERENCES` on one of them (on the whole table, on some columns, or through `GRANT ALL`), because the keys of those schemas cannot be read. The keys on tables the user can access are still checked. A role such as `SELECT_CATALOG_ROLE` is picked up as soon as you press Connect again (the pooled Oracle sessions are dropped then, and when the check stops and asks). Tables that only have a column nulled (`SET NULL`) and tables with no rows are not asked about: nothing passes through them. A catalog query that fails stops the load and asks too; it is never read as "no keys". What it cannot see at all is a schema that holds the `REFERENCES ANY TABLE` system privilege instead of a grant on the table. Answering "continue" loads the rows without emptying the table; a user with `SELECT_CATALOG_ROLE` reloads populated Oracle tables without questions.
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

## License

MIT License - see the [LICENSE](LICENSE) file.

The executable bundles third-party components under their own licenses: Avalonia, ReactiveUI, Microsoft.Data.SqlClient and the other NuGet packages under the MIT license, Npgsql under the PostgreSQL License, and Oracle.ManagedDataAccess.Core under the Oracle Free Distribution, Hosting, and Use Terms and Conditions (its `LICENSE.txt` ships in the NuGet package).

## Contributing

Contributions are welcome! Please read the ARCHITECTURE.md file to understand the codebase structure.

Open pull requests against `develop`: every push to `develop` builds, tests and publishes a `vX.Y.Z-dev` prerelease. A stable release is cut by the maintainer by fast-forwarding `staging` to `develop` and merging `staging` into `main` with a **merge commit** (not squash or rebase); any other push to `main` publishes nothing.
