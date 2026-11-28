#Requires -Version 5.1
param([switch]$Configure, [switch]$Once, [string]$SignToolPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SoftKey.GitHub.ps1')
$repo = 'nitti87/Softkey'
$api = "https://api.github.com/repos/$repo"
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SoftKey\Delivery'
$configuration = Join-Path $data 'configuration.json'
$status = Join-Path $data 'status.json'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Write-DeliveryJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 4))
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-DeliveryRun([hashtable]$Headers) {
    $result = Invoke-RestMethod -Uri "$api/actions/workflows/build-and-test.yml/runs?branch=main&event=push&status=success&per_page=1" `
        -Headers $Headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
    $result.workflow_runs | Select-Object -First 1
}

function Set-DeliveryStatus([string]$State, [string]$Message = '') {
    Write-DeliveryJson $status @{
        State = $State
        Step = $step
        Build = $run.run_number
        Commit = $run.head_sha
        PublishedBuild = $publishedBuild
        PublishedCommit = $publishedCommit
        Message = $Message
        Time = [DateTime]::UtcNow.ToString('o')
    }
}

if (-not $Configure -and -not $Once) { throw 'Choose -Configure or -Once.' }
if ($Configure) {
    $tool = (Get-Item -LiteralPath $SignToolPath).FullName
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arguments = '-NoProfile -ExecutionPolicy RemoteSigned -File "{0}" -Configure -SignToolPath "{1}"' -f $PSCommandPath, $tool
        $worker = Start-Process -FilePath $powershell -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments
        $worker.WaitForExit()
        if ($worker.ExitCode -ne 0) {
            $detail = if (Test-Path -LiteralPath $status) {
                try { (Get-Content -LiteralPath $status -Raw | ConvertFrom-Json).Message } catch { $null }
            } else { $null }
            if ($detail) { throw "SoftKey delivery configuration failed: $detail" }
            throw "SoftKey delivery configuration failed with exit code $($worker.ExitCode)."
        }
        Write-Output 'SoftKey delivery is configured and the first check has started.'
        return
    }
}
$null = New-Item -ItemType Directory -Path $data -Force
$headers = $null
$run = $null
$token = $null
$lock = $null
$publishedBuild = 0
$publishedCommit = $null
$step = 'ReadConfiguration'
if ($Once -and -not $Configure) {
    try { $lock = [IO.File]::Open((Join-Path $data 'delivery.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        # Both Task Scheduler and a manual retry can invoke -Once.
        if (($_.Exception.HResult -band 0xffff) -in 32, 33) { return }
        throw
    }
}
try {
    if (Test-Path -LiteralPath $status) {
        $previous = Get-Content -LiteralPath $status -Raw | ConvertFrom-Json
        if ($previous.PublishedBuild) {
            $publishedBuild = $previous.PublishedBuild
            $publishedCommit = $previous.PublishedCommit
        } elseif ($previous.State -eq 'Published') {
            $publishedBuild = $previous.Build
            $publishedCommit = $previous.Commit
        }
    }
    if ($Configure) {
        $signingCert = Get-Item -LiteralPath 'Cert:\LocalMachine\My\093405919B8DF0EF0F13D124D7DB22FE685979BB' -ErrorAction SilentlyContinue
        if (-not $signingCert -or -not $signingCert.HasPrivateKey) {
            throw 'The local SoftKey signing certificate with private key was not found in LocalMachine\My.'
        }
    } else {
        $saved = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
        $tool = $saved.SignToolPath
        if ($saved.Token) {
            $secret = ConvertTo-SecureString $saved.Token
            try { $token = ([pscredential]::new('SoftKey', $secret)).GetNetworkCredential().Password }
            finally { $secret.Dispose() }
        }
    }
    $step = 'CheckGitHub'
    if (-not $Configure) { Set-DeliveryStatus 'Running' }
    $headers = Get-SoftKeyGitHubHeaders -Token $token -AllowBrowserSignIn:$Configure
    $repository = Invoke-RestMethod -Uri $api -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
    if ($repository.full_name -ine $repo) { throw 'SoftKey delivery requires its GitHub repository.' }
    $run = Get-DeliveryRun $headers
    if ($Configure) {
        Write-DeliveryJson $configuration @{SignToolPath=$tool}
        $action = New-ScheduledTaskAction -Execute $powershell -WorkingDirectory $PSScriptRoot `
            -Argument ('-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "{0}" -Once' -f $PSCommandPath)
        $account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $periodic = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
        $logon = New-ScheduledTaskTrigger -AtLogOn -User $account
        $owner = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 20) `
            -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 1)
        Register-ScheduledTask -TaskName 'SoftKey-DeliverUpdates' -Action $action -Trigger @($periodic, $logon) -Principal $owner -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName 'SoftKey-DeliverUpdates'
        Write-Output 'SoftKey delivery is configured and the first check has started.'
        return
    }

    if (-not $run) {
        Set-DeliveryStatus 'Waiting' 'No successful build for main was found.'
        return
    }
    if ($run.run_number -lt 1) { throw 'GitHub returned an invalid build number.' }
    if ($publishedBuild -gt $run.run_number -or ($publishedBuild -eq $run.run_number -and $publishedCommit -ceq $run.head_sha)) {
        Set-DeliveryStatus 'Published'
        return
    }
    $git = Get-SoftKeyGitPath
    $package = Join-Path $PSScriptRoot "obj\Updates\SoftKey-update-$($run.run_number).zip"
    if (-not (Test-Path -LiteralPath $package)) {
        $step = 'FetchSource'
        Set-DeliveryStatus 'Running'
        $origin = & $git -C $PSScriptRoot remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $origin -ine "https://github.com/$repo.git") { throw 'Unexpected Git remote for the signing checkout.' }
        $previousPrompt = $env:GIT_TERMINAL_PROMPT
        $previousInteractive = $env:GCM_INTERACTIVE
        $env:GIT_TERMINAL_PROMPT = '0'
        $env:GCM_INTERACTIVE = 'false'
        try {
            & $git -C $PSScriptRoot -c credential.username=nitti87 -c credential.interactive=false fetch --quiet origin main
            if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the tested SoftKey commit.' }
        } finally {
            $env:GIT_TERMINAL_PROMPT = $previousPrompt
            $env:GCM_INTERACTIVE = $previousInteractive
        }
        $step = 'BuildAndSign'
        Set-DeliveryStatus 'Running'
        $previousActionsToken = $env:SOFTKEY_GITHUB_ACTIONS_TOKEN
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $headers.Authorization.Substring('Bearer '.Length)
        try { & (Join-Path $PSScriptRoot 'New-SoftKeyUpdatePackage.ps1') -RunId $run.id -SignToolPath $tool -Unattended | Out-Null }
        finally { $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $previousActionsToken }
    }
    $step = 'VerifyPackage'
    Set-DeliveryStatus 'Running'
    . (Join-Path $PSScriptRoot 'SoftKey.UpdateDistribution.ps1')
    $manifest = Read-SoftKeyUpdateArchive -ArchivePath $package -TrustedCertificate (Get-SoftKeyReleaseCertificate)
    if ($manifest.Commit -cne $run.head_sha -or $manifest.BuildNumber -ne $run.run_number) {
        throw 'The signed package does not match the selected successful push.'
    }
    $releaseNotes = Get-SoftKeyDeliveryReleaseNotes -GitPath $git -RepositoryPath $PSScriptRoot -HeadCommit $run.head_sha -PreviousCommit $publishedCommit
    $step = 'PublishAndVerify'
    Set-DeliveryStatus 'Running'
    $previousContentsToken = $env:SOFTKEY_GITHUB_CONTENTS_TOKEN
    $env:SOFTKEY_GITHUB_CONTENTS_TOKEN = $headers.Authorization.Substring('Bearer '.Length)
    try {
        $publishArgs = @{ PackagePath = $package; PublishPreview = $true }
        if ($releaseNotes) { $publishArgs.ReleaseNotes = $releaseNotes }
        $delivery = & (Join-Path $PSScriptRoot 'Publish-SoftKeyUpdatePackage.ps1') @publishArgs
    } finally { $env:SOFTKEY_GITHUB_CONTENTS_TOKEN = $previousContentsToken }
    if ($delivery.Status -ne 'Preview' -or $delivery.Commit -cne $run.head_sha -or $delivery.BuildNumber -ne $run.run_number) {
        throw 'GitHub did not confirm the selected update.'
    }
    $publishedBuild = $run.run_number
    $publishedCommit = $run.head_sha
    Set-DeliveryStatus 'Published'
} catch {
    Set-DeliveryStatus 'Failed' $_.Exception.Message
    throw
} finally {
    if ($headers) { $headers.Clear() }
    if ($lock) { $lock.Dispose() }
}
