# Database Migrator - Quick Start Guide

## ⚡ Inizio Rapido (5 minuti)

### 1️⃣ Primo Avvio
```powershell
# Naviga alla cartella del progetto
cd c:\_repositories\dbmigrator

# Esegui direttamente
.\release\DatabaseMigrator.exe
```

L'applicazione partirà con interfaccia vuota.

---

## 🔌 Configurazione Connessioni

### Step 1: Database Sorgente
Nel tab **"Connessioni Database"**, sezione sinistra:

**Esempio SQL Server**:
```
Tipo Database:      SqlServer
Server:             localhost (o 192.168.1.100)
Porta:              1433
Database:           MySourceDB
Username:           sa
Password:           YourPassword123
```

**Esempio PostgreSQL**:
```
Tipo Database:      PostgreSQL
Server:             localhost
Porta:              5432
Database:           source_db
Username:           postgres
Password:           postgres123
```

**Esempio Oracle**:
```
Tipo Database:      Oracle
Server:             localhost
Porta:              1521
Database:           XE
Username:           system
Password:           oracle123
```

### Step 2: Database Target
Nel tab **"Connessioni Database"**, sezione destra:

```
Tipo Database:      PostgreSQL (diverso dalla sorgente!)
Server:             192.168.1.200
Porta:              5432
Database:           target_db
Username:           postgres
Password:           postgres456
```

### Step 3: Connessione
Clicca il pulsante verde **"Connetti ai Database"**

Vedrai:
- ✅ Barra di avanzamento
- ✅ Messaggi di status
- ✅ Numero di tabelle trovate

---

## 📋 Selezione Tabelle

Nel tab **"Selezione Tabelle"** (abilitato dopo connessione):

### Option A: Seleziona Manualmente
- [ ] Clicca il checkbox accanto a ogni tabella
- Vedrai il nome tabella, schema e numero di righe

### Option B: Seleziona Tutto
- Clicca **"Seleziona Tutto"**

### Option C: Deseleziona Tutto
- Clicca **"Deseleziona Tutto"**

---

## ▶️ Avvia Migrazione

Nel tab **"Migrazione"**:

1. Rivedi il messaggio di status (dovrebbe dire "Connesso!")
2. Clicca il pulsante rosso **"Avvia Migrazione"**

Succede automaticamente:
- ✅ Crea il database target se non esiste
- ✅ Crea le tabelle con lo schema corretto
- ✅ Migra tutti i dati selezionati
- ✅ Mostra la barra di progresso

---

## 📊 Monitoraggio

Durante la migrazione vedrai:
- **Barra di progresso**: 0% → 100%
- **Testo percentuale**: Aggiornato in tempo reale
- **Status message**: Mostra operazione corrente
  - "Verifica database target..."
  - "Migrazione schema..."
  - "Migrazione dati: tabella1..."
  - "Migrazione completata! 5 tabelle migrate"

---

## ✅ Verifica Risultato

### In PostgreSQL (con pgAdmin o psql)
```sql
-- Controlla se il database è stato creato
\l

-- Controlla le tabelle
\dt

-- Controlla il numero di righe
SELECT COUNT(*) FROM table_name;
```

### In SQL Server (con SQL Server Management Studio)
```sql
-- Seleziona il database
USE target_db;

-- Controlla le tabelle
SELECT * FROM INFORMATION_SCHEMA.TABLES;

-- Controlla il numero di righe
SELECT COUNT(*) FROM dbo.table_name;
```

### In Oracle (con SQL*Plus o SQL Developer)
```sql
-- Controlla le tabelle
SELECT table_name FROM user_tables;

-- Controlla il numero di righe
SELECT COUNT(*) FROM table_name;
```

---

## 🆘 Troubleshooting Rapido

### ❌ "Impossibile connettersi al database sorgente"
**Soluzione**:
1. Verifica che il server sia raggiungibile: `ping localhost`
2. Verifica le credenziali (username/password)
3. Verifica la porta (1433 SQL Server, 5432 PostgreSQL, 1521 Oracle)
4. Controlla firewall

### ❌ "Errore durante creazione schema"
**Soluzione**:
1. Assicurati che l'utente target abbia permessi DDL (CREATE TABLE)
2. Controlla se il database esiste già
3. Verifica lo spazio disponibile

### ❌ "Timeout migrazione"
**Soluzione**:
1. Riduci il numero di tabelle (riprova con meno tabelle)
2. Controlla la velocità della rete
3. Controlla il carico del database sorgente

### ❌ "Nessuna tabella trovata"
**Soluzione**:
1. Verifica che il database non sia vuoto
2. Controlla i permessi dell'utente
3. Assicurati di usare il nome corretto del database

---

## 💡 Tips & Tricks

### Migrazione Test
Prima di una migrazione importante:
```
1. Copia il database sorgente in test
2. Usa la copia per la prima migrazione
3. Valida i risultati
4. Poi fai la migrazione finale
```

### Migrazione Parziale
Puoi migrare solo alcune tabelle:
```
1. Connetti ai database
2. Deseleziona le tabelle che NON vuoi migrare
3. Avvia la migrazione (solo le selezionate verranno migrate)
```

### Database Diversi
Perfetto per:
```
SQL Server 2019 → PostgreSQL 14
Oracle 19c → SQL Server 2022
PostgreSQL 13 → Oracle 21c
```

---

## 📱 Requisiti Minimi

| Aspetto | Requisito |
|--------|-----------|
| **Sistema Operativo** | Windows 11+ (64-bit) |
| **RAM** | 2 GB minimo |
| **Disco** | 200 MB spazio libero |
| **Connessione Rete** | Accesso ai database sorgente/target |
| **Altre Dipendenze** | Nessuna (runtime incluso) |

---

## 🎯 Checklist Pre-Migrazione

Primo di avviare una migrazione critica:

- [ ] Backup del database target
- [ ] Backup del database sorgente
- [ ] Verifica connettività rete
- [ ] Verifica credenziali database
- [ ] Test con small dataset (poche tabelle)
- [ ] Valida risultati dopo test
- [ ] Sincronizza con il team

---

## 📚 Documentazione Completa

Per informazioni dettagliate:
- **README.md**: Manuale completo
- **DEPLOYMENT.md**: Deployment e troubleshooting avanzato
- **ARCHITECTURE.md**: Dettagli tecnici
- **PROJECT_SUMMARY.md**: Panoramica progetto

---

## 🚀 Prossimi Passi

1. **Usa subito**: Esegui `DatabaseMigrator.exe` ora!
2. **Crea shortcut**: Aggiungi a Desktop/StartMenu per accesso rapido
3. **Condividi**: Distribuisci l'exe ai colleghi
4. **Automatizza**: Integra in script di backup/migration

---

## 📞 Supporto Rapido

**Domanda**: Quale versione di database supporta?
**Risposta**: SQL Server 2019+, Oracle 19c+, PostgreSQL 12+

**Domanda**: Posso migrare tra lo stesso tipo di database?
**Risposta**: Sì! Perfetto anche per backup/clone

**Domanda**: Quanto tempo ci vuole per migrare 1 milione di righe?
**Risposta**: ~1-3 minuti (dipende da velocità rete)

**Domanda**: I dati originali vengono modificati?
**Risposta**: NO! Solo lettura da sorgente, solo scrittura su target

---

**Buona Migrazione! 🎉**

Versione: 1.0.0 | Data: Novembre 2025
