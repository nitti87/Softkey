# These steps are tested with in-memory callbacks, without changing Windows.
function Invoke-SoftKeyPairedUpdate {
    param([bool]$SeparateLogon, [scriptblock]$Desktop, [scriptblock]$Backup,
        [scriptblock]$Disable, [scriptblock]$Logon, [scriptblock]$Verify, [scriptblock]$Restore)
    & $Desktop
    if (-not $SeparateLogon) { & $Verify; return }
    & $Backup
    try { & $Disable; & $Logon; & $Verify }
    catch {
        $failure = $_.Exception.Message
        try { & $Restore }
        catch { throw ($failure + ' Sign-in recovery failed: ' + $_.Exception.Message) }
        throw ($failure + ' The previous sign-in installation was restored; the paired update did not complete.')
    }
}
