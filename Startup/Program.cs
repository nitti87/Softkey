using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class StartupProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--verify-installation")
            {
                using var verifierIdentity = WindowsIdentity.GetCurrent();
                using var verifierStore = new WindowsStartupStore(verifierIdentity.User.Value);
                var verifierInstallation = StartupInstallation.Current(verifierStore);
                var verifierConfiguration = new StartupConfiguration(verifierStore, verifierInstallation, new WindowsStartupTasks(verifierIdentity.User.Value), new WindowsStartupTasks(verifierIdentity.User.Value, true));
                var actual = verifierConfiguration.Read();
                verifierInstallation.Validate(actual.Login, true);
                if (verifierConfiguration.NeedsUpgrade) return 5;
                return 0;
            }
            if (args.Length == 1 && args[0] == "--launch") { StartupPriority.Apply(); return StartupLauncher.Run(); }
            if (args.Length == 1 && args[0] == "--login-broker") { LockedDesktopBroker.Run(); return 0; }
            if (args.Length == 4 && args[0] == DesktopCrashRecovery.WatchArgument)
            {
                if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int watched) || watched <= 0
                    || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out long creationTime) || creationTime <= 0) return 2;
                int attempted = 0;
                if (args[3] != "0" && !DesktopCrashRecovery.TryReadAttempt(args[3], out attempted)) return 2;
                return DesktopCrashRecovery.Watch(watched, creationTime, attempted);
            }
            if (args.Length == 2 && (args[0] == "--upgrade-autostart" || args[0] == "--enable-automatic"))
            {
                using var installerIdentity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(installerIdentity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
                string sid = new SecurityIdentifier(args[1]).Value;
                if (sid == "S-1-5-18" || sid == "S-1-5-19" || sid == "S-1-5-20") return 2;
                using var installerStore = new WindowsStartupStore(sid);
                var installation = StartupInstallation.Current(installerStore);
                var installerConfiguration = new StartupConfiguration(installerStore, installation, new WindowsStartupTasks(sid), new WindowsStartupTasks(sid, true));
                using var mutex = new Mutex(false, @"Global\SoftKey.Startup.Configuration.v1");
                bool owned;
                try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) return 1;
                try
                {
                    var current = installerConfiguration.Read();
                    if (args[0] == "--upgrade-autostart" && !installerConfiguration.NeedsUpgrade) return 0;
                    installerConfiguration.Apply(new StartupRequest { Login = current.Login,
                        Automatic = current.Automatic || args[0] == "--enable-automatic", AfterUnlock = current.AfterUnlock }, _ => { });
                }
                finally { mutex.ReleaseMutex(); }
                return 0;
            }
            if (args.Length != 3 || args[0] != "--configure" || !args[1].StartsWith("SoftKey.Startup.", StringComparison.Ordinal)
                || !Guid.TryParseExact(args[1].Substring("SoftKey.Startup.".Length), "N", out _) || !int.TryParse(args[2], out int parent) || parent <= 0) return 2;
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
            string root = AppContext.BaseDirectory;
            StartupTrust.ProtectedDirectory(root);
            string user = StartupTrust.ProcessUser(parent, Path.Combine(root, "SoftKey.exe"));
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(20000);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parent) return 4;
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            void Report(StartupProgress update) => writer.WriteLine(JsonSerializer.Serialize(update));
            using var store = new WindowsStartupStore(user);
            try
            {
                string line = reader.ReadLine();
                if (line == null || line.Length > 8192) throw new InvalidOperationException("Startinställningarna kunde inte läsas.");
                var request = JsonSerializer.Deserialize<StartupRequest>(line) ?? throw new InvalidOperationException("Startinställningarna kunde inte läsas.");
                using var mutex = new Mutex(false, @"Global\SoftKey.Startup.Configuration.v1");
                bool locked;
                try { locked = mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new InvalidOperationException("En annan inställning sparas. Försök igen om en stund.");
                StartupChoices actual = null;
                try
                {
                    var installation = StartupInstallation.Current(store);
                    if (request.KeyboardArea != null && request.KeyboardLayout == null)
                        throw new InvalidOperationException("Tangentbordets mått kräver en tangentbordslayout.");
                    int preferences = (request.KeyboardLayout.HasValue ? 1 : 0)
                        + (request.AutoclickTiming.HasValue ? 1 : 0) + (request.SmallDragKeyIcons.HasValue ? 1 : 0);
                    if (preferences > 1) throw new InvalidOperationException("Spara en inställning i taget.");
                    if (preferences == 1)
                    {
                        installation.Validate(login: false, automatic: true);
                        request.KeyboardLayout?.WriteInstalled(store, request.KeyboardArea);
                        request.AutoclickTiming?.WriteInstalled(store);
                        if (request.SmallDragKeyIcons is { } small) SignInDragKeySettings.WriteSmallSize(store, small);
                    }
                    else
                    {
                        var configuration = new StartupConfiguration(store, installation, new WindowsStartupTasks(user), new WindowsStartupTasks(user, true));
                        actual = configuration.Apply(request, Report);
                    }
                }
                finally { mutex.ReleaseMutex(); }
                Report(new(100, "Inställningarna har verkställts.", true, true, actual));
                return 0;
            }
            catch (Exception ex)
            {
                try { Report(new(0, ex.Message, true, false)); } catch (IOException) { }
                return 1;
            }
        }
        catch (Exception ex)
        {
            // Installer-only diagnostics contain no pipe payload, words or key events.
            if (args.Length == 2 && (args[0] == "--upgrade-autostart" || args[0] == "--enable-automatic"))
            {
                try
                {
                    StartupTrust.ProtectedDirectory(AppContext.BaseDirectory);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-install-error.json"),
                        JsonSerializer.Serialize(new { Error = ex.Message, Code = ex.HResult, Type = ex.GetType().Name, Stack = ex.StackTrace, TaskXml = ex.Data["StartupTaskXml"] }));
                }
                catch { }
            }
            return 1;
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
}
