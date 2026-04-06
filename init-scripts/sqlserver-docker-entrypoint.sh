#!/bin/bash
# Starts SQL Server, waits until it accepts connections, then runs sqlserver-init.sql
# once (when TestDB does not exist). The official mssql/server image does not run
# docker-entrypoint-initdb.d scripts automatically (unlike PostgreSQL).

set -euo pipefail

SA_PASS="${MSSQL_SA_PASSWORD:-${SA_PASSWORD:-}}"
if [[ -z "$SA_PASS" ]]; then
  echo "Error: set MSSQL_SA_PASSWORD in docker-compose." >&2
  exit 1
fi

INIT_SQL="/docker-entrypoint-initdb.d/01-init.sql"

echo "Starting SQL Server..."
/opt/mssql/bin/sqlservr &
SQL_PID=$!

echo "Waiting for SQL Server to accept connections..."
ready=0
for ((i = 1; i <= 90; i++)); do
  if /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASS" -C -Q "SELECT 1" -b -o /dev/null 2>/dev/null; then
    ready=1
    break
  fi
  if ! kill -0 "$SQL_PID" 2>/dev/null; then
    echo "sqlservr exited before becoming ready." >&2
    wait "$SQL_PID" || true
    exit 1
  fi
  sleep 2
done

if [[ "$ready" -ne 1 ]]; then
  echo "SQL Server did not become ready in time." >&2
  exit 1
fi

if [[ -f "$INIT_SQL" ]]; then
  COUNT_LINE=$(/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASS" -C -h-1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'TestDB';" -b 2>/dev/null | grep -E '^[[:space:]]*[0-9]+[[:space:]]*$' | head -1 | tr -d '[:space:]\r' || echo "0")
  if [[ "$COUNT_LINE" == "0" ]]; then
    echo "Running sqlserver-init.sql (first-time setup)..."
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASS" -C -i "$INIT_SQL" -b
    echo "SQL Server initialization completed."
  else
    echo "TestDB already present; skipping sqlserver-init.sql."
  fi
fi

wait "$SQL_PID"
