using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    static readonly TimeSpan InitialUpdateDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(2);
    readonly CancellationTokenSource updateCancellation = new();
    Func<CancellationToken, Task<SoftKeyUpdates.Preview>> fetchUpdate;
    DispatcherTimer updateTimer;
    bool updateCheckDeferred;
    SoftKeyUpdateWindow updateWindow;
    SoftKeyUpdates.Preview availableUpdate;
    Task<SoftKeyUpdates.Preview> pendingUpdateCheck;

    internal void EnableUpdateChecks(Func<CancellationToken, Task<SoftKeyUpdates.Preview>> fetch = null)
    {
        if (secureMode || updateTimer != null || (fetch == null && !SoftKeyUpdates.CanInstall)) return;
        fetchUpdate = fetch ?? SoftKeyUpdates.CheckAsync;
        updateTimer = new DispatcherTimer(DispatcherPriority.Background);
        updateTimer.Tick += CheckForUpdateOnTimer;
        Closed += (_, _) =>
        {
            updateTimer.Stop();
            updateTimer.Tick -= CheckForUpdateOnTimer;
            updateCancellation.Cancel();
            updateCancellation.Dispose();
        };
        ScheduleUpdateCheck(InitialUpdateDelay);
    }

    void ScheduleUpdateCheck(TimeSpan delay)
    {
        updateCheckDeferred = desktopSessionSuspended;
        if (updateCheckDeferred) return;
        updateTimer.Interval = delay;
        updateTimer.Start();
    }

    void ResumeUpdateChecks()
    {
        if (updateCheckDeferred || updateTimer?.IsEnabled == true)
        {
            updateTimer.Stop();
            ScheduleUpdateCheck(InitialUpdateDelay);
        }
    }

    async void CheckForUpdateOnTimer(object sender, EventArgs e)
    {
        updateTimer.Stop();
        if (desktopSessionSuspended)
        {
            updateCheckDeferred = true;
            return;
        }
        try { await CheckForUpdateAsync(); }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested) { }
        catch (Exception ex) { Trace.TraceWarning("SoftKey update check failed ({0}).", ex.GetType().Name); }
    }

    Task<SoftKeyUpdates.Preview> CheckForUpdateAsync()
    {
        if (pendingUpdateCheck?.IsCompleted == false) return pendingUpdateCheck;
        updateTimer?.Stop();
        return pendingUpdateCheck = FetchAndDisplayUpdateAsync();
    }

    async Task<SoftKeyUpdates.Preview> FetchAndDisplayUpdateAsync()
    {
        var cancel = updateCancellation.Token;
        try
        {
            var preview = await (fetchUpdate ?? SoftKeyUpdates.CheckAsync)(cancel);
            cancel.ThrowIfCancellationRequested();
            availableUpdate = preview;
            RefreshUpdateIndicator();
            updateWindow?.SetAvailable(preview);
            return preview;
        }
        finally
        {
            if (!cancel.IsCancellationRequested && updateTimer != null) ScheduleUpdateCheck(UpdateCheckInterval);
        }
    }

    void OpenUpdateSettings()
    {
        if (secureMode || desktopSessionSuspended) return;
        if (updateWindow != null) { Native.KeepOnTop(new WindowInteropHelper(updateWindow).Handle); return; }
        updateWindow = new SoftKeyUpdateWindow(availableUpdate, CheckForUpdateAsync, async (preview, progress) =>
        {
            string package = await SoftKeyUpdates.DownloadAsync(preview, progress, updateCancellation.Token);
            if (desktopSessionSuspended || !DesktopContext.IsCurrentInputDesktop || !CancelMouseAction() || !ReleaseHeldModifiers() || !SaveSettings())
                throw new InvalidOperationException("Uppdatera från det aktiva Windows-skrivbordet.");
            progress.Report(new SoftKeyUpdates.Progress(59, "Väntar på Windows godkännande", "Bekräfta i Windows för att fortsätta installationen."));
            var dialog = updateWindow;
            await SoftKeyUpdates.StartInstallAsync(preview, package, dialog.Left, dialog.Top, Math.Max(280, dialog.ActualWidth - 18));
            _ = Dispatcher.BeginInvoke(new Action(CloseForDesktopTransition), DispatcherPriority.ApplicationIdle);
        }) { Owner = this, Topmost = cfg.AlwaysOnTop };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
    }
}
