using System;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    StartupSettingsWindow startupWin;
    void OpenStartupSettings()
    {
        if (secureMode || desktopSessionSuspended) return;
        if (startupWin != null) { Native.KeepOnTop(new WindowInteropHelper(startupWin).Handle); return; }
        IntPtr previous = Native.ForegroundWindow;
        ClearWordBuffer();
        if (!ReleaseHeldModifiers()) return;
        startupWin = new StartupSettingsWindow(new StartupClient(cfg)) { Owner = this, Topmost = cfg.AlwaysOnTop };
        startupWin.Closed += (_, _) =>
        {
            startupWin = null; SetMainWindowActionsAvailable(true);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Native.RestoreForegroundWindow(previous)));
        };
        SetMainWindowActionsAvailable(false);
        try { startupWin.Show(); }
        catch { startupWin = null; SetMainWindowActionsAvailable(true); throw; }
    }
}
