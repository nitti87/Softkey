function Show-SoftKeyInstallStep($Display, [double]$Percent, [string]$Status, [string]$Detail) {
    $Display.State.Snapshot = @{Percent=$Percent; Status=$Status; Detail=$Detail}
}

function Start-SoftKeyInstallProgress([string]$Content, [double]$Left, [double]$Top) {
    # The signed app supplies XAML generated from the same WPF controls as its download dialog.
    $state = [hashtable]::Synchronized(@{
        Ready=$false
        CloseAt=[DateTime]::MaxValue
        Snapshot=@{Percent=60; Status='Förbereder installationen'; Detail='Kontrollerar det signerade paketet.'}
    })
    $space = [runspacefactory]::CreateRunspace()
    $space.ApartmentState = 'STA'
    $space.ThreadOptions = 'ReuseThread'
    $space.Open()
    $ui = [PowerShell]::Create()
    $ui.Runspace = $space
    $null = $ui.AddScript({
        param($State, [string]$Content, [double]$Left, [double]$Top)
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName PresentationFramework
        $view = [Windows.Markup.XamlReader]::Parse($Content)
        $status = [Windows.LogicalTreeHelper]::FindLogicalNode($view, 'UpdateStatus')
        $detail = [Windows.LogicalTreeHelper]::FindLogicalNode($view, 'UpdateDetail')
        $bar = [Windows.LogicalTreeHelper]::FindLogicalNode($view, 'UpdateBar')
        if (-not $status -or -not $detail -or -not $bar) { throw 'The installation progress controls are missing.' }
        $window = [Windows.Window]::new()
        $window.Title = 'Uppdaterar SoftKey'
        $window.WindowStyle = 'None'
        $window.AllowsTransparency = $true
        $window.Background = [Windows.Media.Brushes]::Transparent
        $window.FontFamily = [Windows.Media.FontFamily]::new('Segoe UI')
        $window.ResizeMode = 'NoResize'
        $window.SizeToContent = 'WidthAndHeight'
        $window.WindowStartupLocation = 'Manual'
        $window.Left = $Left
        $window.Top = $Top
        $window.Content = $view
        $window.Add_Loaded({ $State.Ready = $true })
        $window.Add_Closing({
            param($sender, $event)
            if ($State.CloseAt -eq [DateTime]::MaxValue) { $event.Cancel = $true }
        })
        $timer = [Windows.Threading.DispatcherTimer]::new()
        $timer.Interval = [TimeSpan]::FromMilliseconds(90)
        $timer.Add_Tick({
            $snapshot = $State.Snapshot
            $status.Text = $snapshot.Status
            $detail.Text = $snapshot.Detail
            $bar.Value = $snapshot.Percent
            if ([DateTime]::UtcNow -ge $State.CloseAt) { $window.Close() }
        })
        $timer.Start()
        try { $null = $window.ShowDialog() } finally { $timer.Stop() }
    }).AddArgument($state).AddArgument($Content).AddArgument($Left).AddArgument($Top)
    $job = $ui.BeginInvoke()
    $limit = [Diagnostics.Stopwatch]::StartNew()
    while (-not $state.Ready -and -not $job.IsCompleted -and $limit.Elapsed.TotalSeconds -lt 8) {
        Start-Sleep -Milliseconds 40
    }
    if (-not $state.Ready) {
        $state.CloseAt = [DateTime]::UtcNow
        if ($job.AsyncWaitHandle.WaitOne(1000)) {
            try { $null = $ui.EndInvoke($job) } finally { $ui.Dispose(); $space.Dispose() }
        }
        throw 'Could not open the SoftKey installation progress window.'
    }
    [pscustomobject]@{State=$state; UI=$ui; Space=$space; Job=$job}
}

function Close-SoftKeyInstallProgress($Display, [bool]$Succeeded) {
    if ($Succeeded) { Show-SoftKeyInstallStep $Display 100 'Installationen är klar' 'Startar om SoftKey.' }
    else { Show-SoftKeyInstallStep $Display $Display.State.Snapshot.Percent 'Uppdateringen avbröts' 'Kontrollera SoftKey efter omstarten.' }
    $delay = if ($Succeeded) { 600 } else { 2500 }
    $Display.State.CloseAt = [DateTime]::UtcNow.AddMilliseconds($delay)
    if ($Display.Job.AsyncWaitHandle.WaitOne(4000)) {
        try { $null = $Display.UI.EndInvoke($Display.Job) }
        catch { Write-Warning 'The installation progress window ended unexpectedly.' }
        finally { $Display.UI.Dispose(); $Display.Space.Dispose() }
    }
}

function Invoke-SoftKeyInstallWithProgress([scriptblock]$Install) {
    $display = Start-SoftKeyInstallProgress $updateParameters.ProgressContent $updateParameters.Left $updateParameters.Top
    $succeeded = $false
    try {
        & $Install $display
        $succeeded = $true
    } finally { Close-SoftKeyInstallProgress $display $succeeded }
}
