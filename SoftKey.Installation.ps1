# Shared installation actions. Loading this file does not change Windows settings.
function Assert-SoftKeyInstallAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Administrator privileges are required for the private installation.'
    }
}

function Invoke-SoftKeyCiTool {
    param([ValidateSet('Refresh', 'List')][string]$Operation)
    $tool = Join-Path $env:SystemRoot 'System32\CiTool.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $tool
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'CiTool must have a valid Microsoft signature.'
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $tool
    $start.Arguments = if ($Operation -eq 'Refresh') { '-r' } else { '-lp -json' }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'CiTool could not be started.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(45000)) {
            if (-not $process.HasExited) { $process.Kill() }
            $null = $process.WaitForExit(5000)
            throw [TimeoutException]::new('CiTool did not finish within 45 seconds. The current SAC state must be checked before installation continues.')
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "CiTool $Operation failed (exit $($process.ExitCode)): $errorOutput" }
        if ($Operation -eq 'List') {
            $result = $output | ConvertFrom-Json
            if ($result.OperationResult -and $result.OperationResult -ne 0) { throw 'CiTool could not inspect the active policies.' }
            return $result
        }
    }
    finally { $process.Dispose() }
}

function Disable-SoftKeyPrivateInstallSmartAppControl {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([Parameter(Mandatory = $true)][string]$ResultPath)

    $policyPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
    $property = 'VerifiedAndReputablePolicyState'
    $before = (Get-ItemProperty -LiteralPath $policyPath -Name $property -ErrorAction Stop).$property
    if ($null -eq $before -or $before -notin 0, 1, 2) { throw 'The Smart App Control state is unknown.' }
    if (-not $PSCmdlet.ShouldProcess('Smart App Control for the entire computer', 'Turn off for the final private SoftKey installation')) {
        return [pscustomobject]@{ Status='WouldDisable'; PreviousState=$before; CurrentState=$before; Changed=$false }
    }

    Assert-SoftKeyInstallAdministrator
    $protection = Get-MpComputerStatus -ErrorAction Stop
    if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled) {
        throw 'Antivirus and real-time protection must remain enabled.'
    }
    # Inspect the policies before changing anything, including CiTool's signature.
    $initialPolicies = Invoke-SoftKeyCiTool -Operation List
    $initialActiveSac = @($initialPolicies.Policies | Where-Object {
        ([string]$_.PolicyID).Trim('{}') -ieq '0283ac0f-fff1-49ae-ada1-8a933130cad6' -and ([string]$_.IsEnforced -ieq 'True')
    })
    $record = [ordered]@{
        Timestamp=(Get-Date).ToString('o'); Status='Starting'; PreviousState=$before
        CurrentState=$before; Changed=$false; AppliesTo='EntireComputer'; RefreshTimedOut=$false; Error=$null
    }
    function Save-PrivateInstallSecurityResult {
        $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding UTF8 -ErrorAction Stop
    }
    Save-PrivateInstallSecurityResult
    try {
        if ($before -ne 0) {
            Set-ItemProperty -LiteralPath $policyPath -Name $property -Value 0 -Type DWord -ErrorAction Stop
            $record.Changed = $true
        }
        $record.CurrentState = (Get-ItemProperty -LiteralPath $policyPath -Name $property -ErrorAction Stop).$property
        if ($before -ne 0 -or $initialActiveSac.Count -gt 0) {
            $record.Status = 'UpdatingPolicy'
            Save-PrivateInstallSecurityResult
            try { Invoke-SoftKeyCiTool -Operation Refresh }
            catch [TimeoutException] {
                $record.RefreshTimedOut = $true
                Write-Warning 'Windows policyverktyg avslutades inte i tid. Installationen fortsätter bara om en separat kontroll bekräftar att SAC verkligen är av.'
            }
        }
        $record.CurrentState = (Get-ItemProperty -LiteralPath $policyPath -Name $property -ErrorAction Stop).$property
        $policies = Invoke-SoftKeyCiTool -Operation List
        $activeSac = @($policies.Policies | Where-Object {
            ([string]$_.PolicyID).Trim('{}') -ieq '0283ac0f-fff1-49ae-ada1-8a933130cad6' -and ([string]$_.IsEnforced -ieq 'True')
        })
        if ($record.CurrentState -ne 0 -or $activeSac.Count -gt 0) { throw 'Windows has not confirmed that Smart App Control is off.' }
        $protection = Get-MpComputerStatus -ErrorAction Stop
        if (-not $protection.AntivirusEnabled -or -not $protection.RealTimeProtectionEnabled) {
            throw 'Antivirus or real-time protection changed during the installation.'
        }
        $record.Status = 'OffVerified'
        Save-PrivateInstallSecurityResult
        return [pscustomobject]$record
    }
    catch {
        $record.Status = 'Failed'
        $record.Error = $_.Exception.Message
        try { $record.CurrentState = (Get-ItemProperty -LiteralPath $policyPath -Name $property -ErrorAction Stop).$property } catch { $record.CurrentState = $null }
        Save-PrivateInstallSecurityResult
        # An automatic re-enable cannot be promised on older Windows versions.
        throw
    }
}
