if ($PSVersionTable.PSEdition -eq 'Desktop') {
    Add-Type -AssemblyName System.Security
} else {
    Add-Type -AssemblyName System.Security.Cryptography.Pkcs
}
Add-Type -AssemblyName System.IO.Compression
# The embedded workers start PowerShell without a profile, so the Cert: drive
# is not necessarily registered by the time they verify the publisher.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1')

function Get-SoftKeyUpdateFiles {
    foreach ($component in 'SoftKey', 'SoftKey.Logon', 'SoftKey.Locked', 'SoftKey.Startup') {
        foreach ($extension in 'exe', 'dll', 'deps.json', 'runtimeconfig.json') {
            "$component.$extension"
        }
    }
}

function New-SoftKeyUpdateArchive {
    param(
        [string]$PackageDirectory,
        [string]$ArchivePath,
        [long]$BuildNumber,
        [ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
        [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    if ($BuildNumber -lt 1) { throw 'The update build number must be positive.' }
    $unsignedManifest = [ordered]@{
        Schema = 1
        BuildNumber = $BuildNumber
        Commit = $Commit
        Files = @()
    }
    $archivePath = [IO.Path]::GetFullPath($ArchivePath)
    if ([IO.File]::Exists($archivePath)) { throw 'The update archive already exists.' }
    $tempPath = $archivePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $file = [IO.File]::Open($tempPath, 'CreateNew', 'ReadWrite', 'None')
        try {
            $zip = [IO.Compression.ZipArchive]::new($file, 'Create', $true)
            try {
                foreach ($name in Get-SoftKeyUpdateFiles) {
                    $inputFile = [IO.File]::OpenRead((Join-Path $PackageDirectory $name))
                    try {
                        if ($inputFile.Length -lt 1 -or $inputFile.Length -gt 32MB) { throw "Invalid update file size: $name" }
                        $unsignedManifest.Files += [ordered]@{
                            Name = $name
                            Size = $inputFile.Length
                            SHA256 = (Get-FileHash -InputStream $inputFile -Algorithm SHA256).Hash
                        }
                        $inputFile.Position = 0
                        $destination = $zip.CreateEntry($name).Open()
                        try { $inputFile.CopyTo($destination) } finally { $destination.Dispose() }
                    } finally { $inputFile.Dispose() }
                }

                $json = $unsignedManifest | ConvertTo-Json -Depth 5 -Compress
                $content = [Security.Cryptography.Pkcs.ContentInfo]::new([Text.Encoding]::UTF8.GetBytes($json))
                $signature = [Security.Cryptography.Pkcs.SignedCms]::new($content)
                $signer = [Security.Cryptography.Pkcs.CmsSigner]::new($Certificate)
                $signer.IncludeOption = 'EndCertOnly'
                $signer.DigestAlgorithm = [Security.Cryptography.Oid]::new('2.16.840.1.101.3.4.2.1')
                $signature.ComputeSignature($signer, $true)
                $encoded = $signature.Encode()
                $destination = $zip.CreateEntry('update.p7m').Open()
                try { $destination.Write($encoded, 0, $encoded.Length) } finally { $destination.Dispose() }
            } finally { $zip.Dispose() }
        } finally { $file.Dispose() }

        $null = Read-SoftKeyUpdateArchive -ArchivePath $tempPath -TrustedCertificate $Certificate
        [IO.File]::Move($tempPath, $archivePath)
    } finally {
        if ([IO.File]::Exists($tempPath)) { [IO.File]::Delete($tempPath) }
    }
}

function Read-SoftKeyUpdateArchive {
    param(
        [string]$ArchivePath,
        [Security.Cryptography.X509Certificates.X509Certificate2]$TrustedCertificate,
        [long]$InstalledBuildNumber = 0
    )

    $expectedFiles = @(Get-SoftKeyUpdateFiles)
    $file = [IO.File]::OpenRead($ArchivePath)
    try {
        if ($file.Length -gt 64MB) { throw 'The update archive is too large.' }
        $zip = [IO.Compression.ZipArchive]::new($file, 'Read', $true)
        try {
            $names = @($zip.Entries | ForEach-Object FullName)
            if (Compare-Object ($expectedFiles + 'update.p7m') $names -CaseSensitive) {
                throw 'The update archive contains missing, duplicate, or unexpected entries.'
            }

            $manifestEntry = $zip.GetEntry('update.p7m')
            if ($manifestEntry.Length -lt 1 -or $manifestEntry.Length -gt 64KB) { throw 'Invalid signed manifest size.' }
            $reader = [IO.BinaryReader]::new($manifestEntry.Open())
            try {
                $encoded = $reader.ReadBytes([int]$manifestEntry.Length)
                if ($encoded.Length -ne $manifestEntry.Length -or $reader.BaseStream.ReadByte() -ne -1) {
                    throw 'The signed manifest length is inconsistent.'
                }
            } finally { $reader.Dispose() }

            $signature = [Security.Cryptography.Pkcs.SignedCms]::new()
            $signature.Decode($encoded)
            if ($signature.SignerInfos.Count -ne 1) { throw 'The update manifest must have one signer.' }
            $signer = $signature.SignerInfos[0]
            if ($null -eq $signer.Certificate -or
                $signer.DigestAlgorithm.Value -ne '2.16.840.1.101.3.4.2.1' -or
                [Convert]::ToBase64String($signer.Certificate.RawData) -cne [Convert]::ToBase64String($TrustedCertificate.RawData)) {
                throw 'The update manifest was not signed by the trusted SoftKey certificate.'
            }
            $signature.CheckSignature($true)
            $manifest = [Text.Encoding]::UTF8.GetString($signature.ContentInfo.Content) | ConvertFrom-Json
            if ($manifest.Schema -ne 1 -or $manifest.Commit -cnotmatch '^[0-9a-f]{40}$' -or
                ($manifest.BuildNumber -isnot [int] -and $manifest.BuildNumber -isnot [long]) -or
                $manifest.BuildNumber -le [Math]::Max(0L, $InstalledBuildNumber)) {
                throw 'The signed manifest is invalid or does not describe a newer build.'
            }
            if (Compare-Object $expectedFiles @($manifest.Files | ForEach-Object Name) -CaseSensitive) {
                throw 'The signed manifest does not describe the expected SoftKey files.'
            }
            foreach ($record in $manifest.Files) {
                $entry = $zip.GetEntry($record.Name)
                if ($entry.Length -lt 1 -or $entry.Length -gt 32MB -or $entry.Length -ne $record.Size) {
                    throw "Invalid update file size: $($record.Name)"
                }
                $reader = [IO.BinaryReader]::new($entry.Open())
                try {
                    $bytes = $reader.ReadBytes([int]$entry.Length)
                    if ($bytes.Length -ne $entry.Length -or $reader.BaseStream.ReadByte() -ne -1) {
                        throw "Invalid update file size: $($record.Name)"
                    }
                    $hasher = [Security.Cryptography.SHA256]::Create()
                    try { $hash = [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '') }
                    finally { $hasher.Dispose() }
                    if ($hash -cne $record.SHA256) {
                        throw "Update file hash does not match: $($record.Name)"
                    }
                } finally { $reader.Dispose() }
            }
            $manifest
        } finally { $zip.Dispose() }
    } finally { $file.Dispose() }
}
