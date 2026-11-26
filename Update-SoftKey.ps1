param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

# Development entry point. Reuse both existing installers; keep the active Windows
# registration at its present protected path and never copy personal word files.
$ErrorActionPreference = 'Stop'
$shellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $worker = Start-Process -FilePath $shellPath -Verb RunAs -WindowStyle Hidden -PassThru `
        -ArgumentList ('-NoProfile -ExecutionPolicy RemoteSigned -File "{0}" -Configuration {1}' -f $PSCommandPath, $Configuration)
    # Start-Process -Wait also waits for descendants, including the reopened GUI.
    $worker.WaitForExit()
    exit $worker.ExitCode
}
. (Join-Path $PSScriptRoot 'SoftKey.Update.ps1')
. (Join-Path $PSScriptRoot 'SoftKey.Logon.Installation.ps1')
$mainRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-UIAccess-Test'
$logonRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-Logon-Test'
$reportPath = Join-Path $PSScriptRoot 'obj\UIAccessTest\paired-update-result.json'
$thumbprint = '093405919B8DF0EF0F13D124D7DB22FE685979BB'
$machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$user = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$users = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::Users, [Microsoft.Win32.RegistryView]::Registry64)
$context = @{ Backup = $null; Hashes = @{}; Recovered = $false; Separate = $false }
$desktopSettingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SoftKey\settings.json'
$logonFiles = @('SoftKey.Logon.exe','SoftKey.Logon.dll','SoftKey.Logon.deps.json','SoftKey.Logon.runtimeconfig.json',
    'SoftKey.Locked.exe','SoftKey.Locked.dll','SoftKey.Locked.deps.json','SoftKey.Locked.runtimeconfig.json','logon-settings.json')
function Invoke-UpdateChild([string]$Script, [string[]]$Options) {
    & $shellPath -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $PSScriptRoot $Script) @Options
    if ($LASTEXITCODE -ne 0) { throw ($Script + ' failed with exit code ' + $LASTEXITCODE) }
}
function Assert-UpdateFile([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'An update path must not be a link.' }
    if ([IO.Path]::GetExtension($Path) -in '.exe','.dll') {
        $signature = Get-AuthenticodeSignature -LiteralPath $Path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint) { throw ('Invalid signed component: ' + $Path) }
    }
}
function Read-DesktopSettings([string]$Path) {
    try {
        $file = Get-Item -LiteralPath $Path -ErrorAction Stop
        if ($file.Length -gt 65536 -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
        Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -ErrorAction Stop
    }
    catch { return }
}
function Initialize-KeyboardSpacing {
    $key = $machine.OpenSubKey('SOFTWARE\SoftKey', $true)
    try {
        if ($key -and $null -ne ($stored = $key.GetValue('KeyboardSpacing'))) {
            if ($stored -is [byte[]] -and $stored.Length -eq 2 -and $stored[0] -le 12 -and $stored[1] -le 12 -and
                ($stored[0] -lt 3 -or $stored[1] -lt 3)) {
                $gap = [byte[]]@([Math]::Max(3, [int]$stored[0]), [Math]::Max(3, [int]$stored[1]))
                $key.SetValue('KeyboardSpacing', $gap, [Microsoft.Win32.RegistryValueKind]::Binary)
            }
            return
        }
    }
    finally { if ($key) { $key.Dispose() } }

    foreach ($path in @($desktopSettingsPath, ($desktopSettingsPath + '.bak'))) {
        $settings = Read-DesktopSettings $path
        if ($null -eq $settings) { continue }
        if ($settings.HasPersonalKeyboardLayout -eq $true) { return }
        $horizontal = $settings.HorizontalKeyGap
        $vertical = $settings.VerticalKeyGap
        if ($null -eq $horizontal -and $null -eq $vertical) { return }
        if (($null -ne $horizontal -and $horizontal -isnot [int] -and $horizontal -isnot [long]) -or
            ($null -ne $vertical -and $vertical -isnot [int] -and $vertical -isnot [long])) { continue }
        $gap = foreach ($value in @($horizontal, $vertical)) {
            if ($null -eq $value) { 4 }
            else { [Math]::Min(12, [Math]::Max(3L, [long]$value)) }
        }
        $key = $machine.CreateSubKey('SOFTWARE\SoftKey')
        try {
            if ($null -eq $key.GetValue('KeyboardSpacing')) {
                $key.SetValue('KeyboardSpacing', [byte[]]$gap, [Microsoft.Win32.RegistryValueKind]::Binary)
            }
        }
        finally { $key.Dispose() }
        return
    }
}
function Initialize-AutoclickTiming {
    $key = $machine.CreateSubKey('SOFTWARE\SoftKey')
    try {
        if ($null -ne $key.GetValue('AutoclickTiming')) { return }
        foreach ($path in @($desktopSettingsPath, ($desktopSettingsPath + '.bak'))) {
            $settings = Read-DesktopSettings $path
            $delay = $settings.DwellMs
            $tolerance = $settings.JitterPx
            if (($delay -isnot [int] -and $delay -isnot [long]) -or ($tolerance -isnot [int] -and $tolerance -isnot [long])) { continue }
            $delay = [Math]::Min(5000L, [Math]::Max(200L, $delay))
            $tolerance = [Math]::Min(100L, [Math]::Max(2L, $tolerance))
            $timing = [byte[]]@(($delay -band 255), ($delay -shr 8), $tolerance)
            $key.SetValue('AutoclickTiming', $timing, [Microsoft.Win32.RegistryValueKind]::Binary)
            return
        }
    }
    finally { $key.Dispose() }
}
function Initialize-KeyboardArea {
    $key = $machine.CreateSubKey('SOFTWARE\SoftKey')
    try {
        if ($null -ne $key.GetValue('KeyboardAreaSize')) { return $true }
        # The reopened desktop keyboard saves its laid-out key area after the startup render.
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            $settings = Read-DesktopSettings $desktopSettingsPath
            $width = $settings.KeyboardArea.Width
            $height = $settings.KeyboardArea.Height
            if (($width -is [int] -or $width -is [long]) -and ($height -is [int] -or $height -is [long]) -and
                $width -ge 160 -and $width -le 4096 -and $height -ge 90 -and $height -le 2160) {
                $bytes = [byte[]]@(($width -band 255), ($width -shr 8), ($height -band 255), ($height -shr 8))
                $key.SetValue('KeyboardAreaSize', $bytes, [Microsoft.Win32.RegistryValueKind]::Binary)
                return $true
            }
            Start-Sleep -Milliseconds 200
        }
        return $false
    }
    finally { $key.Dispose() }
}
function Initialize-DragKeySize {
    $key = $machine.CreateSubKey('SOFTWARE\SoftKey')
    try {
        if ($null -ne $key.GetValue('SmallDragKeyIcons')) { return }
        foreach ($path in @($desktopSettingsPath, ($desktopSettingsPath + '.bak'))) {
            $settings = Read-DesktopSettings $path
            if ($null -eq $settings) { continue }
            $small = $settings.SmallDragKeyIcons -is [bool] -and $settings.SmallDragKeyIcons
            $key.SetValue('SmallDragKeyIcons', [int]$small, [Microsoft.Win32.RegistryValueKind]::DWord)
            return
        }
    }
    finally { $key.Dispose() }
}
try {
    if (Get-Process -Name SoftKey -ErrorAction SilentlyContinue) { throw 'Close SoftKey before updating.' }
    $at = $machine.OpenSubKey("$script:SoftKeyAccessibilityPath\ATs\$script:SoftKeyLogonAtName")
    try { $registeredExe = if ($at) { $at.GetValue('StartExe', '') } else { '' } }
    finally { if ($at) { $at.Dispose() } }
    $enabled = @()
    foreach ($hive in @($machine,$user)) {
        $key = $hive.OpenSubKey($script:SoftKeyAccessibilityPath)
        try { $value = if ($key) { $key.GetValue('Configuration', $null) } else { $null } }
        finally { if ($key) { $key.Dispose() } }
        $enabled += (@((Get-SoftKeyLogonAtList $value) | Where-Object { $_.Trim() -ieq $script:SoftKeyLogonAtName }).Count -gt 0)
    }
    if ($enabled[0] -ne $enabled[1]) { throw 'The existing sign-in choices disagree. No update was started.' }
    if ($enabled[0]) {
        if ($registeredExe -ieq (Join-Path $logonRoot 'SoftKey.Logon.exe')) { $context.Separate = $true }
        elseif ($registeredExe -ine (Join-Path $mainRoot 'SoftKey.Logon.exe')) { throw 'The active sign-in installation has an unexpected path.' }
    }
    $sofTypeSnapshot = Get-SoftKeySofTypeSecureSnapshot $machine
    Invoke-SoftKeyPairedUpdate -SeparateLogon $context.Separate -Desktop {
        Invoke-UpdateChild 'Sign-SoftKey.ps1' @('-Configuration', $Configuration)
    } -Backup {
        foreach ($path in @([Environment]::GetFolderPath('ProgramFiles'),$logonRoot) + @($logonFiles | ForEach-Object { Join-Path $logonRoot $_ })) { Assert-UpdateFile $path }
        $context.Backup = Join-Path $logonRoot ('.paired-update-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $context.Backup | Out-Null
        foreach ($name in $logonFiles) {
            $path = Join-Path $logonRoot $name
            $context.Hashes[$name] = (Get-FileHash -LiteralPath $path).Hash
            Copy-Item -LiteralPath $path -Destination (Join-Path $context.Backup $name)
        }
    } -Disable {
        Invoke-UpdateChild 'Install-SoftKeyLogonTest.ps1' @('-Disable')
    } -Logon {
        Invoke-UpdateChild 'Install-SoftKeyLogonTest.ps1' @('-UseInstalledPackage','-PreserveSettings')
    } -Verify {
        if ($context.Separate) {
            foreach ($name in $logonFiles | Where-Object { $_ -ne 'logon-settings.json' }) {
                Assert-UpdateFile (Join-Path $logonRoot $name)
                if ((Get-FileHash -LiteralPath (Join-Path $mainRoot $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $logonRoot $name)).Hash) { throw ('The two installed packages disagree: ' + $name) }
            }
            if ((Get-FileHash -LiteralPath (Join-Path $logonRoot 'logon-settings.json')).Hash -ne $context.Hashes['logon-settings.json']) { throw 'The sign-in settings changed.' }
        }
    } -Restore {
        Invoke-UpdateChild 'Install-SoftKeyLogonTest.ps1' @('-Disable')
        # Validate the entire protected recovery set before replacing any file.
        foreach ($name in $logonFiles) {
            $backup = Join-Path $context.Backup $name
            Assert-UpdateFile $backup
            if ((Get-FileHash -LiteralPath $backup).Hash -ne $context.Hashes[$name]) { throw 'A recovery file changed.' }
        }
        foreach ($name in $logonFiles) { [IO.File]::Copy((Join-Path $context.Backup $name), (Join-Path $logonRoot $name), $true) }
        Set-SoftKeyLogonRegistration $machine $user $registeredExe $true $users
        Set-SoftKeySofTypeSecureAccommodation $machine $sofTypeSnapshot $true
        $taskName = 'SoftKey-LoginBroker-' + $identity.User.Value
        $task = Get-ScheduledTask -TaskName $taskName
        if ($task.Actions.Count -ne 1 -or $task.Actions[0].Execute -ine (Join-Path $mainRoot 'SoftKey.Startup.exe') -or $task.Actions[0].Arguments -ne '--login-broker') { throw 'The widgets task has an unexpected action.' }
        Start-ScheduledTask -TaskName $taskName
        $context.Recovered = $true
    }
    Initialize-KeyboardSpacing
    Initialize-AutoclickTiming
    Initialize-DragKeySize
    $keyboardAreaReady = Initialize-KeyboardArea
    [pscustomobject]@{Status='Installed';Time=(Get-Date).ToString('o');Desktop=$mainRoot;SeparateLoginUpdated=$context.Separate;
        ActiveLogin=$registeredExe;LoginSettingsPreserved=$true;KeyboardAreaAvailable=$keyboardAreaReady;
        RecoveryPath=$context.Backup;UserSignInTest='Awaiting user'} |
        ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Output 'SoftKey and the enabled sign-in components are updated together.'
    exit 0
}
catch {
    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
    [pscustomobject]@{Status='Failed';Time=(Get-Date).ToString('o');Message=$_.Exception.Message;
        PreviousLoginRestored=$context.Recovered;RecoveryPath=$context.Backup} | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
finally { $machine.Dispose(); $user.Dispose(); $users.Dispose() }
