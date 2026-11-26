param()

# Read-only Windows checks. The only file written is the diagnostic report below.
$ErrorActionPreference = 'Stop'
$outputPath = Join-Path $PSScriptRoot 'obj\UIAccessTest\policy-diagnostics.json'
$installResultPath = Join-Path $PSScriptRoot 'UIAccess-install-result.json'
$installResult = if (Test-Path -LiteralPath $installResultPath) {
    Get-Content -LiteralPath $installResultPath -Raw | ConvertFrom-Json
} else { $null }
$launchResultPath = Join-Path $PSScriptRoot 'obj\UIAccessTest\launch-result.json'
$lastLaunch = if (Test-Path -LiteralPath $launchResultPath) {
    Get-Content -LiteralPath $launchResultPath -Raw | ConvertFrom-Json
} else { $null }
$since = (Get-Date).AddHours(-2)
if ($installResult -and $installResult.Timestamp) {
    try { $since = [DateTimeOffset]::Parse($installResult.Timestamp).LocalDateTime.AddMinutes(-5) }
    catch { }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$eventLogError = $null
$policyError = $null
$events = @()
$policies = $null
$applicationEvents = @()
$applicationLogError = $null

try {
    $raw = @(& "$env:WINDIR\System32\CiTool.exe" -lp -json 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($raw -join [Environment]::NewLine) }
    $policies = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($policies.OperationResult -and $policies.OperationResult -ne 0) {
        throw "CiTool returned $($policies.OperationResult). Run this script as administrator."
    }
}
catch { $policyError = $_.Exception.Message }

try {
    $rawEvents = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Microsoft-Windows-CodeIntegrity/Operational'
        StartTime = $since
        Id = 3033, 3076, 3077, 3089
    } -MaxEvents 2000 -ErrorAction Stop)
    $parsed = @($rawEvents | ForEach-Object {
        $eventXml = [xml]$_.ToXml()
        $data = [ordered]@{}
        foreach ($entry in $eventXml.Event.EventData.Data) {
            $data[[string]$entry.Name] = [string]$entry.'#text'
        }
        [pscustomobject]@{
            Time = $_.TimeCreated.ToString('o')
            Id = $_.Id
            RecordId = $_.RecordId
            ActivityId = [string]$eventXml.Event.System.Correlation.ActivityID
            Data = $data
        }
    })
    $softKeyEvents = @($parsed | Where-Object { ($_.Data.Values -join ' ') -match 'SoftKey' })
    $activityIds = @($softKeyEvents | Where-Object { $_.ActivityId } | Select-Object -ExpandProperty ActivityId -Unique)
    $events = @($parsed | Where-Object {
        $_.RecordId -in $softKeyEvents.RecordId -or ($_.ActivityId -and $_.ActivityId -in $activityIds)
    })
}
catch { $eventLogError = $_.Exception.Message }

try {
    $crashSince = if ($lastLaunch -and $lastLaunch.Timestamp) {
        [DateTimeOffset]::Parse($lastLaunch.Timestamp).LocalDateTime.AddMinutes(-2)
    } else { $since }
    $applicationEvents = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        StartTime = $crashSince
        Id = 1026, 1000, 1001
    } -MaxEvents 200 -ErrorAction Stop | Where-Object { $_.Message -match 'SoftKey' } | ForEach-Object {
        [pscustomobject]@{
            Time = $_.TimeCreated.ToString('o')
            Id = $_.Id
            Provider = $_.ProviderName
            Message = $_.Message
        }
    })
}
catch {
    if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') {
        $applicationLogError = $_.Exception.Message
    }
}

$sacState = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue
$report = [ordered]@{
    CollectedAt = (Get-Date).ToString('o')
    Administrator = $isAdmin
    Installation = $installResult
    LastLaunch = $lastLaunch
    SmartAppControlState = $sacState.VerifiedAndReputablePolicyState
    Policies = $policies
    PolicyReadError = $policyError
    CodeIntegrityEvents = $events
    EventLogReadError = $eventLogError
    ApplicationEvents = $applicationEvents
    ApplicationLogReadError = $applicationLogError
}
New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Output "Diagnostic report: $outputPath"
if ($eventLogError -or $policyError -or $applicationLogError) {
    Write-Warning 'The report is incomplete. Run this script from PowerShell as administrator to read the policy and event logs.'
    exit 1
}
Write-Output "Collected $($events.Count) SoftKey-related Code Integrity events. No Windows security settings were changed."
Write-Output "Collected $($applicationEvents.Count) SoftKey application crash events."
foreach ($event in $applicationEvents | Where-Object { $_.Id -eq 1026 }) {
    Write-Output $event.Message
}
