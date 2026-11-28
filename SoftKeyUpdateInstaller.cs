using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

// Native C# elevated updater executed by signed SoftKey.Startup.exe without spawning PowerShell.
static class SoftKeyUpdateInstaller
{
    public const string ApplyArgument = "--apply-update";
    public const string StagedArgument = "--apply-update-staged";

    const string RunnerPrefix = ".update-runner-";
    const string BackupDirectoryPrefix = "update-";
    const string PreviousBuildMarker = "previous-build";
    const string AccessibilityPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility";
    const string CiPolicyPath = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
    static readonly Regex CommitPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    static readonly string[] RunnerFiles =
    {
        "SoftKey.Startup.exe", "SoftKey.Startup.dll", "SoftKey.Startup.deps.json", "SoftKey.Startup.runtimeconfig.json"
    };
    static readonly Guid FolderIdLocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    sealed record InstallRequest(long BuildNumber, string Commit, long InstalledBuild,
        int DesktopPid, long DesktopStartedTicks, double Left, double Top, double Width, string CacheDirectory);

    sealed record StagedFile(string Name, string NextPath, string DestinationPath, string BackupPath, string ExpectedHash);

    public static int StageAndLaunch(string[] args)
    {
        if (args.Length != 9 || args[0] != ApplyArgument) return 2;
        if (!IsAdministrator()) return 3;
        if (!TryParseRequest(args, hasCache: false, out var request)) return 2;

        string main = ExpectedMainDirectory();
        string current = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(current, main, StringComparison.OrdinalIgnoreCase)) return 3;

        StartupTrust.ProtectedDirectory(main);
        StartupTrust.Verify(Path.Combine(main, "SoftKey.Startup.exe"));
        StartupTrust.Verify(Path.Combine(main, "SoftKey.Startup.dll"));

        CleanupOldRunners(main);

        string cacheDirectory = ResolveUserCacheDirectory(request.DesktopPid, request.DesktopStartedTicks, Path.Combine(main, "SoftKey.exe"));
        string runnerDir = Path.Combine(main, RunnerPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runnerDir);
        try
        {
            foreach (string file in RunnerFiles)
            {
                string source = Path.Combine(main, file);
                AssertNotReparsePoint(source);
                string target = Path.Combine(runnerDir, file);
                File.Copy(source, target, overwrite: false);
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    StartupTrust.Verify(target);
            }

            string stagedExe = Path.Combine(runnerDir, "SoftKey.Startup.exe");
            var start = new ProcessStartInfo(stagedExe)
            {
                UseShellExecute = false,
                WorkingDirectory = runnerDir
            };
            start.ArgumentList.Add(StagedArgument);
            start.ArgumentList.Add(request.BuildNumber.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.Commit);
            start.ArgumentList.Add(request.InstalledBuild.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.DesktopPid.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.DesktopStartedTicks.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.Left.ToString("R", CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.Top.ToString("R", CultureInfo.InvariantCulture));
            start.ArgumentList.Add(request.Width.ToString("R", CultureInfo.InvariantCulture));
            start.ArgumentList.Add(cacheDirectory);

            using var launched = Process.Start(start);
            return launched != null ? 0 : 1;
        }
        catch
        {
            TryDeleteDirectory(runnerDir, main);
            throw;
        }
    }

    public static int RunStaged(string[] args)
    {
        if (args.Length != 10 || args[0] != StagedArgument) return 2;
        if (!IsAdministrator()) return 3;
        if (!TryParseRequest(args, hasCache: true, out var request)) return 2;

        string main = ExpectedMainDirectory();
        string current = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var currentInfo = new DirectoryInfo(current);
        if (currentInfo.Parent == null
            || !string.Equals(currentInfo.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar), main, StringComparison.OrdinalIgnoreCase)
            || !currentInfo.Name.StartsWith(RunnerPrefix, StringComparison.OrdinalIgnoreCase)
            || (currentInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return 3;
        }

        StartupTrust.ProtectedDirectory(main);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var progressView = new SoftKeyUpdateProgressView();
        progressView.Show(new SoftKeyUpdates.Progress(60, "Förbereder installationen", "Kontrollerar det signerade paketet."));
        double width = Math.Clamp(request.Width, 280, SoftKeySettingsDesign.Width);
        var body = new Border { Width = width, Child = progressView.Content, Padding = new Thickness(16) };
        var window = new Window
        {
            Title = "Uppdaterar SoftKey",
            Left = request.Left,
            Top = request.Top,
            Topmost = true
        };
        SoftKeyPopupDesign.ConfigureWindow(window);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Content = SoftKeyPopupDesign.Frame(window.Title, body, null, null);

        bool allowClose = false;
        window.Closing += (_, e) => { if (!allowClose) e.Cancel = true; };

        void Report(double percent, string status, string detail)
        {
            if (window.Dispatcher.HasShutdownStarted) return;
            window.Dispatcher.BeginInvoke(new Action(() =>
                progressView.Show(new SoftKeyUpdates.Progress(percent, status, detail))));
        }

        int exitCode = 1;
        window.Loaded += (_, _) =>
        {
            _ = Task.Run(() =>
            {
                bool succeeded = false;
                string step = "init";
                try
                {
                    ApplyUpdateCore(main, request, Report, s => step = s);
                    TryDeleteFile(Path.Combine(main, "update-install-error.json"));
                    succeeded = true;
                    exitCode = 0;
                    Report(100, "Installationen är klar", "Startar om SoftKey.");
                    Thread.Sleep(600);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("SoftKey update installation failed at {0} ({1}).", step, ex.GetType().Name);
                    WriteInstallError(main, request.BuildNumber, step, ex);
                    Report(60, "Uppdateringen kunde inte slutföras", $"Återställde tidigare version ({step}). Startar SoftKey igen.");
                    Thread.Sleep(3500);
                }
                finally
                {
                    RelaunchDesktop(main);
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        allowClose = true;
                        window.Close();
                        app.Shutdown(succeeded ? 0 : 1);
                    }));
                }
            });
        };

        window.Show();
        app.Run();
        return exitCode;
    }

    static void ApplyUpdateCore(string main, InstallRequest request, Action<double, string, string> report, Action<string> setStep)
    {
        string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string logon = Path.Combine(programFiles, "SoftKey-Logon-Test");
        string desktopExe = Path.Combine(main, "SoftKey.exe");
        string installedVersionPath = Path.Combine(main, "installed-update-build.txt");

        setStep("verify-paths");
        AssertNotReparsePoint(programFiles);
        AssertNotReparsePoint(main);
        AssertNotReparsePoint(desktopExe);
        StartupTrust.Verify(desktopExe);

        setStep("verify-protection-before");
        int initialSac = ReadSmartAppControlState();
        AssertWindowsProtection(initialSac);

        Process oldDesktop = null;
        try
        {
            setStep("check-desktop-process");
            oldDesktop = TryOpenDesktopProcess(request.DesktopPid, request.DesktopStartedTicks, desktopExe);

            setStep("read-logon-registration");
            var (logonEnabled, separateLogon) = ReadLogonRegistration(main, logon);
            if (separateLogon) AssertNotReparsePoint(logon);

            setStep("acquire-install-lock");
            using var installLock = new FileStream(Path.Combine(main, ".install.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);

            string stage = Path.Combine(main, BackupDirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var replaced = new List<StagedFile>();
            var pausedBrokers = new List<string>();
            bool completed = false;
            bool retainRecovery = false;
            long installedBuild = request.InstalledBuild;

            try
            {
                setStep("verify-archive");
                string packageName = FormattableString.Invariant($"SoftKey-update-{request.BuildNumber}.zip");
                report(62, "Verifierar uppdateringen", packageName);

                if (File.Exists(installedVersionPath))
                {
                    AssertNotReparsePoint(installedVersionPath);
                    if (long.TryParse(File.ReadAllText(installedVersionPath).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long diskBuild))
                        installedBuild = Math.Max(installedBuild, diskBuild);
                }

                string cacheDir = Path.GetFullPath(request.CacheDirectory);
                string sourcePackage = Path.Combine(cacheDir, packageName);
                AssertNotReparsePoint(Path.GetDirectoryName(cacheDir));
                AssertNotReparsePoint(cacheDir);
                AssertNotReparsePoint(sourcePackage);

                string stageArchive = Path.Combine(stage, "package.zip");
                using (var input = new FileStream(sourcePackage, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length < 1 || input.Length > 64L * 1024 * 1024)
                        throw new InvalidDataException("Uppdateringspaketets storlek är ogiltig.");
                    using var copy = new FileStream(stageArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    input.CopyTo(copy);
                }

                using var trustedCert = SoftKeyUpdateArchive.LoadTrustedCertificate();
                var manifest = SoftKeyUpdateArchive.ReadVerified(stageArchive, trustedCert, installedBuild);
                if (manifest.BuildNumber != request.BuildNumber || !string.Equals(manifest.Commit, request.Commit, StringComparison.Ordinal))
                    throw new InvalidOperationException("Det valda uppdateringspaketet har ändrats.");

                setStep("extract-and-verify");
                report(65, "Förbereder programfilerna", "Kontrollerar SoftKeys signerade programdelar.");
                var files = new List<StagedFile>();
                using (var archiveStream = new FileStream(stageArchive, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
                {
                    foreach (var record in manifest.Files)
                    {
                        bool isLogonComponent = record.Name.StartsWith("SoftKey.Logon.", StringComparison.Ordinal)
                            || record.Name.StartsWith("SoftKey.Locked.", StringComparison.Ordinal);
                        var targets = separateLogon && isLogonComponent ? new[] { main, logon } : new[] { main };
                        foreach (string targetDir in targets)
                        {
                            string destination = Path.Combine(targetDir, record.Name);
                            AssertNotReparsePoint(destination);
                            int index = files.Count;
                            string next = Path.Combine(stage, $"{index}-{record.Name}");
                            string backup = Path.Combine(stage, $"{index}.backup");
                            var entry = zip.GetEntry(record.Name) ?? throw new InvalidDataException("Saknad fil i paketet.");
                            using (var src = entry.Open())
                            using (var dst = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                src.CopyTo(dst);

                            if (record.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                || record.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                StartupTrust.Verify(next);
                                StartupTrust.Verify(destination);
                            }
                            files.Add(new StagedFile(record.Name, next, destination, backup, record.SHA256));
                        }
                    }
                }

                setStep("wait-desktop-exit");
                report(70, "Förbereder installationen", "Väntar på att SoftKey stängs.");
                if (oldDesktop != null && !oldDesktop.WaitForExit(20000))
                    throw new TimeoutException("Skrivbordstangentbordet hann inte stängas före uppdateringen.");

                setStep("stop-brokers");
                pausedBrokers.AddRange(StopRunningLoginBrokers(Path.Combine(main, "SoftKey.Startup.exe")));
                setStep("wait-processes");
                WaitForSoftKeyProcessesToExit(Environment.ProcessId, TimeSpan.FromSeconds(10));

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    setStep($"replace-{file.Name}");
                    report(72 + 21.0 * (i + 1) / files.Count, "Installerar programfiler", $"{file.Name}  ·  {i + 1} av {files.Count}");
                    ReplaceFileWithRetry(file.NextPath, file.DestinationPath, file.BackupPath);
                    replaced.Add(file);
                }

                setStep("verify-installed-files");
                report(94, "Kontrollerar installationen", "Verifierar programfilerna och provar programstarten.");
                foreach (var file in files)
                {
                    string actualHash = ComputeFileSha256(file.DestinationPath);
                    if (!string.Equals(actualHash, file.ExpectedHash, StringComparison.Ordinal))
                        throw new InvalidOperationException("En installerad uppdateringsfil har ändrats.");
                    if (file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        StartupTrust.Verify(file.DestinationPath);
                    }
                }

                setStep("probe-desktop");
                RunStartupProbe(desktopExe, new[] { "--verify-update-startup", manifest.BuildNumber.ToString(CultureInfo.InvariantCulture) }, expectedExitCode: 0);
                if (logonEnabled)
                {
                    string signInRoot = separateLogon ? logon : main;
                    setStep("probe-logon");
                    RunStartupProbe(Path.Combine(signInRoot, "SoftKey.Logon.exe"), new[] { "--verify-update-boundary" }, expectedExitCode: 2);
                    setStep("probe-locked");
                    RunStartupProbe(Path.Combine(signInRoot, "SoftKey.Locked.exe"), new[] { "--verify-uiaccess" }, expectedExitCode: 0);
                }

                setStep("verify-protection-after");
                AssertWindowsProtection(initialSac);
                setStep("restart-brokers");
                RestartLoginBrokers(pausedBrokers);
                pausedBrokers.Clear();

                setStep("write-installed-build");
                string nextVersion = Path.Combine(stage, "build-number");
                File.WriteAllText(nextVersion, manifest.BuildNumber.ToString(CultureInfo.InvariantCulture));
                if (File.Exists(installedVersionPath)) ReplaceFileWithRetry(nextVersion, installedVersionPath, Path.Combine(stage, PreviousBuildMarker));
                else File.Move(nextVersion, installedVersionPath);
                completed = true;
            }
            catch
            {
                if (replaced.Count > 0)
                    report(85, "Återställer tidigare programfiler", "Uppdateringen kunde inte slutföras.");
                for (int i = replaced.Count - 1; i >= 0; i--)
                {
                    var file = replaced[i];
                    try { ReplaceFileWithRetry(file.BackupPath, file.DestinationPath, null); }
                    catch { retainRecovery = true; }
                }
                throw;
            }
            finally
            {
                if (pausedBrokers.Count > 0)
                {
                    try { RestartLoginBrokers(pausedBrokers); } catch { }
                }
                if (!completed && !retainRecovery)
                    TryDeleteDirectory(stage, main);
            }

            // Keep the installation lock while pruning. Cleanup is outside the rollback path.
            PruneCompletedBackups(main, stage, installedBuild, replaced.Count);
        }
        finally
        {
            oldDesktop?.Dispose();
        }
    }

    static bool TryParseRequest(string[] args, bool hasCache, out InstallRequest request)
    {
        request = null;
        if (!long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out long build) || build < 1
            || !CommitPattern.IsMatch(args[2])
            || !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long installedBuild) || installedBuild < 0
            || !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int desktopPid) || desktopPid <= 0
            || !long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out long desktopStartedTicks) || desktopStartedTicks <= 0
            || !double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double left) || double.IsNaN(left) || double.IsInfinity(left)
            || !double.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double top) || double.IsNaN(top) || double.IsInfinity(top)
            || !double.TryParse(args[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) || double.IsNaN(width) || double.IsInfinity(width))
            return false;

        string cache = hasCache ? args[9] : null;
        if (hasCache && string.IsNullOrWhiteSpace(cache)) return false;
        request = new InstallRequest(build, args[2], installedBuild, desktopPid, desktopStartedTicks, left, top, width, cache);
        return true;
    }

    static string ExpectedMainDirectory() =>
        Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SoftKey-UIAccess-Test"))
            .TrimEnd(Path.DirectorySeparatorChar);

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void AssertNotReparsePoint(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Ogiltig sökväg.");
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("En installationssökväg får inte vara en länk.");
    }

    static int ReadSmartAppControlState()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(CiPolicyPath, writable: false);
        if (key?.GetValue("VerifiedAndReputablePolicyState") is int state && state is 0 or 1 or 2)
            return state;
        throw new InvalidOperationException("Smart App Control-läget kunde inte kontrolleras.");
    }

    static void AssertWindowsProtection(int expectedSac)
    {
        int currentSac = ReadSmartAppControlState();
        if (currentSac != expectedSac)
            throw new InvalidOperationException("Smart App Control-läget ändrades under uppdateringen.");

        Exception last = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            object locator = null, service = null, results = null;
            try
            {
                locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true));
                service = ((dynamic)locator).ConnectServer(".", @"root\Microsoft\Windows\Defender");
                results = ((dynamic)service).ExecQuery("SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
                bool verified = false;
                foreach (var item in (System.Collections.IEnumerable)results)
                {
                    try
                    {
                        bool av = Convert.ToBoolean(((dynamic)item).Properties_.Item("AntivirusEnabled").Value, CultureInfo.InvariantCulture);
                        bool rtp = Convert.ToBoolean(((dynamic)item).Properties_.Item("RealTimeProtectionEnabled").Value, CultureInfo.InvariantCulture);
                        if (!av || !rtp) throw new InvalidOperationException("Microsoft Defender antivirus eller realtidsskydd är inte aktivt.");
                        verified = true;
                    }
                    finally { if (Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
                }
                if (!verified) throw new InvalidOperationException("Windows-skyddet kunde inte verifieras.");
                return;
            }
            catch (COMException ex) when (attempt == 0)
            {
                last = ex;
                Thread.Sleep(300);
            }
            finally
            {
                foreach (var com in new[] { results, service, locator })
                    if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
            }
        }
        throw last ?? new InvalidOperationException("Windows-skyddet kunde inte verifieras.");
    }

    static Process TryOpenDesktopProcess(int pid, long startedTicks, string expectedExe)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
        try
        {
            if (process.HasExited) { process.Dispose(); return null; }
            if (!string.Equals(process.MainModule.FileName, expectedExe, StringComparison.OrdinalIgnoreCase)
                || process.StartTime.ToUniversalTime().Ticks != startedTicks)
            {
                process.Dispose();
                throw new InvalidOperationException("Skrivbordsprocessen ändrades innan uppdateringen kunde börja.");
            }
            return process;
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            process.Dispose();
            return null;
        }
    }

    static string ResolveUserCacheDirectory(int desktopPid, long startedTicks, string expectedExe)
    {
        try
        {
            using var desktop = TryOpenDesktopProcess(desktopPid, startedTicks, expectedExe);
            if (desktop != null && OpenProcessToken(desktop.Handle, 0x0008 | 0x0004, out var token))
            {
                using (token)
                {
                    if (SHGetKnownFolderPath(FolderIdLocalAppData, 0, token.DangerousGetHandle(), out IntPtr pathPtr) == 0 && pathPtr != IntPtr.Zero)
                    {
                        try
                        {
                            string localAppData = Marshal.PtrToStringUni(pathPtr);
                            if (!string.IsNullOrWhiteSpace(localAppData))
                                return Path.Combine(localAppData, "SoftKey", "Updates");
                        }
                        finally { Marshal.FreeCoTaskMem(pathPtr); }
                    }
                }
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftKey", "Updates");
    }

    static (bool Enabled, bool Separate) ReadLogonRegistration(string main, string logon)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var acc = hklm.OpenSubKey(AccessibilityPath, writable: false);
        string config = acc?.GetValue("Configuration") as string ?? "";
        bool enabled = config.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Any(item => string.Equals(item.Trim(), "SoftKey_Logon_v1", StringComparison.OrdinalIgnoreCase));
        if (!enabled) return (false, false);

        using var at = hklm.OpenSubKey(AccessibilityPath + @"\ATs\SoftKey_Logon_v1", writable: false);
        string startExe = at?.GetValue("StartExe") as string ?? "";
        string mainLogon = Path.Combine(main, "SoftKey.Logon.exe");
        string separateLogon = Path.Combine(logon, "SoftKey.Logon.exe");
        if (string.Equals(startExe, mainLogon, StringComparison.OrdinalIgnoreCase)) return (true, false);
        if (string.Equals(startExe, separateLogon, StringComparison.OrdinalIgnoreCase)) return (true, true);
        throw new InvalidOperationException("Den registrerade inloggningsvägen känns inte igen.");
    }

    static List<string> StopRunningLoginBrokers(string expectedBrokerExe)
    {
        var stopped = new List<string>();
        object service = null, folder = null, tasks = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            tasks = ((dynamic)folder).GetTasks(0);
            foreach (var task in (System.Collections.IEnumerable)tasks)
            {
                try
                {
                    string name = (string)((dynamic)task).Name;
                    int state = (int)((dynamic)task).State; // 4 = TASK_STATE_RUNNING
                    if (!name.StartsWith("SoftKey-LoginBroker-", StringComparison.OrdinalIgnoreCase) || state != 4) continue;
                    string xml = (string)((dynamic)task).Xml;
                    string root = StartupTask.InstallationRoot(xml);
                    if (root == null || !string.Equals(Path.Combine(root, "SoftKey.Startup.exe"), expectedBrokerExe, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Den körande låsskärmsuppgiften matchar inte SoftKey.");
                    if (stopped.Count > 0)
                        throw new InvalidOperationException("Fler än en inloggningsbroker körs samtidigt.");
                    ((dynamic)task).Stop(0);
                    var stopWait = Stopwatch.StartNew();
                    while ((int)((dynamic)task).State == 4 && stopWait.Elapsed < TimeSpan.FromSeconds(5))
                        Thread.Sleep(100);
                    stopped.Add(name);
                }
                finally { if (Marshal.IsComObject(task)) Marshal.FinalReleaseComObject(task); }
            }
        }
        finally
        {
            foreach (var com in new[] { tasks, folder, service })
                if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
        return stopped;
    }

    static void RestartLoginBrokers(IEnumerable<string> taskNames)
    {
        object service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            foreach (string name in taskNames)
            {
                object task = null, running = null;
                try
                {
                    task = ((dynamic)folder).GetTask(name);
                    running = ((dynamic)task).Run(null);
                }
                finally
                {
                    if (running != null && Marshal.IsComObject(running)) Marshal.FinalReleaseComObject(running);
                    if (task != null && Marshal.IsComObject(task)) Marshal.FinalReleaseComObject(task);
                }
            }
        }
        finally
        {
            foreach (var com in new[] { folder, service })
                if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
    }

    static void WaitForSoftKeyProcessesToExit(int selfPid, TimeSpan timeout)
    {
        string[] names = { "SoftKey", "SoftKey.Startup", "SoftKey.Logon", "SoftKey.Locked" };
        var watch = Stopwatch.StartNew();
        while (true)
        {
            bool any = false;
            foreach (string name in names)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (process.Id != selfPid && !process.HasExited) any = true;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // A running protected/UIAccess/SYSTEM SoftKey process may reject HasExited queries.
                        any = true;
                    }
                    catch (InvalidOperationException) { }
                    catch (ArgumentException) { }
                    finally { process.Dispose(); }
                }
            }
            if (!any) return;
            if (watch.Elapsed >= timeout)
                throw new TimeoutException("SoftKey är fortfarande aktivt. Försök uppdatera igen.");
            Thread.Sleep(150);
        }
    }

    internal static void ReplaceFileWithRetry(string sourcePath, string destinationPath, string backupPath)
    {
        const int maxAttempts = 15;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, backupPath);
                return;
            }
            catch (Exception ex) when (attempt + 1 < maxAttempts && (ex is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(Math.Min(250, 75 + attempt * 25));
            }
        }
    }

    static void RunStartupProbe(string executable, string[] arguments, int expectedExitCode)
    {
        // UseShellExecute = true uses ShellExecuteEx so Windows AppInfo grants uiAccess="true" tokens.
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable),
            Arguments = string.Join(" ", arguments)
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Kunde inte starta kontrollprocessen.");
        if (!process.WaitForExit(12000))
        {
            try { process.Kill(); process.WaitForExit(); } catch { }
            throw new TimeoutException("Startkontrollen för den nya versionen avslutades inte i tid.");
        }
        if (process.ExitCode != expectedExitCode)
            throw new InvalidOperationException($"Startkontrollen misslyckades för {Path.GetFileName(executable)} (kod {process.ExitCode}, förväntade {expectedExitCode}).");
    }

    static void WriteInstallError(string main, long buildNumber, string step, Exception ex)
    {
        try
        {
            // Installer-only diagnostics: contains no typed text, personal words or credentials.
            string path = Path.Combine(main, "update-install-error.json");
            string json = JsonSerializer.Serialize(new
            {
                TimeUtc = DateTime.UtcNow.ToString("o"),
                BuildNumber = buildNumber,
                Step = step,
                Type = ex.GetType().Name,
                HResult = ex.HResult,
                Message = ex.Message
            });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    static void RelaunchDesktop(string main)
    {
        try
        {
            if (Process.GetProcessesByName("SoftKey").Length > 0) return;
            var installation = new StartupInstallation(main, main);
            if (!StartupLauncher.LaunchFromExplorer(installation, ""))
            {
                Process.Start(new ProcessStartInfo(installation.MainExecutable)
                { UseShellExecute = true, WorkingDirectory = main });
            }
        }
        catch { }
    }

    static void CleanupOldRunners(string main)
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(main, RunnerPrefix + "*"))
                TryDeleteDirectory(dir, main);
        }
        catch { }
    }

    internal static void PruneCompletedBackups(string main, string retainedPath, long previousBuild, int expectedBackupCount)
    {
        try
        {
            var root = new DirectoryInfo(main);
            var retained = new DirectoryInfo(retainedPath);
            if (previousBuild < 0 || expectedBackupCount < SoftKeyUpdateArchive.ExpectedFiles.Length
                || (root.Attributes & FileAttributes.ReparsePoint) != 0
                || !IsBackupDirectory(retained, root)) return;

            var retainedFiles = retained.GetFileSystemInfos();
            if (!retainedFiles.All(IsKnownBackupFile) || !retainedFiles.Any(file => file.Name == "package.zip")
                || retainedFiles.Count(file => file.Extension == ".backup") != expectedBackupCount) return;

            string markerPath = Path.Combine(retained.FullName, PreviousBuildMarker);
            // A first installation may have had no version stamp for File.Replace to back up.
            if (!File.Exists(markerPath))
            {
                using var marker = new StreamWriter(new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None));
                marker.Write(previousBuild.ToString(CultureInfo.InvariantCulture));
            }
            if (!TryReadBackupVersion(new FileInfo(markerPath), out long retainedBuild) || retainedBuild != previousBuild) return;

            foreach (var backup in root.EnumerateDirectories(BackupDirectoryPrefix + "*"))
            {
                try
                {
                    if (!IsBackupDirectory(backup, root)
                        || string.Equals(backup.FullName, retained.FullName, StringComparison.OrdinalIgnoreCase)) continue;

                    var marker = new FileInfo(Path.Combine(backup.FullName, PreviousBuildMarker));
                    if (!TryReadBackupVersion(marker, out long oldBuild) || oldBuild >= previousBuild) continue;

                    // Completed stages are flat. Keep recovery folders, links and unfamiliar files.
                    var entries = backup.GetFileSystemInfos();
                    if (!entries.All(IsKnownBackupFile)) continue;
                    foreach (var file in entries)
                        if (file.Name != PreviousBuildMarker) File.Delete(file.FullName);
                    // Leave the marker until the end so a locked file can be retried on the next update.
                    marker.Delete();
                    backup.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("SoftKey backup cleanup skipped ({0}).", ex.GetType().Name);
        }
    }

    static bool IsBackupDirectory(DirectoryInfo directory, DirectoryInfo root) =>
        directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0
        && string.Equals(directory.Parent?.FullName, root.FullName.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        && directory.Name.StartsWith(BackupDirectoryPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(directory.Name.AsSpan(BackupDirectoryPrefix.Length), "N", out _);

    static bool TryReadBackupVersion(FileInfo marker, out long build)
    {
        build = 0;
        return marker.Exists && (marker.Attributes & FileAttributes.ReparsePoint) == 0 && marker.Length <= 32
            && long.TryParse(File.ReadAllText(marker.FullName).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out build);
    }

    static bool IsKnownBackupFile(FileSystemInfo entry) =>
        entry is FileInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0
        && (entry.Name is "package.zip" or PreviousBuildMarker
            || (entry.Extension == ".backup"
                && int.TryParse(entry.Name.AsSpan(0, entry.Name.Length - entry.Extension.Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out _)));

    static void TryDeleteDirectory(string path, string expectedParent)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            var info = new DirectoryInfo(path);
            if (info.Parent != null
                && string.Equals(info.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar), expectedParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && (info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Directory.Delete(info.FullName, recursive: true);
            }
        }
        catch { }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
