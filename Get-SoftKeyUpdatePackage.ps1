#Requires -Version 5.1
param([Parameter(Mandatory)][ValidateRange(0, [long]::MaxValue)][long]$InstalledBuildNumber)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SoftKey.UpdateDistribution.ps1')

$certificate = Get-SoftKeyReleaseCertificate
$headers = Get-SoftKeyReleaseHeaders 'GitHub-token (Contents read-only)'
try {
    $preview = Receive-SoftKeyPreview $InstalledBuildNumber (Join-Path $PSScriptRoot 'obj\Updates\Received') $headers $certificate
    if ($preview) { $preview } else { Write-Output 'No newer SoftKey preview is available.' }
} finally { $headers.Clear() }
