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

Function CloseAppIfRunning
    ; Force kill NewSync.exe using taskkill (runs as admin, most reliable)
    nsExec::ExecToLog 'taskkill /F /IM "${EXENAME}"'
    nsExec::ExecToLog 'taskkill /F /IM "ShellTaskbar.exe"'
    Sleep 1000
FunctionEnd

;--------------------------------
; Version Information
;--------------------------------
VIProductVersion "1.0.0.0"
VIAddVersionKey "CompanyName" "Austin Sharman"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2024 Austin Sharman. All rights reserved."
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "ProductVersion" "1.0.0.0"
VIAddVersionKey "Author" "Austin Sharman"
VIAddVersionKey "FileDescription" "App to launch and monitor any number of apps, designed to be ran as a custom shell app Written By Austin Sharman"
VIAddVersionKey "InternalName" "${APPNAME}"

;--------------------------------
; Installer Icon
;--------------------------------
Icon "C:\\temp file transfer\\9.VisualStudio\\field testing\\ShellLauncher\\ShellLauncher\\bin\\Release\\net8.0\\publish\\win-x86\\ShellLauncher\\Shell_dark.ico"

Section "Install"
    Call CloseAppIfRunning

    SetOutPath "$INSTDIR"

    ; Copy all files EXCEPT .pdb and .pdb-related files
    File /r /x "*.pdb" "C:\temp file transfer\9.VisualStudio\field testing\ShellLauncher\ShellLauncher\bin\Release\net8.0\publish\win-x86\ShellLauncher\*.*"

    ; Write uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; start menu shortcuts
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "/c"

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

    ; delete start menu shortcuts
    setshellvarcontext all
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"

    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
    nsExec::ExecToLog 'schtasks /Delete /TN "AutoLoginChecker" /F'



SectionEnd
