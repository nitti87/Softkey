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
