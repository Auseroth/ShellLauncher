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
Set-ItemProperty $regPath -Name "DefaultPassword"   -Value ""
Set-ItemProperty $regPath -Name "DefaultDomainName" -Value ""
Write-Host "Auto-login disabled." -ForegroundColor Green


Write-Host "--- Done. Reboot when ready. ---" -ForegroundColor Green