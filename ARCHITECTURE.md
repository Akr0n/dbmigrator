# Database Migrator - Architettura Tecnica

## Panoramica Architettura

```
┌─────────────────────────────────────────────────────────┐
│                    DATABASE MIGRATOR                     │
│                   (Avalonia UI - MVVM)                  │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌───────────────┐  ┌────────────────┐  ┌────────────┐ │
│  │  MainWindow   │  │  UI Binding    │  │  Commands  │ │
│  │   (XAML)      │  │  ViewModels    │  │  ReactiveUI│ │
│  └───────────────┘  └────────────────┘  └────────────┘ │
│                           │                              │
│                           ▼                              │
│  ┌──────────────────────────────────────────────────┐   │
│  │         DatabaseMigrator.Core (Libreria)        │   │
│  │                                                   │   │
│  │  ┌─────────────────────────────────────────┐    │   │
│  │  │  Services                               │    │   │
│  │  │ ├─ DatabaseService                      │    │   │
│  │  │ │  └─ Connessioni, Query, Discovery     │    │   │
│  │  │ ├─ SchemaMigrationService               │    │   │
│  │  │ │  └─ DDL, Mapping Tipi Dati            │    │   │
│  │  │ └─ DataMigrationService (opzionale)     │    │   │
│  │  └─────────────────────────────────────────┘    │   │
│  │                                                   │   │
│  │  ┌─────────────────────────────────────────┐    │   │
│  │  │  Models                                 │    │   │
│  │  │ ├─ ConnectionInfo                       │    │   │
│  │  │ ├─ DatabaseType                         │    │   │
│  │  │ ├─ TableInfo                            │    │   │
│  │  │ └─ ColumnDefinition                     │    │   │
│  │  └─────────────────────────────────────────┘    │   │
│  └──────────────────────────────────────────────────┘   │
│                           │                              │
│                           ▼                              │
│  ┌──────────────────────────────────────────┐           │
│  │      Database Drivers                    │           │
│  │  ├─ Microsoft.Data.SqlClient             │           │
│  │  ├─ Oracle.ManagedDataAccess.Core        │           │
│  │  └─ Npgsql (PostgreSQL)                  │           │
│  └──────────────────────────────────────────┘           │
│                           │                              │
└───────────────┬───────────┴───────────────┬──────────────┘
                │                           │
    ┌───────────▼────────┐        ┌────────▼──────────┐
    │  Source Database   │        │ Target Database   │
    │  (SQL/Oracle/PG)   │        │ (SQL/Oracle/PG)   │
    └────────────────────┘        └───────────────────┘
```

## Struttura Progetti

### DatabaseMigrator.Core (Libreria)
**Target**: .NET 8.0
**Dipendenze**:
- Microsoft.Data.SqlClient 5.2.0
- Npgsql 8.0.3
- Oracle.ManagedDataAccess.Core 23.4.0

**Namespace**:
```
DatabaseMigrator.Core
├── Models
│   ├── ConnectionInfo.cs
│   ├── DatabaseType.cs
│   ├── TableInfo.cs
│   └── ColumnDefinition.cs (interno)
└── Services
    ├── IDatabaseService.cs
    ├── DatabaseService.cs
    └── SchemaMigrationService.cs
```

### DatabaseMigrator (Applicazione UI)
**Target**: .NET 8.0
**Runtime**: win-x64 (self-contained)
**UI Framework**: Avalonia 11.0.10
**Binding**: ReactiveUI + System.Reactive

**Namespace**:
```
DatabaseMigrator
├── Program.cs (Entry point)
├── App.axaml
├── Views/
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
└── ViewModels/
    ├── ViewModelBase.cs (base class)
    ├── MainWindowViewModel.cs
    └── ConnectionViewModel.cs
```

## Componenti Chiave

### 1. DatabaseService (DatabaseMigrator.Core)

**Responsabilità**:
- Gestione connessioni ai database
- Discovery tabelle e schema
- Migrazione dati in batch
- Validazione connessioni

**Metodi Principali**:
```csharp
// Test connessione
Task<bool> TestConnectionAsync(ConnectionInfo connectionInfo)

// Recupero tabelle
Task<List<TableInfo>> GetTablesAsync(ConnectionInfo connectionInfo)

// Verifica esistenza database
Task<bool> DatabaseExistsAsync(ConnectionInfo connectionInfo)

// Creazione database
Task CreateDatabaseAsync(ConnectionInfo connectionInfo)

// Recupero schema tabella
Task<string> GetTableSchemaAsync(ConnectionInfo connectionInfo, 
                                  string tableName, string schema)

// Migrazione dati con progress
Task MigrateTableAsync(ConnectionInfo source, ConnectionInfo target, 
                       TableInfo table, IProgress<int> progress)
```

**Caratteristiche**:
- Batch size: 1000 righe per insert
- Command timeout: 300 secondi
- Support DbConnection base per abstraction

### 2. SchemaMigrationService (DatabaseMigrator.Core)

**Responsabilità**:
- Migrazione DDL (Data Definition Language)
- Mapping tipi dati cross-database
- Creazione schema nel target

**Mapping Tipi Dati**:
- **SQL Server ↔ PostgreSQL**: 25+ conversioni
- **SQL Server ↔ Oracle**: 20+ conversioni  
- **PostgreSQL ↔ Oracle**: 20+ conversioni
- Supporto precision/scale per numerici

**Esempio Mapping**:
```
SQL Server                PostgreSQL              Oracle
int                  →    integer            →    NUMBER(10)
varchar(255)         →    varchar(255)       →    VARCHAR2(255)
datetime2            →    timestamp          →    TIMESTAMP
bit                  →    boolean            →    NUMBER(1)
decimal(18,2)        →    numeric(18,2)      →    NUMBER(18,2)
```

### 3. MainWindowViewModel (DatabaseMigrator)

**Responsabilità**:
- Orchestrazione logica UI
- Binding dati connessioni
- Gestione flusso migrazione
- Progress tracking

**Proprietà Reactive**:
```csharp
// Connessioni
ConnectionViewModel? SourceConnection
ConnectionViewModel? TargetConnection

// Dati
ObservableCollection<TableInfo> Tables

// Stato
bool IsConnected
bool IsMigrating
string StatusMessage

// Progress
int ProgressPercentage (0-100)
string ProgressText
```

**Comandi**:
```csharp
ConnectDatabasesCommand      // Connetti ai DB
StartMigrationCommand        // Avvia migrazione
SelectAllTablesCommand       // Seleziona tutto
DeselectAllTablesCommand     // Deseleziona tutto
```

### 4. ConnectionViewModel (DatabaseMigrator)

**Responsabilità**:
- Gestione parametri connessione
- Validazione dati input
- Creazione ConnectionInfo

**Proprietà**:
```csharp
string Server
int Port
string Database
string Username
string Password
DatabaseType SelectedDatabaseType
ConnectionInfo? ConnectionInfo (derived)
```

**Porta Default per DB**:
- SQL Server: 1433
- PostgreSQL: 5432
- Oracle: 1521

## Flusso di Migrazione

### 1. Connessione
```
User Input (Connessioni) 
    ↓
ConnectDatabasesCommand
    ↓
DatabaseService.TestConnectionAsync (sorgente)
    ↓
DatabaseService.TestConnectionAsync (target)
    ↓
DatabaseService.GetTablesAsync (sorgente)
    ↓
UI: Popolamento lista tabelle
    ↓
IsConnected = true
```

### 2. Selezione
```
User seleziona tabelle (UI)
    ↓
TableInfo.IsSelected = true/false
    ↓
SelectAllTablesCommand / DeselectAllTablesCommand
```

### 3. Migrazione
```
User clicca "Avvia Migrazione"
    ↓
IsMigrating = true
    ↓
Verifica DatabaseExistsAsync (target)
    ↓
Se non esiste: CreateDatabaseAsync
    ↓
SchemaMigrationService.MigrateSchemaAsync
    ├─ GetTableColumnsAsync (sorgente)
    ├─ MapDataType (per ogni colonna)
    ├─ BuildCreateTableStatement
    └─ Esegui DDL nel target
    ↓
Per ogni tabella selezionata:
    DatabaseService.MigrateTableAsync
    ├─ SELECT * FROM sorgente (batch)
    ├─ INSERT INTO target (batch)
    └─ Progress callback
    ↓
IsMigrating = false
    ↓
Completo!
```

## Patterns Utilizzati

### 1. Reactive Programming (ReactiveUI)
- **WhenAnyValue**: Tracking cambiamenti proprietà
- **ReactiveCommand**: Comandi con async/await
- **RaiseAndSetIfChanged**: Notifiche binding

```csharp
public ReactiveCommand<Unit, Unit> ConnectDatabasesCommand { get; }

ConnectDatabasesCommand = ReactiveCommand.CreateFromTask(
    ConnectDatabasesAsync,
    this.WhenAnyValue(vm => vm.IsConnected, vm => vm.IsMigrating,
        (connected, migrating) => connected && !migrating)
);
```

### 2. Dependency Injection Pattern
```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly SchemaMigrationService _schemaMigrationService;

    public MainWindowViewModel()
    {
        _databaseService = new DatabaseService();
        _schemaMigrationService = new SchemaMigrationService();
    }
}
```

### 3. Factory Pattern (DatabaseService)
```csharp
private DbConnection CreateConnection(ConnectionInfo connectionInfo) 
    => connectionInfo.DatabaseType switch
{
    DatabaseType.SqlServer => new SqlConnection(...),
    DatabaseType.PostgreSQL => new NpgsqlConnection(...),
    DatabaseType.Oracle => new OracleConnection(...),
    _ => throw new NotSupportedException()
};
```

### 4. Strategy Pattern (SchemaMigrationService)
```csharp
private string GetTableColumnsQuery(DatabaseType dbType, ...)
    => dbType switch
{
    DatabaseType.SqlServer => /* SQL Server query */,
    DatabaseType.PostgreSQL => /* PostgreSQL query */,
    DatabaseType.Oracle => /* Oracle query */,
};
```

## Sicurezza

### Connessioni
- ✅ TrustServerCertificate per SQL Server
- ✅ SQL Injection Prevention: Query parametrizzate
- ✅ Password in memoria durante sessione
- ⚠️ Nota: Password non criptate in memoria (miglioramento futuro)

### Dati
- ✅ Batch processing con transazioni
- ✅ Error handling con rollback
- ✅ Validazione schema prima migrazione

## Performance

### Ottimizzazioni Implementate
1. **Batch Processing**: 1000 righe per batch
2. **Async/Await**: Non-blocking operations
3. **Connection Pooling**: Gestito da driver nativi
4. **Early Exit**: Validazioni prima operazioni lunghe

### Metriche Tipiche
| Operazione | Tempo Tipico |
|-----------|------------|
| Test connessione | < 1s |
| Discovery 100 tabelle | 2-5s |
| Schema migration 100 tabelle | 10-30s |
| Migrazione 1M righe | 30-120s |

## Testing

### Unit Test (Consigliato)
```csharp
[TestClass]
public class DatabaseServiceTests
{
    [TestMethod]
    public async Task TestConnection_WithValidCredentials_ReturnsTrue()
    {
        // Arrange
        var service = new DatabaseService();
        var connectionInfo = new ConnectionInfo { /* ... */ };
        
        // Act
        var result = await service.TestConnectionAsync(connectionInfo);
        
        // Assert
        Assert.IsTrue(result);
    }
}
```

## Deployment e Build

### Build Command
```bash
dotnet build -c Release
```

### Publish Command (Win-x64)
```bash
dotnet publish src\DatabaseMigrator\DatabaseMigrator.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true
```

### Risultato
- Single-file executable: 166 MB
- Includes: .NET 8.0 runtime + tutte le dipendenze
- Runtime richiesto: Windows 11 x64+

## Miglioramenti Futuri

1. **Sicurezza**:
   - [ ] Encryption delle password in memoria
   - [ ] Secure credential storage
   - [ ] Audit logging

2. **Performance**:
   - [ ] Parallel table migration
   - [ ] Streaming per large datasets
   - [ ] Index optimization post-migration

3. **Features**:
   - [ ] Incremental migration (CDC)
   - [ ] Data validation/reconciliation
   - [ ] Scheduling e automation
   - [ ] Web UI
   - [ ] CLI Interface

4. **UI**:
   - [ ] Advanced filtering tabelle
   - [ ] Column mapping customization
   - [ ] Pre-migration validation report

---

**Ultima Revisione**: Novembre 2025
**Versione**: 1.0.0
