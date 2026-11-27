function Get-SoftKeyGitPath {
    $installed = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Git\cmd\git.exe'
    if (Test-Path -LiteralPath $installed -PathType Leaf) { return $installed }
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) { return $git.Source }
    throw 'Git for Windows with Git Credential Manager is required on the SoftKey development computer.'
}

function Get-SoftKeyStoredGitHubToken {
    param([string]$GitPath)

    $credentialRequest = "protocol=https`nhost=github.com`nusername=nitti87`n`n"
    $PSNativeCommandUseErrorActionPreference = $false
    $ErrorActionPreference = 'Continue'
    $previousInteractive = $env:GCM_INTERACTIVE
    $env:GCM_INTERACTIVE = 'false'
    try { $credential = $credentialRequest | & $GitPath credential-manager get 2>$null }
    finally { $env:GCM_INTERACTIVE = $previousInteractive }
    if ($LASTEXITCODE -eq 0) {
        foreach ($line in $credential) {
            if ($line -cmatch '^password=(.+)$') { return $Matches[1] }
        }
    }
}

function Get-SoftKeyGitHubHeaders {
    param([string]$Token, [switch]$AllowBrowserSignIn)

    if (-not $Token) {
        $git = Get-SoftKeyGitPath
        $Token = Get-SoftKeyStoredGitHubToken $git
        if (-not $Token -and $AllowBrowserSignIn) {
            & $git credential-manager github login --username nitti87 --browser | Out-Host
            if ($LASTEXITCODE -eq 0) { $Token = Get-SoftKeyStoredGitHubToken $git }
        }
        if (-not $Token) {
            throw 'GitHub sign-in is required. Run git credential-manager github login --username nitti87 --browser as this Windows user.'
        }
    }
    @{
        Authorization = 'Bearer ' + $Token
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
}

function Invoke-SoftKeyGitUtf8 {
    param([string]$GitPath, [string]$RepositoryPath, [string]$Arguments)

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $GitPath
    $psi.Arguments = '-c i18n.logOutputEncoding=utf-8 -C "{0}" {1}' -f $RepositoryPath.Replace('"', '\"'), $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEnd()
        $null = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout }
    } finally {
        $process.Dispose()
    }
}

function Format-SoftKeyDeliveryReleaseNotes {
    param([string[]]$Subjects)

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($subject in @($Subjects)) {
        if ($null -eq $subject) { continue }
        $clean = $subject.Trim()
        if ($clean.Length -gt 0) { $lines.Add('• ' + $clean) }
    }
    if ($lines.Count -eq 0) { return $null }
    $text = $lines -join "`n"
    if ($text.Length -gt 12000) { $text = $text.Substring(0, 12000).TrimEnd() }
    return $text
}

function Get-SoftKeyDeliveryReleaseNotes {
    param([string]$GitPath, [string]$RepositoryPath, [string]$HeadCommit, [string]$PreviousCommit)

    if (-not $HeadCommit -or $HeadCommit -cnotmatch '^[0-9a-f]{40}$') { return $null }
    $headCheck = Invoke-SoftKeyGitUtf8 -GitPath $GitPath -RepositoryPath $RepositoryPath -Arguments "rev-parse --quiet --verify ${HeadCommit}^{commit}"
    if ($headCheck.ExitCode -ne 0) { return $null }

    $rangeArgs = "log -1 --format=%s $HeadCommit"
    if ($PreviousCommit -and $PreviousCommit -cmatch '^[0-9a-f]{40}$' -and $PreviousCommit -cne $HeadCommit) {
        $ancestorCheck = Invoke-SoftKeyGitUtf8 -GitPath $GitPath -RepositoryPath $RepositoryPath -Arguments "merge-base --is-ancestor $PreviousCommit $HeadCommit"
        if ($ancestorCheck.ExitCode -eq 0) {
            $rangeArgs = "log --no-merges --format=%s -n 15 ${PreviousCommit}..${HeadCommit}"
        }
    }

    $logResult = Invoke-SoftKeyGitUtf8 -GitPath $GitPath -RepositoryPath $RepositoryPath -Arguments $rangeArgs
    if ($logResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($logResult.Output)) { return $null }
    $subjects = $logResult.Output -split "`r?`n"
    return Format-SoftKeyDeliveryReleaseNotes -Subjects $subjects
}
