$script:SoftKeyLogonAtName = 'SoftKey_Logon_v1'
$script:SoftKeyAccessibilityPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility'
$script:SoftKeyWelcomePath = ".DEFAULT\$script:SoftKeyAccessibilityPath"

function Get-SoftKeySofTypeSecureSnapshot($MachineHive) {
    $key = $MachineHive.OpenSubKey("$script:SoftKeyAccessibilityPath\ATs\oic_softype_45")
    if (-not $key) { return $null }
    try {
        $value = $key.GetValue('SecureDesktopAccommodation', $null)
        if ($null -ne $value -and $key.GetValueKind('SecureDesktopAccommodation') -ne [Microsoft.Win32.RegistryValueKind]::String) { throw 'Unexpected SofType secure-desktop setting type.' }
        return [pscustomobject]@{ Version = 1; Present = $null -ne $value; Value = $value }
    }
    finally { $key.Dispose() }
}

function Set-SoftKeySofTypeSecureAccommodation($MachineHive, $Snapshot, [bool]$TakeOver) {
    if ($null -eq $Snapshot) { return }
    if ($Snapshot.Version -ne 1 -or $Snapshot.Present -isnot [bool] -or
        ($Snapshot.Present -and ($Snapshot.Value -isnot [string] -or $Snapshot.Value.Length -gt 255))) { throw 'Invalid SofType recovery record.' }
    $key = $MachineHive.OpenSubKey("$script:SoftKeyAccessibilityPath\ATs\oic_softype_45", $true)
    if (-not $key) { return }
    try {
        if ($TakeOver) {
            $key.SetValue('SecureDesktopAccommodation', $script:SoftKeyLogonAtName, [Microsoft.Win32.RegistryValueKind]::String)
        }
        elseif ($key.GetValue('SecureDesktopAccommodation', $null) -ieq $script:SoftKeyLogonAtName) {
            if ($Snapshot.Present) { $key.SetValue('SecureDesktopAccommodation', $Snapshot.Value, [Microsoft.Win32.RegistryValueKind]::String) }
            else { $key.DeleteValue('SecureDesktopAccommodation', $false) }
        }
    }
    finally { $key.Dispose() }
}

function Get-SoftKeyLogonAtList([AllowNull()][object]$Value) {
    if ($null -eq $Value) { return @() }
    if ($Value -isnot [string]) { throw 'Windows accessibility Configuration must be a string.' }
    return @($Value.Split(',') | Where-Object { $_.Trim().Length -gt 0 })
}

function Update-SoftKeyLogonAtList([AllowNull()][object]$Value, [bool]$Enabled) {
    $items = @(Get-SoftKeyLogonAtList $Value)
    $present = @($items | Where-Object { $_.Trim() -ieq $script:SoftKeyLogonAtName }).Count -gt 0
    if ($present -eq $Enabled) { return ($items -join ',') }
    if ($Enabled) { return ((@($items) + $script:SoftKeyLogonAtName) -join ',') }
    return (@($items | Where-Object { $_.Trim() -ine $script:SoftKeyLogonAtName }) -join ',')
}

function Get-SoftKeyLogonRegistrySnapshot($Key) {
    $values = @{}
    if ($null -ne $Key) {
        foreach ($name in $Key.GetValueNames()) {
            $values[$name] = @{ Value = $Key.GetValue($name); Kind = $Key.GetValueKind($name) }
        }
    }
    return $values
}

# Only our own registration and list membership are changed; other assistive technologies are preserved.
function Set-SoftKeyLogonRegistration {
    param(
        [Parameter(Mandatory)]$MachineHive,
        [Parameter(Mandatory)]$UserHive,
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][bool]$Enabled,
        [Parameter(Mandatory)]$UsersHive
    )
    $atPath = "$script:SoftKeyAccessibilityPath\ATs\$script:SoftKeyLogonAtName"
    $oldAt = $MachineHive.OpenSubKey($atPath)
    $atExisted = $null -ne $oldAt
    try { $snapshot = Get-SoftKeyLogonRegistrySnapshot $oldAt }
    finally { if ($oldAt) { $oldAt.Dispose() } }
    $contexts = @()
    foreach ($target in @(@{Hive=$MachineHive; Path=$script:SoftKeyAccessibilityPath},
            @{Hive=$UserHive; Path=$script:SoftKeyAccessibilityPath},
            @{Hive=$UsersHive; Path=$script:SoftKeyWelcomePath})) {
        $key = $target.Hive.OpenSubKey($target.Path)
        try {
            $value = if ($key) { $key.GetValue('Configuration', $null) } else { $null }
            $items = @(Get-SoftKeyLogonAtList $value) # Abort before any mutation if Windows data is unexpected.
            $target.Present = @($items | Where-Object { $_.Trim() -ieq $script:SoftKeyLogonAtName }).Count -gt 0
            $contexts += $target
        }
        finally { if ($key) { $key.Dispose() } }
    }
    try {
        if ($Enabled) {
            $key = $MachineHive.CreateSubKey($atPath)
            try {
                foreach ($entry in @{
                    ApplicationName = 'SoftKey – Inloggning'
                    Description = 'Tangentbord och viloklick vid Windows-inloggning.'
                    ATExe = 'SoftKey.Logon.exe'; StartExe = $Executable; StartParams = '--ease-of-access'
                    SimpleProfile = 'On-screen keyboard'
                    Profile = '<HCIModel><Accommodation type="severe dexterity"/></HCIModel>'
                }.GetEnumerator()) { $key.SetValue($entry.Key, $entry.Value, [Microsoft.Win32.RegistryValueKind]::String) }
                $key.SetValue('TerminateOnDesktopSwitch', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
                $key.SetValue('CopySettingsToLockedDesktop', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            }
            finally { $key.Dispose() }
        }
        foreach ($context in $contexts) {
            if ($context.Present -eq $Enabled) { continue }
            $key = $context.Hive.CreateSubKey($context.Path)
            try {
                $value = Update-SoftKeyLogonAtList $key.GetValue('Configuration', $null) $Enabled
                $key.SetValue('Configuration', $value, [Microsoft.Win32.RegistryValueKind]::String)
                if ($key.GetValue('Configuration') -cne $value) { throw 'Could not verify the sign-in configuration.' }
            }
            finally { $key.Dispose() }
        }
        if (-not $Enabled -and $atExisted) { $MachineHive.DeleteSubKeyTree($atPath, $false) }
    }
    catch {
        $failure = $_
        $recoveryErrors = @()
        # Restore just our membership, preserving any intervening additions by other applications.
        foreach ($context in $contexts) {
            try {
                $key = $context.Hive.CreateSubKey($context.Path)
                try { $key.SetValue('Configuration', (Update-SoftKeyLogonAtList $key.GetValue('Configuration', $null) $context.Present), [Microsoft.Win32.RegistryValueKind]::String) }
                finally { $key.Dispose() }
            }
            catch { $recoveryErrors += $_.Exception.Message }
        }
        try {
            $MachineHive.DeleteSubKeyTree($atPath, $false)
            if ($atExisted) {
                $key = $MachineHive.CreateSubKey($atPath)
                try { foreach ($entry in $snapshot.GetEnumerator()) { $key.SetValue($entry.Key, $entry.Value.Value, $entry.Value.Kind) } }
                finally { $key.Dispose() }
            }
        }
        catch { $recoveryErrors += $_.Exception.Message }
        if ($recoveryErrors.Count -gt 0) { throw ($failure.Exception.Message + ' Registry recovery requires attention: ' + ($recoveryErrors -join '; ')) }
        throw $failure
    }
}
