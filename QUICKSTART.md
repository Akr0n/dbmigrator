# Database Migrator - Quick Start Guide

## ⚡ Quick Start (5 minutes)

### 1️⃣ First Launch
```powershell
# Navigate to the project folder
cd c:\_repositories\dbmigrator

# Run directly
.\release\DatabaseMigrator.exe
```

The application will start with an empty interface.

---

## 🔌 Connection Configuration

### Step 1: Source Database
In the **"Database Connections"** tab, left section:

**SQL Server Example**:
```
Database Type:      SqlServer
Server:             localhost (or 192.168.1.100)
Port:               1433
Database:           MySourceDB
Username:           sa
Password:           YourPassword123
```

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
Database:           XE
Username:           system
Password:           oracle123
```

### Step 2: Target Database
In the **"Database Connections"** tab, right section:

```
Database Type:      PostgreSQL (can be different from source!)
Server:             192.168.1.200
Port:               5432
Database:           target_db
Username:           postgres
Password:           postgres456
```

### Step 3: Connect
Click the green **"Connect to Databases"** button

You will see:
- ✅ Progress bar
- ✅ Status messages
- ✅ Number of tables found

---

## 📋 Table Selection

In the **"Table Selection"** tab (enabled after connection):

### Option A: Manual Selection
- Click the checkbox next to each table
- You'll see table name, schema, and row count

### Option B: Select All
- Click **"Select All"**

### Option C: Deselect All
- Click **"Deselect All"**

### Search/Filter
- Use the search box to filter tables by name
- The filter applies to table name and schema

---

## 🔀 Migration Modes

Choose a migration mode before starting:

| Mode | Description | Use Case |
|------|-------------|----------|
| **Schema + Data** | Creates tables and migrates data | Full migration to new database |
| **Schema Only** | Creates empty tables | Prepare target for manual data load |
| **Data Only** | Migrates data only | Tables already exist in target |

**Important**: In "Schema + Data" mode, if migration fails, all created tables are automatically dropped (rollback).

---

## ▶️ Start Migration

In the **"Migration"** tab:

1. Review the status message (should say "Connected!")
2. Select your migration mode
3. Click the **"Start Migration"** button

What happens automatically:
- ✅ Creates target database if it doesn't exist
- ✅ Creates tables with correct schema
- ✅ Migrates data in batches
- ✅ Shows progress percentage
- ✅ Rollback on failure (Schema + Data mode)

---

## 💾 Save/Load Configurations

### Save Configuration
1. Configure both source and target connections
2. File → Save Configuration (or Ctrl+S)
3. Choose a location and filename

### Load Configuration
1. File → Load Configuration (or Ctrl+O)
2. Select a previously saved configuration file
3. Connection fields will be populated automatically

---

## 🔄 Refresh Tables

After connection, if you need to update the table list:
- Click the **"Refresh"** button
- Table selections are preserved
- Row counts are updated

---

## ⚠️ Common Issues

### Connection Failed
- Verify server is reachable (ping)
- Check port is correct and open
- Verify credentials
- Check firewall settings

### "Table does not exist" (Data Only mode)
- Tables must exist in target before using Data Only mode
- Use "Schema + Data" or "Schema Only" first

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
