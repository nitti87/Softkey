using System;
using System.Security.Principal;
using System.Windows;

// Startpunkten: skapar programmet och öppnar tangentbordsfönstret.
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-secure-attention") { Environment.ExitCode = SecureAttention.Probe() ? 0 : 3; return; }
        if (args.Length == 2 && args[0] == "--verify-update-startup")
        {
            Environment.ExitCode = long.TryParse(args[1], out long build) && build > 0 && SoftKeyUpdates.BuildNumber == build
                && VerifyUpdateStartup() ? 0 : 3;
            return;
        }
        int recoveryAttempt = 0;
        if (args.Length != 0 && (args.Length != 2 || args[0] != DesktopCrashRecovery.RestartArgument
            || !DesktopCrashRecovery.TryReadAttempt(args[1], out recoveryAttempt))) { Environment.ExitCode = 2; return; }
        StartupPriority.Apply();
        SingleInstance instance;
        try
        {
            instance = new SingleInstance();
        }
        catch (UnauthorizedAccessException)
        {
            new SoftKeyNoticeWindow("SoftKey", "SoftKey kunde inte kontrollera om programmet redan körs. Stäng en eventuell befintlig instans och försök igen.").ShowDialog();
            return;
        }
        using (instance)
        {
            if (!instance.IsOwner) { SingleInstance.NotifyExisting(); return; }
            using var identity = WindowsIdentity.GetCurrent();
            using var ready = DesktopStartupReady.Create(identity.User.Value);
            DesktopCrashRecovery.StartWatching(recoveryAttempt);
            var app = new Application();
            var window = new KeyboardWindow();
            SoftKeyResilience.Attach(app, "desktop", window.RecoverFromTransientError);
            window.EnableUpdateChecks();
            window.EnableDesktopSessionTracking(startupReady: () =>
            {
                ready?.Set();
                LogonDiagnostics.Record("desktop-keyboard-shown");
            });
            Environment.ExitCode = app.Run(window);
        }
    }

    static bool VerifyUpdateStartup()
    {
        try
        {
            if (DesktopContext.IsSecure) return false;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var probe = new KeyboardWindow(null, true, new Settings { AutoClick = false });
            var content = (FrameworkElement)probe.Content;
            content.Measure(new Size(probe.Width, probe.Height));
            bool built = content.DesiredSize.Width > 0 && content.DesiredSize.Height > 0;
            app.Shutdown();
            return built;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name); return false; }
    }
}
