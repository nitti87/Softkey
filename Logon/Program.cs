using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;

static class LogonProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-secure-attention") return SecureAttention.Probe() ? 0 : 3;
        bool preview = args.Length == 1 && args[0] == "--preview";
        bool registeredLaunch = args.Length == 1 && args[0] == "--ease-of-access";
        using var identity = WindowsIdentity.GetCurrent();
        // Only Windows may start the real sign-in keyboard. UIAccess alone does not grant this context.
        if (!preview && !registeredLaunch) return 2;
        if (registeredLaunch && !DesktopContext.IsSecure)
        {
            LockedDesktopBroker.Run();
            return 0;
        }
        if (!preview && !identity.IsSystem) return 2;
        if (preview && (identity.IsSystem || DesktopContext.IsSecure)) return 2;
        if (!preview) StartupPriority.Apply();
        try
        {
            using var instance = new SingleInstance(@"Local\SoftKey.Logon.v1." + identity.User.Value + "." + DesktopContext.CurrentName);
            if (!instance.IsOwner) return 0;
            if (!preview) LogonDiagnostics.Record("secure-keyboard-starting");
            var settings = LogonSettings.Load(AppContext.BaseDirectory);
            // Preview opens paused so it cannot compete with the user's existing dwell keyboard.
            if (preview) settings.AutoClick = false;
            var app = new Application();
            Native.RequireActiveDesktop = true;
            var window = new KeyboardWindow(null, true, settings);
            SoftKeyResilience.Attach(app, "secure", window.RecoverFromTransientError);
            if (!preview)
            {
                using var process = Process.GetCurrentProcess();
                int sessionId = process.SessionId;
                void ReportSecureKeyboardVisit(bool active)
                {
                    if (active && LockSession.IsLocked == false && !Native.IsWindowsConsentForeground)
                        LogonRegistration.RecordSecureKeyboardVisit(sessionId);
                }
                var display = new KeyboardStartupDisplay(window, () =>
                {
                    RecordSecureStart();
                    LogonDiagnostics.Record("secure-keyboard-shown");
                });
                bool? previousActive = null;
                var monitor = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                monitor.Tick += (_, _) =>
                {
                    bool? registered = LogonRegistration.ReadState();
                    if (registered == false)
                    {
                        monitor.Stop(); window.CloseForDesktopTransition(); return;
                    }
                    bool active = DesktopContext.IsCurrentInputDesktop;
                    display.Observe(active && registered == true);
                    if (active == previousActive) return;
                    previousActive = active;
                    if (active) window.RefreshSignInSettings();
                    ReportSecureKeyboardVisit(active);
                    LogonDiagnostics.Record(active ? "secure-keyboard-active" : "secure-keyboard-inactive");
                };
                window.Loaded += (_, _) =>
                {
                    bool active = DesktopContext.IsCurrentInputDesktop;
                    previousActive = active;
                    ReportSecureKeyboardVisit(active);
                    monitor.Start();
                };
                window.Closed += (_, _) => monitor.Stop();
            }
            if (preview)
            {
                window.Title = "SoftKey – Inloggning (förhandsvisning)";
                window.EnablePreviewClose();
            }
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            // Do not log exception messages, key events, buffers or credentials.
            Trace.WriteLine("SoftKey sign-in startup failed: " + ex.GetType().Name);
            if (!preview) LogonDiagnostics.Record("secure-keyboard-failed-" + ex.GetType().Name);
            return 1;
        }
    }

    static void RecordSecureStart()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\SoftKey_Logon_v1", true);
            // A timestamp proves startup for diagnostics. No key events, target text or credentials are recorded.
            key?.SetValue("LastSecureStartUtc", DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
    }
}
