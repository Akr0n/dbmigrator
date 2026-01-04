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
- 🚀 Automatic target database creation
- 💾 Single-file executable (.exe)
- 📁 Save/Load connection configurations
- 🔀 Three migration modes: Schema+Data, Schema Only, Data Only
- ↩️ Automatic rollback on failure (Schema+Data mode)

## Requirements

- Windows 10/11 (64-bit)
- .NET 10.0 Runtime (included in standalone exe)

## Installation

### Method 1: Standalone Executable (Recommended)
1. Download `DatabaseMigrator-Setup-v1.0.0.exe` from Releases
2. Run the installer
3. Launch the application from Start Menu

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

**Batch/CMD:**
```cmd
REM Build and publish for Windows x64
publish.bat

REM The executable will be in: .\release\DatabaseMigrator.exe
```

**Manual with dotnet CLI:**
```bash
dotnet publish src/DatabaseMigrator/DatabaseMigrator.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=true
```

## Usage

### Step 1: Connect to Databases
1. Launch the application
2. In the "Database Connections" tab:
   - **Source Database**: Enter connection details for the source DB
   - **Target Database**: Enter connection details for the target DB
3. Click "Connect to Databases"

### Step 2: Select Tables
1. Select the tables to migrate in the "Table Selection" tab
2. Use the search box to filter tables by name
3. Use "Select All" and "Deselect All" buttons for quick management
4. Row counts are loaded automatically

### Step 3: Choose Migration Mode
Select one of three migration modes:
- **Schema + Data**: Creates tables and migrates data (with automatic rollback on failure)
- **Schema Only**: Creates only the table structure without data
- **Data Only**: Migrates data only (tables must already exist in target)

### Step 4: Start Migration
1. Go to the "Migration" tab
2. Review the status information
3. Click "Start Migration"
4. Monitor progress with the progress bar
5. The target database will be created automatically if it doesn't exist

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
- **Server**: TNS name or IP
- **Port**: 1521 (default)
- **Database**: SID or service name (e.g., XE, ORCL)
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

- **Save**: File → Save Configuration (or Ctrl+S)
- **Load**: File → Load Configuration (or Ctrl+O)

Configurations are saved as JSON files and include both source and target connection settings.

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

## License

MIT License - See LICENSE file for details.

## Contributing

Contributions are welcome! Please read the ARCHITECTURE.md file to understand the codebase structure.
