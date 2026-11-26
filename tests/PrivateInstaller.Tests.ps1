$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$policyPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
$realStateBefore = (Microsoft.PowerShell.Management\Get-ItemProperty -LiteralPath $policyPath).VerifiedAndReputablePolicyState
. (Join-Path $root 'SoftKey.Installation.ps1')
$checkRoot = Join-Path $root ('obj\UIAccessTest\private-install-checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkRoot | Out-Null
$passed = [Collections.Generic.List[string]]::new()

# All Windows writes and policy commands in the helper are simulated here.
function Get-ItemProperty {
    [CmdletBinding()] param([string]$LiteralPath, [string]$Name)
    if ($LiteralPath -ne $policyPath -or $Name -ne 'VerifiedAndReputablePolicyState') { throw 'Unexpected registry read.' }
    return [pscustomobject]@{VerifiedAndReputablePolicyState=$script:fakeState}
}
function Set-ItemProperty {
    [CmdletBinding()] param([string]$LiteralPath, [string]$Name, [int]$Value, [string]$Type)
    if ($LiteralPath -ne $policyPath -or $Name -ne 'VerifiedAndReputablePolicyState' -or $Value -ne 0 -or $Type -ne 'DWord') { throw 'Unexpected registry write.' }
    if ($script:writeFails) { throw 'Simulated write failure.' }
    $script:writes++
    $script:fakeState = $Value
}
function Assert-SoftKeyInstallAdministrator {
    if (-not $script:administrator) { throw 'Simulated lack of administrator privileges.' }
}
function Get-MpComputerStatus {
    [CmdletBinding()] param()
    return [pscustomobject]@{AntivirusEnabled=$script:antivirus;RealTimeProtectionEnabled=$script:realtime}
}
function Invoke-SoftKeyCiTool {
    param([string]$Operation)
    $script:nativeCalls++
    if ($Operation -eq 'Refresh') {
        if ($script:refreshTimesOut) { throw [TimeoutException]::new('Simulated policy refresh timeout.') }
        if ($script:refreshFails) { throw 'Simulated policy refresh failure.' }
        return
    }
    if ($Operation -ne 'List') { throw 'Unexpected native operation.' }
    $enforced=($script:fakeState -ne 0 -or $script:policyStaysEnforced)
    if ($script:policyIsString) { $enforced=$enforced.ToString() }
    return [pscustomobject]@{Policies=@([pscustomobject]@{
        PolicyID='{0283ac0f-fff1-49ae-ada1-8a933130cad6}'
        IsEnforced=$enforced
    })}
}
function Reset-Scenario([string]$Name) {
    $script:fakeState=1; $script:writes=0; $script:nativeCalls=0
    $script:administrator=$true; $script:antivirus=$true; $script:realtime=$true
    $script:refreshFails=$false; $script:refreshTimesOut=$false; $script:writeFails=$false; $script:policyStaysEnforced=$false; $script:policyIsString=$false
    $script:journal=Join-Path $checkRoot ($Name + '.json')
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Failure([scriptblock]$Action) {
    $failed=$false
    try { & $Action | Out-Null } catch { $failed=$true }
    Require $failed 'Expected the operation to fail.'
}

Reset-Scenario 'whatif'
$plan=Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -WhatIf
Require ($plan.Status -eq 'WouldDisable' -and $writes -eq 0 -and $nativeCalls -eq 0 -and -not (Test-Path -LiteralPath $journal)) 'WhatIf changed state or invoked a policy command.'
$passed.Add('WhatIf performs no writes or policy operations')

Reset-Scenario 'success'
$result=Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false
Require ($result.Status -eq 'OffVerified' -and $result.PreviousState -eq 1 -and $fakeState -eq 0 -and $writes -eq 1) 'The simulated installation did not verify the intended state.'
Require ((Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json).Status -eq 'OffVerified') 'The success journal is missing.'
$passed.Add('Successful change verifies the state and records the previous value')

Reset-Scenario 'administrator-required'
$administrator=$false
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ($writes -eq 0 -and $nativeCalls -eq 0) 'A non-administrator changed state.'
$passed.Add('Administrator privileges are required before mutation')

Reset-Scenario 'antivirus-required'
$antivirus=$false
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ($writes -eq 0 -and $nativeCalls -eq 0) 'The operation proceeded without antivirus.'
$passed.Add('Antivirus must be active')

Reset-Scenario 'realtime-required'
$realtime=$false
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ($writes -eq 0 -and $nativeCalls -eq 0) 'The operation proceeded without real-time protection.'
$passed.Add('Real-time protection must be active')

Reset-Scenario 'refresh-failure'
$refreshFails=$true
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
$failure=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
Require ($failure.Status -eq 'Failed' -and $failure.CurrentState -eq 0 -and $writes -eq 1) 'A partial failure was reported as success or silently re-enabled SAC.'
$passed.Add('A policy refresh failure records the partial change and fails')

Reset-Scenario 'enforced-policy'
$policyStaysEnforced=$true
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ((Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json).Status -eq 'Failed') 'A still-enforced SAC policy was reported as off.'
$passed.Add('Registry state alone is insufficient while SAC remains enforced')

Reset-Scenario 'already-off'
$fakeState=0
$result=Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false
Require ($result.Status -eq 'OffVerified' -and $writes -eq 0 -and $nativeCalls -eq 2) 'An already-off system had an unnecessary registry write or policy refresh.'
$passed.Add('Already-off systems are verified without a registry write or policy refresh')

Reset-Scenario 'timeout-off-verified'
$refreshTimesOut=$true
$result=Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false
Require ($result.Status -eq 'OffVerified' -and $result.RefreshTimedOut -and $fakeState -eq 0) 'An independently verified off state was not handled after a refresh timeout.'
$passed.Add('A refresh timeout is accepted only after independent verification that SAC is off')

Reset-Scenario 'timeout-still-enforced'
$refreshTimesOut=$true; $policyStaysEnforced=$true
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ((Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json).Status -eq 'Failed') 'A timeout with SAC still enforced was reported as success.'
$passed.Add('A refresh timeout with SAC still enforced stops the installation')

Reset-Scenario 'string-false'
$fakeState=0; $policyIsString=$true
$result=Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false
Require ($result.Status -eq 'OffVerified' -and $writes -eq 0) 'The string False was interpreted as an enforced policy.'
$passed.Add('CiTool string False is treated as inactive')

Reset-Scenario 'string-true'
$fakeState=0; $policyIsString=$true; $policyStaysEnforced=$true
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ((Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json).Status -eq 'Failed') 'The string True was interpreted as inactive.'
$passed.Add('CiTool string True is treated as enforced')

Reset-Scenario 'write-failure'
$writeFails=$true
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ($fakeState -eq 1 -and (Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json).Status -eq 'Failed') 'A write failure was reported incorrectly.'
$passed.Add('A rejected registry write fails and records the unchanged state')

Reset-Scenario 'unknown-state'
$fakeState=3
Expect-Failure { Disable-SoftKeyPrivateInstallSmartAppControl -ResultPath $journal -Confirm:$false }
Require ($writes -eq 0 -and $nativeCalls -eq 0) 'An unknown system state was changed.'
$passed.Add('Unknown SAC states are rejected before mutation')

foreach ($name in 'SoftKey.Installation.ps1','Install-Local-UIAccess.ps1','Sign-SoftKey.ps1') {
    $parseTokens=$null; $parseErrors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root $name),[ref]$parseTokens,[ref]$parseErrors)
    Require ($parseErrors.Count -eq 0) "PowerShell parsing failed: $name"
}
$passed.Add('All changed production scripts parse successfully')

foreach ($name in 'Install-Local-UIAccess.ps1','Sign-SoftKey.ps1') {
    $oldPreference=$ErrorActionPreference
    $ErrorActionPreference='Continue'
    $output=@(& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $root $name) -SignOnly -DisableSmartAppControl 2>&1)
    $guardExit=$LASTEXITCODE
    $ErrorActionPreference=$oldPreference
    Require ($guardExit -ne 0 -and ($output -join ' ') -match 'SignOnly cannot change Smart App Control') "SignOnly guard failed: $name"
}
$passed.Add('Both entry points reject changing SAC during sign-only preparation')

$realStateAfter=(Microsoft.PowerShell.Management\Get-ItemProperty -LiteralPath $policyPath).VerifiedAndReputablePolicyState
Require ($realStateAfter -eq $realStateBefore) 'The actual SAC state changed during the checks.'
[pscustomobject]@{Timestamp=(Get-Date).ToString('o');Passed=$passed.Count;Checks=@($passed.ToArray());ActualSACStateBefore=$realStateBefore;ActualSACStateAfter=$realStateAfter;ActualSecurityWritesPerformed=$false} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'obj\UIAccessTest\private-installer-check-result.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $root 'obj\UIAccessTest\private-installer-check-result.json')
