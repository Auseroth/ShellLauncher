!include "FileFunc.nsh"
!include "x64.nsh"

!define APPNAME "ShellLauncher"
!define EXENAME "ShellLauncher.exe"
!define INSTALLDIR "$PROGRAMFILES\${APPNAME}"

Outfile "C:\\temp file transfer\\9.VisualStudio\\exe wrapper scripts\\NSIS Output\\${APPNAME}_Install.exe"
InstallDir "${INSTALLDIR}"

RequestExecutionLevel admin

SilentInstall silent
SilentUninstall silent

Page instfiles

;--------------------------------
; Version Information
;--------------------------------
VIProductVersion "1.0.0.0"
VIAddVersionKey "CompanyName" "City of Newport News - Public Safety IT"
VIAddVersionKey "LegalCopyright" "© 2025 City of Newport News"
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "ProductVersion" "1.0.0.0"
VIAddVersionKey "Author" "Austin Sharman"
VIAddVersionKey "FileDescription" "App to launch and monitor any number of apps, designed to be ran as a custom shell app Written By Austin Sharman"
VIAddVersionKey "InternalName" "${APPNAME}"
VIAddVersionKey "Trademarks" "City of Newport News"

;--------------------------------
; Installer Icon
;--------------------------------
Icon "C:\\temp file transfer\\9.VisualStudio\\field testing\\ShellLauncher\\ShellLauncher\\bin\\Release\\net8.0\\publish\\win-x86\\ShellLauncher\\Shell_dark.ico"

Section "Install"

    SetOutPath "$INSTDIR"

    ; Copy all files EXCEPT .pdb and .pdb-related files
    File /r /x "*.pdb" "C:\temp file transfer\9.VisualStudio\field testing\ShellLauncher\ShellLauncher\bin\Release\net8.0\publish\win-x86\ShellLauncher\*.*"

    ; Write uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Start Menu shortcut
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

    ; Write registry info
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${EXENAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "1.0.0.0"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1

    ;powershell enableShell
    nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\resources\EnableShell.ps1"'

    ; schedule autologon checker task
    nsExec::ExecToLog 'schtasks /Create /TN "AutoLoginChecker" /TR "powershell.exe -ExecutionPolicy Bypass -File \"C:\Program Files (x86)\ShellLauncher\resources\autologin.ps1\"" /SC ONLOGON /RU SYSTEM /rl HIGHEST /f'

SectionEnd

Section "Uninstall"

    ;powershell DisableShell
    nsExec::ExecToLog 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\resources\DisableShell.ps1"'
    ; Remove files
    Delete "$INSTDIR\*.*"
    RMDir /r "$INSTDIR"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
    nsExec::ExecToLog 'schtasks /Delete /TN "AutoLoginChecker" /F'



SectionEnd
