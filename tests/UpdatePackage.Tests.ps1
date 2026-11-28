$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'SoftKey.UpdateDistribution.ps1')

function Assert-UpdateRejected([scriptblock]$Check, [string]$Message) {
    try { & $Check | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        Write-Output "PASS rejected: $Message"
        return
    }
    throw "The package unexpectedly passed: $Message"
}

& {
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'SoftKey.GitHub.ps1')
    $gitState = @{ Lookups = 0; SignIns = 0; HasCredential = $false }
    function Get-SoftKeyGitPath { 'Invoke-TestGit' }
    function Invoke-TestGit {
        $global:LASTEXITCODE = 0
        if (($args -join ' ') -ceq 'credential-manager github login --username nitti87 --browser') {
            $gitState.SignIns++
            $gitState.HasCredential = $true
            return
        }
        if (($args -join ' ') -cne 'credential-manager get' -or $env:GCM_INTERACTIVE -ne 'false' -or
            ($input -join '') -cne "protocol=https`nhost=github.com`nusername=nitti87`n`n") {
            throw 'The GitHub credential lookup must be noninteractive and limited to the expected account.'
        }
        $gitState.Lookups++
        if ($gitState.HasCredential) { 'username=nitti87'; 'password=test-cached-credential' }
        else { $global:LASTEXITCODE = 1 }
    }

    $previousInteractive = $env:GCM_INTERACTIVE
    $env:GCM_INTERACTIVE = 'true'
    try {
        $explicit = Get-SoftKeyGitHubHeaders -Token 'test-explicit-credential' -AllowBrowserSignIn
        if ($explicit.Authorization -cne 'Bearer test-explicit-credential' -or $gitState.Lookups -ne 0) {
            throw 'An explicitly supplied token must bypass Git Credential Manager.'
        }
        Assert-UpdateRejected { Get-SoftKeyGitHubHeaders } 'GitHub sign-in is required'
        if ($gitState.SignIns -ne 0) { throw 'An unattended run opened a browser.' }

        $first = Get-SoftKeyGitHubHeaders -AllowBrowserSignIn
        $second = Get-SoftKeyGitHubHeaders -AllowBrowserSignIn
        $unattended = Get-SoftKeyGitHubHeaders
        if ($gitState.SignIns -ne 1 -or $first.Authorization -cne 'Bearer test-cached-credential' -or
            $second.Authorization -cne $first.Authorization -or $unattended.Authorization -cne $first.Authorization -or
            $env:GCM_INTERACTIVE -ne 'true') {
            throw 'SoftKey should sign in once and reuse the saved credential in both interactive and unattended runs.'
        }
        Write-Output 'PASS reused one browser sign-in for subsequent GitHub operations'
    } finally { $env:GCM_INTERACTIVE = $previousInteractive }
}

& {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    foreach ($scriptName in 'Start-SoftKeyDelivery.ps1', 'Publish-SoftKeyUpdatePackage.ps1', 'SoftKey.GitHub.ps1') {
        $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot $scriptName), [ref]$null, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw "Syntax error in ${scriptName}: $($parseErrors[0].Message)" }
    }
    . (Join-Path $repoRoot 'SoftKey.GitHub.ps1')
    if ($null -ne (Format-SoftKeyDeliveryReleaseNotes @('', '   '))) { throw 'Empty subjects must return null release notes.' }
    $formatted = Format-SoftKeyDeliveryReleaseNotes @('  Förbättrat kraschskydd  ', '', 'Tydligare pil vid Arkiv')
    if ($formatted -cne "• Förbättrat kraschskydd`n• Tydligare pil vid Arkiv") {
        throw "Unexpected formatted release notes: $formatted"
    }
    $oversized = Format-SoftKeyDeliveryReleaseNotes @(('å' * 13000))
    if ($oversized.Length -gt 12000) { throw 'Release notes exceeded GitHub validation limit.' }

    $gitExe = Get-SoftKeyGitPath
    $tmpRepo = Join-Path ([IO.Path]::GetTempPath()) ('SoftKey-notes-git-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $tmpRepo
    try {
        $null = & $gitExe -C $tmpRepo init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize temporary git repo.' }
        [IO.File]::WriteAllText((Join-Path $tmpRepo 'a.txt'), '1')
        $null = & $gitExe -C $tmpRepo add a.txt
        $null = & $gitExe -C $tmpRepo -c user.name=Test -c user.email=test@example.invalid commit --quiet -m 'Basversion'
        $baseCommit = (& $gitExe -C $tmpRepo rev-parse HEAD).Trim()

        [IO.File]::WriteAllText((Join-Path $tmpRepo 'a.txt'), '2')
        $null = & $gitExe -C $tmpRepo add a.txt
        $null = & $gitExe -C $tmpRepo -c user.name=Test -c user.email=test@example.invalid commit --quiet -m 'Förbättrad åäö-hantering'
        [IO.File]::WriteAllText((Join-Path $tmpRepo 'a.txt'), '3')
        $null = & $gitExe -C $tmpRepo add a.txt
        $null = & $gitExe -C $tmpRepo -c user.name=Test -c user.email=test@example.invalid commit --quiet -m 'Smidigare uppdateringsflöde'
        $headCommit = (& $gitExe -C $tmpRepo rev-parse HEAD).Trim()

        $rangeNotes = Get-SoftKeyDeliveryReleaseNotes -GitPath $gitExe -RepositoryPath $tmpRepo -HeadCommit $headCommit -PreviousCommit $baseCommit
        if ($rangeNotes -cne "• Smidigare uppdateringsflöde`n• Förbättrad åäö-hantering") {
            throw "Unexpected range release notes: $rangeNotes"
        }
        $singleNotes = Get-SoftKeyDeliveryReleaseNotes -GitPath $gitExe -RepositoryPath $tmpRepo -HeadCommit $headCommit -PreviousCommit $null
        if ($singleNotes -cne '• Smidigare uppdateringsflöde') {
            throw "Unexpected single-commit release notes: $singleNotes"
        }
        [IO.File]::WriteAllText((Join-Path $tmpRepo 'a.txt'), '4')
        $null = & $gitExe -C $tmpRepo add a.txt
        $null = & $gitExe -C $tmpRepo -c user.name=Test -c user.email=test@example.invalid commit --quiet `
            -m 'Tydligare uppdateringsdialog' -m "Visar kategorier och bevarade inställningar.`n`nCo-authored-by: Test <test@example.invalid>"
        $bodyCommit = (& $gitExe -C $tmpRepo rev-parse HEAD).Trim()
        $bodyNotes = Get-SoftKeyDeliveryReleaseNotes -GitPath $gitExe -RepositoryPath $tmpRepo -HeadCommit $bodyCommit -PreviousCommit $headCommit
        if ($bodyNotes -cne "• Tydligare uppdateringsdialog`n  Visar kategorier och bevarade inställningar.") {
            throw "Unexpected detailed release notes: $bodyNotes"
        }
        if ($null -ne (Get-SoftKeyDeliveryReleaseNotes -GitPath $gitExe -RepositoryPath $tmpRepo -HeadCommit 'not-a-sha' -PreviousCommit $baseCommit)) {
            throw 'Invalid commit hash must return null.'
        }
        Write-Output 'PASS delivery scripts parse cleanly and generate UTF-8 Swedish release notes from Git commits'
    } finally {
        if (Test-Path -LiteralPath $tmpRepo) { Remove-Item -LiteralPath $tmpRepo -Recurse -Force }
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('SoftKey-package-checks-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$key = [Security.Cryptography.RSACng]::new(2048)
$certificate = $null
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=SoftKey update package test', $key, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddMinutes(-1), [DateTimeOffset]::Now.AddHours(1))
    foreach ($name in Get-SoftKeyUpdateFiles) {
        [IO.File]::WriteAllBytes((Join-Path $scratch $name), [Text.Encoding]::UTF8.GetBytes($name))
    }
    $archive = Join-Path $scratch 'SoftKey-update-42.zip'
    $commit = '1234567890abcdef1234567890abcdef12345678'
    $newArchive = @{PackageDirectory=$scratch; ArchivePath=$archive; BuildNumber=42; Commit=$commit; Certificate=$certificate}
    New-SoftKeyUpdateArchive @newArchive
    $manifest = Read-SoftKeyUpdateArchive -ArchivePath $archive -TrustedCertificate $certificate -InstalledBuildNumber 41
    if ($manifest.BuildNumber -ne 42 -or $manifest.Commit -ne $commit -or @($manifest.Files).Count -ne 16) {
        throw 'The signed package lost build identity or a component.'
    }
    Write-Output 'PASS verified the signed build and all sixteen files'

    & {
        $packageFile = Get-Item -LiteralPath $archive
        $asset = [pscustomobject]@{
            id=1; name=$packageFile.Name; state='uploaded'; size=$packageFile.Length
            digest='sha256:' + (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
        }
        $release = [pscustomobject]@{
            id=7; draft=$false; prerelease=$true; tag_name='softkey-preview-42'; target_commitish=$commit
            body='Changes shown before the package is downloaded.'; published_at='2026-11-27T12:00:00Z'; assets=@($asset)
        }
        $transfer = @{Count=0; Bytes=0; Verifications=0}
        function Get-SoftKeyReleases([hashtable]$Headers) {
            if ($Headers.ContainsKey('Authorization')) { throw 'Public discovery attempted to authenticate.' }
            $release
        }
        function Invoke-RestMethod($Uri, $Headers, [switch]$UseBasicParsing, $MaximumRedirection, $TimeoutSec) {
            if ($Uri -cne "$softKeyReleaseApi/releases/tags/softkey-preview-42" -or $Headers.ContainsKey('Authorization')) {
                throw 'Publication must use the same release tag that public clients see, without credentials.'
            }
            $release
        }
        function Copy-SoftKeyReleaseAsset($Asset, [hashtable]$Headers, [IO.Stream]$Destination, [scriptblock]$OnBytes) {
            if ($Headers.ContainsKey('Authorization')) { throw 'Public download attempted to authenticate.' }
            $transfer.Count++
            $bytes = [IO.File]::ReadAllBytes($archive)
            $Destination.Write($bytes, 0, $bytes.Length)
            if ($OnBytes) { & $OnBytes $bytes.Length $Asset.size }
        }
        $public = Get-SoftKeyPublicReleaseHeaders
        $preview = Find-SoftKeyPreview 41 $public
        if ($transfer.Count -ne 0 -or $preview.BuildNumber -ne 42 -or $preview.Notes -cne $release.body -or
            (Find-SoftKeyPreview 42 $public)) {
            throw 'Discovery must return newer release metadata without downloading.'
        }
        $download = @{Preview=$preview; InstalledBuildNumber=41; Cache=(Join-Path $scratch 'PublicCache'); Headers=$public;
            Certificate=$certificate; OnBytes={ param($current, $length) $transfer.Bytes = $current };
            OnVerify={ $transfer.Verifications++ }}
        $receivedPackage = Receive-SoftKeyPreview @download
        $retriedPackage = Receive-SoftKeyPreview @download
        if ($transfer.Count -ne 1 -or $transfer.Bytes -ne $asset.size -or $transfer.Verifications -ne 2 -or
            $receivedPackage.Commit -cne $commit -or $retriedPackage.VerifiedDownload -ine $receivedPackage.VerifiedDownload) {
            throw ("Unexpected public update result. Downloads: $($transfer.Count). Bytes: $($transfer.Bytes) of $($asset.size). " +
                "Verifications: $($transfer.Verifications). Results: $(@($receivedPackage).Count)/$(@($retriedPackage).Count). " +
                "Commit: '$($receivedPackage.Commit)' (expected '$commit'). Paths: '$($receivedPackage.VerifiedDownload)' / '$($retriedPackage.VerifiedDownload)'.")
        }
        Write-Output 'PASS public discovery did not download, and the user-initiated download verified its bytes and signature'

        $release.draft = $true
        Assert-UpdateRejected { Confirm-SoftKeyPublicPreview $release $asset $manifest $receivedPackage.VerifiedDownload $certificate } `
            'not yet visible on the public GitHub release'
        if ($transfer.Count -ne 1) { throw 'A draft must not be downloaded through the public path.' }
        $release.draft = $false
        $confirmed = Confirm-SoftKeyPublicPreview $release $asset $manifest $receivedPackage.VerifiedDownload $certificate
        if ($confirmed.Commit -cne $commit -or $transfer.Count -ne 2) {
            throw 'Publication must verify a fresh public download even when a verified local copy already exists.'
        }
        Write-Output 'PASS publication requires a signed download accessible without credentials'
    }

    $feed = Join-Path $scratch 'OneDrive\SoftKey\Updates'
    $cache = Join-Path $scratch 'Cache'
    $published = Copy-SoftKeyUpdateToFolder -Source $archive -Destination $feed -Certificate $certificate
    $retried = Copy-SoftKeyUpdateToFolder -Source $archive -Destination $feed -Certificate $certificate
    if ($retried.VerifiedDownload -cne $published.VerifiedDownload) { throw 'A OneDrive retry changed the destination.' }
    [IO.File]::Copy($archive, (Join-Path $feed 'SoftKey-update-44-DEVPC.zip'))
    $received = Receive-SoftKeyFolderPreview 41 $feed $cache $certificate
    if ($received.BuildNumber -ne 42 -or $received.Commit -cne $commit -or
        $received.VerifiedDownload -cne (Join-Path $cache 'SoftKey-update-42.zip') -or
        (Get-FileHash -LiteralPath $received.VerifiedDownload).Hash -cne (Get-FileHash -LiteralPath $archive).Hash -or
        (Receive-SoftKeyFolderPreview 42 $feed $cache $certificate)) {
        throw 'The synced folder did not deliver only newer verified packages to the local cache.'
    }
    [IO.File]::Copy($archive, (Join-Path $feed 'SoftKey-update-43.zip'))
    Assert-UpdateRejected { Receive-SoftKeyFolderPreview 41 $feed $cache $certificate } 'filename does not match'
    if (@([IO.Directory]::EnumerateFiles($cache)).Count -ne 1) { throw 'A failed OneDrive check left an unverified file in the cache.' }
    Write-Output 'PASS copied and verified a newer signed update from a local OneDrive folder without credentials'

    $hash = (Get-FileHash -LiteralPath $archive).Hash
    Assert-UpdateRejected { New-SoftKeyUpdateArchive @newArchive } 'already exists'
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $hash) { throw 'A retry replaced the verified package.' }
    Assert-UpdateRejected {
        Read-SoftKeyUpdateArchive -ArchivePath $archive -TrustedCertificate $certificate -InstalledBuildNumber 42
    } 'newer build'

    $otherCertificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddMinutes(-1), [DateTimeOffset]::Now.AddHours(2))
    try {
        Assert-UpdateRejected {
            Read-SoftKeyUpdateArchive -ArchivePath $archive -TrustedCertificate $otherCertificate
        } 'trusted SoftKey certificate'
    } finally { $otherCertificate.Dispose() }

    $modifiedFile = [IO.File]::Open($archive, 'Open', 'ReadWrite', 'None')
    try {
        $zip = [IO.Compression.ZipArchive]::new($modifiedFile, 'Update', $true)
        try {
            $entry = $zip.GetEntry('SoftKey.exe').Open()
            try { $entry.WriteByte(0) } finally { $entry.Dispose() }
        } finally { $zip.Dispose() }
    } finally { $modifiedFile.Dispose() }
    Assert-UpdateRejected {
        Read-SoftKeyUpdateArchive -ArchivePath $archive -TrustedCertificate $certificate
    } 'Update file hash does not match: SoftKey.exe'

    $modifiedFile = [IO.File]::Open($archive, 'Open', 'ReadWrite', 'None')
    try {
        $zip = [IO.Compression.ZipArchive]::new($modifiedFile, 'Update', $true)
        try { $null = $zip.CreateEntry('../settings.json') } finally { $zip.Dispose() }
    } finally { $modifiedFile.Dispose() }
    Assert-UpdateRejected {
        Read-SoftKeyUpdateArchive -ArchivePath $archive -TrustedCertificate $certificate
    } 'unexpected entries'
} finally {
    if ($certificate) { $certificate.Dispose() }
    $key.Dispose()
    $directory = Get-Item -LiteralPath $scratch
    if ($directory.FullName -eq $scratch -and -not ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
}
