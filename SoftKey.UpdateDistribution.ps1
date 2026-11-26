. (Join-Path $PSScriptRoot 'SoftKey.UpdatePackage.ps1')
Add-Type -AssemblyName System.Net.Http

$softKeyReleaseRepository = 'nitti87/Softkey'
$softKeyReleaseApi = "https://api.github.com/repos/$softKeyReleaseRepository"

function Get-SoftKeyReleaseHeaders([string]$Prompt) {
    $token = $env:SOFTKEY_GITHUB_CONTENTS_TOKEN
    if (-not $token) {
        $secret = Read-Host -Prompt $Prompt -AsSecureString
        try { $token = ([pscredential]::new('SoftKey', $secret)).GetNetworkCredential().Password }
        finally { $secret.Dispose() }
    }
    if (-not $token) { throw 'A GitHub token is required.' }
    @{
        Authorization = 'Bearer ' + $token
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
}

function Get-SoftKeyReleaseCertificate {
    Get-Item -LiteralPath 'Cert:\LocalMachine\Root\093405919B8DF0EF0F13D124D7DB22FE685979BB' -ErrorAction Stop
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

function Copy-SoftKeyReleaseAsset($Asset, [hashtable]$Headers, [IO.Stream]$Destination) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(90))
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$softKeyReleaseApi/releases/assets/$($Asset.id)")
    $request.Headers.Add('User-Agent', 'SoftKey-Updates')
    $request.Headers.Add('Authorization', $Headers.Authorization)
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
    param($Asset, [hashtable]$Headers, [string]$Path, $Certificate, [long]$BuildNumber, [string]$Commit)

    $temporaryPath = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $file = [IO.File]::Open($temporaryPath, 'CreateNew', 'Write', 'None')
        try { Copy-SoftKeyReleaseAsset $Asset $Headers $file } finally { $file.Dispose() }

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

function Receive-SoftKeyPreview([long]$InstalledBuildNumber, [string]$Cache, [hashtable]$Headers, $Certificate) {
    $candidate = $null
    $newestBuild = $InstalledBuildNumber
    foreach ($release in Get-SoftKeyReleases $Headers) {
        if ($release.draft -ne $false -or $release.prerelease -ne $true -or
            $release.target_commitish -cnotmatch '^[0-9a-f]{40}$') { continue }
        $tag = [regex]::Match($release.tag_name, '^softkey-preview-([1-9][0-9]*)$')
        [long]$build = 0
        if ($tag.Success -and [long]::TryParse($tag.Groups[1].Value, [ref]$build) -and $build -gt $newestBuild) {
            $newestBuild = $build
            $candidate = $release
        }
    }
    if (-not $candidate) { return }
    $asset = Get-SoftKeyReleaseAsset $candidate $newestBuild
    if (-not $asset) { throw 'The newest SoftKey preview is missing its update asset.' }

    $directory = (New-Item -ItemType Directory -Path $Cache -Force).FullName
    $path = Join-Path $directory $asset.name
    if ([IO.File]::Exists($path) -and (Get-Item -LiteralPath $path).Length -eq $asset.size -and
        ('sha256:' + (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()) -ceq $asset.digest) {
        $manifest = Read-SoftKeyUpdateArchive -ArchivePath $path -TrustedCertificate $Certificate -InstalledBuildNumber $InstalledBuildNumber
        if ($manifest.BuildNumber -ne $newestBuild -or $manifest.Commit -cne $candidate.target_commitish) {
            throw 'The verified cache does not match the selected GitHub release.'
        }
    } else {
        $manifest = Save-SoftKeyReleaseAsset -Asset $asset -Headers $Headers -Path $path `
            -Certificate $Certificate -BuildNumber $newestBuild -Commit $candidate.target_commitish
    }
    [pscustomobject]@{BuildNumber=$manifest.BuildNumber; Commit=$manifest.Commit; VerifiedDownload=$path}
}
