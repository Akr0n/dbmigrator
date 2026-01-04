# Database Migrator - Deployment Guide

## Overview

**Database Migrator** is a professional tool for cross-database data migration between:
- SQL Server
- Oracle
- PostgreSQL

## Release Files

### Standalone Executable (Recommended)
- **File**: `DatabaseMigrator.exe` (~166 MB)
- **Location**: `/release/`
- **Requirements**: Windows 10/11 64-bit, no external dependencies
- **Runtime**: .NET 10.0 (self-contained)

## Installation

### Option 1: Direct Execution
```powershell
# Copy exe to your preferred folder
Copy-Item release\DatabaseMigrator.exe "C:\Program Files\DatabaseMigrator\"

# Run
& "C:\Program Files\DatabaseMigrator\DatabaseMigrator.exe"
```

### Option 2: NSIS Installer (Recommended for Distribution)
```powershell
# Generate the installer
& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi

# Result: DatabaseMigrator-Setup-v1.0.0.exe
# Run the installer and follow the wizard
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
- **Access**: Direct connection or TNS

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
- Verify database name/SID

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

### If Installed via NSIS:
1. Control Panel → Programs → Programs and Features
2. Select "Database Migrator"
3. Click "Uninstall"

### If Using Standalone Executable:
1. Delete the DatabaseMigrator.exe file
2. Delete the installation folder
3. Optionally delete configurations from `%LOCALAPPDATA%\DatabaseMigrator\`

## Version Information

**Current Version**: 1.0.0  
**Release Date**: January 2026  
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
