# Database Migrator - Guida al Deployment

## Panoramica

**Database Migrator** è un tool professionale per la migrazione cross-database di dati tra:
- SQL Server
- Oracle
- PostgreSQL

## File di Rilascio

### Eseguibile Standalone (Consigliato)
- **File**: `DatabaseMigrator.exe` (166 MB)
- **Ubicazione**: `/release/`
- **Requisiti**: Windows 11+, nessuna dipendenza esterna
- **Runtime incluso**: .NET 8.0 (self-contained)

## Installazione

### Opzione 1: Esecuzione Diretta
```powershell
# Copia l'exe in una cartella a tua scelta
Copy-Item release\DatabaseMigrator.exe "C:\Program Files\DatabaseMigrator\"

# Esegui
C:\Program Files\DatabaseMigrator\DatabaseMigrator.exe
```

### Opzione 2: Installer NSIS (Consigliato)
```powershell
# Genera l'installer
& "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi

# Risultato: DatabaseMigrator-Setup-v1.0.0.exe
# Esegui l'installer e segui la procedura guidata
```

## Requisiti di Sistema

### Minimi
- **OS**: Windows 11 (64-bit) o superior
- **CPU**: Dual-core 2.0 GHz
- **RAM**: 2 GB
- **Disco**: 200 MB disponibili

### Consigliati
- **OS**: Windows 11 Pro/Enterprise
- **CPU**: Quad-core 2.5 GHz+
- **RAM**: 4+ GB
- **Disco**: 1+ GB (a seconda della dimensione dei database)

## Database di Origine e Destinazione

L'applicazione supporta connessioni a:

### SQL Server
- Versioni: 2019 SP3+, 2022
- Edizioni: Enterprise, Standard, Express
- Accesso: SQL Auth, Windows Auth

### Oracle
- Versioni: 19c, 21c, 23c
- Accesso: Direct o TNS

### PostgreSQL
- Versioni: 12+, 14+, 15+
- Accesso: Direct connection

## Procedura di Utilizzo

### 1. Configurazione Connessioni
1. Avvia l'applicazione
2. Tab "Connessioni Database"
3. Inserisci i dati di connessione:
   - Server/Host
   - Porta
   - Database
   - Username/Password
4. Clicca "Connetti ai Database"

### 2. Selezione Oggetti
1. Tab "Selezione Tabelle"
2. Seleziona le tabelle da migrare
3. Visualizza il numero di righe per tabella
4. Usa i pulsanti di selezione rapida

### 3. Avvio Migrazione
1. Tab "Migrazione"
2. Clicca "Avvia Migrazione"
3. Monitora il progresso
4. Al completamento, il database target sarà popcolato

## Mapping Automatico Tipi Dati

L'applicazione esegue il mapping automatico intelligente dei tipi dati:

| SQL Server | PostgreSQL | Oracle |
|-----------|-----------|---------|
| int | integer | NUMBER(10) |
| bigint | bigint | NUMBER(19) |
| varchar(n) | varchar(n) | VARCHAR2(n) |
| datetime2 | timestamp | TIMESTAMP |
| bit | boolean | NUMBER(1) |
| text | text | CLOB |
| binary | bytea | BLOB |

## Monitoraggio e Logging

L'applicazione fornisce feedback in tempo reale:
- Stato della connessione
- Numero tabelle trovate
- Progresso della migrazione (percentuale)
- Numero righe migrate per tabella
- Messaggi di errore dettagliati

## Troubleshooting

### Connessione Fallita
**Problema**: "Impossibile connettersi al database sorgente"
**Soluzione**:
- Verifica che il server sia raggiungibile
- Controlla credenziali (username/password)
- Verifica porta (1433 SQL Server, 1521 Oracle, 5432 PostgreSQL)
- Verifica firewall

### Timeout Connessione
**Problema**: L'applicazione non risponde durante la connessione
**Soluzione**:
- Aumenta il timeout (modifica CommandTimeout nel codice)
- Verifica la velocità della rete
- Controlla il carico del database di origine

### Migrazione Parziale
**Problema**: Non tutte le righe vengono migrate
**Soluzione**:
- Controlla i messaggi di errore
- Verifica che il target abbia spazio disponibile
- Controlla i vincoli e le relazioni tra tabelle

### Errori di Schema
**Problema**: Errore durante creazione schema
**Soluzione**:
- Verifica che l'utente target abbia permessi DDL
- Controlla se il database target già esiste
- Verifica la compatibilità dei tipi dati

## Performance

### Ottimizzazione
- **Batch Size**: 1000 righe per batch (configurabile nel codice)
- **Timeout**: 300 secondi per operazione
- **Memoria**: ~100-200 MB RAM in uso

### Velocità Tipica
- Connessione: < 1 secondo
- Discovery tabelle: 1-5 secondi
- Schema migration: 5-30 secondi
- Data migration: 10-100MB per minuto (dipende dalla velocità rete)

## Backup Consigliato

Prima di avviare una migrazione importante:
1. Crea backup del database target
2. Testa con un subset di dati
3. Valida l'integrità dei dati post-migrazione

## Uninstallazione

### Se installato tramite NSIS:
1. Pannello di Controllo → Programmi → Programmi e Funzionalità
2. Seleziona "Database Migrator"
3. Clicca "Disinstalla"

### Se eseguibile standalone:
1. Elimina il file DatabaseMigrator.exe
2. Elimina la cartella di installazione

## Supporto Tecnico

Per problemi o richieste:
1. Consulta il README.md per informazioni generali
2. Verifica i log di output dell'applicazione
3. Controlla la connettività di rete

## Versione e Aggiornamenti

**Versione Corrente**: 1.0.0
**Data Rilascio**: Novembre 2025
**Build**: Win-x64, .NET 8.0 self-contained

### Note di Rilascio
- ✅ Supporto SQL Server, Oracle, PostgreSQL
- ✅ UI Avalonia moderna
- ✅ Mapping tipi dati intelligente
- ✅ Single-file executable
- ✅ Progress tracking in tempo reale

---

**Creato con**: .NET 8.0, Avalonia 11.0, ReactiveUI
**Piattaforme Supportate**: Windows 11+ (64-bit)
