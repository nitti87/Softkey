#Requires -Version 5.1
param([Parameter(Mandatory)][ValidateRange(0, [long]::MaxValue)][long]$InstalledBuildNumber)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SoftKey.UpdateDistribution.ps1')

$headers = Get-SoftKeyPublicReleaseHeaders
$preview = Find-SoftKeyPreview $InstalledBuildNumber $headers
if ($preview) {
    Receive-SoftKeyPreview $preview $InstalledBuildNumber (Join-Path $PSScriptRoot 'obj\Updates\Received') $headers (Get-SoftKeyReleaseCertificate)
} else { Write-Output 'No newer SoftKey preview is available.' }
