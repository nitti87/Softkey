using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
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

    internal static bool TryReadAttempt(string value, out int attempt) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out attempt) && attempt is >= 1 and <= MaxRestarts;

    internal static int? NextAttempt(uint exitCode, int attempted, long runMs)
    {
        if (exitCode == 0) return null;
        if (runMs >= StableRunMs) attempted = 0;
        return attempted < MaxRestarts ? attempted + 1 : null;
    }

    public static void StartWatching(int attempted)
    {
        if (!KeyboardLayoutSettings.IsInstalled) return;
        _ = Task.Run(() =>
        {
            try
            {
                string root = AppContext.BaseDirectory;
                var installation = new StartupInstallation(root, root);
                installation.Validate(login: false, automatic: false);
                using var current = Process.GetCurrentProcess();
                var start = new ProcessStartInfo(installation.HelperExecutable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = root
                };
                start.ArgumentList.Add(WatchArgument);
                start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(current.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(attempted.ToString(CultureInfo.InvariantCulture));
                using var watcher = Process.Start(start);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                or InvalidOperationException or SecurityException or CryptographicException)
            {
                Trace.TraceWarning("SoftKeys kraschövervakning kunde inte starta ({0}).", ex.GetType().Name);
            }
        });
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

    internal static SafeProcessHandle OpenObservedProcess(int pid, long creationTime, int sessionId, string sid, string expectedPath)
    {
        const uint Synchronize = 0x100000, QueryLimitedInformation = 0x1000, TokenQuery = 8;
        var handle = OpenProcess(Synchronize | QueryLimitedInformation, false, pid);
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
}
