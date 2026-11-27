#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'GitHub')]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(ParameterSetName = 'GitHub')][switch]$PublishPreview,
    [Parameter(ParameterSetName = 'GitHub')][ValidateLength(1, 12000)][string]$ReleaseNotes,
    [Parameter(Mandatory, ParameterSetName = 'OneDrive')][switch]$ToOneDrive
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SoftKey.GitHub.ps1')
. (Join-Path $PSScriptRoot 'SoftKey.UpdateDistribution.ps1')

$certificate = Get-SoftKeyReleaseCertificate
$package = Get-Item -LiteralPath $PackagePath
if ($ToOneDrive) {
    Copy-SoftKeyUpdateToFolder -Source $package.FullName -Destination (Get-SoftKeyOneDriveUpdateFolder) -Certificate $certificate
    return
}

$heldFile = [IO.File]::OpenRead($package.FullName)
try {
    $manifest = Read-SoftKeyUpdateArchive -ArchivePath $package.FullName -TrustedCertificate $certificate
    $name = "SoftKey-update-$($manifest.BuildNumber).zip"
    $tag = "softkey-preview-$($manifest.BuildNumber)"
    $packageSize = $heldFile.Length
    $hash = (Get-FileHash -InputStream $heldFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $headers = Get-SoftKeyGitHubHeaders -Token $env:SOFTKEY_GITHUB_CONTENTS_TOKEN -AllowBrowserSignIn
    try {
        $repo = Invoke-RestMethod -Uri $softKeyReleaseApi -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
        if ($repo.full_name -ine $softKeyReleaseRepository) {
            throw 'The release must belong to the SoftKey repository.'
        }

        $existingReleases = @(Get-SoftKeyReleases $headers | Where-Object { $_.tag_name -ceq $tag })
        if ($existingReleases.Count -gt 1) { throw "More than one release uses $tag." }
        $release = $existingReleases | Select-Object -First 1
        if (-not $release) {
            $body = [Text.Encoding]::UTF8.GetBytes((@{
                tag_name = $tag
                target_commitish = $manifest.Commit
                name = "SoftKey test build $($manifest.BuildNumber)"
                body = if ($ReleaseNotes) { $ReleaseNotes } else {
                    'Signerat uppdateringspaket för SoftKey, autostart, låsskärm och Windows-inloggning.'
                }
                draft = $true
                prerelease = $true
                make_latest = 'false'
            } | ConvertTo-Json -Compress))
            $release = Invoke-RestMethod -Uri "$softKeyReleaseApi/releases" -Method Post -Body $body `
                -ContentType 'application/json; charset=utf-8' -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
        }
        if ($release.id -lt 1 -or $release.tag_name -cne $tag -or $release.prerelease -ne $true -or
            $release.target_commitish -cne $manifest.Commit) {
            throw "The existing release does not match the signed update build: $tag."
        }

        $asset = Get-SoftKeyReleaseAsset $release $manifest.BuildNumber
        if (-not $asset) {
            if (-not $release.draft) { throw 'The published preview is missing its update asset.' }
            $assetEndpoint = "https://uploads.github.com/repos/$softKeyReleaseRepository/releases/$($release.id)/assets"
            if ($release.upload_url -ine ($assetEndpoint + '{?name,label}')) { throw 'Unexpected GitHub upload endpoint.' }
            $asset = Send-SoftKeyReleaseAsset -Uri "$($assetEndpoint)?name=$name" -Headers $headers -PackageStream $heldFile
            $asset = Get-SoftKeyReleaseAsset @{assets=@($asset)} $manifest.BuildNumber
        }
        if ($asset.digest -cne "sha256:$hash" -or $asset.size -ne $packageSize) {
            throw 'The existing GitHub asset does not match the verified local package.'
        }

        $received = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'obj\Updates\Received') -Force).FullName
        $receivedPackage = Join-Path $received $name
        $null = Save-SoftKeyReleaseAsset -Asset $asset -Headers $headers -Path $receivedPackage `
            -Certificate $certificate -BuildNumber $manifest.BuildNumber -Commit $manifest.Commit

        if ($PublishPreview -and $release.draft) {
            $body = [Text.Encoding]::UTF8.GetBytes((@{draft=$false; prerelease=$true; make_latest='false'} | ConvertTo-Json -Compress))
            $release = Invoke-RestMethod -Uri "$softKeyReleaseApi/releases/$($release.id)" -Method Patch -Body $body `
                -ContentType 'application/json; charset=utf-8' -Headers $headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
            if ($release.draft -ne $false -or $release.prerelease -ne $true) { throw 'GitHub did not confirm publication of the preview.' }
        }

        [pscustomobject]@{
            BuildNumber = $manifest.BuildNumber
            Commit = $manifest.Commit
            Status = if ($release.draft) { 'Draft' } else { 'Preview' }
            ReleaseUrl = $release.html_url
            VerifiedDownload = $receivedPackage
        }
    } finally { $headers.Clear() }
} finally { $heldFile.Dispose() }
