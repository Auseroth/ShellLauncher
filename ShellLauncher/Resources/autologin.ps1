# Variables
$kioskUser = "KioskUser"
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$desiredAutoLogin = "1"
$desiredUserName = $kioskUser
$desiredDomain = $env:COMPUTERNAME

# Configurable verification loop
$maxTries = 30      # Number of verification attempts
$waitSeconds = 1    # Seconds to wait between attempts

# Safe writes with basic error handling
try {
    Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value $desiredAutoLogin -ErrorAction Stop
    Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value $desiredUserName -ErrorAction Stop
    Set-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $desiredDomain -ErrorAction Stop
} catch {
    Write-Host "Failed to set AutoLogin registry values: $($_.Exception.Message)" -ForegroundColor Red
}

# Verify with maxTries and waitSeconds
$tries = 0
do {
    Start-Sleep -Seconds $waitSeconds
    $tries++
    try {
        $currentAutoLogin = (Get-ItemProperty -Path $regPath -Name "AutoAdminLogon" -ErrorAction Stop).AutoAdminLogon
        $currentUserName  = (Get-ItemProperty -Path $regPath -Name "DefaultUserName" -ErrorAction Stop).DefaultUserName
        $currentDomain    = (Get-ItemProperty -Path $regPath -Name "DefaultDomainName" -ErrorAction Stop).DefaultDomainName
    } catch {
        Write-Host "Verification read failed: $($_.Exception.Message)"
        $currentAutoLogin = $null; $currentUserName = $null; $currentDomain = $null
    }

    if ($currentAutoLogin -eq $desiredAutoLogin -and $currentUserName -eq $desiredUserName -and $currentDomain -eq $desiredDomain) {
        Write-Host "Autologin values written and verified."
        exit 0
    }
} while ($tries -lt $maxTries)

Write-Host "Autologin verification failed after $maxTries tries. Current: AutoAdminLogon=$currentAutoLogin, DefaultUserName=$currentUserName, DefaultDomainName=$currentDomain"
exit 2