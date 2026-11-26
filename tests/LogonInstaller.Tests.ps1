$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'SoftKey.Logon.Installation.ps1')
$script:checks = 0
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++; Write-Output "PASS $message"
}

# Entirely in-memory registry adapters: no machine or user registry settings are changed.
class LogonTestRegistry {
    [hashtable]$Keys = @{}
    [int]$Writes = 0
    [int]$FailOnceAt = -1
    [object] OpenSubKey([string]$path) { return $this.Keys[$path] }
    [object] OpenSubKey([string]$path, [bool]$writable) { return $this.Keys[$path] }
    [object] CreateSubKey([string]$path) {
        if (-not $this.Keys.ContainsKey($path)) { $this.Keys[$path] = [LogonTestKey]::new($this) }
        return $this.Keys[$path]
    }
    [void] DeleteSubKeyTree([string]$path, [bool]$throwOnMissing) { $this.Keys.Remove($path) }
}
class LogonTestKey {
    [object]$Owner
    [hashtable]$Data = @{}
    [hashtable]$Kinds = @{}
    LogonTestKey([object]$owner) { $this.Owner = $owner }
    [string[]] GetValueNames() { return @($this.Data.Keys) }
    [object] GetValue([string]$name) { return $this.Data[$name] }
    [object] GetValue([string]$name, [object]$fallback) {
        if ($this.Data.ContainsKey($name)) { return $this.Data[$name] }
        return $fallback
    }
    [object] GetValueKind([string]$name) { return $this.Kinds[$name] }
    [void] DeleteValue([string]$name, [bool]$throwOnMissing) { $this.Data.Remove($name); $this.Kinds.Remove($name) }
    [void] SetValue([string]$name, [object]$value, [Microsoft.Win32.RegistryValueKind]$kind) {
        $this.Owner.Writes++
        if ($this.Owner.Writes -eq $this.Owner.FailOnceAt) { throw 'Simulated registry write failure' }
        $this.Data[$name] = $value; $this.Kinds[$name] = $kind
    }
    [void] Dispose() { }
}

$root = $script:SoftKeyAccessibilityPath; $atPath = "$root\ATs\$script:SoftKeyLogonAtName"
Check ((Update-SoftKeyLogonAtList 'osk,Narrator' $true) -eq 'osk,Narrator,SoftKey_Logon_v1') 'enabling preserves other assistive technologies'
Check ((Update-SoftKeyLogonAtList 'osk,SoftKey_Logon_v1,Narrator' $false) -eq 'osk,Narrator') 'disabling removes only SoftKey'
Check ((Update-SoftKeyLogonAtList 'softkey_logon_v1' $true) -eq 'softkey_logon_v1') 'enabling is case-insensitive and idempotent'
$machine = [LogonTestRegistry]::new(); $user = [LogonTestRegistry]::new(); $users = [LogonTestRegistry]::new()
$welcome = $script:SoftKeyWelcomePath
$users.CreateSubKey($welcome).SetValue('Configuration','magnifierpane',[Microsoft.Win32.RegistryValueKind]::String)
$machine.CreateSubKey($root).SetValue('Configuration','osk,Other',[Microsoft.Win32.RegistryValueKind]::String)
$user.CreateSubKey($root).SetValue('Configuration','Narrator',[Microsoft.Win32.RegistryValueKind]::String)
Set-SoftKeyLogonRegistration $machine $user 'C:\Program Files\SoftKey-Logon-Test\SoftKey.Logon.exe' $true $users
Check ($machine.OpenSubKey($root).GetValue('Configuration') -eq 'osk,Other,SoftKey_Logon_v1') 'machine startup preserves other help'
Check ($users.OpenSubKey($welcome).GetValue('Configuration') -eq 'magnifierpane,SoftKey_Logon_v1') 'Windows welcome desktop enables SoftKey before the user profile loads'
Check ($user.OpenSubKey($root).GetValue('Configuration') -eq 'Narrator,SoftKey_Logon_v1') 'secure-desktop transitions are enabled for the current user'
Check ($machine.OpenSubKey($atPath).GetValue('TerminateOnDesktopSwitch') -eq 0) 'Windows keeps a separate assistive instance available on each desktop'
Check ($machine.OpenSubKey($atPath).GetValue('StartParams') -eq '--ease-of-access') 'Windows uses the restricted sign-in entry point'
Set-SoftKeyLogonRegistration $machine $user 'C:\Program Files\SoftKey-Logon-Test\SoftKey.Logon.exe' $false $users
Check ($machine.OpenSubKey($root).GetValue('Configuration') -eq 'osk,Other' -and $user.OpenSubKey($root).GetValue('Configuration') -eq 'Narrator' -and
    $users.OpenSubKey($welcome).GetValue('Configuration') -eq 'magnifierpane') 'disabling restores original membership in all three contexts'
Check ($null -eq $machine.OpenSubKey($atPath)) 'disabling removes SoftKey registration only'
$user.FailOnceAt = $user.Writes + 1
$failed = $false
try { Set-SoftKeyLogonRegistration $machine $user 'C:\Program Files\SoftKey-Logon-Test\SoftKey.Logon.exe' $true $users } catch { $failed = $true }
Check $failed 'partial installation failure is reported'
Check ($machine.OpenSubKey($root).GetValue('Configuration') -eq 'osk,Other' -and $user.OpenSubKey($root).GetValue('Configuration') -eq 'Narrator') 'a failed second-context write rolls back both contexts'
Check ($null -eq $machine.OpenSubKey($atPath)) 'a failed enable leaves no new registered executable'
$machine.CreateSubKey($atPath).SetValue('StartExe','original.exe',[Microsoft.Win32.RegistryValueKind]::String)
$user.FailOnceAt = $user.Writes + 1
try { Set-SoftKeyLogonRegistration $machine $user 'replacement.exe' $true $users } catch { }
Check ($machine.OpenSubKey($atPath).GetValue('StartExe') -eq 'original.exe') 'a failed update restores the prior SoftKey registration'
$users.FailOnceAt = $users.Writes + 1
$failed = $false
try { Set-SoftKeyLogonRegistration $machine $user 'replacement.exe' $true $users } catch { $failed = $true }
Check ($failed -and $machine.OpenSubKey($atPath).GetValue('StartExe') -eq 'original.exe' -and
    $user.OpenSubKey($root).GetValue('Configuration') -eq 'Narrator' -and
    $users.OpenSubKey($welcome).GetValue('Configuration') -eq 'magnifierpane') 'a failed welcome-desktop write rolls back the previous changes'
$machine.OpenSubKey($root).SetValue('Configuration',42,[Microsoft.Win32.RegistryValueKind]::DWord)
$writes = $machine.Writes; $failed = $false
try { Set-SoftKeyLogonRegistration $machine $user 'replacement.exe' $true $users } catch { $failed = $true }
Check ($failed -and $machine.Writes -eq $writes) 'unexpected Windows configuration types abort before any registry mutation'
$sofType = $machine.CreateSubKey("$root\ATs\oic_softype_45")
$sofType.SetValue('StartExe','SofType.exe',[Microsoft.Win32.RegistryValueKind]::String)
$snapshot = Get-SoftKeySofTypeSecureSnapshot $machine
Set-SoftKeySofTypeSecureAccommodation $machine $snapshot $true
Check ($sofType.GetValue('SecureDesktopAccommodation') -eq 'SoftKey_Logon_v1') 'SofType can delegate its secure keyboard to SoftKey to avoid two dwell engines'
Check ($sofType.GetValue('StartExe') -eq 'SofType.exe') 'the regular SofType launcher is preserved'
Set-SoftKeySofTypeSecureAccommodation $machine $snapshot $false
Check ($null -eq $sofType.GetValue('SecureDesktopAccommodation')) 'restoration removes an accommodation override that did not previously exist'
$sofType.SetValue('SecureDesktopAccommodation','osk',[Microsoft.Win32.RegistryValueKind]::String)
$snapshot = Get-SoftKeySofTypeSecureSnapshot $machine
Set-SoftKeySofTypeSecureAccommodation $machine $snapshot $true
Set-SoftKeySofTypeSecureAccommodation $machine $snapshot $false
Check ($sofType.GetValue('SecureDesktopAccommodation') -eq 'osk') 'an existing SofType secure-desktop accommodation is restored exactly'
$sofType.SetValue('SecureDesktopAccommodation','ChangedElsewhere',[Microsoft.Win32.RegistryValueKind]::String)
Set-SoftKeySofTypeSecureAccommodation $machine $snapshot $false
Check ($sofType.GetValue('SecureDesktopAccommodation') -eq 'ChangedElsewhere') 'restoration preserves later independent changes'
Write-Output "All $script:checks sign-in installer checks passed."
