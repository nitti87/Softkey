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
            window.EnableDesktopSessionTracking(startupReady: () =>
            {
                ready?.Set();
                LogonDiagnostics.Record("desktop-keyboard-shown");
            });
            Environment.ExitCode = app.Run(window);
        }
    }
}
