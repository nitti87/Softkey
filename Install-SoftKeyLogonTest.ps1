param([switch]$Disable, [switch]$UsePreparedPackage, [switch]$UseInstalledPackage, [switch]$PreserveSettings, [ValidateSet('TakeOver')][string]$DwellMode = 'TakeOver')

$ErrorActionPreference = 'Stop'
if ($UsePreparedPackage -and $UseInstalledPackage) { throw 'Choose one verified package source.' }
$reportPath = Join-Path $PSScriptRoot 'obj\UIAccessTest\logon-install-result.json'
$targetDir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-Logon-Test'
$thumbprint = '093405919B8DF0EF0F13D124D7DB22FE685979BB'
$sofTypeBackupPath = Join-Path $targetDir 'softype-secure-desktop-backup.json'
$signTool = Join-Path $PSScriptRoot 'obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
function Save-LogonResult([string]$status, [string]$message) {
    [pscustomobject]@{ Status = $status; Message = $message; InstallPath = $targetDir; Files = @($fileChecks)
        SmartAppControl = $expectedSac; DwellMode = $DwellMode; AntivirusEnabled = $protection.AntivirusEnabled
        RealTimeProtectionEnabled = $protection.RealTimeProtectionEnabled; Time = (Get-Date).ToString('o')
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        $arguments = '-NoProfile -ExecutionPolicy RemoteSigned -File "{0}"' -f $PSCommandPath
        if ($Disable) { $arguments += ' -Disable' }
        if ($UsePreparedPackage) { $arguments += ' -UsePreparedPackage' }
        if ($UseInstalledPackage) { $arguments += ' -UseInstalledPackage' }
        if ($PreserveSettings) { $arguments += ' -PreserveSettings' }
        $arguments += ' -DwellMode ' + $DwellMode
        $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
            -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        exit $process.ExitCode
    }
    catch { Save-LogonResult 'ElevationCancelled' $_.Exception.Message; exit 1223 }
}

. (Join-Path $PSScriptRoot 'SoftKey.Logon.Installation.ps1')
$machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$user = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$users = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::Users, [Microsoft.Win32.RegistryView]::Registry64)
$files = @('SoftKey.Logon.exe', 'SoftKey.Logon.dll', 'SoftKey.Logon.deps.json', 'SoftKey.Logon.runtimeconfig.json',
    'SoftKey.Locked.exe', 'SoftKey.Locked.dll', 'SoftKey.Locked.deps.json', 'SoftKey.Locked.runtimeconfig.json', 'logon-settings.json')
$fileChecks = @(); $replaced = @{}; $stage = $null; $installLock = $null; $registered = $false; $registrationStarted = $false
try {
    if ($Disable) {
        if (Test-Path -LiteralPath $sofTypeBackupPath) {
            if ((Get-Item -LiteralPath $sofTypeBackupPath).Length -gt 4096) { throw 'The SofType recovery record is too large.' }
            $sofTypeSnapshot = Get-Content -LiteralPath $sofTypeBackupPath -Raw | ConvertFrom-Json
            Set-SoftKeySofTypeSecureAccommodation $machine $sofTypeSnapshot $false
        }
        Set-SoftKeyLogonRegistration -MachineHive $machine -UserHive $user -Executable (Join-Path $targetDir 'SoftKey.Logon.exe') -Enabled $false -UsersHive $users
        # EoA can retain its dormant normal-desktop instance after configuration is removed.
        # Stop only our own user's helper in this session. Never stop a SYSTEM sign-in keyboard.
        foreach ($idle in @(Get-CimInstance -ClassName Win32_Process -Filter "Name='SoftKey.Logon.exe' OR Name='SoftKey.Locked.exe'")) {
            if ($idle.ExecutablePath -ine (Join-Path $targetDir $idle.Name) -or
                $idle.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId) { continue }
            $owner = Invoke-CimMethod -InputObject $idle -MethodName GetOwnerSid
            if ($owner.ReturnValue -eq 0 -and $owner.Sid -eq $identity.User.Value -and
                $idle.CommandLine -match '\s--(ease-of-access|preview|locked-desktop)\s*$') { Stop-Process -Id $idle.ProcessId -ErrorAction Stop }
        }
        # SYSTEM instances observe removal of their registration and close themselves.
        # Give their own shutdown monitor time to release the program files; never force-kill them.
        $shutdownWait = [Diagnostics.Stopwatch]::StartNew()
        do {
            $remaining = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='SoftKey.Logon.exe' OR Name='SoftKey.Locked.exe'" |
                Where-Object { $_.ExecutablePath -ieq (Join-Path $targetDir $_.Name) })
            if ($remaining.Count -eq 0) { break }
            Start-Sleep -Milliseconds 100
        } while ($shutdownWait.Elapsed.TotalSeconds -lt 3)
        Save-LogonResult 'Disabled' 'SoftKey sign-in startup is disabled; other accessibility applications are preserved.'
        exit 0
    }
    $expectedSac = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
    if ($null -eq $expectedSac -or $expectedSac -notin 0,1,2) { throw 'The current Smart App Control state is unknown.' }
    $protection = Get-MpComputerStatus
    if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled) { throw 'Antivirus and real-time protection must be active.' }
    $signingReport = Join-Path $PSScriptRoot 'obj\UIAccessTest\signing-result.json'
    if (-not $UsePreparedPackage -and -not $UseInstalledPackage) {
        $started = [DateTime]::UtcNow
        & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy RemoteSigned -File `
            (Join-Path $PSScriptRoot 'Install-Local-UIAccess.ps1') -Build -IncludeLogon -SignOnly -Configuration Debug -SignToolPath $signTool
        if ($LASTEXITCODE -ne 0) { throw 'Signing failed. No sign-in startup was enabled.' }
        if ((Get-Item -LiteralPath $signingReport).LastWriteTimeUtc -lt $started) { throw 'The signing report is stale.' }
    }
    if ($UseInstalledPackage) { $signingReport = Join-Path $PSScriptRoot 'UIAccess-install-result.json' }
    $signed = Get-Content -LiteralPath $signingReport -Raw | ConvertFrom-Json
    $expectedStatus = if ($UseInstalledPackage) { 'Installed' } else { 'Signed' }
    if ($signed.Status -ne $expectedStatus) { throw 'No verified signed package is available.' }
    $source = Split-Path -Parent $signed.InstallPath
    if ($UseInstalledPackage -and $source -ine (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-UIAccess-Test')) {
        throw 'The installed package is not in the expected protected directory.'
    }
    foreach ($name in @('SoftKey.exe','SoftKey.dll','SoftKey.Logon.exe','SoftKey.Logon.dll','SoftKey.Locked.exe','SoftKey.Locked.dll')) {
        $path = Join-Path $source $name
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        $record = @($signed.Files | Where-Object Name -eq $name)
        if ($record.Count -ne 1 -or $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record[0].SHA256) { throw "Invalid signature or changed package: $name" }
    }
    # Installation has a fixed protected location. Reject junctions before writing program files.
    foreach ($path in @([Environment]::GetFolderPath('ProgramFiles'), $targetDir)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'The installation path must not be a link.' }
    }
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    $acl = Get-Acl -LiteralPath $targetDir
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        if ($rule.AccessControlType -eq 'Allow' -and $sid -in @('S-1-1-0','S-1-5-11','S-1-5-32-545',$identity.User.Value) -and
            ([int]$rule.FileSystemRights -band 0x116) -ne 0) { throw 'The install directory permits unprivileged writes.' }
    }
    if (Get-Process -Name 'SoftKey.Logon','SoftKey.Locked' -ErrorAction SilentlyContinue) { throw 'The sign-in test is already running. Disable the test before updating.' }
    $installLock = [IO.FileStream]::new((Join-Path $targetDir '.install.lock'), [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
    $stage = Join-Path $targetDir ('.stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in $files | Where-Object { $_ -ne 'logon-settings.json' }) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $stage $name) }
    $settings = @{ DwellMs = 800; JitterPx = 10; WindowWidth = 860; WindowHeight = 330 }
    $personalSettings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'SoftKey\settings.json'
    if ((Test-Path -LiteralPath $personalSettings) -and (Get-Item -LiteralPath $personalSettings).Length -le 65536) {
        try {
            $personal = Get-Content -LiteralPath $personalSettings -Raw | ConvertFrom-Json
            foreach ($property in @($settings.Keys)) {
                $number = 0.0
                if ([double]::TryParse([string]$personal.$property, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and
                    -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) { $settings[$property] = $number }
            }
        }
        catch { Write-Warning 'Could not copy keyboard preferences; safe defaults will be used.' }
    }
    $settings['StartPaused'] = $false
    [IO.File]::WriteAllText((Join-Path $stage 'logon-settings.json'), ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if ($PreserveSettings) {
        $existingSettings = Join-Path $targetDir 'logon-settings.json'
        if (-not (Test-Path -LiteralPath $existingSettings) -or (Get-Item -LiteralPath $existingSettings).Length -gt 4096 -or
            ((Get-Item -LiteralPath $existingSettings -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'The existing sign-in preferences cannot be preserved safely.'
        }
        $preservedSettings = Get-Content -LiteralPath $existingSettings -Raw | ConvertFrom-Json
        if ($null -ne $preservedSettings.StartPaused -and $preservedSettings.StartPaused -isnot [bool]) {
            throw 'The existing sign-in autoclick preference has an unexpected format.'
        }
        if ($preservedSettings.StartPaused -eq $false) {
            Copy-Item -LiteralPath $existingSettings -Destination (Join-Path $stage 'logon-settings.json') -Force
        }
        else {
            # Migrate the former paused pilot mode while keeping all numeric preferences.
            $preservedSettings | Add-Member -MemberType NoteProperty -Name StartPaused -Value $false -Force
            [IO.File]::WriteAllText((Join-Path $stage 'logon-settings.json'), ($preservedSettings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        }
    }
    # A minimal image mapping checks Code Integrity without launching the sign-in keyboard.
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class SoftKeyLogonImageCheck {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFileMappingW(SafeFileHandle file, IntPtr attributes, uint protection, uint hi, uint lo, string name);
    public static int Check(SafeFileHandle file) {
        using (var map = CreateFileMappingW(file, IntPtr.Zero, 0x01000002, 0, 0, null))
            return map.IsInvalid ? Marshal.GetLastWin32Error() : 0;
    }
}
'@
    foreach ($name in $files) {
        $destination = Join-Path $targetDir $name
        if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'An installed file is a link.' }
        $replaced[$name] = Test-Path -LiteralPath $destination
        if ($replaced[$name]) { [IO.File]::Replace((Join-Path $stage $name), $destination, (Join-Path $stage "$name.bak")) }
        else { [IO.File]::Move((Join-Path $stage $name), $destination) }
    }
    foreach ($name in @('SoftKey.Logon.exe','SoftKey.Logon.dll','SoftKey.Locked.exe','SoftKey.Locked.dll')) {
        $path = Join-Path $targetDir $name
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint) { throw "Installed signature failed: $name" }
        $stream = [IO.File]::OpenRead($path)
        try { $errorCode = [SoftKeyLogonImageCheck]::Check($stream.SafeFileHandle) }
        finally { $stream.Dispose() }
        if ($errorCode -ne 0) { throw "Windows rejected $name (error $errorCode)." }
        $fileChecks += @{ Name = $name; SignatureStatus = 'Valid'; WindowsCodeIntegrity = 'Accepted'; SHA256 = (Get-FileHash -LiteralPath $path).Hash }
    }
    $protection = Get-MpComputerStatus
    $currentSac = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
    if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled -or $currentSac -ne $expectedSac) { throw 'Windows protection changed during installation.' }
    # Non-interactive startup must reject an ordinary desktop. No credentials or keyboard input are sent.
    $probe = Start-Process -FilePath (Join-Path $targetDir 'SoftKey.Logon.exe') -WindowStyle Hidden -PassThru
    if (-not $probe.WaitForExit(10000) -or $probe.ExitCode -ne 2) { throw 'The sign-in startup boundary check failed.' }
    $lockedProbe = Start-Process -FilePath (Join-Path $targetDir 'SoftKey.Locked.exe') -ArgumentList '--verify-uiaccess' -WindowStyle Hidden -PassThru
    if (-not $lockedProbe.WaitForExit(10000) -or $lockedProbe.ExitCode -ne 0) { throw 'Windows did not grant UIAccess to the locked-screen helper.' }
    $lockedBoundary = Start-Process -FilePath (Join-Path $targetDir 'SoftKey.Locked.exe') -ArgumentList '--locked-desktop' -WindowStyle Hidden -PassThru
    if (-not $lockedBoundary.WaitForExit(10000) -or $lockedBoundary.ExitCode -ne 2) { throw 'The locked-screen helper did not reject an unlocked session.' }
    # Check actual UIAccess tokens, policy and the Windows SAS export without sending Ctrl+Alt+Delete.
    foreach ($sasComponent in 'SoftKey.Logon.exe','SoftKey.Locked.exe') {
        $sasProbe = Start-Process -FilePath (Join-Path $targetDir $sasComponent) -ArgumentList '--verify-secure-attention' -WindowStyle Hidden -PassThru
        if (-not $sasProbe.WaitForExit(10000) -or $sasProbe.ExitCode -ne 0) { throw "Windows Ctrl+Alt+Delete prerequisites failed for $sasComponent." }
    }
    $registrationStarted = $true
    if (Test-Path -LiteralPath $sofTypeBackupPath) {
        if ((Get-Item -LiteralPath $sofTypeBackupPath).Length -gt 4096) { throw 'The SofType recovery record is too large.' }
        $sofTypeSnapshot = Get-Content -LiteralPath $sofTypeBackupPath -Raw | ConvertFrom-Json
    }
    else {
        $sofTypeSnapshot = Get-SoftKeySofTypeSecureSnapshot $machine
        if ($null -ne $sofTypeSnapshot) {
            [IO.File]::WriteAllText($sofTypeBackupPath, ($sofTypeSnapshot | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        }
    }
    Set-SoftKeyLogonRegistration -MachineHive $machine -UserHive $user -Executable (Join-Path $targetDir 'SoftKey.Logon.exe') -Enabled $true -UsersHive $users
    Set-SoftKeySofTypeSecureAccommodation $machine $sofTypeSnapshot ($DwellMode -eq 'TakeOver')
    $registered = $true
    $brokerTaskName = 'SoftKey-LoginBroker-' + $identity.User.Value
    $brokerTask = Get-ScheduledTask -TaskName $brokerTaskName -ErrorAction SilentlyContinue
    if ($brokerTask) {
        $expectedBroker = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-UIAccess-Test\SoftKey.Startup.exe'
        if ($brokerTask.Actions.Count -ne 1 -or $brokerTask.Actions[0].Execute -ine $expectedBroker -or $brokerTask.Actions[0].Arguments -ne '--login-broker') {
            throw 'The widgets startup task has an unexpected action.'
        }
        Start-ScheduledTask -TaskName $brokerTaskName -ErrorAction Stop
    }
    Save-LogonResult 'EnabledAwaitingUserTest' 'Signed sign-in keyboard installed and registered. HeadMouse, dwell input and sign-in still require a user test on the locked screen.'
    Write-Output 'Inloggningstestet är installerat. Lås själv datorn och prova med HeadMouse. Ange aldrig din PIN-kod eller ditt lösenord i chatten.'
}
catch {
    $message = $_.Exception.Message
    if ($registrationStarted -and -not $registered) {
        try {
            Set-SoftKeySofTypeSecureAccommodation $machine $sofTypeSnapshot $false
            Set-SoftKeyLogonRegistration -MachineHive $machine -UserHive $user -Executable (Join-Path $targetDir 'SoftKey.Logon.exe') -Enabled $false -UsersHive $users
        }
        catch { $message += ' Sign-in startup recovery requires attention: ' + $_.Exception.Message }
    }
    if (-not $registered -and -not $registrationStarted) {
        foreach ($name in $replaced.Keys) {
            $destination = Join-Path $targetDir $name; $backup = Join-Path $stage "$name.bak"
            try {
                if (Test-Path -LiteralPath $backup) { [IO.File]::Replace($backup, $destination, [NullString]::Value) }
                elseif (-not $replaced[$name]) { Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue }
            }
            catch { $message += " Recovery is required for $name in $stage." }
        }
    }
    Save-LogonResult 'Failed' $message
    Write-Error -Message $message -ErrorAction Continue
    exit 1
}
finally {
    $machine.Dispose(); $user.Dispose(); $users.Dispose()
    if ($installLock) { $installLock.Dispose() }
    # Keep the small protected staging directory as recovery evidence; never recursively remove a computed path.
}
