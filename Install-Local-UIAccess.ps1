param(
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint = '093405919B8DF0EF0F13D124D7DB22FE685979BB',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Build,
    [switch]$SignOnly,
    [switch]$IncludeLogon,
    [switch]$IncludeStartup,
    [switch]$EnableAutomaticStartup,
    [string]$StartupUserSid = '',
    [switch]$DisableSmartAppControl,
    [string]$SignToolPath = '',
    [long]$UpdateBuildNumber = 0,
    [ValidatePattern('^$|^[0-9a-f]{40}$')][string]$UpdateCommit = ''
)

$ErrorActionPreference = 'Stop'
if ($IncludeStartup) { $IncludeLogon = $true }
if ($EnableAutomaticStartup -and -not $IncludeStartup) { throw 'Automatic startup requires the Startup component.' }
if ($IncludeStartup -and -not $StartupUserSid) { $StartupUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
if ($StartupUserSid) { $StartupUserSid = ([Security.Principal.SecurityIdentifier]::new($StartupUserSid)).Value }
if ($SignOnly -and $DisableSmartAppControl) { throw 'SignOnly cannot change Smart App Control.' }
if (($UpdateBuildNumber -ne 0 -or $UpdateCommit) -and
    (-not $SignOnly -or -not $IncludeStartup -or $Configuration -ne 'Release' -or $UpdateBuildNumber -lt 1 -or -not $UpdateCommit)) {
    throw 'Update packages require a Release sign-only build with all components, a build number and a commit.'
}
if ($DisableSmartAppControl) {
    Write-Warning 'Den slutliga privata installationen stänger av Smart App Control för hela datorn. Antivirus och realtidsskydd behålls. Möjligheten att slå på Smart App Control igen beror på Windows-versionen.'
    . (Join-Path $PSScriptRoot 'SoftKey.Installation.ps1')
}
$buildDir = Join-Path $PSScriptRoot 'obj\UIAccessTest'
$sourceDir = Join-Path (Join-Path $buildDir 'bin\SoftKey') $Configuration.ToLowerInvariant()
$targetDir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-UIAccess-Test'
$targetExe = Join-Path $targetDir 'SoftKey.exe'
$resultPath = if ($SignOnly) { Join-Path $buildDir 'signing-result.json' }
              else { Join-Path $PSScriptRoot 'UIAccess-install-result.json' }
$binaries = 'SoftKey.exe', 'SoftKey.dll'
$files = 'SoftKey.deps.json', 'SoftKey.runtimeconfig.json', 'SoftKey.pdb', 'SoftKey.dll', 'SoftKey.exe'
if ($IncludeLogon) {
    $binaries += 'SoftKey.Logon.exe', 'SoftKey.Logon.dll', 'SoftKey.Locked.exe', 'SoftKey.Locked.dll'
    $files += 'SoftKey.Logon.deps.json', 'SoftKey.Logon.runtimeconfig.json', 'SoftKey.Logon.dll', 'SoftKey.Logon.exe',
        'SoftKey.Locked.deps.json', 'SoftKey.Locked.runtimeconfig.json', 'SoftKey.Locked.dll', 'SoftKey.Locked.exe'
}
if ($IncludeStartup) {
    $binaries += 'SoftKey.Startup.exe', 'SoftKey.Startup.dll'
    $files += 'SoftKey.Startup.exe', 'SoftKey.Startup.dll', 'SoftKey.Startup.deps.json', 'SoftKey.Startup.runtimeconfig.json'
}
$logonSourceDir = Join-Path (Join-Path $buildDir 'bin\SoftKey.Logon') $Configuration.ToLowerInvariant()
$lockedSourceDir = Join-Path (Join-Path $buildDir 'bin\SoftKey.Locked') $Configuration.ToLowerInvariant()
$startupSourceDir = Join-Path (Join-Path $buildDir 'bin\SoftKey.Startup') $Configuration.ToLowerInvariant()
function Get-SoftKeyBuildFile([string]$name) {
    if ($name.StartsWith('SoftKey.Logon.', [StringComparison]::Ordinal)) { return Join-Path $logonSourceDir $name }
    if ($name.StartsWith('SoftKey.Locked.', [StringComparison]::Ordinal)) { return Join-Path $lockedSourceDir $name }
    if ($name.StartsWith('SoftKey.Startup.', [StringComparison]::Ordinal)) { return Join-Path $startupSourceDir $name }
    return Join-Path $sourceDir $name
}
$fileChecks = [ordered]@{}
$smartAppControlChange = $null
$securityResultPath = Join-Path $buildDir 'private-install-security-result.json'
if ($SignToolPath) { $SignToolPath = [IO.Path]::GetFullPath($SignToolPath) }

function Save-Result([string]$status, [string]$message, [string]$path = '', [string]$thumbprint = '') {
    [pscustomobject]@{
        Status = $status
        Message = $message
        InstallPath = $path
        CertificateThumbprint = $thumbprint
        Configuration = $Configuration
        SigningTool = if ($SignToolPath) { 'SignTool' } else { 'PowerShell' }
        SignToolPath = $SignToolPath
        Files = @($fileChecks.Values)
        SmartAppControlChange = $smartAppControlChange
        SmartAppControlChangeReport = if ($DisableSmartAppControl) { $securityResultPath } else { $null }
        Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

if ($Build) {
    try {
        & dotnet build (Join-Path $PSScriptRoot 'SoftKey.csproj') --artifacts-path $buildDir `
            -c $Configuration -p:EnableUIAccess=true -p:UseSharedCompilation=false -v minimal
        if ($LASTEXITCODE -ne 0) { throw "UIAccess build failed with exit code $LASTEXITCODE." }
        if ($IncludeLogon) {
            & dotnet build (Join-Path $PSScriptRoot 'Logon\SoftKey.Logon.csproj') --artifacts-path $buildDir `
                -c $Configuration -p:UseSharedCompilation=false -v minimal
            if ($LASTEXITCODE -ne 0) { throw "Sign-in build failed with exit code $LASTEXITCODE." }
            & dotnet build (Join-Path $PSScriptRoot 'LockedDesktop\SoftKey.Locked.csproj') --artifacts-path $buildDir `
                -c $Configuration -p:UseSharedCompilation=false -v minimal
            if ($LASTEXITCODE -ne 0) { throw "Locked-desktop build failed with exit code $LASTEXITCODE." }
        }
        if ($IncludeStartup) {
            & dotnet build (Join-Path $PSScriptRoot 'Startup\SoftKey.Startup.csproj') --artifacts-path $buildDir `
                -c $Configuration -p:UseSharedCompilation=false -v minimal
            if ($LASTEXITCODE -ne 0) { throw "Startup settings build failed with exit code $LASTEXITCODE." }
        }
    }
    catch {
        New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
        Save-Result 'BuildFailed' $_.Exception.Message
        exit 1
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        $powershellExe = (Get-Process -Id $PID).Path
        $quotedScript = '"{0}"' -f $PSCommandPath
        $arguments = "-NoProfile -ExecutionPolicy RemoteSigned -File $quotedScript -CertificateThumbprint $CertificateThumbprint -Configuration $Configuration"
        if ($SignOnly) { $arguments += ' -SignOnly' }
        if ($IncludeLogon) { $arguments += ' -IncludeLogon' }
        if ($IncludeStartup) { $arguments += ' -IncludeStartup' }
        if ($EnableAutomaticStartup) { $arguments += ' -EnableAutomaticStartup' }
        if ($StartupUserSid) { $arguments += (' -StartupUserSid "{0}"' -f $StartupUserSid) }
        if ($DisableSmartAppControl) { $arguments += ' -DisableSmartAppControl' }
        if ($SignToolPath) { $arguments += (' -SignToolPath "{0}"' -f $SignToolPath) }
        if ($UpdateBuildNumber -gt 0) { $arguments += " -UpdateBuildNumber $UpdateBuildNumber -UpdateCommit $UpdateCommit" }
        $elevatedProcess = Start-Process -FilePath $powershellExe -Verb RunAs -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -PassThru -Wait -WindowStyle Hidden
        exit $elevatedProcess.ExitCode
    }
    catch {
        Save-Result 'ElevationCancelled' $_.Exception.Message
        exit 1223
    }
}

function Assert-Signature($signature, [string]$name) {
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
        throw "Signature verification failed for ${name}: $($signature.Status) - $($signature.StatusMessage)"
    }
}

function Assert-SoftKeyClosed {
    if (Get-Process -Name SoftKey -ErrorAction SilentlyContinue) {
        throw 'Close SoftKey before updating the local installation.'
    }
}

function Assert-CodeIntegrity([string]$path) {
    if (-not ('SoftKeyImagePolicy' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class SoftKeyImagePolicy
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileMappingW(SafeFileHandle file, IntPtr attributes,
        uint protection, uint sizeHigh, uint sizeLow, IntPtr name);

    public static int Check(SafeFileHandle file)
    {
        const uint PageReadOnly = 0x02;
        const uint SecImage = 0x01000000;
        using (var section = CreateFileMappingW(file, IntPtr.Zero, SecImage | PageReadOnly, 0, 0, IntPtr.Zero))
            return section.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }
}
'@
    }

    # An executable image mapping invokes Windows Code Integrity without starting SoftKey.
    $stream = [IO.File]::OpenRead($path)
    try { $errorCode = [SoftKeyImagePolicy]::Check($stream.SafeFileHandle) }
    finally { $stream.Dispose() }
    if ($errorCode -ne 0) {
        $reason = [System.ComponentModel.Win32Exception]::new($errorCode).Message
        throw [System.ComponentModel.Win32Exception]::new($errorCode,
            "Windows could not load $(Split-Path -Leaf $path) (error $errorCode). $reason")
    }
}

function Assert-PackageCodeIntegrity([string]$directory) {
    $firstFailure = $null
    foreach ($name in $binaries) {
        try {
            Assert-CodeIntegrity (Join-Path $directory $name)
            $fileChecks[$name].WindowsCodeIntegrity = 'Accepted'
            $fileChecks[$name].WindowsMessage = ''
        }
        catch {
            $fileChecks[$name].WindowsCodeIntegrity = 'Rejected'
            $fileChecks[$name].WindowsMessage = $_.Exception.Message
            if ($null -eq $firstFailure) { $firstFailure = $_.Exception }
        }
    }
    if ($null -ne $firstFailure) { throw $firstFailure }
}

$stage = $null
$keepStage = $false
$installLock = $null
$replacedFiles = [ordered]@{}
$brokerTaskToResume = $null
try {
    foreach ($name in $files) {
        if (-not (Test-Path -LiteralPath (Get-SoftKeyBuildFile $name) -PathType Leaf)) {
            throw "UIAccess build file missing: $name in $sourceDir"
        }
    }

    $cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint"
    $now = Get-Date
    if (-not $cert.HasPrivateKey -or $cert.NotBefore -gt $now -or $cert.NotAfter -le $now) {
        throw 'The selected local signing certificate cannot be used.'
    }

    if ($SignToolPath) {
        $tool = Get-Item -LiteralPath $SignToolPath
        $toolSignature = Get-AuthenticodeSignature -LiteralPath $SignToolPath
        if ($tool.PSIsContainer -or $tool.Extension -ine '.exe' -or $toolSignature.Status -ne 'Valid' -or
            $toolSignature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
            throw 'SignTool must be a valid Microsoft-signed executable.'
        }
    }

    $stageRoot = $buildDir
    if (-not $SignOnly) {
        Assert-SoftKeyClosed
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        if ((Get-Item -LiteralPath $targetDir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'The install directory must not be a link or junction.'
        }
        $installLock = [IO.FileStream]::new((Join-Path $targetDir '.install.lock'),
            [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
        $stageRoot = $targetDir
    }

    $stage = Join-Path $stageRoot ('SoftKey-signed-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in $files) {
        Copy-Item -LiteralPath (Get-SoftKeyBuildFile $name) -Destination (Join-Path $stage $name)
    }

    $stagedExe = Join-Path $stage 'SoftKey.exe'
    $exeText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($stagedExe))
    $executionLevels = [regex]::Matches($exeText, '<requestedExecutionLevel\b[^>]*>')
    if ($executionLevels.Count -ne 1 -or $executionLevels[0].Value -cnotmatch '\buiAccess="true"') {
        throw 'This build does not request UIAccess.'
    }

    foreach ($name in $binaries) {
        $stagedFile = Join-Path $stage $name
        if ($SignToolPath) {
            & $SignToolPath sign /v /sm /s My /sha1 $CertificateThumbprint /fd SHA256 $stagedFile
            if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed for $name (exit $LASTEXITCODE)." }
            & $SignToolPath verify /pa /all /v $stagedFile
            if ($LASTEXITCODE -ne 0) { throw "SignTool signature verification failed for $name (exit $LASTEXITCODE)." }
        }
        else {
            Set-AuthenticodeSignature -LiteralPath $stagedFile -Certificate $cert -HashAlgorithm SHA256 | Out-Null
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $stagedFile
        Assert-Signature $signature $name
        $fileChecks[$name] = [pscustomobject]@{
            Name = $name
            SHA256 = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash
            SignatureStatus = [string]$signature.Status
            CertificateThumbprint = $signature.SignerCertificate.Thumbprint
            WindowsCodeIntegrity = 'NotChecked'
            WindowsMessage = ''
        }
    }

    if ($SignOnly) {
        if ($UpdateBuildNumber -gt 0) {
            . (Join-Path $PSScriptRoot 'SoftKey.UpdatePackage.ps1')
            $archive = Join-Path $buildDir "SoftKey-update-$UpdateBuildNumber.zip"
            New-SoftKeyUpdateArchive -PackageDirectory $stage -ArchivePath $archive -BuildNumber $UpdateBuildNumber `
                -Commit $UpdateCommit -Certificate $cert
        }
        Save-Result 'Signed' 'The local test package is signed and verified.' $stagedExe $cert.Thumbprint
        $keepStage = $true
        exit 0
    }

    if ($DisableSmartAppControl) {
        Assert-SoftKeyClosed
        $smartAppControlChange = Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $securityResultPath
    }
    Assert-PackageCodeIntegrity $stage
    Assert-SoftKeyClosed
    if ($IncludeStartup -and $StartupUserSid) {
        $brokerTaskName = 'SoftKey-LoginBroker-' + $StartupUserSid
        $brokerTask = Get-ScheduledTask -TaskName $brokerTaskName -ErrorAction SilentlyContinue
        if ($brokerTask -and $brokerTask.State -eq 'Running') {
            if ($brokerTask.Actions.Count -ne 1 -or $brokerTask.Actions[0].Execute -ine (Join-Path $targetDir 'SoftKey.Startup.exe') -or
                $brokerTask.Actions[0].Arguments -ne '--login-broker') { throw 'The existing widgets task has an unexpected action.' }
            $brokerTaskToResume = $brokerTaskName
            Stop-ScheduledTask -TaskName $brokerTaskName -ErrorAction Stop
            $brokerStopWait = [Diagnostics.Stopwatch]::StartNew()
            while ((Get-ScheduledTask -TaskName $brokerTaskName).State -eq 'Running') {
                if ($brokerStopWait.Elapsed.TotalSeconds -ge 5) { throw 'The widgets task did not stop before updating its program files.' }
                Start-Sleep -Milliseconds 100
            }
        }
    }
    foreach ($name in $files) {
        $stagedFile = Join-Path $stage $name
        $installedFile = Join-Path $targetDir $name
        $replacedFiles[$name] = Test-Path -LiteralPath $installedFile
        if ($replacedFiles[$name]) {
            [IO.File]::Replace($stagedFile, $installedFile, (Join-Path $stage "$name.bak"))
        }
        else {
            [IO.File]::Move($stagedFile, $installedFile)
        }
    }
    foreach ($name in $binaries) {
        $installedFile = Join-Path $targetDir $name
        Assert-Signature (Get-AuthenticodeSignature -LiteralPath $installedFile) $name
    }
    Assert-PackageCodeIntegrity $targetDir

    if ($IncludeStartup) {
        $startupMode = if ($EnableAutomaticStartup) { '--enable-automatic' } else { '--upgrade-autostart' }
        $startupMigration = Start-Process -FilePath (Join-Path $targetDir 'SoftKey.Startup.exe') `
            -ArgumentList @($startupMode, $StartupUserSid) -WorkingDirectory $targetDir -WindowStyle Hidden -Wait -PassThru
        if ($startupMigration.ExitCode -ne 0) { throw "The startup configuration could not be upgraded (exit $($startupMigration.ExitCode))." }
    }

    Save-Result 'Installed' 'Both installed binaries passed signature and Windows Code Integrity checks.' $targetExe $cert.Thumbprint
    exit 0
}
catch {
    $status = if ($_.Exception -is [System.ComponentModel.Win32Exception] -and $_.Exception.NativeErrorCode -eq 4551) {
        'BlockedByWindows'
    }
    else { 'Failed' }
    $message = $_.Exception.Message
    $names = @($replacedFiles.Keys)
    for ($i = $names.Count - 1; $i -ge 0; $i--) {
        $name = $names[$i]
        $backup = Join-Path $stage "$name.bak"
        $installedFile = Join-Path $targetDir $name
        try {
            if (Test-Path -LiteralPath $backup) {
                if (Test-Path -LiteralPath $installedFile) {
                    [IO.File]::Replace($backup, $installedFile, [NullString]::Value)
                }
                else {
                    [IO.File]::Move($backup, $installedFile)
                }
            }
            elseif (-not $replacedFiles[$name] -and (Test-Path -LiteralPath $installedFile)) {
                Remove-Item -LiteralPath $installedFile -Force
            }
        }
        catch {
            $keepStage = $true
            $status = 'Failed'
            $message += " Rollback failed for $name. Recovery files are in $stage."
        }
    }
    Save-Result $status $message $targetExe $CertificateThumbprint
    if ($status -eq 'BlockedByWindows') {
        # This process is already elevated. Collect the exact policy/signature events
        # while they are fresh, without changing policy or retrying the installation.
        try {
            $diagnosticScript = Join-Path $PSScriptRoot 'Get-UIAccessDiagnostics.ps1'
            $diagnosticPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            Write-Warning 'Windows blocked the new package. The previous installation was kept. Collecting read-only diagnostics.'
            & $diagnosticPowerShell -NoProfile -ExecutionPolicy RemoteSigned -File $diagnosticScript
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Windows diagnostics are incomplete; see obj\UIAccessTest\policy-diagnostics.json.' }
        }
        catch { Write-Warning "Could not collect Windows diagnostics: $($_.Exception.Message)" }
    }
    exit 1
}
finally {
    if ($brokerTaskToResume) {
        try {
            $brokerTask = Get-ScheduledTask -TaskName $brokerTaskToResume -ErrorAction SilentlyContinue
            if ($brokerTask -and $brokerTask.State -ne 'Running') { Start-ScheduledTask -TaskName $brokerTaskToResume -ErrorAction Stop }
        }
        catch { Write-Warning ('Could not resume the widgets task: ' + $_.Exception.Message) }
    }
    try {
        if ($stage -and -not $keepStage) {
            foreach ($name in $files) {
                Remove-Item -LiteralPath (Join-Path $stage $name),(Join-Path $stage "$name.bak") -Force -ErrorAction SilentlyContinue
            }
            [IO.Directory]::Delete($stage)
        }
    }
    catch { Write-Warning $_.Exception.Message }
    finally { if ($installLock) { $installLock.Dispose() } }
}
