#log file
$log = "$env:TEMP\EnableShell.log"
"Script started: $(Get-Date)" | Out-File $log

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

# Example log after relaunch check
"Checked for 32-bit process: $(Get-Date)" | Add-Content $log

# Variables
$kioskUser = "KioskUser"
$kioskApp = "`"C:\Program Files (x86)\ShellLauncher\ShellLauncher.exe`""
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$desiredAutoLogin = "1"
$desiredUserName = $kioskUser
$desiredDomain = $env:COMPUTERNAME

#create new user
try {
    if (Get-LocalUser -Name $kioskUser -ErrorAction SilentlyContinue) {
        Write-Host "User account already exists - updating..." -ForegroundColor Yellow
        Set-LocalUser -Name $kioskUser -Password ([securestring]::new()) -AccountNeverExpires -UserMayChangePassword $false -Description "Kiosk User Account" | Set-LocalUser -PasswordNeverExpires $true
        Enable-LocalUser -Name $kioskUser
        Write-Host "User updated." -ForegroundColor Green
        "Created/updated user: $(Get-Date)" | Add-Content $log
    } else {
        New-LocalUser -Name $kioskUser -NoPassword -AccountNeverExpires -UserMayNotChangePassword -Description "Kiosk User Account" | Set-LocalUser -PasswordNeverExpires $true
        Add-LocalGroupMember -Group "Users" -Member $kioskUser
        Enable-LocalUser -Name $kioskUser
        Write-Host "User created." -ForegroundColor Green
        "Created/updated user: $(Get-Date)" | Add-Content $log
    }
} catch {
    Write-Host "Couldn't create/update user account." -ForegroundColor Red
    "Error: $($_.Exception.Message) at $(Get-Date)" | Add-Content $log
}
#set auto login registry values
try {
Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value $desiredAutoLogin
Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value $desiredUserName
Set-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $desiredDomain
} catch {
    Write-Host "Failed to set AutoLogin registry values: $($_.Exception.Message)" -ForegroundColor Red
    "Error: $($_.Exception.Message) at $(Get-Date)" | Add-Content $log
}
do {
    $currentAutoLogin = (Get-ItemProperty -Path $regPath -Name "AutoAdminLogon").AutoAdminLogon
    $currentUserName = (Get-ItemProperty -Path $regPath -Name "DefaultUserName").DefaultUserName
    $currentDomain = (Get-ItemProperty -Path $regPath -Name "DefaultDomainName").DefaultDomainName

    if (
        $currentAutoLogin -ne $desiredAutoLogin -or
        $currentUserName -ne $desiredUserName -or
        $currentDomain -ne $desiredDomain
    ) {
        Start-Sleep -Seconds 1
    }
} while (
    $currentAutoLogin -ne $desiredAutoLogin -or
    $currentUserName -ne $desiredUserName -or
    $currentDomain -ne $desiredDomain
)
Write-Host "All AutoLogin registry values are set."
"Set registry values: $(Get-Date)" | Add-Content $log

#disable and re-enable wesl
dism /online /disable-feature /featurename:Client-EmbeddedShellLauncher /norestart
dism /online /disable-feature /featurename:Client-DeviceLockdown /norestart
dism /online /enable-feature /featurename:Client-DeviceLockdown /all /norestart
dism /online /enable-feature /featurename:Client-EmbeddedShellLauncher /all /norestart


# Allow blank password
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Lsa" -Name "LimitBlankPasswordUse" -Value 0

# Get SID
$sid = (New-Object System.Security.Principal.NTAccount($kioskUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
Write-Host "SID: $sid" -ForegroundColor Cyan

# Load WMI
try {
    $WESL = [wmiclass]"\\.\root\standardcimv2\embedded:WESL_UserSetting"
    Write-Host "WESL loaded OK." -ForegroundColor Cyan
    "WESL loaded: $(Get-Date)" | Add-Content $log
} catch {
    Write-Host "WESL not available - reboot and re-run." -ForegroundColor Red
    "Error: $($_.Exception.Message) at $(Get-Date)" | Add-Content $log
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



Write-Host "--- Done. Reboot when ready. ---" -ForegroundColor Green
"Script completed: $(Get-Date)" | Add-Content $log