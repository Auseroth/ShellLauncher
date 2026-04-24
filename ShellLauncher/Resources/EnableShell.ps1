#disable and re-enable wesl
dism /online /disable-feature /featurename:Client-EmbeddedShellLauncher /norestart
dism /online /disable-feature /featurename:Client-DeviceLockdown /norestart
dism /online /enable-feature /featurename:Client-DeviceLockdown /all /norestart
dism /online /enable-feature /featurename:Client-EmbeddedShellLauncher /all /norestart

# Variables
$kioskUser = "KioskUser"
$kioskApp = "`"C:\Program Files (x86)\ShellLauncher\ShellLauncher.exe`""


#create new user
try {
    if (Get-LocalUser -Name $kioskUser -ErrorAction SilentlyContinue) {
        Write-Host "User account already exists - updating..." -ForegroundColor Yellow
        Set-LocalUser -Name $kioskUser -Password ([securestring]::new()) -PasswordNeverExpires $true
        Enable-LocalUser -Name $kioskUser
        Write-Host "User updated." -ForegroundColor Green
    } else {
        New-LocalUser -Name $kioskUser -NoPassword -PasswordNeverExpires
        Add-LocalGroupMember -Group "Users" -Member $kioskUser
        Enable-LocalUser -Name $kioskUser
        Write-Host "User created." -ForegroundColor Green
    }
} catch {
    Write-Host "Couldn't create/update user account." -ForegroundColor Red
}

# Allow blank password
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Lsa" -Name "LimitBlankPasswordUse" -Value 0

# Get SID
$sid = (New-Object System.Security.Principal.NTAccount($kioskUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
Write-Host "SID: $sid" -ForegroundColor Cyan

# Load WMI
try {
    $WESL = [wmiclass]"\\.\root\standardcimv2\embedded:WESL_UserSetting"
    Write-Host "WESL loaded OK." -ForegroundColor Cyan
} catch {
    Write-Host "WESL not available - reboot and re-run." -ForegroundColor Red
    $WESL = $null
}

# Apply Shell Launcher
if ($WESL) {
    $WESL.SetDefaultShell("explorer.exe", 0)
    try { $WESL.RemoveCustomShell($sid) } catch {}
    $WESL.SetCustomShell($sid, $kioskApp, $null, $null, 0)
    $WESL.SetEnabled($true)
    Write-Host "Shell Launcher configured." -ForegroundColor Green
} else {
    Write-Host "Skipped WESL config." -ForegroundColor Yellow
}

# Auto-login
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty $regPath -Name "AutoAdminLogon"    -Value "1"
Set-ItemProperty $regPath -Name "DefaultUserName"   -Value $kioskUser
Set-ItemProperty $regPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME
Write-Host "Auto-login set." -ForegroundColor Green

Write-Host "--- Done. Reboot when ready. ---" -ForegroundColor Green