# Database Migrator - Technical Architecture

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     DATABASE MIGRATOR                        │
│                    (Avalonia UI - MVVM)                      │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌───────────────┐  ┌────────────────┐  ┌────────────────┐  │
│  │  MainWindow   │  │   ViewModels   │  │   Commands     │  │
│  │   (AXAML)     │  │   + Bindings   │  │   ReactiveUI   │  │
│  └───────────────┘  └────────────────┘  └────────────────┘  │
│                            │                                 │
│                            ▼                                 │
│  ┌───────────────────────────────────────────────────────┐  │
│  │          DatabaseMigrator.Core (Library)              │  │
│  │                                                        │  │
│  │  ┌──────────────────────────────────────────────┐     │  │
│  │  │  Services                                    │     │  │
│  │  │  ├─ DatabaseService                          │     │  │
│  │  │  │  └─ Connections, Queries, Data Migration  │     │  │
│  │  │  ├─ SchemaMigrationService                   │     │  │
│  │  │  │  └─ DDL, Data Type Mapping, Schema Ops    │     │  │
│  │  │  └─ LoggerService                            │     │  │
│  │  │     └─ Centralized Logging                   │     │  │
│  │  └──────────────────────────────────────────────┘     │  │
│  │                                                        │  │
│  │  ┌──────────────────────────────────────────────┐     │  │
│  │  │  Models                                      │     │  │
│  │  │  ├─ ConnectionInfo                           │     │  │
│  │  │  ├─ ConnectionConfig                         │     │  │
│  │  │  ├─ DatabaseType                             │     │  │
│  │  │  ├─ MigrationMode                            │     │  │
│  │  │  └─ TableInfo                                │     │  │
│  │  └──────────────────────────────────────────────┘     │  │
│  └───────────────────────────────────────────────────────┘  │
│                            │                                 │
│                            ▼                                 │
│  ┌───────────────────────────────────────────────────────┐  │
│  │              Database Drivers (ADO.NET)               │  │
│  │  ├─ Microsoft.Data.SqlClient (SQL Server)             │  │
│  │  ├─ Oracle.ManagedDataAccess.Core (Oracle)            │  │
│  │  └─ Npgsql (PostgreSQL)                               │  │
│  └───────────────────────────────────────────────────────┘  │
│                            │                                 │
└────────────────────────────┼─────────────────────────────────┘
                             │
         ┌───────────────────┴───────────────────┐
         │                                       │
    ┌────▼─────────────┐              ┌─────────▼──────────┐
    │  Source Database │              │  Target Database   │
    │  (SQL/Oracle/PG) │              │  (SQL/Oracle/PG)   │
    └──────────────────┘              └────────────────────┘
```

## Project Structure

### DatabaseMigrator.Core (Library)

**Target Framework**: .NET 10.0

**Dependencies**:
- Microsoft.Data.SqlClient 6.1.3
- Npgsql 10.0.1
- Oracle.ManagedDataAccess.Core 23.26.0
- ReactiveUI 22.3.1 (the observable `TableInfo` and `DatabaseObject` models)

**Namespace Structure**:
```
DatabaseMigrator.Core
├── Models/
│   ├── ConnectionInfo.cs      # Connection details and connection string builder
│   ├── ConnectionConfig.cs    # Serializable configuration for save/load
│   ├── DatabaseType.cs        # Enum: SqlServer, PostgreSQL, Oracle
│   ├── MigrationMode.cs       # Enum: SchemaAndData, SchemaOnly, DataOnly
│   ├── TableInfo.cs           # Table metadata (name, schema, row count)
│   ├── ForeignKeyInfo.cs      # A FOREIGN KEY as read from a catalog
│   ├── DatabaseObject.cs      # Object selectable in the "Genera Script" tab
│   ├── DatabaseObjectType.cs  # Enum: Table, View, StoredProcedure, Function, Trigger, Sequence, Index
│   ├── ScriptGenerationOptions.cs # Options of the script export
│   ├── ScriptGenerationProgress.cs # Progress reported while a script is generated
│   ├── TruncateFailureContext.cs # What the user is told when emptying a table fails or is refused
│   ├── LogEntry.cs            # One line of the log (time, level, message)
│   └── RuntimeOptions.cs      # Runtime settings (appsettings.json, DBMIGRATOR_* variables)
└── Services/
    ├── IDatabaseService.cs        # Interface for database operations
    ├── DatabaseService.cs         # Connection testing, table discovery, data migration
    ├── SchemaMigrationService.cs  # Schema DDL generation, type mapping
    ├── LoggerService.cs           # Centralized logging service
    ├── DatabaseServiceBase.cs     # Shared base: retry, connection factory, identifier formatting
    ├── ForeignKeyService.cs       # FOREIGN KEY handling around a data load (keys off/on on SQL Server)
    ├── TableDependencyOrderer.cs  # Parents-first load order of tables
    ├── DeleteCascadeReach.cs      # Tables a DELETE reaches through ON DELETE CASCADE / SET NULL (Oracle)
    ├── SourceResume.cs            # Resume of a big table read after the source connection is lost
    ├── TableSelection.cs          # Search filter and "select all" / "deselect all" rules
    ├── ScriptGenerationService.cs # "Genera Script": DDL + data export to a .sql file
    └── CredentialProtectionService.cs # DPAPI protection of saved passwords
```

### DatabaseMigrator (UI Application)

**Target Framework**: .NET 10.0  
**Runtime**: win-x64 (self-contained)  
**UI Framework**: Avalonia 11.3.10  
**Binding**: ReactiveUI + System.Reactive

**Namespace Structure**:
```
DatabaseMigrator
├── Program.cs              # Entry point
├── App.axaml               # Application configuration
├── App.axaml.cs            # Creates the main window
├── AppVersion.cs           # Build version shown in the window title
├── appsettings.json        # Runtime settings template
├── Assets/                 # Application icons and resources
├── Converters/             # Value converters (log level colours)
├── Views/
│   ├── MainWindow.axaml    # Main UI definition (XAML)
│   ├── MainWindow.axaml.cs # Code-behind
│   └── WindowFit.cs        # Keeps the window inside the screen's work area
└── ViewModels/
    ├── ViewModelBase.cs        # Base class with INotifyPropertyChanged
    ├── MainWindowViewModel.cs  # Main application logic
    ├── ConnectionViewModel.cs  # Connection form logic
    └── ScriptGenerationViewModel.cs # "Genera Script" tab logic
```

## Key Components

### 1. DatabaseService

The `DatabaseService` class handles all database operations:

```csharp
public interface IDatabaseService
{
    Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo);
    Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo);
    Task<bool> DatabaseExistsAsync(ConnectionInfo connectionInfo);
    Task<string?> CreateDatabaseAsync(ConnectionInfo connectionInfo);
    Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, 
        TableInfo table, IProgress<int> progress,
        IEnumerable<TableInfo>? tablesLoadedLater = null);
}
```

**Key Features**:
- Connection testing with detailed error reporting
- Table discovery with row counts read one table at a time over a single connection
- Batch data migration (1000 rows per batch by default, `DBMIGRATOR_BATCH_SIZE`)
- Transaction support with commit/rollback
- IDENTITY_INSERT handling for SQL Server

### 2. SchemaMigrationService

The `SchemaMigrationService` handles DDL operations, type mapping, and constraint migration:

```csharp
public class SchemaMigrationService
{
    Task<bool> CheckTableExistsAsync(ConnectionInfo connectionInfo, string schema, string tableName);
    Task<bool> DropTableAsync(ConnectionInfo connectionInfo, string schema, string tableName);
    Task<bool> DropConstraintAsync(ConnectionInfo connectionInfo, string schema, string tableName,
        string constraintName, string constraintType);
    Task<SchemaMigrationResult> MigrateSchemaAsync(ConnectionInfo source, ConnectionInfo target,
        List<TableInfo> tablesToMigrate, List<TableInfo>? tablesCreatedOnTarget = null);
}
```

**Schema Migration Process**:
1. Extract column definitions from source database
2. Map data types to target database format
3. Generate CREATE TABLE DDL with proper identifier quoting
4. Extract PRIMARY KEY and UNIQUE constraints
5. Generate ALTER TABLE ADD CONSTRAINT DDL
6. Apply identifier case conventions per database type

**Key Features**:
- Cross-database data type mapping (25+ type conversions)
- DDL generation for CREATE TABLE statements
- Primary Key and UNIQUE constraint migration
- Automatic identifier case handling:
  - PostgreSQL: lowercase identifiers
  - Oracle: UPPERCASE identifiers
  - SQL Server: original case preserved
- Support for MAX types (VARCHAR(MAX) → TEXT/CLOB)
- Same-database migrations with type preservation

### 3. MainWindowViewModel

The main ViewModel orchestrates the migration process:

```csharp
public class MainWindowViewModel : ViewModelBase
{
    // Commands
    ReactiveCommand<Unit, Unit> ConnectDatabasesCommand { get; }
    ReactiveCommand<Unit, Unit> StartMigrationCommand { get; }
    
    // Migration modes
    MigrationMode SelectedMigrationMode { get; set; }
}
```

**Key Features**:
- Reactive UI updates with progress tracking
- Thread-safe collection updates using `Dispatcher.UIThread.InvokeAsync()`
- Table filtering and selection management
- Configuration save/load (JSON format)
- Automatic rollback on migration failure (Schema+Data mode)

## Migration Flow

### Schema + Data Mode

```
1. Check if target database exists
   └─ If not: Create database (with privileges for Oracle)
   
2. Identify tables that need to be created
   └─ Track in tablesCreatedDuringMigration list
   
3. For each table:
   a. Check if table exists in target
   b. If not: Get column definitions from source
   c. Map data types to target database
   d. Apply identifier case conventions:
      - PostgreSQL: lowercase
      - Oracle: UPPERCASE  
      - SQL Server: original case
   e. Generate and execute CREATE TABLE DDL
   f. Extract PRIMARY KEY and UNIQUE constraints from source
   g. Generate and execute ALTER TABLE ADD CONSTRAINT DDL
   
4. For each table (data migration):
   a. Load tables parents-first following the target's FOREIGN KEYs (`ForeignKeyService`, `TableDependencyOrderer`);
      then empty the target table: SQL Server `TRUNCATE` (DELETE when a key points at the table; the keys are switched
      off for the load), PostgreSQL `TRUNCATE ... CASCADE` and Oracle `DELETE FROM`, the last two only after checking
      that the cascade would not empty populated tables the run does not load later (`DeleteCascadeReach` for Oracle)
   b. Read data from source in batches (1000 rows)
   c. Generate INSERT statements with proper identifier quoting
   d. Execute with transaction support
   e. Commit on success
   
5. On failure:
   └─ Rollback: DROP the constraints added and all tables created during this migration
```

### Constraint Migration

The application automatically migrates PRIMARY KEY and UNIQUE constraints:

**Constraint Extraction Queries**:
- **SQL Server**: `INFORMATION_SCHEMA.TABLE_CONSTRAINTS` + `CONSTRAINT_COLUMN_USAGE`
- **PostgreSQL**: `information_schema.table_constraints` + `key_column_usage`
- **Oracle**: `all_constraints` + `all_cons_columns`

**Constraint DDL Generation**:
```sql
-- SQL Server
ALTER TABLE [schema].[table] ADD CONSTRAINT [PK_name] PRIMARY KEY ([column])

-- PostgreSQL  
ALTER TABLE "schema"."table" ADD CONSTRAINT "pk_name" PRIMARY KEY ("column")

-- Oracle
ALTER TABLE SCHEMA_NAME.TABLE_NAME ADD CONSTRAINT "PK_NAME" PRIMARY KEY (COLUMN)
```

**Identifier Case Handling**:
| Target Database | Identifier Case | Quoting |
|-----------------|-----------------|---------|
| SQL Server | Original (preserved) | `[brackets]` |
| PostgreSQL | lowercase | `"double quotes"` |
| Oracle | UPPERCASE | No quotes |

**Constraint Name Length Limits**:
- SQL Server: 128 characters
- PostgreSQL: 63 characters
- Oracle: 30 characters (truncated if necessary)

### Data Type Mapping

The application supports bidirectional mapping between all supported databases:

**SQL Server → PostgreSQL**:
| SQL Server | PostgreSQL |
|------------|------------|
| int | integer |
| bigint | bigint |
| varchar(n) | varchar(n) |
| nvarchar(n) | varchar(n) |
| varchar(max) | text |
| datetime2(p) | timestamp(min(p,6)) |
| bit | boolean |
| varbinary | bytea |
| uniqueidentifier | uuid |

**SQL Server → Oracle**:
| SQL Server | Oracle |
|------------|--------|
| int | NUMBER(10) |
| bigint | NUMBER(19) |
| varchar(n) | VARCHAR2(n) |
| nvarchar(n) | NVARCHAR2(n) |
| varchar(max) | CLOB |
| datetime2(p) | TIMESTAMP(min(p,9)) |
| bit | NUMBER(1) |
| varbinary(n) | RAW(n), n capped at 2000 |
| varbinary(max) | BLOB |

**PostgreSQL / Oracle → SQL Server** (text is always Unicode, whatever the source type):
| Source | SQL Server |
|--------|------------|
| varchar(n) / VARCHAR2(n) / NVARCHAR2(n), char(n) / CHAR(n) / NCHAR(n), n <= 4000 | nvarchar(n) |
| text / CLOB / NCLOB / LONG / JSON / any unknown type, or n > 4000 | nvarchar(max) |

**Same Database Migrations**:
When source and target are the same database type, original types are preserved with correct sizes, including handling of MAX/unlimited length types.

## Concurrency and Performance

### Table Row Count Retrieval

Row counts are read one table at a time over a single open connection (`GetAllTableRowCountsAsync`), so a database with many tables (1000+) does not open one connection per table. A count that fails is logged and shown as 0. In Data Only mode the existence check of the target tables runs up to 10 checks at a time (a `SemaphoreSlim` in `MainWindowViewModel`).

### Batch Processing

Data is migrated in batches of 1000 rows by default (`DBMIGRATOR_BATCH_SIZE`; a wide table gets fewer rows per `INSERT` on a SQL Server target) to:
- Reduce memory consumption
- Provide granular progress updates
- Enable partial recovery on errors

### Transaction Management

- **SQL Server/PostgreSQL**: Use ADO.NET transactions
- **Oracle**: Use explicit COMMIT/ROLLBACK statements

## Security Considerations

### Connection Strings

- Passwords with special characters are properly escaped
- Oracle passwords support special characters via quoted identifiers
- SQL Server supports both SQL Auth and Windows Auth

### SQL Injection Prevention

- Parameterized queries for table existence checks
- Identifier escaping for dynamic DDL
- Schema and table names are escaped per dialect, and names read from a source catalog are never trusted: quotes are
  doubled inside literals and identifiers, and line breaks are removed from `--` comments in generated scripts

### Oracle Privileges

For Oracle target databases, the connecting user needs:
- CREATE SESSION
- CREATE TABLE
- CREATE SEQUENCE
- CREATE PROCEDURE

To create a missing target user, the connecting account also needs the CREATE USER and GRANT ANY PRIVILEGE system privileges; the new user is granted the four privileges above.

## Error Handling

### Connection Errors
- Detailed error messages with server/port information
- Timeout handling with configurable timeouts (300 seconds default)

### Migration Errors
- Automatic rollback of created tables on failure
- Transaction rollback for data operations on SQL Server and PostgreSQL (Oracle commits each statement; see README, Known limitations)
- Detailed logging with stack traces

### Validation
- Data-only mode validates table existence before starting
- Connection validation before migration start

## Configuration Storage

Configurations are stored as JSON files:

```json
{
  "source": {
    "databaseType": "SqlServer",
    "server": "localhost",
    "port": 1433,
    "database": "SourceDB",
    "username": "sa",
    "password": "<DPAPI-protected>",
    "passwordProtected": true,
    "trustServerCertificate": false,
    "requireEncryption": false
  },
  "target": {
    "databaseType": "PostgreSQL",
    "server": "localhost",
    "port": 5432,
    "database": "TargetDB",
    "username": "postgres",
    "password": "<DPAPI-protected>",
    "passwordProtected": true,
    "trustServerCertificate": false,
    "requireEncryption": false
  },
  "timestamp": "2026-01-02T10:30:00",
  "name": "config_name"
}
```

Default storage location: `%LOCALAPPDATA%\DatabaseMigrator\Configurations\`
