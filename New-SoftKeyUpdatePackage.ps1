#Requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$RunId,
    [Parameter(Mandatory)][string]$SignToolPath
)

$ErrorActionPreference = 'Stop'
$repository = 'nitti87/Softkey'
$actionsToken = $env:SOFTKEY_GITHUB_ACTIONS_TOKEN
if (-not $actionsToken) { throw 'Set SOFTKEY_GITHUB_ACTIONS_TOKEN to a token with read access to the repository Actions.' }

$commit = & git -C $PSScriptRoot rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'A Git checkout of SoftKey is required.' }
$headers = @{
    Authorization = 'Bearer ' + $actionsToken
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}
$run = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$RunId" `
    -Headers $headers -MaximumRedirection 0 -TimeoutSec 25
if ($run.id -ne $RunId -or $run.status -cne 'completed' -or $run.conclusion -cne 'success' -or
    $run.repository.full_name -ine $repository -or $run.head_repository.full_name -ine $repository -or
    $run.path -cne '.github/workflows/build-and-test.yml' -or $run.event -cne 'push' -or
    $run.head_branch -cne 'main' -or $run.head_sha -cne $commit -or $run.run_number -lt 1) {
    throw 'The selected run must be a successful build-and-test run for the checked-out commit pushed to main.'
}

$signTool = (Get-Item -LiteralPath $SignToolPath).FullName
$output = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'obj\Updates') -Force).FullName
$name = 'SoftKey-update-{0}.zip' -f $run.run_number
$target = Join-Path $output $name
if (Test-Path -LiteralPath $target) { throw "The update package already exists: $target" }

# Build the tested commit from a fresh checkout of its contents.
$staging = (New-Item -ItemType Directory -Path (Join-Path $output ([Guid]::NewGuid().ToString('N')))).FullName
try {
    $sourceArchive = Join-Path $staging 'source.zip'
    $source = Join-Path $staging 'source'
    & git -C $PSScriptRoot archive --format=zip "--output=$sourceArchive" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare a snapshot of the tested commit.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($sourceArchive, $source)

    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Push-Location -LiteralPath $source
    try {
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $null
        & $windowsPowerShell -NoProfile -ExecutionPolicy RemoteSigned -File .\Install-Local-UIAccess.ps1 `
            -Build -SignOnly -IncludeStartup -Configuration Release -SignToolPath $signTool `
            -UpdateBuildNumber $run.run_number -UpdateCommit $commit
        if ($LASTEXITCODE -ne 0) { throw "SoftKey signing failed with exit code $LASTEXITCODE." }
    } finally {
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $actionsToken
        Pop-Location
    }

    [IO.File]::Move((Join-Path $source "obj\UIAccessTest\$name"), $target)
    Get-Item -LiteralPath $target
} finally {
    $directory = Get-Item -LiteralPath $staging
    if ($directory.Parent.FullName -eq $output -and -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}
