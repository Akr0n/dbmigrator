; Database Migrator Installer
; NSIS Installer Script
; Per usare questo file: scarica NSIS da http://nsis.sourceforge.net/
; Poi esegui: makensis installer.nsi

!include "MUI2.nsh"
!include "x64.nsh"

; Nome dell'installer e versione
Name "Database Migrator"
OutFile "DatabaseMigrator-Setup-v1.0.0.exe"
InstallDir "$PROGRAMFILES64\DatabaseMigrator"

; Require admin privileges
RequestExecutionLevel admin

; MUI Settings
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "Italian"
!insertmacro MUI_LANGUAGE "English"

; Installer sections
Section "Install"
  SetOutPath "$INSTDIR"
  
  ; Copy all files from release directory
  File /r "release\*"
  
  ; Create Start Menu shortcut
  CreateDirectory "$SMPROGRAMS\Database Migrator"
  CreateShortcut "$SMPROGRAMS\Database Migrator\Database Migrator.lnk" "$INSTDIR\DatabaseMigrator.exe"
  CreateShortcut "$SMPROGRAMS\Database Migrator\Uninstall.lnk" "$INSTDIR\uninstall.exe"
  
  ; Create Desktop shortcut (optional)
  CreateShortcut "$DESKTOP\Database Migrator.lnk" "$INSTDIR\DatabaseMigrator.exe"
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"
  
  ; Write registry info for uninstall
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator" "DisplayName" "Database Migrator"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator" "UninstallString" "$INSTDIR\uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator" "DisplayVersion" "1.0.0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator" "Publisher" "Database Migrator"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator" "URLInfoAbout" "https://github.com"
SectionEnd

; Uninstaller
Section "Uninstall"
  ; Remove shortcuts
  Delete "$SMPROGRAMS\Database Migrator\Database Migrator.lnk"
  Delete "$SMPROGRAMS\Database Migrator\Uninstall.lnk"
  RMDir "$SMPROGRAMS\Database Migrator"
  Delete "$DESKTOP\Database Migrator.lnk"
  
  ; Remove files
  Delete "$INSTDIR\DatabaseMigrator.exe"
  Delete "$INSTDIR\*"
  RMDir "$INSTDIR"
  
  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DatabaseMigrator"
SectionEnd

; Uninstaller pages
Function un.onInit
  MessageBox MB_ICONQUESTION|MB_YESNO "Sei sicuro di voler disinstallare Database Migrator?" IDYES +2
  Abort
FunctionEnd
