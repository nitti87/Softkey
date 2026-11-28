using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

static class DesktopCrashRecovery
{
    public const string RestartArgument = "--desktop-recovered";
    public const string WatchArgument = "--watch-desktop";
    const int MaxRestarts = 2;
    const long StableRunMs = 60_000;
    const long RecoveryPromptIntervalMs = 60_000;
    const uint WaitTimeout = 258;
    const uint RestartUnresponsiveExitCode = 1;
    static string HeartbeatName(int processId, long creationTime)
        => FormattableString.Invariant($@"Local\SoftKey.DesktopHeartbeat.v1.{processId}.{creationTime}");

    internal static bool TryReadAttempt(string value, out int attempt) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out attempt) && attempt is >= 1 and <= MaxRestarts;

    internal static int? NextAttempt(uint exitCode, int attempted, long runMs)
    {
        if (exitCode == 0) return null;
        if (runMs >= StableRunMs) attempted = 0;
        return attempted < MaxRestarts ? attempted + 1 : null;
    }

    public static EventWaitHandle StartWatching(int attempted)
    {
        if (!KeyboardLayoutSettings.IsInstalled) return null;
        using var current = Process.GetCurrentProcess();
        int processId = current.Id;
        long creationTime = current.StartTime.ToFileTimeUtc();
        EventWaitHandle heartbeat = null;
        try { heartbeat = new EventWaitHandle(false, EventResetMode.AutoReset, HeartbeatName(processId, creationTime)); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        { Trace.TraceWarning("SoftKey kunde inte skapa sin signal till kraschbevakaren ({0}).", ex.GetType().Name); }
        _ = Task.Run(() =>
        {
            try
            {
                string root = AppContext.BaseDirectory;
                var installation = new StartupInstallation(root, root);
                installation.Validate(login: false, automatic: false);
                var start = new ProcessStartInfo(installation.HelperExecutable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = root
                };
                start.ArgumentList.Add(WatchArgument);
                start.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(creationTime.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(attempted.ToString(CultureInfo.InvariantCulture));
                using var watcher = Process.Start(start);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                or InvalidOperationException or SecurityException or CryptographicException)
            {
                Trace.TraceWarning("SoftKeys kraschövervakning kunde inte starta ({0}).", ex.GetType().Name);
            }
        });
        return heartbeat;
    }

    public static int Watch(int processId, long creationTime, int attempted)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || DesktopContext.CurrentName != "Default") return 2;
        string sid = identity.User.Value;
        using var owner = new SingleInstance(FormattableString.Invariant($@"Local\SoftKey.DesktopRecovery.v1.{sid}.{processId}.{creationTime}"));
        if (!owner.IsOwner) return 0;
        string root = AppContext.BaseDirectory;
        var installation = new StartupInstallation(root, root);
        installation.Validate(login: false, automatic: true);
        using var process = Process.GetCurrentProcess();
        using var observed = OpenObservedProcess(processId, creationTime, process.SessionId, sid, installation.MainExecutable);
        if (observed == null) return 2;
        LogonDiagnostics.Record("desktop-recovery-watching");
        var run = Stopwatch.StartNew();
        using var heartbeat = OpenHeartbeat(processId, creationTime);
        if (heartbeat != null)
        {
            try { WatchForUnresponsiveDesktop(observed, heartbeat, processId, creationTime, process.SessionId, sid, installation, attempted, run); }
            catch (Exception ex) when (!SoftKeyResilience.IsFatal(ex))
            { Trace.TraceWarning("SoftKeys reservdialog kunde inte visas ({0}).", ex.GetType().Name); }
        }
        if (WaitForExit(observed) is not uint exitCode) return 1;
        if (exitCode == 0) return 0;
        if (NextAttempt(exitCode, attempted, run.ElapsedMilliseconds) is not int next)
        {
            LogonDiagnostics.Record("desktop-recovery-retry-limit");
            return 0;
        }
        if (LockSession.IsLocked != false || !DesktopContext.IsCurrentInputDesktop) return 0;
        // Let Windows release the previous input engine and keep immediate crashes from looping.
        Thread.Sleep(750);
        bool restarted = StartupLauncher.RestartCrashedDesktop(installation, sid, next);
        LogonDiagnostics.Record(restarted ? "desktop-recovery-available" : "desktop-recovery-unavailable");
        return restarted ? 0 : 1;
    }

    static EventWaitHandle OpenHeartbeat(int processId, long creationTime)
    {
        try
        {
            return EventWaitHandleAcl.TryOpenExisting(HeartbeatName(processId, creationTime), EventWaitHandleRights.Synchronize,
                out var heartbeat) ? heartbeat : null;
        }
        catch (UnauthorizedAccessException) { return null; }
    }

    static void WatchForUnresponsiveDesktop(SafeProcessHandle observed, EventWaitHandle heartbeat, int processId,
        long creationTime, int sessionId, string sid, StartupInstallation installation, int attempted, Stopwatch run)
    {
        var monitor = new DesktopHangMonitor();
        long nextOffer = 0;
        bool Unresponsive()
        {
            bool active = LockSession.IsLocked == false && DesktopContext.IsCurrentInputDesktop;
            bool hung = monitor.Observe(run.ElapsedMilliseconds, heartbeat.WaitOne(0), active);
            if (!hung) nextOffer = 0;
            return hung && WaitForSingleObject(observed, 0) == WaitTimeout;
        }

        bool CanRestart() => NextAttempt(RestartUnresponsiveExitCode, attempted, run.ElapsedMilliseconds).HasValue;
        bool ConfirmRestart()
        {
            if (!Unresponsive() || !CanRestart()) return false;
            using var restart = OpenObservedProcess(processId, creationTime, sessionId, sid, installation.MainExecutable, canTerminate: true);
            bool stopped = restart != null && Unresponsive() && TerminateProcess(restart, RestartUnresponsiveExitCode);
            LogonDiagnostics.Record(stopped ? "desktop-recovery-restart-confirmed" : "desktop-recovery-restart-failed");
            return stopped;
        }

        while (WaitForSingleObject(observed, 1000) == WaitTimeout)
        {
            if (!Unresponsive() || run.ElapsedMilliseconds < nextOffer || !CanRestart()) continue;
            LogonDiagnostics.Record("desktop-recovery-unresponsive");
            var timing = SoftKeyStorage.Default.Load("settings.json", new Settings());
            timing.Normalize();
            if (new DesktopRecoveryWindow(Unresponsive, ConfirmRestart, timing.DwellMs, timing.JitterPx).ShowDialog() == true)
                return;
            if (Unresponsive()) nextOffer = run.ElapsedMilliseconds + RecoveryPromptIntervalMs;
        }
    }

    internal static SafeProcessHandle OpenObservedProcess(int pid, long creationTime, int sessionId, string sid, string expectedPath,
        bool canTerminate = false)
    {
        const uint Synchronize = 0x100000, QueryLimitedInformation = 0x1000, TokenQuery = 8, ProcessTerminate = 1;
        uint access = Synchronize | QueryLimitedInformation;
        if (canTerminate) access |= ProcessTerminate;
        var handle = OpenProcess(access, false, pid);
        var image = new StringBuilder(32_768);
        uint length = (uint)image.Capacity;
        if (!handle.IsInvalid && GetProcessTimes(handle, out long created, out _, out _, out _) && created == creationTime
            && ProcessIdToSessionId(pid, out int session) && session == sessionId
            && QueryFullProcessImageName(handle, 0, image, ref length)
            && string.Equals(image.ToString(), expectedPath, StringComparison.OrdinalIgnoreCase)
            && OpenProcessToken(handle, TokenQuery, out var token))
        {
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                if (identity.User.Value == sid) return handle;
        }
        handle.Dispose();
        return null;
    }

    internal static uint? WaitForExit(SafeProcessHandle handle) =>
        WaitForSingleObject(handle, uint.MaxValue) == 0 && GetExitCodeProcess(handle, out uint exitCode) ? exitCode : null;

    [DllImport("kernel32.dll")] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int process, out int session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}

sealed class DesktopHangMonitor
{
    const long UnresponsiveMs = 30_000, MaxObservationGapMs = 3_000;
    long? lastObservation, lastResponse;

    public bool Observe(long now, bool heartbeat, bool active)
    {
        // Require a working desktop first and a continuous interval on that desktop.
        if (heartbeat) lastResponse = now;
        else if (lastResponse.HasValue && (!active || now - lastObservation > MaxObservationGapMs)) lastResponse = now;
        lastObservation = now;
        return active && lastResponse is long response && now - response >= UnresponsiveMs;
    }
}
