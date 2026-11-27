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

if (-not $Configure -and -not $Once) { throw 'Choose -Configure or -Once.' }
$null = New-Item -ItemType Directory -Path $data -Force
$headers = $null
$run = $null
$token = $null
try {
    if ($Configure) {
        $tool = (Get-Item -LiteralPath $SignToolPath).FullName
    } else {
        $saved = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
        $tool = $saved.SignToolPath
        if ($saved.Token) {
            $secret = ConvertTo-SecureString $saved.Token
            try { $token = ([pscredential]::new('SoftKey', $secret)).GetNetworkCredential().Password }
            finally { $secret.Dispose() }
        }
    }
    $headers = Get-SoftKeyGitHubHeaders -Token $token -AllowBrowserSignIn:$Configure
    $repository = Invoke-RestMethod -Uri $api -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
    if ($repository.full_name -ine $repo) { throw 'SoftKey delivery requires its GitHub repository.' }
    $run = Get-DeliveryRun $headers
    if ($Configure) {
        Write-DeliveryJson $configuration @{SignToolPath=$tool}
        $action = New-ScheduledTaskAction -Execute $powershell -WorkingDirectory $PSScriptRoot `
            -Argument ('-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "{0}" -Once' -f $PSCommandPath)
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
            -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
        $owner = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) -LogonType Interactive -RunLevel Limited
        $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 20) `
            -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName 'SoftKey-DeliverUpdates' -Action $action -Trigger $trigger -Principal $owner -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName 'SoftKey-DeliverUpdates'
        Write-Output 'SoftKey delivery is configured and the first check has started.'
        return
    }

    if (-not $run) { return }
    $previous = if (Test-Path -LiteralPath $status) { Get-Content -LiteralPath $status -Raw | ConvertFrom-Json } else { $null }
    if ($previous.State -eq 'Published' -and $previous.Build -ge $run.run_number) { return }
    if ($run.run_number -lt 1) { throw 'GitHub returned an invalid build number.' }
    $package = Join-Path $PSScriptRoot "obj\Updates\SoftKey-update-$($run.run_number).zip"
    if (-not (Test-Path -LiteralPath $package)) {
        $git = Get-SoftKeyGitPath
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
        $previousActionsToken = $env:SOFTKEY_GITHUB_ACTIONS_TOKEN
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $headers.Authorization.Substring('Bearer '.Length)
        try { & (Join-Path $PSScriptRoot 'New-SoftKeyUpdatePackage.ps1') -RunId $run.id -SignToolPath $tool -Unattended | Out-Null }
        finally { $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $previousActionsToken }
    }
    . (Join-Path $PSScriptRoot 'SoftKey.UpdateDistribution.ps1')
    $manifest = Read-SoftKeyUpdateArchive -ArchivePath $package -TrustedCertificate (Get-SoftKeyReleaseCertificate)
    if ($manifest.Commit -cne $run.head_sha -or $manifest.BuildNumber -ne $run.run_number) {
        throw 'The signed package does not match the selected successful push.'
    }
    $previousContentsToken = $env:SOFTKEY_GITHUB_CONTENTS_TOKEN
    $env:SOFTKEY_GITHUB_CONTENTS_TOKEN = $headers.Authorization.Substring('Bearer '.Length)
    try {
        $delivery = & (Join-Path $PSScriptRoot 'Publish-SoftKeyUpdatePackage.ps1') -PackagePath $package -PublishPreview
    } finally { $env:SOFTKEY_GITHUB_CONTENTS_TOKEN = $previousContentsToken }
    if ($delivery.Status -ne 'Preview' -or $delivery.Commit -cne $run.head_sha -or $delivery.BuildNumber -ne $run.run_number) {
        throw 'GitHub did not confirm the selected update.'
    }
    Write-DeliveryJson $status @{State='Published'; Build=$run.run_number; Commit=$run.head_sha; Time=[DateTime]::UtcNow.ToString('o')}
} catch {
    Write-DeliveryJson $status @{State='Failed'; Build=$run.run_number; Message=$_.Exception.Message; Time=[DateTime]::UtcNow.ToString('o')}
    throw
} finally { if ($headers) { $headers.Clear() } }
