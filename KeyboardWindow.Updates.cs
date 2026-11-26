using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
            if (desktopSessionSuspended || !SoftKeyUpdates.HasCredential) return;
            try { availableUpdate = await CheckForUpdateAsync(); }
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

    Task<SoftKeyUpdates.Preview> CheckForUpdateAsync()
    {
        if (pendingUpdateCheck?.IsCompleted != false)
            pendingUpdateCheck = SoftKeyUpdates.CheckAsync(updateCancellation.Token);
        return pendingUpdateCheck;
    }

    void OpenUpdateSettings()
    {
        if (secureMode || desktopSessionSuspended) return;
        if (updateWindow != null) { updateWindow.Activate(); return; }
        updateWindow = new SoftKeyUpdateWindow(availableUpdate, async () => availableUpdate = await CheckForUpdateAsync(), async preview =>
        {
            if (desktopSessionSuspended || !DesktopContext.IsCurrentInputDesktop || !CancelMouseAction() || !ReleaseHeldModifiers() || !SaveSettings())
                throw new InvalidOperationException("Uppdatera från det aktiva Windows-skrivbordet.");
            await SoftKeyUpdates.StartInstallAsync(preview);
            _ = Dispatcher.BeginInvoke(new Action(CloseForDesktopTransition), DispatcherPriority.ApplicationIdle);
        }) { Owner = this };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
    }
}
