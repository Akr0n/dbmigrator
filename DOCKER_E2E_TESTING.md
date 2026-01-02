# E2E Testing Setup with Docker

This setup starts 3 Docker containers with PostgreSQL, Oracle, and SQL Server, preloaded with test data for testing the DatabaseMigrator application.

## Prerequisites

- Docker Desktop installed and running
- PowerShell (for scripts)
- At least 4GB RAM allocated to Docker

## Starting the Databases

```powershell
# Navigate to project root
cd c:\_repositories\dbmigrator

# Start all 3 containers
docker-compose up -d

# Check status
docker-compose ps
```

## Connection Credentials

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
- **Database/SID**: `XE`
- **User**: `migration_test`
- **Password**: `oraclepass123`

### SQL Server
- **Host**: `localhost`
- **Port**: `1433`
- **Database**: `TestDB`
- **User**: `sa`
- **Password**: `SqlServer@123`
- **Schema**: `migration_test`

## Available Tables

Each database contains the following tables with test data:

| Table | Records | Special Columns |
|-------|---------|-----------------|
| users | 4 | password_hash (binary) |
| products | 5 | image, thumbnail (binary) |
| orders | 8 | references users, products |
| audit_log | 4 | change_data (binary) |

### Relational Schema

```
users (1) ──┬─→ (M) orders ←─┬─ (1) products
            │                 │
          user_id        product_id
```

## Suggested Test Cases

### Test 1: PostgreSQL → SQL Server
```
Source: PostgreSQL (localhost:5432, migration_test.*)
Target: SQL Server (localhost:1433, TestDB)

Expected: 4 users + 5 products + 8 orders + 4 audit_log = 21 records migrated
          + Binary data preserved
```

### Test 2: PostgreSQL → Oracle
```
Source: PostgreSQL (localhost:5432, migration_test.*)
Target: Oracle (localhost:1521, XE)

Expected: Same 21 records + type mapping (BYTEA → BLOB)
```

### Test 3: SQL Server → PostgreSQL
```
Source: SQL Server (localhost:1433, TestDB)
Target: PostgreSQL (localhost:5432, testdb)

Expected: Reverse migration with data intact
```

### Test 4: SQL Server → SQL Server (Same DB Type)
```
Source: SQL Server (localhost:1433, TestDB)
Target: SQL Server (localhost:1433, TestDB2)

Expected: Type preservation, VARCHAR(MAX) → VARCHAR(MAX)
```

### Test 5: Schema Only Mode
```
Source: Any database with tables
Target: Empty database

Mode: Schema Only
Expected: Tables created, no data migrated
```

### Test 6: Data Only Mode
```
Prerequisite: Run Schema Only first
Source: Database with data
Target: Database with empty tables

Mode: Data Only
Expected: Data migrated, no schema changes
```

### Test 7: Rollback Test
```
Source: Database with valid tables
Target: Database where data insertion will fail

Mode: Schema + Data
Expected: Tables created, data fails, tables dropped (rollback)
```

## Stopping the Databases

```powershell
# Stop containers
docker-compose down

# Stop and remove volumes (complete reset)
docker-compose down -v
```

## Troubleshooting

### Oracle Container Doesn't Start
```powershell
# Oracle takes longer to initialize (2-3 minutes). Check logs:
docker logs dbmigrator-oracle

# Wait for "DATABASE IS READY TO USE" message
```

### SQL Server Not Responding
```powershell
# Wait at least 30 seconds after startup
docker logs dbmigrator-sqlserver

# If issues persist, increase Docker Desktop memory:
# Settings → Resources → Memory: 4GB or more
```

### Connection Refused
```powershell
# Verify containers are running:
docker ps

# Test connectivity:
# PostgreSQL: psql -h localhost -U pguser -d testdb
# SQL Server: sqlcmd -S localhost -U sa -P SqlServer@123
# Oracle: sqlplus migration_test/oraclepass123@localhost:1521/XE
```

### Port Already in Use
```powershell
# Check what's using the port:
netstat -ano | findstr :1433
netstat -ano | findstr :5432
netstat -ano | findstr :1521

# Stop conflicting services or change ports in docker-compose.yml
```

## Verifying Test Data

### PostgreSQL
```bash
docker exec dbmigrator-postgres psql -U pguser -d testdb -c "SELECT COUNT(*) FROM migration_test.users;"
docker exec dbmigrator-postgres psql -U pguser -d testdb -c "SELECT * FROM migration_test.users;"
```

### SQL Server
```powershell
docker exec dbmigrator-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P SqlServer@123 -Q "SELECT COUNT(*) FROM migration_test.users;"
```

### Oracle
```bash
docker exec dbmigrator-oracle sqlplus -S migration_test/oraclepass123@localhost:1521/XE <<< "SELECT COUNT(*) FROM users;"
```

## Test Data Details

### Users Table
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    username VARCHAR(50),
    email VARCHAR(100),
    password_hash VARBINARY/BYTEA/RAW,  -- Binary data
    created_at DATETIME/TIMESTAMP
);
```

### Products Table
```sql
CREATE TABLE products (
    id INT PRIMARY KEY,
    name VARCHAR(100),
    price DECIMAL(10,2),
    image VARBINARY/BYTEA/BLOB,        -- Binary data
    thumbnail VARBINARY/BYTEA/BLOB     -- Binary data
);
```

### Orders Table
```sql
CREATE TABLE orders (
    id INT PRIMARY KEY,
    user_id INT REFERENCES users(id),
    product_id INT REFERENCES products(id),
    quantity INT,
    order_date DATETIME/TIMESTAMP
);
```

## Notes

- **Binary Data**: Test data uses ASCII-encoded mock data for simplicity. Production would use actual binary files.
- **Foreign Keys**: Orders reference users and products. For proper migration, migrate in order: users → products → orders
- **Schema Names**: PostgreSQL and SQL Server use `migration_test` schema. Oracle uses the `migration_test` user as schema.
