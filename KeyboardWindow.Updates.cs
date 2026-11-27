using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    readonly CancellationTokenSource updateCancellation = new();
    DispatcherTimer updateTimer;
    SoftKeyUpdateWindow updateWindow;
    SoftKeyUpdates.Preview availableUpdate;
    Task<SoftKeyUpdates.Preview> pendingUpdateCheck;

    internal void EnableUpdateChecks()
    {
        if (secureMode || !SoftKeyUpdates.CanInstall) return;
        updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(12) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(1);
            if (desktopSessionSuspended) return;
            try { await CheckForUpdateAsync(); }
            catch (Exception ex) { Trace.TraceWarning("SoftKey update check failed ({0}).", ex.GetType().Name); }
        };
        Closed += (_, _) =>
        {
            updateTimer.Stop();
            updateCancellation.Cancel();
            updateCancellation.Dispose();
        };
        updateTimer.Start();
    }

    async Task<SoftKeyUpdates.Preview> CheckForUpdateAsync()
    {
        if (pendingUpdateCheck?.IsCompleted != false)
            pendingUpdateCheck = SoftKeyUpdates.CheckAsync(updateCancellation.Token);
        var preview = await pendingUpdateCheck;
        availableUpdate = preview;
        RefreshUpdateIndicator();
        updateWindow?.SetAvailable(preview);
        return preview;
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
