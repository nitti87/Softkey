param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SignOnly,
    [switch]$IncludeStartup = $true,
    [switch]$EnableAutomaticStartup,
    [switch]$DisableSmartAppControl,
    [string]$SignToolPath = (Join-Path $PSScriptRoot 'obj\Tools\WindowsSDKBuildTools\10.0.28000.2957\x64\signtool.exe')
)

$ErrorActionPreference = 'Stop'
if ($SignOnly -and $DisableSmartAppControl) { throw 'SignOnly cannot change Smart App Control.' }
$certificateThumbprint = '093405919B8DF0EF0F13D124D7DB22FE685979BB'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$resultPath = if ($SignOnly) { Join-Path $PSScriptRoot 'obj\UIAccessTest\signing-result.json' }
              else { Join-Path $PSScriptRoot 'UIAccess-install-result.json' }

try {
    $policyPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
    $expectedPolicyState = $null
    if (-not $SignOnly) {
        $initialPolicyState = (Get-ItemProperty -LiteralPath $policyPath).VerifiedAndReputablePolicyState
        if ($null -eq $initialPolicyState -or $initialPolicyState -notin 0, 1, 2) { throw 'The Smart App Control state is unknown.' }
        $expectedPolicyState = if ($DisableSmartAppControl) { 0 } else { $initialPolicyState }
    }
    $started = [DateTime]::UtcNow
    $signArguments = @('-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-File',
        (Join-Path $PSScriptRoot 'Install-Local-UIAccess.ps1'), '-Build',
        '-Configuration', $Configuration, '-CertificateThumbprint', $certificateThumbprint)
    if ($SignOnly) { $signArguments += '-SignOnly' }
    if ($IncludeStartup) { $signArguments += '-IncludeStartup' }
    if ($EnableAutomaticStartup) { $signArguments += '-EnableAutomaticStartup' }
    if ($DisableSmartAppControl) { $signArguments += '-DisableSmartAppControl' }
    if ($SignToolPath) { $signArguments += @('-SignToolPath', (Get-Item -LiteralPath $SignToolPath).FullName) }
    & $windowsPowerShell @signArguments
    $installerExit = $LASTEXITCODE

    if (-not (Test-Path -LiteralPath $resultPath) -or
        (Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $started) {
        throw 'The signing script did not produce a fresh result.'
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $result.Files | Format-Table Name, SignatureStatus, WindowsCodeIntegrity -AutoSize
    if ($installerExit -ne 0) { throw "$($result.Status): $($result.Message)" }
    $expectedStatus = if ($SignOnly) { 'Signed' } else { 'Installed' }
    if ($result.Status -ne $expectedStatus) { throw "Unexpected result: $($result.Status)." }

    $package = Split-Path -Parent $result.InstallPath
    $installedExe = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SoftKey-UIAccess-Test\SoftKey.exe'
    if (-not $SignOnly -and $result.InstallPath -ne $installedExe) { throw 'Unexpected installation path.' }
    foreach ($name in 'SoftKey.exe', 'SoftKey.dll') {
        $path = Join-Path $package $name
        $record = @($result.Files | Where-Object { $_.Name -eq $name })
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($record.Count -ne 1 -or $signature.Status -ne 'Valid' -or
            $signature.SignerCertificate.Thumbprint -ne $certificateThumbprint -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record[0].SHA256) {
            throw "The signed file could not be verified: $name."
        }
        if (-not $SignOnly -and $record[0].WindowsCodeIntegrity -ne 'Accepted') {
            throw "Windows did not approve $name."
        }
    }

    if ($SignOnly) {
        Write-Output "Both files have valid signatures. Signed package: $package"
        exit 0
    }

    $protection = Get-MpComputerStatus
    $policyState = (Get-ItemProperty -LiteralPath $policyPath).VerifiedAndReputablePolicyState
    if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled -or $policyState -ne $expectedPolicyState) {
        throw 'The files were installed, but startup cannot be verified with the required Windows protection active.'
    }

    $process = Start-Process -FilePath $installedExe -WorkingDirectory $package -WindowStyle Normal -PassThru
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 10) {
        $process.Refresh()
        if ($process.HasExited) { throw "SoftKey exited during startup with code $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -eq 'SoftKey') {
            $protection = Get-MpComputerStatus
            $policyState = (Get-ItemProperty -LiteralPath $policyPath).VerifiedAndReputablePolicyState
            if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled -or $policyState -ne $expectedPolicyState) {
                throw 'Windows protection changed during startup verification.'
            }
            if ($expectedPolicyState -eq 0) {
                Write-Output 'Both signatures and installed files are verified. SoftKey started with Smart App Control off and antivirus and real-time protection active.'
            }
            elseif ($expectedPolicyState -eq 2) {
                Write-Output 'Both signatures and installed files are verified. SoftKey started with Smart App Control in evaluation mode and antivirus and real-time protection active.'
            }
            else {
                Write-Output 'Both signatures and installed files are verified. SoftKey started with Windows protection active.'
            }
            exit 0
        }
        $null = $process.WaitForExit(200)
    }
    throw 'The files were installed, but the SoftKey window did not appear within 10 seconds.'
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    Write-Output "Installation or signing report: $resultPath"
    exit 1
}
