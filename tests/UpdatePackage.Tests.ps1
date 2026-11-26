$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'SoftKey.UpdatePackage.ps1')

function Assert-UpdateRejected([scriptblock]$Check, [string]$Message) {
    try { & $Check | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        Write-Output "PASS rejected: $Message"
        return
    }
    throw "The package unexpectedly passed: $Message"
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
