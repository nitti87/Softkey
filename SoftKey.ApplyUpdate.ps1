# The desktop app embeds this worker. It receives only the signed archive and the
# exact running desktop process to wait for. All installation paths are fixed.
param($display)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required.' }

$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$main = Join-Path $programFiles 'SoftKey-UIAccess-Test'
$logon = Join-Path $programFiles 'SoftKey-Logon-Test'
$desktopExe = Join-Path $main 'SoftKey.exe'
$publisher = '093405919B8DF0EF0F13D124D7DB22FE685979BB'
$certificate = Get-Item -LiteralPath "Cert:\LocalMachine\Root\$publisher"
$installedVersionPath = Join-Path $main 'installed-update-build.txt'
$accessibility = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility'

function Assert-LocalPath([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "A SoftKey installation path is a link: $Path"
    }
}

function Assert-UpdateSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $publisher) {
        throw "Windows could not verify the signature of $(Split-Path -Leaf $Path)."
    }
}

function Assert-WindowsProtection {
    $protection = Get-MpComputerStatus
    $sac = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
    if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled -or $sac -ne $initialSac) {
        throw 'Windows protection does not match its state before the update.'
    }
}

function Invoke-UpdateProbe([string]$Executable, [string[]]$Arguments, [int]$ExitCode) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory (Split-Path -Parent $Executable) `
        -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(12000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'The new SoftKey component did not finish its startup check.'
        }
        if ($process.ExitCode -ne $ExitCode) { throw "Startup check failed for $(Split-Path -Leaf $Executable)." }
    } finally { $process.Dispose() }
}

Assert-LocalPath $programFiles
Assert-LocalPath $main
Assert-LocalPath $desktopExe
Assert-UpdateSignature $desktopExe
$initialSac = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
if ($initialSac -notin 0, 1, 2) { throw 'The current Smart App Control state could not be verified.' }
Assert-WindowsProtection

$oldDesktop = Get-Process -Id $updateParameters.DesktopProcess -ErrorAction SilentlyContinue
if ($oldDesktop -and ($oldDesktop.Path -ine $desktopExe -or
    $oldDesktop.StartTime.ToUniversalTime().Ticks -ne [long]$updateParameters.DesktopStarted)) {
    throw 'The desktop instance changed before the update could begin.'
}
$registered = Get-ItemProperty -LiteralPath "$accessibility\ATs\SoftKey_Logon_v1" -ErrorAction SilentlyContinue
$enabled = @((Get-ItemProperty -LiteralPath $accessibility).Configuration -split ',') -contains 'SoftKey_Logon_v1'
if ($enabled -and $registered.StartExe -notin (Join-Path $main 'SoftKey.Logon.exe'), (Join-Path $logon 'SoftKey.Logon.exe')) {
    throw 'The active SoftKey sign-in path is unrecognized.'
}
$separate = $enabled -and $registered.StartExe -ieq (Join-Path $logon 'SoftKey.Logon.exe')
if ($separate) { Assert-LocalPath $logon }

$lock = [IO.FileStream]::new((Join-Path $main '.install.lock'), 'OpenOrCreate', 'ReadWrite', 'None', 4096, 'DeleteOnClose')
$stage = $null
$brokerToResume = $null
$replaced = [Collections.Generic.List[object]]::new()
$completed = $false
$retainRecovery = $false
try {
    Show-SoftKeyInstallStep $display 62 'Verifierar uppdateringen' "SoftKey-update-$($updateParameters.BuildNumber).zip"
    [long]$installedBuild = $updateParameters.InstalledBuild
    if (Test-Path -LiteralPath $installedVersionPath) {
        Assert-LocalPath $installedVersionPath
        $installedBuild = [Math]::Max($installedBuild, [long]([IO.File]::ReadAllText($installedVersionPath).Trim()))
    }
    $stage = Join-Path $main ('update-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $stage
    $archivePath = Join-Path $stage 'package.zip'
    $package = [IO.Path]::GetFullPath([string]$updateParameters.PackagePath)
    $cache = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SoftKey\Updates'
    if ($package -ine (Join-Path $cache "SoftKey-update-$([long]$updateParameters.BuildNumber).zip")) { throw 'The package is outside the SoftKey cache.' }
    Assert-LocalPath (Split-Path -Parent $cache)
    Assert-LocalPath $cache
    Assert-LocalPath $package
    $sourcePackage = [IO.File]::OpenRead($package)
    try {
        if ($sourcePackage.Length -lt 1 -or $sourcePackage.Length -gt 64MB) { throw 'The update package size is invalid.' }
        $copy = [IO.File]::Open($archivePath, 'CreateNew', 'Write', 'None')
        try { $sourcePackage.CopyTo($copy) } finally { $copy.Dispose() }
    } finally { $sourcePackage.Dispose() }
    $manifest = Read-SoftKeyUpdateArchive -ArchivePath $archivePath -TrustedCertificate $certificate -InstalledBuildNumber $installedBuild
    if ($manifest.BuildNumber -ne $updateParameters.BuildNumber -or $manifest.Commit -cne $updateParameters.Commit) {
        throw 'The selected update package changed.'
    }

    Show-SoftKeyInstallStep $display 65 'Förbereder programfilerna' 'Kontrollerar SoftKeys signerade programdelar.'
    $files = [Collections.Generic.List[object]]::new()
    $archiveFile = [IO.File]::OpenRead($archivePath)
    try {
        $zip = [IO.Compression.ZipArchive]::new($archiveFile, 'Read', $true)
        try {
            foreach ($record in $manifest.Files) {
                $isLogon = $record.Name -match '^SoftKey\.(Logon|Locked)\.'
                $targets = @($main)
                if ($separate -and $isLogon) { $targets += $logon }
                foreach ($target in $targets) {
                    $destination = Join-Path $target $record.Name
                    Assert-LocalPath $destination
                    $suffix = $files.Count
                    $next = Join-Path $stage "$suffix-$($record.Name)"
                    $backup = Join-Path $stage "$suffix.backup"
                    $entry = $zip.GetEntry($record.Name).Open()
                    try {
                        $file = [IO.File]::Open($next, 'CreateNew', 'Write', 'None')
                        try { $entry.CopyTo($file) } finally { $file.Dispose() }
                    } finally { $entry.Dispose() }
                    if ($record.Name -match '\.(exe|dll)$') {
                        Assert-UpdateSignature $next
                        Assert-UpdateSignature $destination
                    }
                    $files.Add([pscustomobject]@{Name=$record.Name; Next=$next; Destination=$destination; Backup=$backup; Hash=$record.SHA256})
                }
            }
        } finally { $zip.Dispose() }
    } finally { $archiveFile.Dispose() }

    Show-SoftKeyInstallStep $display 70 'Förbereder installationen' 'Väntar på att SoftKey stängs.'
    if ($oldDesktop -and -not $oldDesktop.WaitForExit(20000)) { throw 'The desktop keyboard did not close for the update.' }
    $brokerPath = Join-Path $main 'SoftKey.Startup.exe'
    foreach ($task in @(Get-ScheduledTask -TaskName 'SoftKey-LoginBroker-*' -ErrorAction SilentlyContinue | Where-Object State -eq 'Running')) {
        if ($task.Actions.Count -ne 1 -or $task.Actions[0].Execute -ine $brokerPath -or $task.Actions[0].Arguments -ne '--login-broker') {
            throw 'The running lock-screen task is not the registered SoftKey broker.'
        }
        if ($brokerToResume) { throw 'More than one SoftKey sign-in broker is running.' }
        $brokerToResume = $task
        Stop-ScheduledTask -InputObject $task
    }

    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (Get-Process -Name SoftKey,SoftKey.Startup,SoftKey.Logon,SoftKey.Locked -ErrorAction SilentlyContinue) {
        if ($wait.Elapsed.TotalSeconds -ge 8) { throw 'SoftKey is still active. The update will wait for a new attempt from the desktop.' }
        Start-Sleep -Milliseconds 150
    }
    foreach ($file in $files) {
        $number = $replaced.Count + 1
        Show-SoftKeyInstallStep $display (72 + 21 * $number / $files.Count) 'Installerar programfiler' "$($file.Name)  ·  $number av $($files.Count)"
        [IO.File]::Replace($file.Next, $file.Destination, $file.Backup)
        $replaced.Add($file)
    }
    Show-SoftKeyInstallStep $display 94 'Kontrollerar installationen' 'Verifierar programfilerna och provar programstarten.'
    foreach ($file in $files) {
        if ((Get-FileHash -LiteralPath $file.Destination).Hash -cne $file.Hash) { throw 'An installed update file changed.' }
        if ($file.Name -match '\.(exe|dll)$') { Assert-UpdateSignature $file.Destination }
    }
    Invoke-UpdateProbe $desktopExe @('--verify-update-startup', [string]$manifest.BuildNumber) 0
    if ($enabled) {
        $signInRoot = if ($separate) { $logon } else { $main }
        Invoke-UpdateProbe (Join-Path $signInRoot 'SoftKey.Logon.exe') @('--verify-update-boundary') 2
        Invoke-UpdateProbe (Join-Path $signInRoot 'SoftKey.Locked.exe') @('--verify-uiaccess') 0
    }
    Assert-WindowsProtection
    if ($brokerToResume) {
        Start-ScheduledTask -InputObject $brokerToResume
        $brokerToResume = $null
    }
    $nextVersion = Join-Path $stage 'build-number'
    [IO.File]::WriteAllText($nextVersion, [string]$manifest.BuildNumber)
    if ([IO.File]::Exists($installedVersionPath)) { [IO.File]::Replace($nextVersion, $installedVersionPath, (Join-Path $stage 'previous-build')) }
    else { [IO.File]::Move($nextVersion, $installedVersionPath) }
    $completed = $true
} catch {
    $failure = $_
    if ($replaced.Count -gt 0) {
        Show-SoftKeyInstallStep $display $display.State.Snapshot.Percent 'Återställer tidigare programfiler' 'Uppdateringen kunde inte slutföras.'
    }
    for ($index = $replaced.Count - 1; $index -ge 0; $index--) {
        $file = $replaced[$index]
        try { [IO.File]::Replace($file.Backup, $file.Destination, [NullString]::Value) }
        catch { $retainRecovery = $true; Write-Error -ErrorAction Continue "Could not restore $($file.Name). Backup: $($file.Backup)" }
    }
    throw $failure
} finally {
    if ($brokerToResume) {
        try { Start-ScheduledTask -InputObject $brokerToResume } catch { Write-Error -ErrorAction Continue $_ }
    }
    if ($oldDesktop) { $oldDesktop.Dispose() }
    $lock.Dispose()
    # Keep the preceding version after a successful update and any needed recovery files after failure.
    if ($stage -and -not $completed -and -not $retainRecovery) {
        $directory = Get-Item -LiteralPath $stage
        if ($directory.Parent.FullName -eq $main -and -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force
        }
    }
}
