# Script per pubblicare Database Migrator per Windows x64
# Crea un eseguibile self-contained single-file

param(
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"

# Definire i percorsi
$projectPath = Join-Path $PSScriptRoot "src\DatabaseMigrator\DatabaseMigrator.csproj"
$outputDir = Join-Path $PSScriptRoot "publish"
$releaseDir = Join-Path $PSScriptRoot "release"

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Database Migrator - Build & Publish Script" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

# Pulire le directory di output precedenti
if (-not $SkipClean) {
    if (Test-Path $outputDir) {
        Write-Host "Pulizia directory publish precedente..." -ForegroundColor Yellow
        Remove-Item $outputDir -Recurse -Force
    }

    if (Test-Path $releaseDir) {
        Write-Host "Pulizia directory release precedente..." -ForegroundColor Yellow
        Remove-Item $releaseDir -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Step 1: Build Release per Win-x64" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

try {
    & dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputDir
}
catch {
    Write-Host "ERRORE: Fallito il publish del progetto!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Step 2: Copia file nel directory release" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

# Copia l'eseguibile principale
$exePath = Join-Path $outputDir "DatabaseMigrator.exe"
if (Test-Path $exePath) {
    Copy-Item $exePath -Destination $releaseDir
    Write-Host "✓ Eseguibile copiato" -ForegroundColor Green
}

# Copia le DLL necessarie (solo se non incluse nell'exe single-file)
$dlls = Get-ChildItem -Path $outputDir -Filter "*.dll" -ErrorAction SilentlyContinue
if ($dlls) {
    Copy-Item @($dlls) -Destination $releaseDir -Force
    Write-Host "✓ DLL copiate" -ForegroundColor Green
}

# Copia i file di configurazione se esistono
$appSettings = Join-Path $outputDir "appsettings.json"
if (Test-Path $appSettings) {
    Copy-Item $appSettings -Destination $releaseDir
    Write-Host "✓ File di configurazione copiati" -ForegroundColor Green
}

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "Build Completato!" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

$exeFile = Join-Path $releaseDir "DatabaseMigrator.exe"
if (Test-Path $exeFile) {
    $fileSize = (Get-Item $exeFile).Length / 1MB
    Write-Host "Eseguibile pubblicato in: $exeFile" -ForegroundColor Green
    Write-Host "Dimensione: $("{0:F2}" -f $fileSize) MB" -ForegroundColor Green
    Write-Host ""
    Write-Host "Per testare l'applicazione, eseguire:" -ForegroundColor Cyan
    Write-Host "`& '$exeFile'" -ForegroundColor Yellow
    Write-Host ""
}
else {
    Write-Host "ERRORE: Eseguibile non trovato!" -ForegroundColor Red
    exit 1
}
