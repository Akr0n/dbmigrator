@echo off
REM Script per pubblicare Database Migrator per Windows x64
REM Crea un eseguibile self-contained single-file

setlocal enabledelayedexpansion

REM Definire i percorsi
set PROJECT_PATH=%~dp0src\DatabaseMigrator\DatabaseMigrator.csproj
set OUTPUT_DIR=%~dp0publish
set RELEASE_DIR=%~dp0release

echo.
echo ====================================================
echo Database Migrator - Build & Publish Script
echo ====================================================
echo.

REM Pulire le directory di output precedenti
if exist "%OUTPUT_DIR%" (
    echo Pulizia directory publish precedente...
    rmdir /s /q "%OUTPUT_DIR%"
)

if exist "%RELEASE_DIR%" (
    echo Pulizia directory release precedente...
    rmdir /s /q "%RELEASE_DIR%"
)

mkdir "%OUTPUT_DIR%"
mkdir "%RELEASE_DIR%"

echo.
echo ====================================================
echo Step 1: Build Release per Win-x64
echo ====================================================
echo.

dotnet publish "%PROJECT_PATH%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:PublishTrimmed=false ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%OUTPUT_DIR%"

if !errorlevel! neq 0 (
    echo.
    echo ERRORE: Fallito il publish del progetto!
    echo.
    pause
    exit /b 1
)

echo.
echo ====================================================
echo Step 2: Copia file nel directory release
echo ====================================================
echo.

REM Copia l'eseguibile principale
copy "%OUTPUT_DIR%\DatabaseMigrator.exe" "%RELEASE_DIR%\DatabaseMigrator.exe"

REM Copia le DLL necessarie
copy "%OUTPUT_DIR%\*.dll" "%RELEASE_DIR%\" 2>nul

REM Copia i file di configurazione se esistono
if exist "%OUTPUT_DIR%\appsettings.json" (
    copy "%OUTPUT_DIR%\appsettings.json" "%RELEASE_DIR%\"
)

echo.
echo ====================================================
echo Build Completato!
echo ====================================================
echo.
echo Eseguibile pubblicato in: %RELEASE_DIR%\DatabaseMigrator.exe
echo Dimensione: 
for /F %%A in ('dir "%RELEASE_DIR%\DatabaseMigrator.exe" ^| find ".exe"') do (
    echo %%A
)
echo.
echo.
echo Per testare l'applicazione, eseguire:
echo %RELEASE_DIR%\DatabaseMigrator.exe
echo.
pause
