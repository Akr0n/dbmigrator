<#
.SYNOPSIS
    Brings up the three test databases (PostgreSQL, Oracle, SQL Server) with the
    configured container engine (Podman by default), waits for them to become healthy,
    seeds the shared fixtures, then runs the E2E test suite and tears everything down.

.DESCRIPTION
    Native container orchestration — no docker-compose / compose provider required.
    Containers are created with explicit `run` commands (identical names, ports and
    healthchecks to the retired docker-compose.yml) and the init SQL is applied AFTER
    the container is healthy via `cp` + `exec` of the native client, so nothing is
    bind-mounted from the Windows host (the fragile part under rootless Podman/WSL).

    The engine is resolved by scripts/container-engine.ps1 and exported via
    DBMIGRATOR_CONTAINER_ENGINE so the E2E tests (which shell out to `exec`) use the
    same one. Because Podman and Docker share the CLI surface used here, -Engine docker
    still works during the transition.

.PARAMETER SkipStartup
    Assume the containers are already running (skip `run`); still waits for health and
    seeds fixtures if missing. Alias: -SkipDockerUp (backwards compatibility).

.PARAMETER KeepContainers
    Leave the containers, volumes and network in place after the tests finish.
#>
[CmdletBinding()]
param(
    [Alias('SkipDockerUp')]
    [switch]$SkipStartup = $false,
    [switch]$KeepContainers = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Resolve the container engine (Podman by default) --------------------------------
. (Join-Path $PSScriptRoot 'container-engine.ps1')
$Engine = Resolve-ContainerEngine
$EngineName = Split-Path -Leaf $Engine
Write-Host "Container engine: $Engine" -ForegroundColor Cyan

# The E2E tests (ScriptGenerationE2ETests) shell out to the engine for `exec`; make sure
# they use the very same resolved binary.
$env:DBMIGRATOR_CONTAINER_ENGINE = $Engine

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$InitScripts = Join-Path $RepoRoot 'init-scripts'

# Names kept identical to the retired docker-compose.yml so the E2E tests keep working.
$Network   = 'dbmigrator-network'
$PgName    = 'dbmigrator-postgres'
$OraName   = 'dbmigrator-oracle'
$MssqlName = 'dbmigrator-sqlserver'

# --- Engine helpers ------------------------------------------------------------------
function Invoke-Engine {
    param([Parameter(Mandatory)][string[]]$EngineArgs)
    & $Engine @EngineArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$EngineName $($EngineArgs -join ' ') failed (exit code $LASTEXITCODE)."
    }
}

function Test-ContainerExists {
    param([Parameter(Mandatory)][string]$Name)
    & $Engine inspect $Name *> $null
    return ($LASTEXITCODE -eq 0)
}

function Remove-ContainerIfExists {
    param([Parameter(Mandatory)][string]$Name)
    if (Test-ContainerExists $Name) {
        & $Engine rm -f $Name *> $null
    }
}

function Copy-IntoContainer {
    param(
        [Parameter(Mandatory)][string]$Container,
        [Parameter(Mandatory)][string]$HostPath,
        [Parameter(Mandatory)][string]$ContainerPath
    )
    Invoke-Engine @('cp', $HostPath, "${Container}:${ContainerPath}")
}

# --- Container lifecycle -------------------------------------------------------------
function Start-DatabaseContainers {
    # Fresh containers every startup (avoids name clashes / stale state from a crash).
    Remove-ContainerIfExists $PgName
    Remove-ContainerIfExists $OraName
    Remove-ContainerIfExists $MssqlName

    # (Re)create the network pinned to the podman-machine (WSL) path MTU of 1280. The
    # default container network MTU is 65520; SQL Server then emits large TLS handshake
    # packets that exceed the WSL egress MTU and are silently dropped, hanging the TDS/TLS
    # login until the client times out ("connection failed"). PostgreSQL/Oracle send small
    # initial packets and are unaffected. Recreated every run so the MTU is always applied.
    & $Engine network rm $Network *> $null
    Invoke-Engine @('network', 'create', '--opt', 'mtu=1280', $Network)

    Write-Host "Starting PostgreSQL ($PgName)..." -ForegroundColor Cyan
    Invoke-Engine @(
        'run', '-d', '--name', $PgName,
        '--network', $Network,
        '-p', '127.0.0.1:5432:5432',
        '-e', 'POSTGRES_USER=pguser',
        '-e', 'POSTGRES_PASSWORD=pgpass123',
        '-e', 'POSTGRES_DB=testdb',
        '-v', 'postgres_data:/var/lib/postgresql/data',
        '--health-cmd', 'pg_isready -U pguser -d testdb',
        '--health-interval', '10s',
        '--health-timeout', '5s',
        '--health-retries', '5',
        'postgres:16-alpine'
    )

    Write-Host "Starting Oracle ($OraName)..." -ForegroundColor Cyan
    Invoke-Engine @(
        'run', '-d', '--name', $OraName,
        '--network', $Network,
        '-p', '127.0.0.1:1521:1521',
        '-e', 'ORACLE_PASSWORD=oraclepass123',
        '-v', 'oracle_data:/opt/oracle/oradata',
        '--shm-size', '1g',
        '--health-cmd', 'healthcheck.sh',
        '--health-interval', '30s',
        '--health-timeout', '10s',
        '--health-retries', '5',
        '--health-start-period', '60s',
        'gvenzl/oracle-free:23-slim'
    )

    Write-Host "Starting SQL Server ($MssqlName)..." -ForegroundColor Cyan
    Invoke-Engine @(
        'run', '-d', '--name', $MssqlName,
        '--network', $Network,
        '-p', '127.0.0.1:1433:1433',
        '-e', 'ACCEPT_EULA=Y',
        '-e', 'MSSQL_SA_PASSWORD=SqlServer@123',
        '-e', 'MSSQL_PID=Express',
        '-v', 'sqlserver_data:/var/opt/mssql',
        '--health-cmd', '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P SqlServer@123 -C -Q "SELECT 1" -b -o /dev/null || exit 1',
        '--health-interval', '10s',
        '--health-timeout', '10s',
        '--health-retries', '5',
        '--health-start-period', '45s',
        'mcr.microsoft.com/mssql/server:2022-latest'
    )
}

function Wait-ContainerHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerName,
        [int]$TimeoutSeconds = 300
    )

    $start = Get-Date
    while ($true) {
        $state = & $Engine inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" $ContainerName 2>$null
        if ($LASTEXITCODE -eq 0 -and ($state -eq "healthy" -or $state -eq "running")) {
            Write-Host "[$ContainerName] status: $state" -ForegroundColor Green
            return
        }

        # Fail fast if the container died (init script crash, etc.) — no point waiting.
        $runState = & $Engine inspect --format "{{.State.Status}}" $ContainerName 2>$null
        if ($LASTEXITCODE -eq 0 -and ($runState -eq "exited" -or $runState -eq "dead")) {
            Write-Host "[$ContainerName] container is $runState — dumping logs:" -ForegroundColor Red
            & $Engine logs --tail 100 $ContainerName
            throw "Container '$ContainerName' exited before becoming healthy (status=$runState, health=$state)."
        }

        if ((Get-Date) - $start -gt (New-TimeSpan -Seconds $TimeoutSeconds)) {
            Write-Host "[$ContainerName] timeout reached — dumping logs:" -ForegroundColor Red
            & $Engine logs --tail 100 $ContainerName
            throw "Timeout waiting for container '$ContainerName' to become healthy."
        }

        Start-Sleep -Seconds 5
    }
}

# --- Fixture seeding (idempotent, gated on an existence check) ------------------------
function Initialize-Postgres {
    Write-Host "Checking PostgreSQL fixtures..." -ForegroundColor Cyan
    $exists = & $Engine exec $PgName psql -U pguser -d testdb -tAc `
        "SELECT 1 FROM information_schema.schemata WHERE schema_name = 'migration_test'"
    if ($LASTEXITCODE -eq 0 -and ("$exists").Trim() -eq '1') {
        Write-Host "PostgreSQL fixtures already present." -ForegroundColor Green
        return
    }

    Write-Host "Initializing PostgreSQL fixtures from init script..." -ForegroundColor Cyan
    Copy-IntoContainer $PgName (Join-Path $InitScripts 'postgres-init.sql') '/tmp/postgres-init.sql'
    Invoke-Engine @('exec', $PgName, 'psql', '-U', 'pguser', '-d', 'testdb',
        '-v', 'ON_ERROR_STOP=1', '-q', '-f', '/tmp/postgres-init.sql')
}

function Initialize-SqlServer {
    Write-Host "Checking SQL Server fixtures..." -ForegroundColor Cyan
    $dbExistsRaw = & $Engine exec $MssqlName /opt/mssql-tools18/bin/sqlcmd `
        -S localhost -U sa -P SqlServer@123 -C -h -1 -W `
        -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('TestDB') IS NULL THEN 0 ELSE 1 END"
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to check SQL Server fixture state."
    }

    $dbExists = $dbExistsRaw | Select-Object -Last 1
    if ("$dbExists".Trim() -eq '1') {
        Write-Host "SQL Server fixtures already present." -ForegroundColor Green
        return
    }

    Write-Host "Initializing SQL Server fixtures from init script..." -ForegroundColor Cyan
    Copy-IntoContainer $MssqlName (Join-Path $InitScripts 'sqlserver-init.sql') '/tmp/sqlserver-init.sql'
    Invoke-Engine @('exec', $MssqlName, '/opt/mssql-tools18/bin/sqlcmd',
        '-S', 'localhost', '-U', 'sa', '-P', 'SqlServer@123', '-C', '-b',
        '-i', '/tmp/sqlserver-init.sql')
}

# Row count of migration_test.users as seen by SYS (empty string if the table is missing).
# Connects as SYS (OS auth / bequeath) to the CDB root, then switches into FREEPDB1 — which
# is where the fixture user lives (hence oracle-init.sql's ALTER SESSION SET CONTAINER).
function Get-OracleUsersCount {
    $sql = @"
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SET VERIFY OFF
SET DEFINE OFF
WHENEVER SQLERROR CONTINUE
ALTER SESSION SET CONTAINER=FREEPDB1;
SELECT COUNT(1) FROM migration_test.users;
EXIT;
"@
    $out = $sql | & $Engine exec -i $OraName sqlplus -S -L '/' as sysdba
    $line = $out | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -Last 1
    if ($line) { return $line.Trim() }
    return ''
}

# The container healthchecks only prove each database is reachable from INSIDE its
# container. The rootless host port-forward warms up lazily, and right after a fresh
# start the machine is still busy finishing the heavy fixture init, so the very first
# host->database connection can exceed the client's connect timeout — only the first test
# would then flake. Prime each published port from the Windows host (real TCP connect,
# looping until reachable) and let things settle before running the suite.
function Wait-HostConnectionsReady {
    foreach ($port in 5432, 1521, 1433) {
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            $client = [System.Net.Sockets.TcpClient]::new()
            try {
                $iar = $client.BeginConnect('127.0.0.1', $port, $null, $null)
                if ($iar.AsyncWaitHandle.WaitOne(2000) -and $client.Connected) {
                    $client.EndConnect($iar)
                    $ready = $true
                }
            }
            catch { }
            finally { $client.Dispose() }
            if (-not $ready) { Start-Sleep -Milliseconds 500 }
        }
        if ($ready) {
            # A couple of extra connects to fully prime the forward path.
            for ($j = 0; $j -lt 3; $j++) {
                $c = [System.Net.Sockets.TcpClient]::new()
                try { $c.Connect('127.0.0.1', $port) } catch { } finally { $c.Dispose() }
            }
            Write-Host "[host warm-up] 127.0.0.1:$port reachable" -ForegroundColor Green
        }
        else {
            Write-Host "[host warm-up] 127.0.0.1:$port NOT reachable" -ForegroundColor Yellow
        }
    }
    # Brief settle so the databases are past the fixture-init load spike before the suite.
    Start-Sleep -Seconds 5
}

function Initialize-Oracle {
    Write-Host "Checking Oracle fixtures..." -ForegroundColor Cyan
    if ((Get-OracleUsersCount) -eq '4') {
        Write-Host "Oracle fixtures already present." -ForegroundColor Green
        return
    }

    Write-Host "Initializing Oracle fixtures from init script..." -ForegroundColor Cyan
    Copy-IntoContainer $OraName (Join-Path $InitScripts 'oracle-init.sql') '/tmp/oracle-init.sql'
    # Reproduce how the gvenzl image runs init scripts: SET DEFINE OFF (the script embeds a
    # literal '&' inside XMLTYPE data, which SQL*Plus would otherwise treat as a substitution
    # variable) and continue past the one known-benign ORA-01426 on the binary_double_max
    # edge-case row (that row is not part of the E2E fixtures). A genuine failure is caught by
    # re-checking the core fixture row count below, so we don't rely on sqlplus' exit code.
    $runSql = @"
SET DEFINE OFF
WHENEVER SQLERROR CONTINUE
@/tmp/oracle-init.sql
EXIT;
"@
    $runSql | & $Engine exec -i $OraName sqlplus -S -L '/' as sysdba | Out-Null

    $count = Get-OracleUsersCount
    if ($count -ne '4') {
        Write-Host "Oracle fixture validation failed: migration_test.users = '$count' (atteso 4)." -ForegroundColor Red
        throw "Oracle fixture initialization failed."
    }
    Write-Host "Oracle fixtures initialized (users=$count)." -ForegroundColor Green
}

# --- Main ----------------------------------------------------------------------------
try {
    if (-not $SkipStartup) {
        Write-Host "Starting database containers via $EngineName..." -ForegroundColor Cyan
        Start-DatabaseContainers
    }

    Wait-ContainerHealthy -ContainerName $PgName    -TimeoutSeconds 180
    Wait-ContainerHealthy -ContainerName $MssqlName -TimeoutSeconds 240
    Wait-ContainerHealthy -ContainerName $OraName   -TimeoutSeconds 420

    Initialize-Postgres
    Initialize-SqlServer
    Initialize-Oracle

    Write-Host "Warming up host->database connections..." -ForegroundColor Cyan
    Wait-HostConnectionsReady

    Write-Host "Running E2E tests (cross-database migration matrix + script generation)..." -ForegroundColor Cyan
    $env:DBMIGRATOR_RUN_E2E = "true"

    dotnet test (Join-Path $RepoRoot 'tests/DatabaseMigrator.Tests/DatabaseMigrator.Tests.csproj') `
        --configuration Release `
        --filter "Category=E2E" `
        --verbosity normal

    if ($LASTEXITCODE -ne 0) {
        throw "E2E tests failed."
    }
}
finally {
    if (-not $KeepContainers) {
        Write-Host "Tearing down containers ($EngineName)..." -ForegroundColor Cyan
        Remove-ContainerIfExists $PgName
        Remove-ContainerIfExists $OraName
        Remove-ContainerIfExists $MssqlName
        foreach ($vol in @('postgres_data', 'oracle_data', 'sqlserver_data')) {
            & $Engine volume rm $vol *> $null
        }
        & $Engine network rm $Network *> $null
    }
}
