# 1. Detect if we are in a 32-bit process on a 64-bit OS
if ($env:PROCESSOR_ARCHITEW6432 -eq "AMD64") {
    Write-Host "32-bit detected. Relaunching in 64-bit mode..." -ForegroundColor Cyan
    $nativePS = "$env:SystemRoot\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
    if (Test-Path $nativePS) {
        # Relaunch script and exit the 32-bit instance
        Start-Process $nativePS -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -WindowStyle Hidden -Wait
        Exit
    } else {
        Write-Host "Error: Could not find Sysnative path." -ForegroundColor Red
        Pause
        Exit
    }
}

# Undo-Kiosk.ps1

$kioskUser = "KioskUser"

# Disable Shell Launcher
try {
    $WESL = [wmiclass]"\\.\root\standardcimv2\embedded:WESL_UserSetting"
    $sid = (New-Object System.Security.Principal.NTAccount($kioskUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    try { $WESL.RemoveCustomShell($sid) } catch {}
    $WESL.SetDefaultShell("explorer.exe", 0)
    $WESL.SetEnabled($false)
    Write-Host "Shell Launcher disabled." -ForegroundColor Green
} catch {
    Write-Host "WESL not available." -ForegroundColor Red
}

# Disable auto-login
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty $regPath -Name "AutoAdminLogon"    -Value "0"
Set-ItemProperty $regPath -Name "DefaultUserName"   -Value ""
Set-ItemProperty $regPath -Name "DefaultDomainName" -Value ""
Write-Host "Auto-login disabled." -ForegroundColor Green


Write-Host "--- Done. Reboot when ready. ---" -ForegroundColor Green