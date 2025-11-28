# Database Migrator

Tool cross-platform per migrazione di dati tra database relazionali (SQL Server, Oracle, PostgreSQL).

## Caratteristiche

- 🔄 Migrazione dati cross-database
- 🗄️ Supporto SQL Server, Oracle, PostgreSQL
- 🎨 Interfaccia grafica moderna (Avalonia)
- 📊 Selezione selettiva delle tabelle
- 🔧 Mapping automatico tipi dati
- 📈 Barra di progresso in tempo reale
- 🚀 Creazione automatica database target
- 💾 Single-file executable (.exe)

## Requisiti

- Windows 11 o superiore (o Linux per versione nativa)
- .NET 8.0 Runtime (incluso nell'exe standalone)

## Installazione

### Metodo 1: Eseguibile Standalone
1. Scarica `DatabaseMigrator-Setup-v1.0.0.exe` dal Release
2. Esegui l'installer
3. Avvia l'applicazione dal Menu Start

### Metodo 2: Build da Sorgente

#### Prerequisiti di compilazione:
- .NET 8.0 SDK
- PowerShell 7+ (Windows)

#### Compilazione e pubblicazione:

**PowerShell:**
```powershell
# Build e pubblica per Windows x64
.\publish.ps1

# L'eseguibile sarà in: .\release\DatabaseMigrator.exe
```

**Batch/CMD:**
```cmd
REM Build e pubblica per Windows x64
publish.bat

REM L'eseguibile sarà in: .\release\DatabaseMigrator.exe
```

**Manuale con dotnet CLI:**
```bash
dotnet publish src\DatabaseMigrator\DatabaseMigrator.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true
```

## Utilizzo

### Passaggio 1: Connessione ai Database
1. Avvia l'applicazione
2. Nella tab "Connessioni Database":
   - **Database Sorgente**: Inserisci i dati di connessione al DB originale
   - **Database Target**: Inserisci i dati di connessione al DB destinazione
3. Clicca "Connetti ai Database"

### Passaggio 2: Selezione Tabelle
1. Seleziona le tabelle da migrare nella tab "Selezione Tabelle"
2. Usa i pulsanti "Seleziona Tutto" e "Deseleziona Tutto" per gestione rapida
3. Le informazioni sulle righe vengono caricate automaticamente

### Passaggio 3: Migrazione
1. Vai alla tab "Migrazione"
2. Rivedi le informazioni di status
3. Clicca "Avvia Migrazione"
4. Monitora il progresso con la barra di avanzamento
5. L'operazione creerà il database target se non esiste

## Configurazione Connessioni

### SQL Server
- **Tipo**: SqlServer
- **Server**: Nome server o IP
- **Porta**: 1433 (default)
- **Database**: Nome database
- **Username**: sa o user SQL
- **Password**: Password account

### Oracle
- **Tipo**: Oracle
- **Server**: Nome TNS o IP
- **Porta**: 1521 (default)
- **Database**: SID o service name
- **Username**: User Oracle
- **Password**: Password account

### PostgreSQL
- **Tipo**: PostgreSQL
- **Server**: Localhost o IP
- **Porta**: 5432 (default)
- **Database**: Nome database
- **Username**: postgres o altro user
- **Password**: Password account

## Architettura

### Progetto: DatabaseMigrator.Core
Libreria .NET 8.0 contenente:
- **Models**: ConnectionInfo, DatabaseType, TableInfo
- **Services**: DatabaseService (query, DDL, DML), SchemaMigrationService (mapping tipi dati)

### Progetto: DatabaseMigrator
Applicazione Avalonia 11.0 contenente:
- **ViewModels**: MVVM binding e logica UI
- **Views**: XAML per interfaccia grafica
- **Program.cs**: Entry point dell'applicazione

## Specifiche Tecniche

### Mapping Tipi Dati Cross-Database
L'applicazione esegue il mapping automatico dei tipi dati:

**SQL Server → PostgreSQL**
- int → integer
- bigint → bigint
- varchar(n) → varchar(n)
- datetime2 → timestamp
- bit → boolean
- ecc.

**SQL Server → Oracle**
- int → NUMBER(10)
- varchar(n) → VARCHAR2(n)
- datetime2 → TIMESTAMP
- ecc.

**PostgreSQL ↔ Oracle** (e viceversa)
- Mapping completo bidirezionale

### Migrazione Dati
- Lettura batch da sorgente (1000 righe per batch)
- Insert batch nel target
- Progress tracking in tempo reale
- Gestione transazioni

## Creazione Installer NSIS

Per creare l'installer standalone:

1. Installa NSIS: http://nsis.sourceforge.net/Download
2. Esegui il publish: `.\publish.ps1`
3. Compila l'installer:
   ```cmd
   "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
   ```
4. L'installer sarà in: `DatabaseMigrator-Setup-v1.0.0.exe`

## Supporto e Bug Report

Per segnalare bug o richiedere feature, visita il repository GitHub.

## License

MIT License

## Compilazione e Build

```powershell
# Build debug (locale)
dotnet build

# Build release (ottimizzato)
dotnet build -c Release

# Pubblica come single-file executable
.\publish.ps1

# Crea installer NSIS (dopo aver eseguito publish.ps1)
& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
```

---

**Versione**: 1.0.0
**Ultimo aggiornamento**: Novembre 2025
