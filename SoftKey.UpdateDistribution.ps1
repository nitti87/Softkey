. (Join-Path $PSScriptRoot 'SoftKey.UpdatePackage.ps1')
Add-Type -AssemblyName System.Net.Http

$softKeyReleaseRepository = 'nitti87/Softkey'
$softKeyReleaseApi = "https://api.github.com/repos/$softKeyReleaseRepository"

function Get-SoftKeyPublicReleaseHeaders {
    @{Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'}
}

function Get-SoftKeyReleaseCertificate {
    Get-Item -LiteralPath 'Cert:\LocalMachine\Root\093405919B8DF0EF0F13D124D7DB22FE685979BB' -ErrorAction Stop
}

function Get-SoftKeyOneDriveUpdateFolder {
    $root = if ($env:OneDriveConsumer) { $env:OneDriveConsumer } else { $env:OneDrive }
    if (-not $root -or -not [IO.Directory]::Exists($root)) { throw 'Sign in to OneDrive on this computer first.' }
    Join-Path $root 'SoftKey\Updates'
}

function Copy-SoftKeyUpdateToFolder {
    param([string]$Source, [string]$Destination, $Certificate, [long]$InstalledBuildNumber, [long]$ExpectedBuild)

    $directory = [IO.Directory]::CreateDirectory($Destination)
    $temporary = Join-Path $directory.FullName ('.softkey-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $package = [IO.File]::OpenRead($Source)
        try {
            if ($package.Length -lt 1 -or $package.Length -gt 64MB) { throw 'The update package size is invalid.' }
            $copy = [IO.File]::Open($temporary, 'CreateNew', 'Write', 'None')
            try { $package.CopyTo($copy) } finally { $copy.Dispose() }
        } finally { $package.Dispose() }

        $manifest = Read-SoftKeyUpdateArchive -ArchivePath $temporary -TrustedCertificate $Certificate -InstalledBuildNumber $InstalledBuildNumber
        if ($ExpectedBuild -gt 0 -and $manifest.BuildNumber -ne $ExpectedBuild) {
            throw 'The filename does not match the signed build number.'
        }
        $path = Join-Path $directory.FullName "SoftKey-update-$($manifest.BuildNumber).zip"
        try { [IO.File]::Move($temporary, $path) }
        catch [IO.IOException] {
            if (-not [IO.File]::Exists($path)) { throw }
            if ((Get-FileHash -LiteralPath $temporary).Hash -cne (Get-FileHash -LiteralPath $path).Hash) {
                throw 'The destination contains a different package for the same build.'
            }
        }
        [pscustomobject]@{BuildNumber=$manifest.BuildNumber; Commit=$manifest.Commit; VerifiedDownload=$path}
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Receive-SoftKeyFolderPreview([long]$InstalledBuildNumber, [string]$Folder, [string]$Cache, $Certificate) {
    $newestBuild = $InstalledBuildNumber
    $candidate = $null
    foreach ($file in [IO.Directory]::EnumerateFiles($Folder, 'SoftKey-update-*.zip')) {
        $name = [regex]::Match([IO.Path]::GetFileName($file), '^SoftKey-update-([1-9][0-9]*)\.zip$')
        [long]$build = 0
        if ($name.Success -and [long]::TryParse($name.Groups[1].Value, [ref]$build) -and $build -gt $newestBuild) {
            $candidate = $file
            $newestBuild = $build
        }
    }
    if ($candidate) {
        Copy-SoftKeyUpdateToFolder -Source $candidate -Destination $Cache -Certificate $Certificate `
            -InstalledBuildNumber $InstalledBuildNumber -ExpectedBuild $newestBuild
    }
}

function Get-SoftKeyReleases([hashtable]$Headers) {
    $page = 1
    do {
        $releases = Invoke-RestMethod -Uri "$softKeyReleaseApi/releases?per_page=100&page=$page" `
            -Headers $Headers -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 25
        $releases
        $page++
    } while ($releases.Count -eq 100)
}

function Get-SoftKeyReleaseAsset($Release, [long]$BuildNumber) {
    $expectedName = "SoftKey-update-$BuildNumber.zip"
    $assets = @($Release.assets | Where-Object { $_.name -ceq $expectedName })
    if ($assets.Count -eq 0) { return }
    if ($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or $assets[0].id -lt 1 -or
        $assets[0].size -lt 1 -or $assets[0].size -gt 64MB -or $assets[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "Invalid GitHub asset metadata for $expectedName."
    }
    $assets[0]
}

function Send-SoftKeyReleaseAsset([uri]$Uri, [hashtable]$Headers, [IO.Stream]$PackageStream) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    $client.MaxResponseContentBufferSize = 1MB
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $Uri)
    try {
        $request.Headers.Add('User-Agent', 'SoftKey-Updates')
        $request.Headers.Add('Authorization', $Headers.Authorization)
        $request.Headers.Add('X-GitHub-Api-Version', $Headers['X-GitHub-Api-Version'])
        $request.Headers.Add('Accept', $Headers.Accept)

        # Send the same locked stream that was verified, starting at its first byte.
        $PackageStream.Position = 0
        $request.Content = [Net.Http.StreamContent]::new($PackageStream)
        $request.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/zip')
        $request.Content.Headers.ContentLength = $PackageStream.Length
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 201) { throw "GitHub update upload returned HTTP $([int]$response.StatusCode)." }
            $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        } finally { $response.Dispose() }
    } finally {
        $request.Dispose()
        $client.Dispose()
    }
}

function Copy-SoftKeyReleaseAsset($Asset, [hashtable]$Headers, [IO.Stream]$Destination, [scriptblock]$OnBytes) {
    $uri = if ($Headers.Authorization) {
        "$softKeyReleaseApi/releases/assets/$($Asset.id)"
    } else {
        if ($Asset.name -cnotmatch '^SoftKey-update-([1-9][0-9]*)\.zip$') { throw 'Invalid public update filename.' }
        "https://github.com/$softKeyReleaseRepository/releases/download/softkey-preview-$($Matches[1])/$($Asset.name)"
    }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(90))
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $uri)
    $request.Headers.Add('User-Agent', 'SoftKey-Updates')
    if ($Headers.Authorization) { $request.Headers.Add('Authorization', $Headers.Authorization) }
    $request.Headers.Add('X-GitHub-Api-Version', $Headers['X-GitHub-Api-Version'])
    $request.Headers.Add('Accept', 'application/octet-stream')
    $response = $null
    try {
        $response = $client.SendAsync($request, 'ResponseHeadersRead', $deadline.Token).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -eq 302) {
            $location = $response.Headers.Location
            if ($null -eq $location -or -not $location.IsAbsoluteUri -or $location.Scheme -ne 'https' -or
                -not $location.IsDefaultPort -or $location.UserInfo -or
                $location.Host -notin 'release-assets.githubusercontent.com','objects.githubusercontent.com') {
                throw 'GitHub returned an unexpected update download address.'
            }
            $response.Dispose()
            # The signed download URL carries its own authorization.
            $response = $client.GetAsync($location, 'ResponseHeadersRead', $deadline.Token).GetAwaiter().GetResult()
        }
        if ([int]$response.StatusCode -ne 200) { throw "GitHub update download returned HTTP $([int]$response.StatusCode)." }

        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        try {
            $buffer = [byte[]]::new(65536)
            while (($length = $stream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
                if ($Destination.Position + $length -gt $Asset.size) { throw 'The download exceeds the GitHub asset size.' }
                $Destination.Write($buffer, 0, $length)
                if ($OnBytes) { $null = & $OnBytes $Destination.Position $Asset.size }
            }
        } finally { $stream.Dispose() }
    } finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
        $deadline.Dispose()
        $client.Dispose()
    }
}

function Save-SoftKeyReleaseAsset {
    param($Asset, [hashtable]$Headers, [string]$Path, $Certificate, [long]$BuildNumber, [string]$Commit,
        [scriptblock]$OnBytes, [scriptblock]$OnVerify)

    $temporaryPath = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $file = [IO.File]::Open($temporaryPath, 'CreateNew', 'Write', 'None')
        try { $null = Copy-SoftKeyReleaseAsset $Asset $Headers $file $OnBytes } finally { $file.Dispose() }

        if ($OnVerify) { $null = & $OnVerify }
        $digest = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
        if ((Get-Item -LiteralPath $temporaryPath).Length -ne $Asset.size -or "sha256:$($digest.ToLowerInvariant())" -cne $Asset.digest) {
            throw 'The download does not match the GitHub asset digest or size.'
        }
        $manifest = Read-SoftKeyUpdateArchive -ArchivePath $temporaryPath -TrustedCertificate $Certificate
        if ($manifest.BuildNumber -ne $BuildNumber -or ($Commit -and $manifest.Commit -cne $Commit)) {
            throw 'The verified package does not match the selected GitHub release.'
        }
        if ([IO.File]::Exists($Path)) {
            if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $digest) { throw 'The destination already contains a different package.' }
        } else {
            [IO.File]::Move($temporaryPath, $Path)
        }
        $manifest
    } finally {
        if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) }
    }
}

function Find-SoftKeyPreview([long]$InstalledBuildNumber, [hashtable]$Headers) {
    $candidate = $null
    $asset = $null
    $newestBuild = $InstalledBuildNumber
    foreach ($release in Get-SoftKeyReleases $Headers) {
        if ($release.draft -ne $false -or $release.prerelease -ne $true -or
            $release.target_commitish -cnotmatch '^[0-9a-f]{40}$') { continue }
        $tag = [regex]::Match($release.tag_name, '^softkey-preview-([1-9][0-9]*)$')
        [long]$build = 0
        if ($tag.Success -and [long]::TryParse($tag.Groups[1].Value, [ref]$build) -and $build -gt $newestBuild) {
            $releaseAsset = Get-SoftKeyReleaseAsset $release $build
            if (-not $releaseAsset) { continue }
            $newestBuild = $build
            $candidate = $release
            $asset = $releaseAsset
        }
    }
    if (-not $candidate) { return }
    $notes = [string]$candidate.body
    if ($notes.Length -gt 12000) { $notes = $notes.Substring(0, 12000) }
    [pscustomobject]@{
        BuildNumber = $newestBuild
        Commit = $candidate.target_commitish
        Notes = $notes
        Published = [string]$candidate.published_at
        Asset = [pscustomobject]@{Id=$asset.id; Name=$asset.name; State=$asset.state; Size=$asset.size; Digest=$asset.digest}
    }
}

function Receive-SoftKeyPreview {
    param($Preview, [long]$InstalledBuildNumber, [string]$Cache, [hashtable]$Headers, $Certificate,
        [scriptblock]$OnBytes, [scriptblock]$OnVerify)

    [long]$build = $Preview.BuildNumber
    if ($build -le $InstalledBuildNumber -or $Preview.Commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'The selected update build is invalid or already installed.'
    }
    $asset = Get-SoftKeyReleaseAsset @{assets=@($Preview.Asset)} $build
    if (-not $asset) { throw 'The selected SoftKey update is missing its archive.' }

    $directory = (New-Item -ItemType Directory -Path $Cache -Force).FullName
    foreach ($root in @((Split-Path -Parent $directory), $directory)) {
        if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'The update cache cannot be a link.'
        }
    }
    $path = Join-Path $directory $asset.name
    if ([IO.File]::Exists($path)) {
        $cached = Get-Item -LiteralPath $path -Force
        if ($cached.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The update cache contains a link.' }
        if ($cached.Length -ne $asset.size -or ('sha256:' + (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()) -cne $asset.digest) {
            # A partial or corrupted cache entry must not prevent a fresh download.
            [IO.File]::Delete($path)
        }
    }
    if ([IO.File]::Exists($path)) {
        if ($OnVerify) { $null = & $OnVerify }
        $manifest = Read-SoftKeyUpdateArchive -ArchivePath $path -TrustedCertificate $Certificate -InstalledBuildNumber $InstalledBuildNumber
        if ($manifest.BuildNumber -ne $build -or $manifest.Commit -cne $Preview.Commit) { throw 'The verified cache has a different build identity.' }
    } else {
        $manifest = Save-SoftKeyReleaseAsset -Asset $asset -Headers $Headers -Path $path -Certificate $Certificate `
            -BuildNumber $build -Commit $Preview.Commit -OnBytes $OnBytes -OnVerify $OnVerify
    }
    [pscustomobject]@{BuildNumber=$manifest.BuildNumber; Commit=$manifest.Commit; VerifiedDownload=$path}
}
