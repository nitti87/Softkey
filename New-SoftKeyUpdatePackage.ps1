#Requires -Version 5.1
param(
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$RunId,
    [Parameter(Mandatory)][string]$SignToolPath,
    [switch]$Unattended
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SoftKey.GitHub.ps1')
$repository = 'nitti87/Softkey'
$environmentToken = $env:SOFTKEY_GITHUB_ACTIONS_TOKEN
$git = Get-SoftKeyGitPath

$checkoutCommit = & $git -C $PSScriptRoot rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $checkoutCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'A Git checkout of SoftKey is required.' }
$signTool = (Get-Item -LiteralPath $SignToolPath).FullName
$headers = Get-SoftKeyGitHubHeaders -Token $environmentToken -AllowBrowserSignIn:(-not $Unattended)
try {
    $run = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$RunId" `
        -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
} finally {
    $headers.Clear()
}
$commit = $run.head_sha
if ($run.id -ne $RunId -or $run.status -cne 'completed' -or $run.conclusion -cne 'success' -or
    $run.repository.full_name -ine $repository -or $run.head_repository.full_name -ine $repository -or
    $run.path -cne '.github/workflows/build-and-test.yml' -or $run.event -cne 'push' -or
    $run.head_branch -cne 'main' -or $commit -cnotmatch '^[0-9a-f]{40}$' -or
    ($run.run_number -isnot [int] -and $run.run_number -isnot [long]) -or $run.run_number -lt 1) {
    throw 'The selected run must be a successful build-and-test run for a commit pushed to main.'
}

$null = & $git -C $PSScriptRoot rev-parse --quiet --verify "${commit}^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'The tested commit is not in the local checkout history.' }
& $git -C $PSScriptRoot merge-base --is-ancestor $commit $checkoutCommit
if ($LASTEXITCODE -ne 0) {
    $remote = & $git -C $PSScriptRoot remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $remote -ine 'https://github.com/nitti87/Softkey.git') { throw 'The tested commit is not in the local checkout history.' }
    & $git -C $PSScriptRoot merge-base --is-ancestor $commit refs/remotes/origin/main
    if ($LASTEXITCODE -ne 0) { throw 'The tested commit is not in the local checkout history.' }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'obj\Updates') -Force).FullName
$name = 'SoftKey-update-{0}.zip' -f $run.run_number
$target = Join-Path $output $name
if (Test-Path -LiteralPath $target) { throw "The update package already exists: $target" }

# Build the tested commit from a fresh checkout of its contents.
$staging = (New-Item -ItemType Directory -Path (Join-Path $output ([Guid]::NewGuid().ToString('N')))).FullName
try {
    $sourceArchive = Join-Path $staging 'source.zip'
    $source = Join-Path $staging 'source'
    & $git -C $PSScriptRoot archive --format=zip "--output=$sourceArchive" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare a snapshot of the tested commit.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($sourceArchive, $source)

    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Push-Location -LiteralPath $source
    try {
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $null
        $options = @('-Build','-SignOnly','-IncludeStartup','-Configuration','Release','-SignToolPath',$signTool,
            '-UpdateBuildNumber',$run.run_number,'-UpdateCommit',$commit)
        if ($Unattended) { $options += '-UnattendedSignOnly' }
        & $windowsPowerShell -NoProfile -ExecutionPolicy RemoteSigned -File .\Install-Local-UIAccess.ps1 @options
        if ($LASTEXITCODE -ne 0) { throw "SoftKey signing failed with exit code $LASTEXITCODE." }
    } finally {
        $env:SOFTKEY_GITHUB_ACTIONS_TOKEN = $environmentToken
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
