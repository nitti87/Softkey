$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'SoftKey.Update.ps1')
$checks = 0
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; $script:checks++; Write-Output "PASS $message" }
foreach ($failure in @('none','desktop','backup','disable','logon','verify','restore')) {
    $events = [Collections.Generic.List[string]]::new()
    $steps = @{}
    foreach ($name in @('desktop','backup','disable','logon','verify','restore')) {
        $stepName = $name
        $steps[$name] = { $events.Add($stepName); if ($failure -eq $stepName -or ($failure -eq 'restore' -and $stepName -eq 'logon')) { throw $stepName } }.GetNewClosure()
    }
    $failed = $false; $message = ''
    try { Invoke-SoftKeyPairedUpdate $true $steps.desktop $steps.backup $steps.disable $steps.logon $steps.verify $steps.restore }
    catch { $failed = $true; $message = $_.Exception.Message }
    if ($failure -eq 'none') { Check (-not $failed -and ($events -join ',') -eq 'desktop,backup,disable,logon,verify') 'paired update uses one desktop package and verifies both installed variants' }
    elseif ($failure -eq 'desktop') { Check ($failed -and ($events -join ',') -eq 'desktop') 'desktop failure never disables sign-in' }
    elseif ($failure -eq 'backup') { Check ($failed -and ($events -join ',') -eq 'desktop,backup') 'unavailable recovery files stop before disabling sign-in' }
    elseif ($failure -eq 'restore') { Check ($failed -and $message -match 'recovery failed' -and $events[-1] -eq 'restore') 'recovery failure is reported separately without claiming completion' }
    else { Check ($failed -and $events[-1] -eq 'restore' -and $message -match 'previous sign-in installation was restored') ($failure + ' failure restores sign-in and still reports an incomplete paired update') }
}
$events = [Collections.Generic.List[string]]::new()
Invoke-SoftKeyPairedUpdate $false { $events.Add('desktop') } { throw 'backup' } { throw 'disable' } { throw 'logon' } { $events.Add('verify') } { throw 'restore' }
Check (($events -join ',') -eq 'desktop,verify') 'disabled login stays disabled and an integrated login needs no separate replacement'
foreach ($name in @('Update-SoftKey.ps1','SoftKey.Update.ps1','Install-SoftKeyLogonTest.ps1')) {
    $parseErrors = $null; $tokens = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path (Split-Path -Parent $PSScriptRoot) $name), [ref]$tokens, [ref]$parseErrors) | Out-Null
    Check ($parseErrors.Count -eq 0) ($name + ' parses in Windows PowerShell')
}
Write-Output "All $checks paired-update checks passed."
