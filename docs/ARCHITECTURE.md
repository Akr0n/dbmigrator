# Architettura del progetto

## Scopo
Strumento desktop Windows per migrare schema e dati tra **SQL Server, PostgreSQL** e **Oracle** in qualsiasi combinazione sorgente→destinazione.

## Moduli principali

| Progetto | Ruolo |
|---|---|
| `DatabaseMigrator.Core` | Logica di migrazione (nessuna UI) |
| `DatabaseMigrator` | Interfaccia grafica Avalonia (MVVM/ReactiveUI) |
| `DatabaseMigrator.Tests` | Unit + E2E test xUnit |
| `DatabaseMigrator.UiTests` | Test UI headless (Avalonia.Headless, Windows-only) |

### Servizi Core (`DatabaseMigrator.Core.Services`)

| Servizio | Responsabilità |
|---|---|
| `DatabaseServiceBase` | Ritrasmissione retry, factory connessioni, escaping identificatori per dialetto |
| `DatabaseService` (IDatabaseService) | Test connessione, discovery tabelle, migrazione dati batch-by-batch |
| `SchemaMigrationService` | Mappatura 25+ tipi cross-dialect, DDL, gestione vincoli |
| `ForeignKeyService` | Lettura FOREIGN KEY, ordinamento load-order (parents-first), disable/enable chiavi |
| `ScriptGenerationService` | Esportazione script `.sql` (DDL + dati) per tabelle, viste, stored procedure, funzioni, trigger, sequenze, indici |
| `TableDependencyOrderer` | Ordine di caricamento tabelle rispettando FOREIGN KEY (con fallback cicli) |
| `DeleteCascadeReach` | Oracle: calcolo tabella colpite da DELETE CASCADE / SET NULL |
| `SourceResume` | Ripresa lettura sorgente dopo perdita connessione (ORDER BY + OFFSET) |
| `TableSelection` | Filtro ricerca, select/deselect all con carry-over |
| `CredentialProtectionService` | Cifratura password connessioni con DPAPI (Windows-only) |
| `LoggerService` | Logging centralizzato su file locale con rotazione |
| `RuntimeOptionsProvider` | Configurazione runtime: appsettings.json → env vars (`DBMIGRATOR_*`) |

### ViewModel (`DatabaseMigrator.ViewModels`)

| ViewModel | Ruolo |
|---|---|
| `MainWindowViewModel` | Connessione sorgente/destinazione, selezione tabelle, avvio migrazione, log in tempo reale |
| `ScriptGenerationViewModel` | Tab "Genera Script": elenco oggetti, filtri, esportazione .sql |
| `ConnectionViewModel` | Stato UI per una connessione (server, porta, database, credenziali, tipo DB) |

## Flusso dei dati

```
Utente → MainWindow.axaml.cs → MainWindowViewModel
                                      │
                    ┌─────────────────┼─────────────────┐
                    ▼                 ▼                 ▼
              DatabaseService   SchemaMigration     ForeignKeyService
                    │                 │                 │
                    ▼                 ▼                 ▼
            [SQL Server] ←→ Identifiers: [brackets]  │
            [PostgreSQL] ←→ Identifiers: "double"     │
            [Oracle]     ←→ Identifiers: none         │
                              (case folding per target)
```

1. **Connessione**: utente inserisce credenziali → `TestConnectionAsync` valida
2. **Discovery**: `GetTablesAsync` legge catalogo sorgente (query specifica per DBMS)
3. **Selezione**: `TableSelection` filtra/seleziona tabelle; `ForeignKeyService` calcola ordine load
4. **Migrazione schema** (se SchemaAndData o SchemaOnly): `SchemaMigrationService` crea strutture target con mappatura tipi
5. **Migrazione dati** (se SchemaAndData o DataOnly): `DatabaseService.MigrateTableAsync` legge sorgente in batch, INSERT su target
6. **Genera Script**: `ScriptGenerationService` esporta DDL+dati di oggetti selezionati

## Tecnologie e versioni

| Stack | Versione |
|---|---|
| .NET | 11.0 (target: `net11.0`) |
| UI framework | Avalonia 11.3 |
| MVVM | ReactiveUI |
| Driver SQL Server | Microsoft.Data.SqlClient |
| Driver PostgreSQL | Npgsql |
| Driver Oracle | Oracle.ManagedDataAccess.Client |
| Test | xUnit, Avalonia.Headless (UiTests) |
| Logging | File-based con rotazione |

## Decisioni già prese

- **Single trunk**: `main` è l'unico branch lungo; release via `workflow_dispatch` di `release.yml`
- **E2E blocca release**: la matrice E2E (Podman, 3 DB) deve passare prima di publish stabile
- **Cross-dialect invariant**: case e quoting per target sono fissi — SQL Server `[brackets]`, PostgreSQL `"double quotes"`, Oracle nessuna
- **Container engine**: Podman nativo (`DBMIGRATOR_CONTAINER_ENGINE` configurabile), nessun Docker compose; MTU 1280; SQL Server via `127.0.0.1` (non localhost)
- **DPAPI per password**: cifratura credenziali solo su Windows
- **Retries transienti**: configurabili via env var, disabilitabili (`DBMIGRATOR_ENABLE_RETRIES=false`)
