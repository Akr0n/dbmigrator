param(
    [switch]$SkipDockerUp = $false,
    [switch]$KeepContainers = $false
)

$ErrorActionPreference = "Stop"

function Wait-ContainerHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerName,
        [int]$TimeoutSeconds = 300
    )

    $start = Get-Date
    while ($true) {
        $state = docker inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" $ContainerName 2>$null
        if ($LASTEXITCODE -eq 0 -and ($state -eq "healthy" -or $state -eq "running")) {
            Write-Host "[$ContainerName] status: $state" -ForegroundColor Green
            return
        }

        if ((Get-Date) - $start -gt (New-TimeSpan -Seconds $TimeoutSeconds)) {
            throw "Timeout waiting for container '$ContainerName' to become healthy."
        }

        Start-Sleep -Seconds 5
    }
}

function Initialize-SqlServerFixtures {
    Write-Host "Checking SQL Server fixtures..." -ForegroundColor Cyan

    $dbExistsRaw = docker exec dbmigrator-sqlserver /opt/mssql-tools18/bin/sqlcmd `
        -S localhost `
        -U sa `
        -P SqlServer@123 `
        -C `
        -h -1 `
        -W `
        -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('TestDB') IS NULL THEN 0 ELSE 1 END"

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to check SQL Server fixture state."
    }

    $dbExists = $dbExistsRaw | Select-Object -Last 1
    if ($dbExists -eq "1") {
        Write-Host "SQL Server fixtures already present." -ForegroundColor Green
        return
    }

    Write-Host "Initializing SQL Server fixtures from init script..." -ForegroundColor Cyan
    docker exec dbmigrator-sqlserver /opt/mssql-tools18/bin/sqlcmd `
        -S localhost `
        -U sa `
        -P SqlServer@123 `
        -C `
        -i /docker-entrypoint-initdb.d/01-init.sql

    if ($LASTEXITCODE -ne 0) {
        throw "SQL Server fixture initialization failed."
    }
}

try {
    if (-not $SkipDockerUp) {
        Write-Host "Starting docker-compose services..." -ForegroundColor Cyan
        docker compose up -d
    }

    Wait-ContainerHealthy -ContainerName "dbmigrator-postgres" -TimeoutSeconds 180
    Wait-ContainerHealthy -ContainerName "dbmigrator-sqlserver" -TimeoutSeconds 240
    Wait-ContainerHealthy -ContainerName "dbmigrator-oracle" -TimeoutSeconds 420
    Initialize-SqlServerFixtures

    Write-Host "Running cross-database E2E migration matrix tests..." -ForegroundColor Cyan
    $env:DBMIGRATOR_RUN_E2E = "true"

    dotnet test "tests/DatabaseMigrator.Tests/DatabaseMigrator.Tests.csproj" `
        --configuration Release `
        --filter "Category=E2E" `
        --verbosity normal

    if ($LASTEXITCODE -ne 0) {
        throw "Cross-database E2E migration matrix tests failed."
    }
}
finally {
    if (-not $KeepContainers) {
        Write-Host "Stopping docker-compose services..." -ForegroundColor Cyan
        docker compose down -v
    }
}
