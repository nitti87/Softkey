using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

static class StartupLauncher
{
    public static int Run()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || !string.Equals(DesktopContext.CurrentName, "Default", StringComparison.OrdinalIgnoreCase)) return 2;
        using var instance = new SingleInstance(LaunchMutex(identity.User.Value));
        if (!instance.IsOwner) return 0;
        using var store = new WindowsStartupStore(identity.User.Value);
        var installation = StartupInstallation.Current(store);
        var configuration = new StartupConfiguration(store, installation, new WindowsStartupTasks(identity.User.Value), new WindowsStartupTasks(identity.User.Value, true));
        var brokerTask = new WindowsStartupTasks(identity.User.Value, true);
        var choices = configuration.Read();
        if (choices.Login && StartupTask.MatchesBroker(brokerTask.Read(), installation, identity.User.Value)) brokerTask.Run();
        var wait = Stopwatch.StartNew();
        long nextConfigurationCheck = 0, nextAttempt = 0, deadline = 10 * 60 * 1000;
        while (wait.ElapsedMilliseconds < deadline)
        {
            if (wait.ElapsedMilliseconds >= nextConfigurationCheck)
            {
                choices = configuration.Read();
                if (!choices.StartsOnDesktop) return 0;
                nextConfigurationCheck = wait.ElapsedMilliseconds + 1000;
            }
            if (wait.ElapsedMilliseconds >= nextAttempt && IsUnlockedDesktop())
            {
                // The broker and logon trigger can arrive together. The launch mutex remains held
                // until the app has initialized; its own mutex protects against manual starts too.
                // A fast unlock can arrive while the desktop app is still saving and closing.
                if (TryLaunchDesktop(installation, identity.User.Value, () => configuration.Read().StartsOnDesktop, choices.AfterUnlock ? 1000 : 0))
                {
                    LogonDiagnostics.Record("desktop-autostart-complete");
                    return 0;
                }
                // Windows can change the input desktop while the signed package is being verified.
                // Keep the task alive through that transition, then let Task Scheduler retry on failure.
                LogonDiagnostics.Record("desktop-autostart-retry");
                long now = wait.ElapsedMilliseconds;
                deadline = Math.Min(deadline, now + 15000);
                nextAttempt = now + 1000;
            }
            Thread.Sleep(100);
        }
        LogonDiagnostics.Record("desktop-autostart-timed-out");
        return 1;
    }

    static string LaunchMutex(string sid) => @"Local\SoftKey.Startup.Launch.v1." + sid;

    static bool IsUnlockedDesktop() => LockSession.IsLocked == false && DesktopContext.IsCurrentInputDesktop;

    static bool TryLaunchDesktop(StartupInstallation installation, string sid, Func<bool> enabled, int waitMilliseconds = 0,
        string arguments = "")
    {
        bool existing;
        using (var running = new SingleInstance(waitMilliseconds: waitMilliseconds)) existing = !running.IsOwner;
        if (!existing)
        {
            installation.Validate(false, true);
            if (!enabled() || !IsUnlockedDesktop() || !LaunchFromExplorer(installation, arguments)) return false;
        }
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 10000 && IsUnlockedDesktop())
        {
            if (DesktopStartupReady.IsSet(sid)) return true;
            if (existing)
            {
                using var running = new SingleInstance();
                if (running.IsOwner) return false;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    static bool LaunchFromExplorer(StartupInstallation installation, string arguments)
    {
        // Keep the keyboard independent of the scheduled task's process lifetime.
        object windows = null, desktop = null, view = null, shell = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true));
            object location = 0, root = 0;
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out int handle, 1); // SWC_DESKTOP, SWFO_NEEDDISPATCH
            if (desktop == null || handle == 0) return false;
            view = ((dynamic)desktop).Document;
            shell = ((dynamic)view).Application;
            ((dynamic)shell).ShellExecute(installation.MainExecutable, arguments, installation.Root, "open", 1);
            return true;
        }
        catch (COMException) { return false; }
        finally
        {
            foreach (var component in new[] { shell, view, desktop, windows })
                if (component != null) Marshal.ReleaseComObject(component);
        }
    }

    public static bool RestartCrashedDesktop(StartupInstallation installation, string sid, int attempt)
    {
        if (!IsUnlockedDesktop()) return false;
        // A preceding launch may still be waiting up to ten seconds for its ready signal.
        using var instance = new SingleInstance(LaunchMutex(sid), waitMilliseconds: 11_000);
        if (!instance.IsOwner || !IsUnlockedDesktop()) return false;
        string arguments = FormattableString.Invariant($"{DesktopCrashRecovery.RestartArgument} {attempt}");
        return TryLaunchDesktop(installation, sid, () => true, arguments: arguments);
    }

    // Only the signed-in user's broker calls this after a reported secure-keyboard visit.
    public static bool RestoreAfterSecureDesktop(string sid)
    {
        if (!IsUnlockedDesktop()) return false;
        using var instance = new SingleInstance(LaunchMutex(sid));
        if (!instance.IsOwner) return false;
        using var store = new WindowsStartupStore(sid);
        var brokerTask = new WindowsStartupTasks(sid, true);
        string xml = brokerTask.Read(), root = StartupTask.InstallationRoot(xml);
        if (root == null) return false;
        var installation = StartupInstallation.Current(store, root);
        var configuration = new StartupConfiguration(store, installation, new WindowsStartupTasks(sid), brokerTask);
        if (!StartupTask.MatchesBroker(xml, installation, sid)) return false;
        return TryLaunchDesktop(installation, sid, () => configuration.Read().Login);
    }

    // Called only by the normal user's Default-desktop broker, never by the SYSTEM keyboard.
    public static bool Handoff(string sid)
    {
        var tasks = new WindowsStartupTasks(sid);
        string xml = tasks.Read(), root = StartupTask.InstallationRoot(xml);
        if (root == null) return true; // Automatic startup is disabled.
        using var store = new WindowsStartupStore(sid);
        var installation = StartupInstallation.Current(store, root);
        if (StartupTask.ReadDesktop(xml, installation, sid)?.Automatic != true) return true;
        installation.Validate(false, true);
        if (!IsUnlockedDesktop()) return false;
        using var process = Process.Start(new ProcessStartInfo(installation.HelperExecutable)
        { Arguments = "--launch", UseShellExecute = true, WorkingDirectory = installation.Root, WindowStyle = ProcessWindowStyle.Hidden });
        return process != null;
    }
}

sealed class StartupHandoff
{
    bool completed;
    bool observedLock;
    long nextAttempt;
    public bool ShouldAttempt(bool? locked, bool active, long now)
    {
        if (locked == true) observedLock = true;
        if (locked == false && !observedLock) completed = true;
        if (completed || locked != false || !active || now < nextAttempt) return false;
        nextAttempt = now + 5000;
        return true;
    }
    public void Complete() => completed = true;
}
