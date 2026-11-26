using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Threading;

// Runs only on the user's Default desktop. Windows owns the SYSTEM keyboard on Winlogon.
static class LockedDesktopBroker
{
    public static void Run()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || !string.Equals(DesktopContext.CurrentName, "Default", StringComparison.OrdinalIgnoreCase)) return;
        string userSid = identity.User.Value;
        using var brokerProcess = Process.GetCurrentProcess();
        int sessionId = brokerProcess.SessionId;
        using var store = new WindowsStartupStore(userSid);
        var installation = StartupInstallation.Current(store);
        string lockedDirectory = installation.LogonDirectory;
        using var instance = new SingleInstance(@"Local\SoftKey.Logon.Idle.v1." + userSid);
        if (!instance.IsOwner) return;
        Process child = null;
        long nextAttempt = 0;
        bool? previousLocked = null;
        bool? previousActive = null;
        var handoff = new StartupHandoff();
        Task<bool> handoffTask = null;
        var secureReturn = new SecureDesktopReturn(() => LogonRegistration.ReadSecureKeyboardVisit(sessionId));
        Task<bool> secureReturnLaunch = null;
        void Check()
        {
            bool? registered = LogonRegistration.ReadState();
            if (registered == false) { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); return; }
            if (registered == null) return;
            bool? locked = LockSession.IsLocked;
            bool active = DesktopContext.IsCurrentInputDesktop;
            if (locked != previousLocked)
            {
                previousLocked = locked;
                LogonDiagnostics.Record(locked == true ? "broker-session-locked" : locked == false ? "broker-session-unlocked" : "broker-session-unknown");
            }
            if (active != previousActive)
            {
                previousActive = active;
                LogonDiagnostics.Record(active ? "broker-default-active" : "broker-default-inactive");
            }
            if (handoffTask?.IsCompleted == true)
            {
                if (handoffTask.Status == TaskStatus.RanToCompletion && handoffTask.Result) handoff.Complete();
                else { _ = handoffTask.Exception; LogonDiagnostics.Record("desktop-handoff-retry"); }
                handoffTask = null;
            }
            if (handoffTask == null && handoff.ShouldAttempt(locked, active, Environment.TickCount64))
                handoffTask = Task.Run(() => StartupLauncher.Handoff(userSid));
            if (secureReturnLaunch?.IsCompleted == true)
            {
                if (secureReturnLaunch.Status == TaskStatus.RanToCompletion && secureReturnLaunch.Result)
                {
                    secureReturn.Complete();
                    LogonDiagnostics.Record("secure-return-desktop-restore-complete");
                }
                else if (secureReturnLaunch.Exception != null) LogonDiagnostics.Record("secure-return-desktop-restore-failed");
                secureReturnLaunch = null;
            }
            long now = Environment.TickCount64;
            if (secureReturn.ShouldAttempt(locked, active, now) && secureReturnLaunch == null)
            {
                secureReturn.Attempted(now);
                LogonDiagnostics.Record("secure-return-desktop-restore-requested");
                secureReturnLaunch = Task.Run(() => StartupLauncher.RestoreAfterSecureDesktop(userSid));
            }
            if (locked != true || !active) return;
            try
            {
                if (child != null && !child.HasExited) return;
                if (now < nextAttempt) return;
                nextAttempt = now + 5000;
                if (child != null) LogonDiagnostics.Record("locked-keyboard-exited-" + child.ExitCode);
                child?.Dispose();
                StartupTrust.ProtectedDirectory(lockedDirectory);
                StartupTrust.Verify(Path.Combine(lockedDirectory, "SoftKey.Locked.exe"));
                StartupTrust.Verify(Path.Combine(lockedDirectory, "SoftKey.Locked.dll"));
                child = Process.Start(new ProcessStartInfo(Path.Combine(lockedDirectory, "SoftKey.Locked.exe"))
                {
                    Arguments = "--locked-desktop", UseShellExecute = true, WorkingDirectory = lockedDirectory
                });
                LogonDiagnostics.Record("locked-keyboard-requested");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                child?.Dispose(); child = null;
                nextAttempt = Environment.TickCount64 + 5000;
                LogonDiagnostics.Record("locked-keyboard-launch-failed-" + ex.GetType().Name);
            }
        }
        LogonDiagnostics.Record("broker-started");
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Check();
        Check(); timer.Start();
        Dispatcher.Run();
        timer.Stop(); child?.Dispose();
    }
}

sealed class SecureDesktopReturn(Func<long?> readSecureVisit)
{
    long? handledVisit = readSecureVisit();
    long ignoreVisitsThrough, deadline, nextAttempt;

    public bool ShouldAttempt(bool? locked, bool active, long now)
    {
        if (locked != false)
        {
            ignoreVisitsThrough = now;
            Complete();
            return false;
        }
        if (!active) return false;
        // Windows can switch away and back between two broker checks. The protected
        // report from Winlogon is the evidence of the visit, even in that case.
        if (readSecureVisit() is long visit && visit != handledVisit && visit <= now)
        {
            handledVisit = visit;
            if (visit > ignoreVisitsThrough)
            {
                deadline = now + 7000;
                nextAttempt = 0;
            }
        }
        if (now >= deadline)
        {
            Complete();
            return false;
        }
        return now >= nextAttempt;
    }

    public void Attempted(long now) => nextAttempt = now + 1000;
    public void Complete()
    {
        deadline = nextAttempt = 0;
    }
}
