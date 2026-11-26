using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

static class LockedProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-secure-attention") return SecureAttention.Probe() ? 0 : 3;
        using var identity = WindowsIdentity.GetCurrent();
        bool uiAccess = GetTokenInformation(identity.AccessToken, 26, out int tokenUiAccess, sizeof(int), out _) && tokenUiAccess != 0;
        if (args.Length == 1 && args[0] == "--verify-uiaccess") return uiAccess ? 0 : 3;
        if (args.Length != 1 || args[0] != "--locked-desktop"
            || !LockSession.CanStartLockedKeyboard(identity.IsSystem, DesktopContext.CurrentName, LockSession.IsLocked)) return 2;
        if (!uiAccess) { LogonDiagnostics.Record("locked-keyboard-no-uiaccess"); return 3; }
        StartupPriority.Apply();
        try
        {
            using var instance = new SingleInstance(@"Local\SoftKey.Locked.v1." + identity.User.Value);
            if (!instance.IsOwner) return 0;
            bool InputAllowed() => DesktopContext.IsCurrentInputDesktop && LockSession.IsLocked == true;
            Native.RequireActiveDesktop = true;
            Native.AdditionalInputGuard = () => LockSession.IsLocked == true;
            var app = new Application();
            var window = new KeyboardWindow(null, true, LogonSettings.Load(AppContext.BaseDirectory));
            window.Title = "SoftKey – Låsskärm";
            window.SetInputGuard(InputAllowed);
            var display = new KeyboardStartupDisplay(window, () => LogonDiagnostics.Record("locked-keyboard-shown"));
            var monitor = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(250) };
            bool? lastActive = null;
            monitor.Tick += (_, _) =>
            {
                bool? locked = LockSession.IsLocked;
                bool? registered = LogonRegistration.ReadState();
                if (LockSession.ShouldStopLockedKeyboard(locked, registered))
                {
                    monitor.Stop();
                    LogonDiagnostics.Record("locked-keyboard-stopped");
                    window.CloseForDesktopTransition();
                    return;
                }
                bool active = locked == true && DesktopContext.IsCurrentInputDesktop;
                display.Observe(active && registered == true);
                if (active != lastActive)
                {
                    lastActive = active;
                    if (active) window.RefreshSignInSettings();
                    LogonDiagnostics.Record(active ? "locked-keyboard-active" : "locked-keyboard-inactive");
                }
            };
            window.Loaded += (_, _) => monitor.Start();
            app.Run(window);
            monitor.Stop();
            return 0;
        }
        catch (Exception ex)
        {
            LogonDiagnostics.Record("locked-keyboard-failed-" + ex.GetType().Name);
            return 1;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int information, out int value, int length, out int needed);
}
