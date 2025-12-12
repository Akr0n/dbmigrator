# E2E Testing Setup with Docker

Questo setup avvia 3 container Docker con PostgreSQL, Oracle e SQL Server, precaricati con dati di test per testare l'applicazione DatabaseMigrator.

## Prerequisiti

- Docker Desktop installato e running
- PowerShell (per gli script)

## Avvio dei Database

```powershell
# Naviga alla root del progetto
cd c:\_repositories\dbmigrator

# Avvia i 3 container
docker-compose up -d

# Verifica lo stato
docker-compose ps
```

## Credenziali di Connessione

### PostgreSQL
- **Host**: `localhost`
- **Port**: `5432`
- **Database**: `testdb`
- **User**: `pguser`
- **Password**: `pgpass123`
- **Schema**: `migration_test`

### Oracle
- **Host**: `localhost`
- **Port**: `1521`
- **Database**: `XE`
- **User**: `migration_test`
- **Password**: `oraclepass123`
- **SID**: `XE`

### SQL Server
- **Host**: `localhost`
- **Port**: `1433`
- **Database**: `TestDB`
- **User**: `sa`
- **Password**: `SqlServer@123`
- **Schema**: `migration_test`

## Tabelle Disponibili

Ogni database ha le seguenti tabelle con dati di test:

1. **users** - 4 record con password_hash (BYTEA/BLOB/VARBINARY)
2. **products** - 5 record con image e thumbnail (BYTEA/BLOB/VARBINARY)
3. **orders** - 8 record con relationship a users e products
4. **audit_log** - 4 record con change_data binari

### Schema Relazionale

```
users (1) ──┬─→ (M) orders ←─┬─ (1) products
            │                 │
          user_id        product_id
```

## Test Cases Suggeriti

### Test 1: PostgreSQL → SQL Server
```
Source: PostgreSQL (localhost:5432, migration_test.*)
Target: SQL Server (localhost:1433, migration_test.*)

Expected: 4 users + 5 products + 8 orders + 4 audit_log = 21 record migrati
          + Binary data (password_hash, image) preservati
```

### Test 2: PostgreSQL → Oracle
```
Source: PostgreSQL (localhost:5432, migration_test.*)
Target: Oracle (localhost:1521, migration_test.*)

Expected: Stessi 21 record + type mapping (BYTEA → BLOB)
```

### Test 3: SQL Server → PostgreSQL (reverse)
```
Source: SQL Server (localhost:1433, migration_test.*)
Target: PostgreSQL (localhost:5432, migration_test.*)

Expected: Reverse migration con dati intatti
```

## Arresto dei Database

```powershell
# Ferma i container
docker-compose down

# Ferma e rimuove i volumi (per reset completo)
docker-compose down -v
```

## Troubleshooting

### Oracle container non parte
```powershell
# Oracle impiega più tempo (2-3 minuti). Controlla i log:
docker logs dbmigrator-oracle
```

### SQL Server non risponde
```powershell
# Attendi almeno 30 secondi dopo l'avvio
docker logs dbmigrator-sqlserver

# Se persiste, aumenta memory a Docker Desktop:
# Settings > Resources > Memory: 4GB o più
```

### Connessione rifiutata
```powershell
# Verifica che i container siano running:
docker ps

# Verifica la connettività:
# PostgreSQL: psql -h localhost -U pguser -d testdb
# SQL Server: sqlcmd -S localhost -U sa -P SqlServer@123
```

## Verifica Dati

### PostgreSQL
```bash
docker exec dbmigrator-postgres psql -U pguser -d testdb -c "SELECT COUNT(*) FROM migration_test.users;"
```

### SQL Server
```powershell
docker exec dbmigrator-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P SqlServer@123 -Q "SELECT COUNT(*) FROM migration_test.users;"
```

### Oracle
```bash
docker exec dbmigrator-oracle sqlplus -S migration_test/oraclepass123@localhost:1521/XE @migration_test.users;
```

## Note Importanti

- **BYTEA/BLOB/VARBINARY**: I dati binari sono mockati con stringhe ASCII per semplicità. In produzione sarebbero veri PNG/file binari.
- **Foreign Keys**: Gli orders referenziano users e products - esegui la migrazione in ordine: users → products → orders
- **Sequences/Identity**: Oracle usa SEQUENCE, SQL Server usa IDENTITY, PostgreSQL usa SERIAL - il tipo mapping è critico
- **Timestamp**: Tutti i timestamp sono UTC (GETUTCDATE per SQL Server, CURRENT_TIMESTAMP per altri)

## Reset Dati

Se vuoi ricominciare con dati freschi:

```powershell
docker-compose down -v
docker-compose up -d
# Attendi 1-2 minuti per che i DB siano pronti
docker-compose ps  # Verifica HEALTHY status
```
