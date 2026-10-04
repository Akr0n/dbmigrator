# Database Migrator - Quick Start Guide

## ⚡ Quick Start (5 minutes)

### 1️⃣ First Launch
```powershell
# Navigate to the project folder
cd c:\_repositories\dbmigrator

# Run directly (.\publish.ps1 writes the exe there; or run the one downloaded from the latest GitHub release)
.\release\DatabaseMigrator.exe
```

The application will start with an empty interface.

---

## 🔌 Connection Configuration

### Step 1: Source Database
In the **"Connessioni Database"** tab (the window is in Italian), left section:

**SQL Server Example**:
```
Database Type:      SqlServer
Server:             localhost (or 192.168.1.100)
Port:               1433
Database:           MySourceDB
Username:           sa
Password:           YourPassword123
```
A SQL Server with a self-signed certificate (for example in a container) needs **"Accetta certificato server (SSL)"** ticked;
the box starts unticked. See "Connection security" in the README for it and for requiring TLS on PostgreSQL and Oracle.

**PostgreSQL Example**:
```
Database Type:      PostgreSQL
Server:             localhost
Port:               5432
Database:           source_db
Username:           postgres
Password:           postgres123
```

**Oracle Example**:
```
Database Type:      Oracle
Server:             localhost
Port:               1521
Database:           FREEPDB1
Username:           system
Password:           oracle123
```

### Step 2: Target Database
In the **"Connessioni Database"** tab, right section:

```
Database Type:      PostgreSQL (can be different from source!)
Server:             192.168.1.200
Port:               5432
Database:           target_db
Username:           postgres
Password:           postgres456
```

### Step 3: Connect
Click the blue **"Connetti ai Database"** button

You will see:
- ✅ Progress bar
- ✅ Status messages
- ✅ Number of tables found

---

## 📋 Table Selection

In the **"Selezione Tabelle"** tab (enabled after connection):

### Option A: Manual Selection
- Click the checkbox next to each table
- You'll see table name, schema, and row count

### Option B: Select All
- Click **"Seleziona Tutto"**: it selects the tables the search filter shows (all of them when the filter is empty), so you can filter one schema and select it, then another

### Option C: Deselect All
- Click **"Deseleziona Tutto"**: it clears every selection, including tables the filter hides

### Search/Filter
- Use the search box to filter tables by name or schema
- If you start a migration while selected tables are hidden by the filter, you are asked to confirm
- The filter applies to table name and schema

---

## 🔀 Migration Modes

Choose a migration mode before starting:

| Mode | Description | Use Case |
|------|-------------|----------|
| **Schema + Data** ("Schema + Dati") | Creates tables with constraints and migrates data | Full migration to new database |
| **Schema Only** ("Solo Schema") | Creates tables with Primary Keys and UNIQUE constraints | Prepare target for manual data load |
| **Data Only** ("Solo Dati") | Migrates data only | Tables already exist in target |

**Important**: 
- With "Schema + Data" and "Data Only", every selected table is **emptied on the target before it is loaded**: its existing rows are replaced
- In "Schema + Data" mode, if migration fails, all created tables are automatically dropped (rollback)
- Primary Keys and UNIQUE constraints are automatically migrated
- Identifier case is handled automatically (PostgreSQL: lowercase, Oracle: UPPERCASE)

---

## ▶️ Start Migration

In the **"Migrazione"** tab:

1. Review the status message (should say "Connesso! Trovate N tabelle")
2. Select your migration mode
3. Click the **"Avvia Migrazione"** button

What happens automatically:
- ✅ Creates the tables on the target (the target database itself must already exist)
- ✅ Creates tables with correct schema and data types
- ✅ Migrates Primary Keys and UNIQUE constraints
- ✅ Applies correct identifier case for target database
- ✅ Migrates data in batches
- ✅ Shows progress percentage
- ✅ Rollback on failure (Schema + Data mode)

---

## 💾 Save/Load Configurations

### Save Configuration
1. Configure both source and target connections
2. File → Salva Configurazione...
3. Choose a location and filename

### Load Configuration
1. File → Carica Configurazione...
2. Select a previously saved configuration file
3. Connection fields will be populated automatically

---

## 🔄 Refresh Tables

After connection, if you need to update the table list:
- Click the **"Aggiorna"** button
- Table selections are preserved
- Row counts are updated

---

## ⚠️ Common Issues

### Connection Failed
- Verify server is reachable (ping)
- Check port is correct and open
- Verify credentials
- Check firewall settings

### "Modalità 'Solo Dati' selezionata ma N tabella/e non esistono nel database di destinazione" (Data Only mode)
- Tables must exist in target before using Data Only mode
- Use "Schema + Data" ("Schema + Dati") or "Schema Only" ("Solo Schema") first

### "String or binary data would be truncated"
- Source column data is larger than target column
- Check data type mapping in logs
- May need to adjust target column size manually

### Timeout
- Large tables may take time
- Default timeout is 300 seconds
- Check network speed

---

## 📊 Supported Migrations

| From \ To | SQL Server | PostgreSQL | Oracle |
|-----------|------------|------------|--------|
| **SQL Server** | ✅ | ✅ | ✅ |
| **PostgreSQL** | ✅ | ✅ | ✅ |
| **Oracle** | ✅ | ✅ | ✅ |

All combinations are supported with automatic data type mapping.
