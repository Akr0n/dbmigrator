# Database Migrator - Deployment Guide

## Overview

**Database Migrator** is a professional tool for cross-database data migration between:
- SQL Server
- Oracle
- PostgreSQL

## Release Files

### Standalone Executable (Recommended)
- **File**: `DatabaseMigrator.exe`
- **Download**: the [latest release](https://github.com/Akr0n/dbmigrator/releases/latest)
- **Built locally**: `.\publish.ps1` writes it to `release\`
- **Requirements**: Windows 10/11 64-bit, no external dependencies
- **Runtime**: .NET 10.0 (self-contained)

## Installation

### Direct Execution
```powershell
# Copy exe to your preferred folder (run this where the exe is: the folder you downloaded it to, or release\ after .\publish.ps1)
Copy-Item DatabaseMigrator.exe "C:\Program Files\DatabaseMigrator\"

# Run
& "C:\Program Files\DatabaseMigrator\DatabaseMigrator.exe"
```

## System Requirements

### Minimum
- **OS**: Windows 10/11 (64-bit)
- **CPU**: Dual-core 2.0 GHz
- **RAM**: 2 GB
- **Disk**: 200 MB available

### Recommended
- **OS**: Windows 11 Pro/Enterprise
- **CPU**: Quad-core 2.5 GHz+
- **RAM**: 4+ GB
- **Disk**: 1+ GB (depending on database sizes)

## Supported Databases

### SQL Server
- **Versions**: 2017, 2019, 2022
- **Editions**: Enterprise, Standard, Express
- **Authentication**: SQL Auth, Windows Auth

### Oracle
- **Versions**: 19c, 21c, 23c
- **Access**: Direct connection to the listener (host name or IP, port, service name); tnsnames.ora aliases are not used

### PostgreSQL
- **Versions**: 12, 13, 14, 15, 16
- **Access**: Direct connection

## Usage Procedure

### 1. Connection Configuration
1. Launch the application
2. Go to "Database Connections" tab
3. Enter connection details:
   - Server/Host
   - Port
   - Database name
   - Username/Password
4. Click "Connect to Databases"

### 2. Object Selection
1. Go to "Table Selection" tab
2. Select tables to migrate
3. Use search box to filter
4. View row counts per table
5. Use quick selection buttons

### 3. Migration Mode
Select the appropriate mode:
- **Schema + Data**: Full migration with constraints and automatic rollback on failure
- **Schema Only**: Create table structures with Primary Keys and UNIQUE constraints
- **Data Only**: Migrate data only (tables must exist)

### 4. Start Migration
1. Go to "Migration" tab
2. Click "Start Migration"
3. Monitor progress
4. Wait for completion

## Data Type Mapping

The application performs intelligent automatic data type mapping:

| SQL Server | PostgreSQL | Oracle |
|------------|------------|--------|
| int | integer | NUMBER(10) |
| bigint | bigint | NUMBER(19) |
| smallint | smallint | NUMBER(5) |
| tinyint | smallint | NUMBER(3) |
| varchar(n) | varchar(n) | VARCHAR2(n) |
| nvarchar(n) | varchar(n) | NVARCHAR2(n) |
| varchar(max) | text | CLOB |
| nvarchar(max) | text | NCLOB |
| char(n) | char(n) | CHAR(n) |
| text | text | CLOB |
| datetime | timestamp | TIMESTAMP(6) |
| datetime2 | timestamp | TIMESTAMP(6) |
| date | date | DATE |
| time | time | TIMESTAMP(0) |
| bit | boolean | NUMBER(1) |
| decimal(p,s) | numeric(p,s) | NUMBER(p,s) |
| float | double precision | BINARY_DOUBLE |
| real | real | BINARY_FLOAT |
| binary(n) | bytea | RAW(n) |
| varbinary | bytea | BLOB |
| varbinary(max) | bytea | BLOB |
| uniqueidentifier | uuid | RAW(16) |

## Monitoring and Logging

The application provides real-time feedback:
- Connection status
- Number of tables found
- Migration progress (percentage)
- Rows migrated per table
- Detailed error messages

Logs include:
- Connection attempts
- DDL statements executed
- Batch progress
- Error stack traces

## Troubleshooting

### Connection Failed
**Problem**: "Unable to connect to source/target database"

**Solutions**:
- Verify server is reachable (`ping hostname`)
- Check credentials (username/password)
- Verify port (1433 SQL Server, 1521 Oracle, 5432 PostgreSQL)
- Check firewall settings
- Verify the database name (for Oracle it is the service name; a SID that is not also a registered service name does not connect)

### Connection Timeout
**Problem**: Application hangs during connection

**Solutions**:
- Check network speed and latency
- Verify server load
- Default timeout is 300 seconds

### "String or binary data would be truncated"
**Problem**: Data migration fails with truncation error

**Solutions**:
- Source data is larger than target column
- Check column size mapping in logs
- Consider using larger column types in target

### Partial Migration
**Problem**: Not all rows migrated

**Solutions**:
- Check error messages for specific failures
- Verify target has sufficient disk space
- Check for constraint violations
- Review foreign key dependencies

### Schema Errors
**Problem**: Error during schema creation

**Solutions**:
- Verify target user has DDL privileges
- Check if table already exists
- Review data type compatibility
- Check for reserved word conflicts

### Oracle-Specific Issues
**Problem**: ORA-01031 insufficient privileges

**Solutions**:
- Ensure user has CREATE SESSION, CREATE TABLE privileges
- For creating new schemas, use SYSTEM or SYS user
- SYSDBA is only available to SYS user

## Performance

### Optimization Settings
- **Batch Size**: 1000 rows per batch
- **Command Timeout**: 300 seconds (5 minutes)
- **Parallel Row Counts**: 10 concurrent operations
- **Memory Usage**: ~100-200 MB during migration

### Typical Performance
- **Connection**: < 1 second
- **Table Discovery**: 1-10 seconds (depending on table count)
- **Schema Migration**: 5-60 seconds
- **Data Migration**: 10-100 MB per minute (network dependent)

### Large Database Tips
- Migrate tables in batches
- Start with smaller tables for testing
- Monitor server resources during migration
- Consider off-peak hours for production migrations

## Backup Recommendations

Before starting an important migration:
1. Create backup of target database
2. Test with a subset of data first
3. Validate data integrity post-migration
4. Keep rollback plan ready

## Uninstallation

1. Delete the DatabaseMigrator.exe file
2. Delete the installation folder
3. Optionally delete configurations from `%LOCALAPPDATA%\DatabaseMigrator\`

## Releasing

`main` is the only long-lived branch. A ruleset on it requires a pull request and the status check `test / build` (the build and all
the tests, run by `ci.yml` on every pull request to `main` and on every push to it), so a direct push to `main` is rejected. Every
change goes through a pull request from a short-lived branch:

```powershell
git switch -c fix/x
# edit, then commit your change
git push -u origin HEAD
gh pr create --title "What the change does, in one line" --fill && gh pr merge --auto --merge --delete-branch
```

The pull request merges itself once `test / build` passes, and its branch is deleted. Merge commits are the only allowed merge method.

A release is one manual run of the `Release` workflow (`.github/workflows/release.yml`):

```powershell
gh workflow run release.yml -f channel=stable
```

| Channel | What it does | Publishes |
|---------|--------------|-----------|
| `dry-run` (default) | Builds, runs all the tests and the cross-database E2E matrix (real SQL Server, PostgreSQL and Oracle containers); the exe is kept for 7 days as the artifact `exe` | Nothing |
| `candidate` | The same, then publishes; it can be built from any branch | A public prerelease `vX.Y.Z-rc.N` |
| `stable` | The same, then publishes; it must run on `main` | The release `vX.Y.Z` |

To try a pull request's build on another PC before merging it, run a candidate from its branch: `--ref` runs the copy of
`release.yml` on that branch and builds that branch's commit.

```powershell
gh workflow run release.yml -f channel=candidate --ref <branch>
```

The prerelease is public; delete it afterwards (see below).

`dry-run` is the default channel, so forgetting `-f channel=...` is harmless; a dry run also runs by itself every Monday at 04:00 UTC. The E2E matrix runs
alongside the build and blocks every `candidate` and `stable` release; there is no switch to skip it. If a job fails, nothing is
published.

The version is the next patch after the last stable release; `-f version=vX.Y.Z` (or `X.Y.Z`) sets another one. The first job,
`plan`, refuses and says why:
- a version that does not increase over the last stable tag, or a tag that already exists (a tag is never reused);
- a missing `README.md`, `LICENSE` or `THIRD-PARTY-NOTICES.txt`, which every release ships;
- for `stable`, a ref other than `main`, or nothing shippable changed since the last stable tag. Only `src/`, `*.sln`, `README.md`,
  `LICENSE` and `THIRD-PARTY-NOTICES.txt` count: a change that touches only tests, other docs or workflows lands on `main` but cannot
  be released on its own.

The release is created as a draft with exactly five assets (`DatabaseMigrator.exe`, `RELEASE_NOTES.txt`, `README.md`, `LICENSE`,
`THIRD-PARTY-NOTICES.txt`), the assets are counted, and only then is it published. The release notes list the titles of the pull
requests merged since the previous stable release (those labelled `dependencies` are left out), so give every pull request a
descriptive title.

A wrong release is not corrected in place: the next release gets the next number. Delete a candidate once it has served:

```powershell
gh release delete vX.Y.Z-rc.N --cleanup-tag --yes
```

A candidate gets the number after the highest `-rc` tag of that version, so deleting an earlier one does not bring its number back
(deleting the highest one with `--cleanup-tag` does, because its tag goes with it); `plan` refuses a number that is already taken.

## Version Information

The executable carries its version and the commit it was built from: its product version is `X.Y.Z+<commit>` (`X.Y.Z-rc.N+<commit>`
for a candidate). The window title reads `Database Migrator X.Y.Z+<7-character commit>`, the About text shows the same, and
`debug.log` gets the line `Database Migrator X.Y.Z+<7-character commit> started` at every start, so a log sent from another PC says
which build produced it. The releases, with their notes, are on the
[Releases page](https://github.com/Akr0n/dbmigrator/releases); the latest stable one is marked "Latest", and a release marked "Pre-release" is a test build.

**Build**: Win-x64, .NET 10.0 self-contained

### Release Notes v1.0.0
- ✅ SQL Server, Oracle, PostgreSQL support
- ✅ Modern Avalonia UI
- ✅ Intelligent data type mapping
- ✅ Single-file executable
- ✅ Real-time progress tracking
- ✅ Three migration modes
- ✅ Automatic rollback on failure
- ✅ Configuration save/load
- ✅ Table search and filtering

---

**Built with**: .NET 10.0, Avalonia 11.3, ReactiveUI
