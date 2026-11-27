using System.Reflection;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

static class StartupChecks
{
    static int checks;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Fields).GetValue(value);
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        foreach (var item in Descendants<T>(child)) yield return item;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--recovery-test-child")
            {
                using var release = EventWaitHandle.OpenExisting(args[1]);
                return release.WaitOne(10_000) ? int.Parse(args[2]) : 99;
            }
            if (args.Length == 1 && args[0] == "--desktop-recovery")
            {
                DesktopRecovery();
                Console.WriteLine($"All {checks} desktop recovery checks passed.");
                return 0;
            }
            if (args.Length == 2 && args[0] is "--keyboard-spacing" or "--keyboard-resize")
            {
                string output = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(output);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                if (args[0] == "--keyboard-spacing")
                {
                    KeyboardSpacing(output);
                    SignInShell(output);
                }
                SignInKeyboardMatch(output);
                Console.WriteLine($"All {checks} keyboard and sign-in checks passed.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--desktop")
            {
                Interface(Path.GetFullPath(args[1]), desktopOnly: true);
                Console.WriteLine($"All {checks} desktop checks passed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--desktop-input-target")
            {
                DesktopInputTarget();
                return 0;
            }
            if (args.Length == 2 && args[0] == "--desktop-lock-exit" && args[1] is "event" or "poll")
            {
                DesktopLockExit(args[1] == "event");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--installed-secure-return")
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                using var caller = System.Diagnostics.Process.GetCurrentProcess();
                bool VisibleDesktopKeyboard()
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName("SoftKey");
                    try { return processes.Any(p => p.SessionId == caller.SessionId && p.MainWindowTitle == "SoftKey"); }
                    finally { foreach (var process in processes) process.Dispose(); }
                }
                bool originallyVisible = VisibleDesktopKeyboard();
                Check(StartupLauncher.RestoreAfterSecureDesktop(identity.User.Value), "secure return accepts the signed installed desktop app for the interactive user");
                Check(SpinWait.SpinUntil(VisibleDesktopKeyboard, TimeSpan.FromSeconds(5)), "a desktop keyboard is visible after the installed secure-return launch");
                Console.WriteLine($"Desktop keyboard was already visible: {originallyVisible}");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--installed-startup")
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                using var installedStore = new WindowsStartupStore(identity.User.Value);
                var installed = StartupInstallation.Current(installedStore, Path.GetFullPath(args[1]));
                var installedTasks = new WindowsStartupTasks(identity.User.Value);
                var actual = new StartupConfiguration(installedStore, installed, installedTasks, new WindowsStartupTasks(identity.User.Value, true));
                Check(actual.Read().StartsOnDesktop && !actual.NeedsUpgrade, "the real Windows task is readable and valid for the signed-in user");
                string installedXml = installedTasks.Read();
                Check(StartupTask.ReadDesktop(installedXml, StartupInstallation.Current(installedStore, StartupTask.InstallationRoot(installedXml)), identity.User.Value) != null,
                    "the broker and normal launcher agree on the installed Windows task");
                Check(StartupLauncher.Handoff(identity.User.Value), "the unlocked user can request the signed installed desktop launcher through the broker handoff");
                return 0;
            }
            DwellClickState();
            DesktopRecovery();
            Configuration();
            KeyboardAccentLabels();
            SecureKeyboardDesktopReturn();
            WordFieldReadRecovery();
            ResponsiveWordFeedback();
            SessionState();
            SecureAttentionLogic();
            Interface(Path.GetFullPath(args.Length > 0 ? args[0] : "obj/UIAccessTest"));
            Console.WriteLine($"All {checks} startup checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void DesktopRecovery()
    {
        const uint abnormalExit = 0xc0000005;
        Check(DesktopCrashRecovery.NextAttempt(0, 0, 120_000) == null
            && DesktopCrashRecovery.NextAttempt(0, 1, 100) == null,
            "normal shutdown, including after a recovery, cannot request a restart");
        Check(DesktopCrashRecovery.NextAttempt(abnormalExit, 0, 0) == 1
            && DesktopCrashRecovery.NextAttempt(abnormalExit, 1, 1000) == 2
            && DesktopCrashRecovery.NextAttempt(abnormalExit, 2, 1000) == null,
            "two rapid recovery attempts are followed by a stop");
        Check(DesktopCrashRecovery.NextAttempt(abnormalExit, 2, 60_000) == 1,
            "a full minute of operation makes the recovery attempts available again");
        Check(DesktopCrashRecovery.TryReadAttempt("2", out int attempt) && attempt == 2
            && !DesktopCrashRecovery.TryReadAttempt("3", out _) && !DesktopCrashRecovery.TryReadAttempt("-1", out _),
            "launch arguments cannot extend the allowed number of recovery attempts");

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string executable = Environment.ProcessPath;
        foreach (uint exitCode in new uint[] { 0, abnormalExit })
        {
            string name = @"Local\SoftKey.RecoveryCheck." + Guid.NewGuid().ToString("N");
            using var release = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(executable) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--recovery-test-child");
            start.ArgumentList.Add(name);
            start.ArgumentList.Add(unchecked((int)exitCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var child = System.Diagnostics.Process.Start(start);
            long created = child.StartTime.ToFileTimeUtc();
            if (exitCode == 0)
            {
                using var wrongCreation = DesktopCrashRecovery.OpenObservedProcess(child.Id, created - 1, child.SessionId, identity.User.Value, executable);
                using var wrongImage = DesktopCrashRecovery.OpenObservedProcess(child.Id, created, child.SessionId, identity.User.Value, executable + ".other");
                using var wrongSession = DesktopCrashRecovery.OpenObservedProcess(child.Id, created, child.SessionId + 1, identity.User.Value, executable);
                Check(wrongCreation == null && wrongImage == null && wrongSession == null,
                    "a different process lifetime, executable or session cannot be watched as SoftKey");
            }
            using var watched = DesktopCrashRecovery.OpenObservedProcess(child.Id, created, child.SessionId, identity.User.Value, executable);
            Check(watched != null, "the recovery helper can wait on the exact process with read-only access");
            release.Set();
            Check(DesktopCrashRecovery.WaitForExit(watched) == exitCode,
                exitCode == 0 ? "a real clean process exit reports zero" : "a real abnormal process exit preserves the Windows failure code");
        }
    }

    static void DwellClickState()
    {
        var tracker = new DwellTracker();
        bool Tick(int x, double now, DwellTarget target, string control = null, bool repeat = false)
            => tracker.Update(x, 20, target, now, 800, 10, repeat, control);

        Check(!Tick(20, 0, DwellTarget.External, "original") && Tick(20, 800, DwellTarget.External, "original"),
            "a still pointer can complete the initial dwell");
        tracker.Complete(800, allowRepeat: false);
        Check(tracker.IsBlockedAt(20, 20) && !tracker.IsBlockedAt(21, 20),
            "the external check can be skipped only at the exact position of a completed one-shot click");
        Check(!Tick(20, 900, DwellTarget.External, "replaced") && !Tick(20, 1700, DwellTarget.External, "replaced"),
            "an external control replaced under a stationary pointer cannot click again with repeat disabled");
        Check(!Tick(20, 1800, DwellTarget.None) && !Tick(20, 2600, DwellTarget.SoftKeyControl, "dialog")
            && !Tick(20, 3400, DwellTarget.SoftKeyControl, "dialog"),
            "a disappearing target and a popup cannot rearm a stationary click");
        Check(!Tick(22, 3500, DwellTarget.SoftKeyControl, "adjacent") && Tick(22, 4300, DwellTarget.SoftKeyControl, "adjacent"),
            "a small movement to another control starts a new full dwell");
        Check(!tracker.IsBlockedAt(20, 20), "moving rearms the old position without changing the selected dwell time");

        tracker = new DwellTracker();
        Tick(20, 0, DwellTarget.SoftKeyRepeat, "key", repeat: true);
        Check(Tick(21, 800, DwellTarget.SoftKeyRepeat, "key", repeat: true), "the repeatable key fires after the initial dwell");
        tracker.Complete(800, allowRepeat: true);
        Check(!tracker.IsBlockedAt(21, 20), "repeatable clicks continue checking the target while the pointer stays still");
        Check(!Tick(21, 1500, DwellTarget.SoftKeyRepeat, "key", repeat: true)
            && Tick(21, 1600, DwellTarget.SoftKeyRepeat, "key", repeat: true),
            "enabled repeat still clicks on the selected interval for repeatable keys");
        tracker.Complete(1600, allowRepeat: false);
        tracker.Reset();
        Check(!Tick(21, 2500, DwellTarget.SoftKeyControl, "popup", repeat: true)
            && !Tick(21, 3300, DwellTarget.SoftKeyControl, "popup", repeat: true),
            "a one-shot click is blocked at the click position through a reset even with repeat enabled");
        tracker.Suppress(21, 20, DwellTarget.None, 4000);
        Check(!Tick(21, 4100, DwellTarget.External, "new") && !Tick(21, 4900, DwellTarget.External, "new")
            && !Tick(22, 5000, DwellTarget.External, "new") && Tick(22, 5800, DwellTarget.External, "new"),
            "suppression also holds when no target is initially present and ends when the pointer moves");
    }

    static void ResponsiveWordFeedback()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-word-feedback-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var keyboard = new KeyboardWindow(storage);
        try
        {
            var target = (Active: new IntPtr(11), Focus: new IntPtr(12));
            var text = new DesktopWordField(target.Active, target.Focus, "plain");
            var password = text with { Kind = DesktopInputKind.Password, ElementId = "password" };
            var sent = new List<string>();
            var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn");
            var status = Field<TextBlock>(keyboard, "statusText");
            void Set(string name, object value) => keyboard.GetType().GetField(name, Fields).SetValue(keyboard, value);
            void Press(string label) => Call(keyboard, "Press", keys.Keys.First(key => key.Label == label));
            Set("keyInput", (Func<KeyDef, bool>)(key => { sent.Add(key.Label); return true; }));
            Set("readDesktopTypingTarget", (Func<(IntPtr, IntPtr)>)(() => target));
            Set("readDesktopWordField", (Func<DesktopWordField>)(() => text));
            Call(keyboard, "SetFocusedSuggestionField", text, null);

            var read = new TaskCompletionSource<(DesktopWordField, string)>();
            int requests = 0;
            Set("beginDesktopWordFieldRead", (Func<(IntPtr, IntPtr), Task<(DesktopWordField, string)>>)(_ =>
            {
                requests++;
                return read.Task;
            }));
            Press("h");
            Press("e");
            Check(sent.SequenceEqual(new[] { "h", "e" }) && requests == 1 && status.Text == "Redo",
                "typing sends each key while a field read is pending and shares the check for consecutive keys");
            read.SetResult((text, null));
            Pump();
            Check(string.Equals(status.Text, "he", StringComparison.OrdinalIgnoreCase),
                "verified feedback displays letters in the order they were sent");
            Press("Enter");
            Check(Field<Dictionary<string, int>>(keyboard, "words").ContainsKey("he") && status.Text == "Redo",
                "Enter submits and learns a verified word using the original preflight");

            read = new();
            Press("p");
            read.SetResult((password, null));
            Pump();
            Check(sent[^1] == "p" && status.Text == "Redo" && Field<string>(keyboard, "currentWord") == "",
                "switching to a password field in the same window still sends input and discards ordinary word feedback");

            read = new();
            Press("r");
            target = (target.Active, new IntPtr(13));
            read.SetResult((text, null));
            Pump();
            Check(sent[^1] == "r" && status.Text == "Redo",
                "a completed read from a former native focus cannot restore word feedback");

            target = (text.Active, text.Focus);
            read = new();
            Press("t");
            Pump(100);
            read.SetResult((text, null));
            Pump();
            Check(sent[^1] == "t" && status.Text == "Redo",
                "a provider timeout cannot hold up input or restore a late word");
        }
        finally
        {
            keyboard.EnablePreviewClose();
            keyboard.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    sealed class Store : IStartupStore
    {
        public readonly Dictionary<(StartupHive, string), Dictionary<string, StartupValue>> Keys = new();
        public StartupValue Read(StartupHive hive, string path, string name) => Keys.TryGetValue((hive, path), out var key) ? key.GetValueOrDefault(name) : null;
        public Dictionary<string, StartupValue> ReadKey(StartupHive hive, string path) => Keys.TryGetValue((hive, path), out var key) ? new(key) : null;
        public void Write(StartupHive hive, string path, string name, StartupValue value)
        {
            if (!Keys.TryGetValue((hive, path), out var key)) Keys[(hive, path)] = key = new();
            key[name] = value;
        }
        public void DeleteValue(StartupHive hive, string path, string name) { if (Keys.TryGetValue((hive, path), out var key)) key.Remove(name); }
        public void DeleteKey(StartupHive hive, string path) => Keys.Remove((hive, path));
    }
    sealed class Installation : StartupInstallation
    {
        public bool FailValidation;
        public int SettingsCalls;
        public Installation() : base(@"C:\Program Files\SoftKey", @"C:\Program Files\SoftKey-Logon-Test") { }
        public override void Validate(bool login, bool automatic) { if (FailValidation) throw new IOException("simulated validation failure"); }
        public override Action EnsureSettings(StartupRequest request, IStartupStore store) { SettingsCalls++; return () => SettingsCalls--; }
        public override SofTypeStartupRecovery ReadSofTypeRecovery() => new(1, false, null);
        public override Action EnsureSofTypeRecovery(IStartupStore store) => () => { };
    }
    sealed class Tasks : IStartupTasks
    {
        public string UserSid => "S-1-5-21-100-200-300-1001";
        public string Xml;
        public Action BeforeWrite;
        public Action BeforeRun;
        public int Runs;
        public string Read() => Xml;
        public void Write(string xml) { BeforeWrite?.Invoke(); Xml = xml; }
        public void Delete() => Xml = null;
        public void Run() { BeforeRun?.Invoke(); Runs++; }
    }
    static void Configuration()
    {
        var store = new Store(); var installation = new Installation(); var tasks = new Tasks(); var brokerTasks = new Tasks(); var config = new StartupConfiguration(store, installation, tasks, brokerTasks);
        var messages = new List<StartupProgress>();
        Check(config.Read() == new StartupChoices(false, false), "missing Windows entries show all startup options disabled");
        foreach (var hive in new[] { StartupHive.Machine, StartupHive.User })
            store.Write(hive, StartupConfiguration.Accessibility, "Configuration", new("osk,OtherAT", RegistryValueKind.String));
        store.Write(StartupHive.Logon, StartupConfiguration.Accessibility, "Configuration", new("Narrator", RegistryValueKind.String));
        store.Write(StartupHive.User, StartupConfiguration.RunPath, "OtherApp", new("other.exe", RegistryValueKind.String));
        store.Write(StartupHive.Machine, StartupConfiguration.SofTypePath, "StartExe", new("softype.exe", RegistryValueKind.String));
        var layout = new KeyboardLayoutSettings(3, 7, 115);
        var area = new KeyboardAreaSize(575, 185);
        var request = JsonSerializer.Deserialize<StartupRequest>(JsonSerializer.Serialize(new StartupRequest { KeyboardLayout = layout, KeyboardArea = area }));
        request.KeyboardLayout.Value.WriteInstalled(store, request.KeyboardArea);
        var sharedValue = store.Read(StartupHive.Machine, @"SOFTWARE\SoftKey", "KeyboardLayout");
        Check(KeyboardAreaSize.Decode(store.Read(StartupHive.Machine, KeyboardAreaSize.RegistryPath, KeyboardAreaSize.RegistryValue)?.Value) == area
            && KeyboardAreaSize.Decode(new byte[] { 0, 0, 185, 0 }) == null,
            "the signed helper stores the measured key area as two bounded dimensions");
        Check(sharedValue?.Kind == RegistryValueKind.Binary && KeyboardLayoutSettings.Decode(sharedValue.Value) == layout
            && KeyboardLayoutSettings.Decode(new byte[] { 0, 7 }) == new KeyboardLayoutSettings(3, 7, 100)
            && KeyboardLayoutSettings.Decode(new byte[] { 0, 13 }) == null
            && KeyboardLayoutSettings.Decode(new byte[] { 3, 7, 126 }) == null && config.Read() == new StartupChoices(false, false),
            "the helper stores spacing and legend size atomically, and reads legacy spacing at 100 percent");
        var timing = new AutoclickTimingSettings(750, 35);
        request = JsonSerializer.Deserialize<StartupRequest>(JsonSerializer.Serialize(new StartupRequest { AutoclickTiming = timing }));
        request.AutoclickTiming.Value.WriteInstalled(store);
        var timingValue = store.Read(StartupHive.Machine, @"SOFTWARE\SoftKey", "AutoclickTiming");
        Check(timingValue?.Kind == RegistryValueKind.Binary && AutoclickTimingSettings.Decode(timingValue.Value) == timing
            && AutoclickTimingSettings.Decode(new byte[] { 0, 0, 35 }) == null
            && AutoclickTimingSettings.Decode(new byte[] { 238, 2, 101 }) == null
            && store.Read(StartupHive.Machine, @"SOFTWARE\SoftKey", "KeyboardLayout") == sharedValue
            && config.Read() == new StartupChoices(false, false),
            "the signed helper request carries only validated timing values and leaves layout and startup choices intact");
        foreach (bool small in new[] { true, false })
        {
            request = JsonSerializer.Deserialize<StartupRequest>(JsonSerializer.Serialize(new StartupRequest { SmallDragKeyIcons = small }));
            SignInDragKeySettings.WriteSmallSize(store, request.SmallDragKeyIcons.Value);
            var sizeChoice = store.Read(StartupHive.Machine, SignInDragKeySettings.RegistryPath, SignInDragKeySettings.RegistryValue);
            Check(sizeChoice?.Kind == RegistryValueKind.DWord && Equals(sizeChoice.Value, small ? 1 : 0)
                && store.Read(StartupHive.Machine, @"SOFTWARE\SoftKey", "AutoclickTiming") == timingValue
                && config.Read() == new StartupChoices(false, false),
                $"the shared DragKey size ({small}) leaves Autoclick timing and startup choices intact");
        }
        var legacySpacing = new Settings { HorizontalKeyGap = 9, VerticalKeyGap = 2 };
        legacySpacing.Normalize();
        Check(legacySpacing.HorizontalKeyGap == 9 && legacySpacing.VerticalKeyGap == 3
            && !new KeyboardLayoutSettings(9, 2).IsValid, "older spacing is clamped per axis and new writes below the minimum are invalid");
        foreach (var desired in new[] { new StartupChoices(true, false), new StartupChoices(true, true), new StartupChoices(true, true, true),
            new StartupChoices(true, false, true), new StartupChoices(false, false, true), new StartupChoices(false, true, true),
            new StartupChoices(false, true), new StartupChoices(false, false) })
        {
            var actual = config.Apply(new StartupRequest { Login = desired.Login, Automatic = desired.Automatic, AfterUnlock = desired.AfterUnlock }, messages.Add);
            Check(actual == desired && config.Read() == desired && !config.NeedsUpgrade, "all startup options can independently reach " + desired);
            Check((string)store.Read(StartupHive.Logon, StartupConfiguration.Accessibility, "Configuration").Value
                == (desired.Login ? "Narrator,SoftKey_Logon_v1" : "Narrator"),
                "the Windows welcome screen preserves other accessibility tools when changing SoftKey startup");
            Check(desired.StartsOnDesktop ? StartupTask.ReadDesktop(tasks.Xml, installation, tasks.UserSid) == (desired.Automatic, desired.AfterUnlock) : tasks.Xml == null,
                "the desktop task has exactly the selected triggers and is removed when both desktop options are off");
            foreach (var hive in new[] { StartupHive.Machine, StartupHive.User })
                Check(((string)store.Read(hive, StartupConfiguration.Accessibility, "Configuration").Value).StartsWith("osk,OtherAT"), "other accessibility startup entries are preserved");
            Check((string)store.Read(StartupHive.User, StartupConfiguration.RunPath, "OtherApp").Value == "other.exe", "other automatic-start programs are preserved");
            Check(desired.Login ? StartupTask.MatchesBroker(brokerTasks.Xml, installation, brokerTasks.UserSid) && brokerTasks.Runs > 0 : brokerTasks.Xml == null,
                "the login option owns an independent widgets broker task, even when desktop automatic startup is off");
            if (desired.Login) Check((string)store.Read(StartupHive.Machine, StartupConfiguration.SofTypePath, "SecureDesktopAccommodation").Value == StartupConfiguration.AtName,
                "SoftKey supplies secure-desktop dwell when its login option is enabled");
            else Check(store.Read(StartupHive.Machine, StartupConfiguration.SofTypePath, "SecureDesktopAccommodation") == null,
                "disabling login restores SofType's previous secure-desktop choice");
        }
        Check(messages.Any(p => p.Percent == 30) && messages.Any(p => p.Percent == 90), "configuration reports installation and verification progress");
        Check(store.ReadKey(StartupHive.Machine, StartupConfiguration.AtPath) == null, "disabling login removes only SoftKey's assistive registration");

        // Inject a failure after login has changed, while another accessibility program adds itself.
        bool injected = false;
        tasks.BeforeWrite = () =>
        {
            if (injected) return;
            injected = true;
            tasks.BeforeWrite = null;
            var existing = store.Read(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration");
            store.Write(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration", new(existing.Value + ",AddedElsewhere", RegistryValueKind.String));
            throw new IOException("simulated scheduled-task failure");
        };
        bool failed = false;
        try { config.Apply(new StartupRequest { Login = true, Automatic = true }, messages.Add); } catch (IOException) { failed = true; }
        Check(failed && config.Read() == new StartupChoices(false, false), "a failed automatic-start write rolls login back to its prior state");
        Check(store.ReadKey(StartupHive.Machine, StartupConfiguration.AtPath) == null, "failed enable leaves no new assistive executable registered");
        Check(((string)store.Read(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration").Value).Contains("AddedElsewhere"), "rollback preserves concurrent additions by other assistive software");
        int prepared = installation.SettingsCalls;
        installation.FailValidation = true;
        try { config.Apply(new StartupRequest { Login = true }, messages.Add); } catch (IOException) { }
        Check(config.Read() == new StartupChoices(false, false) && installation.SettingsCalls == prepared, "validation failure changes no Windows startup entries or configuration files");
        installation.FailValidation = false;
        store.Write(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration", new(42, RegistryValueKind.DWord));
        failed = false;
        try { config.Apply(new StartupRequest { Login = true }, messages.Add); } catch (InvalidOperationException) { failed = true; }
        Check(failed && store.ReadKey(StartupHive.Machine, StartupConfiguration.AtPath) == null, "invalid Windows configuration types abort before mutation");
        store.Write(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration", new("OtherAT,SoftKey_Logon_v1", RegistryValueKind.String));
        config.Apply(new StartupRequest { Login = false, Automatic = false }, messages.Add);
        Check(!StartupConfiguration.Contains(store.Read(StartupHive.Machine, StartupConfiguration.Accessibility, "Configuration")), "disabling cleans up a partially enabled login configuration");
        config.Apply(new StartupRequest { Login = true }, messages.Add);
        store.Write(StartupHive.Machine, StartupConfiguration.AtPath, "LastSecureStartUtc", new("preserved-status", RegistryValueKind.String));
        tasks.BeforeWrite = () =>
        {
            tasks.BeforeWrite = null; throw new IOException("simulated second-stage failure");
        };
        try { config.Apply(new StartupRequest { Login = false, Automatic = true }, messages.Add); } catch (IOException) { }
        Check(config.Read() == new StartupChoices(true, false)
            && (string)store.Read(StartupHive.Machine, StartupConfiguration.AtPath, "LastSecureStartUtc").Value == "preserved-status",
            "a failed disable restores the entire previous login registration and its status values");
        store.Write(StartupHive.Machine, StartupConfiguration.SofTypePath, "SecureDesktopAccommodation", new(StartupConfiguration.AtName, RegistryValueKind.String));
        config.Apply(new StartupRequest { Login = false }, messages.Add);
        var alreadyUnlocked = new StartupHandoff();
        Check(!alreadyUnlocked.ShouldAttempt(false, true, 10000) && !alreadyUnlocked.ShouldAttempt(true, true, 20000)
            && !alreadyUnlocked.ShouldAttempt(false, true, 30000), "starting the persistent broker on an unlocked desktop does not launch or later reopen the desktop app");
        Check(store.Read(StartupHive.Machine, StartupConfiguration.SofTypePath, "SecureDesktopAccommodation") == null,
            "disabling restores SofType when it previously delegated its secure keyboard to SoftKey");
        var wire = new StartupRequest { Login = true, Automatic = false, AfterUnlock = true, DwellMs = 950, JitterPx = 13 };
        var decoded = JsonSerializer.Deserialize<StartupRequest>(JsonSerializer.Serialize(wire));
        Check(decoded.Choices == wire.Choices && decoded.DwellMs == 950 && decoded.JitterPx == 13,
            "the installer request preserves all startup choices and only numeric keyboard preferences");
        var reply = new StartupProgress(100, "Klart", true, true, new(true, false, true));
        Check(JsonSerializer.Deserialize<StartupProgress>(JsonSerializer.Serialize(reply)) == reply,
            "the verified installation result survives the pipe protocol round trip");

        string xml = StartupTask.CreateDesktop(installation, tasks.UserSid, automatic: true, afterUnlock: true);
        Check(StartupTask.ReadDesktop(xml, installation, tasks.UserSid) == (true, true) && xml.Contains("<Delay>PT0S</Delay>") && xml.Contains("<Priority>1</Priority>"),
            "the logon task requests zero delay and Windows High CPU priority");
        var task = XDocument.Parse(xml);
        XNamespace taskNs = task.Root.Name.Namespace;
        var unlockTrigger = task.Root.Element(taskNs + "Triggers").Element(taskNs + "SessionStateChangeTrigger");
        Check(unlockTrigger?.Element(taskNs + "StateChange")?.Value == "SessionUnlock"
            && unlockTrigger.Element(taskNs + "UserId")?.Value == tasks.UserSid
            && unlockTrigger.Element(taskNs + "Delay")?.Value == "PT0S", "desktop startup is also triggered on unlock for the same user without delay");
        Check(StartupTask.ReadDesktop(xml.Replace("SessionUnlock", "SessionLock"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml.Replace("<SessionStateChangeTrigger><Enabled>true", "<SessionStateChangeTrigger><Enabled>0"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml.Replace("<UserId>" + tasks.UserSid + "</UserId><Delay>PT0S</Delay><StateChange>",
                "<UserId>S-1-5-21-999-200-300-1001</UserId><Delay>PT0S</Delay><StateChange>"), installation, tasks.UserSid) == null,
            "an unlock trigger must be enabled and scoped to the current user, and startup on lock is rejected");
        tasks.Xml = xml;
        Check(config.Read() == new StartupChoices(false, true, true) && !config.NeedsUpgrade,
            "an installed task with both triggers keeps both choices enabled in the new dialog");
        config.Apply(new StartupRequest { Automatic = true }, messages.Add);
        Check(StartupTask.ReadDesktop(tasks.Xml, installation, tasks.UserSid) == (true, false) && !config.NeedsUpgrade,
            "turning off unlock keeps sign-in startup enabled");
        Check(xml.Contains("InteractiveToken") && xml.Contains("LeastPrivilege") && !xml.Contains("Password"),
            "automatic startup uses the interactive user without a password or administrator run level");
        using (var currentIdentity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            string realSid = currentIdentity.User.Value;
            string normalized = StartupTask.CreateDesktop(installation, realSid, automatic: true, afterUnlock: true).Replace("<RunLevel>LeastPrivilege</RunLevel>", "")
                .Replace("<UserId>" + realSid + "</UserId>", "<UserId>" + System.Security.SecurityElement.Escape(currentIdentity.Name) + "</UserId>");
            Check(StartupTask.ReadDesktop(normalized, installation, realSid) == (true, true), "Windows-normalized account names and the omitted default run level remain valid");
        }
        Check(StartupTask.ReadDesktop(xml.Replace("PT0S", "PT30S"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml.Replace("LeastPrivilege", "HighestAvailable"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml.Replace("<Priority>1", "<Priority>0"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml.Replace("--launch", "--anything"), installation, tasks.UserSid) == null,
            "task validation rejects delay, elevation, realtime priority and unexpected arguments");
        Check(StartupTask.ReadDesktop(xml.Replace("</Actions>", "<Exec><Command>other.exe</Command></Exec></Actions>"), installation, tasks.UserSid) == null
            && StartupTask.ReadDesktop(xml, installation, "S-1-5-21-999-200-300-1001") == null, "task validation rejects extra actions and another user");
        Check(StartupTask.InstallationRoot(xml) == installation.Root && StartupTask.InstallationRoot("bad xml") == null,
            "the broker locates only the named startup helper and tolerates malformed task XML");
        var brokerInstallation = new StartupInstallation(StartupTask.InstallationRoot(xml), installation.LogonDirectory);
        Check(StartupTask.ReadDesktop(xml.Replace("</WorkingDirectory>", "\\</WorkingDirectory>"), brokerInstallation, tasks.UserSid) == (true, true),
            "broker and launcher recognize the same installation with or without a trailing directory separator");

        store.Write(StartupHive.User, StartupConfiguration.RunPath, "SoftKey", new(installation.RunCommand, RegistryValueKind.String));
        Check(config.Read().Automatic && config.NeedsUpgrade, "an existing Run-based automatic-start choice is recognized for migration");
        config.Apply(new StartupRequest { Automatic = true }, messages.Add);
        Check(StartupTask.ReadDesktop(tasks.Xml, installation, tasks.UserSid) == (true, false)
            && store.Read(StartupHive.User, StartupConfiguration.RunPath, "SoftKey") == null && !config.NeedsUpgrade,
            "migration installs and verifies the fast task before removing the old Run command");
        tasks.Delete();
        store.Write(StartupHive.User, StartupConfiguration.RunPath, "SoftKey", new(installation.RunCommand, RegistryValueKind.String));
        bool failRemoval = true;
        try
        {
            config.Apply(new StartupRequest { Automatic = true }, p => { if (p.Percent == 90 && failRemoval) { failRemoval = false; throw new IOException("verification delivery failed"); } });
        }
        catch (IOException) { }
        Check(tasks.Xml == null && config.Read().Automatic && config.NeedsUpgrade,
            "a migration failure restores the old Run entry and removes the newly created task");
        string previousTask = xml.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>");
        tasks.Xml = previousTask;
        tasks.BeforeWrite = () => { tasks.BeforeWrite = null; throw new IOException("registration failed"); };
        try { config.Apply(new StartupRequest { Automatic = true }, messages.Add); } catch (IOException) { }
        Check(tasks.Xml == previousTask && config.NeedsUpgrade, "a failed task replacement preserves the previous task XML");
        config.Apply(new StartupRequest { Automatic = false }, messages.Add);
        Check(tasks.Xml == null && !config.Read().Automatic, "disabling automatic startup removes both the task and its legacy Run entry");

        var handoff = new StartupHandoff();
        Check(!handoff.ShouldAttempt(true, true, 10000) && !handoff.ShouldAttempt(null, true, 10000)
            && !handoff.ShouldAttempt(false, false, 10000), "desktop handoff waits for a known unlocked, active desktop");
        Check(handoff.ShouldAttempt(false, true, 10000) && !handoff.ShouldAttempt(false, true, 10100)
            && handoff.ShouldAttempt(false, true, 15000), "failed handoff can retry without a rapid launch loop");
        handoff.Complete();
        Check(!handoff.ShouldAttempt(false, true, 30000) && !handoff.ShouldAttempt(true, true, 40000)
            && !handoff.ShouldAttempt(false, true, 50000), "a completed handoff does not reopen a keyboard after later locks in the same broker session");
        string brokerXml = StartupTask.CreateBroker(installation, brokerTasks.UserSid);
        Check(StartupTask.MatchesBroker(brokerXml, installation, brokerTasks.UserSid)
            && brokerXml.Contains("--login-broker") && brokerXml.Contains("<Delay>PT0S</Delay>") && brokerXml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
            && !brokerXml.Contains("SessionStateChangeTrigger"),
            "the widgets broker starts independently with no logon delay and no execution time limit");
        Check(StartupTask.ReadDesktop(brokerXml, installation, brokerTasks.UserSid) == null
            && !StartupTask.MatchesBroker(brokerXml.Replace("PT0S</ExecutionTimeLimit>", "PT12M</ExecutionTimeLimit>"), installation, brokerTasks.UserSid),
            "the broker task cannot be mistaken for desktop startup or given the launcher's time limit");
        brokerTasks.BeforeRun = () => { brokerTasks.BeforeRun = null; throw new IOException("simulated broker launch failure"); };
        bool brokerFailed = false;
        try { config.Apply(new StartupRequest { Login = true }, messages.Add); } catch (IOException) { brokerFailed = true; }
        Check(brokerFailed && config.Read() == new StartupChoices(false, false) && brokerTasks.Xml == null
            && store.ReadKey(StartupHive.Machine, StartupConfiguration.AtPath) == null, "a failed immediate broker start rolls back its task and new login registration");
        config.Apply(new StartupRequest { Login = true }, messages.Add);
        brokerTasks.Delete();
        Check(config.Read().Login && config.NeedsUpgrade, "an older login registration remains enabled while its missing widgets-start task is detected for migration");
        config.Apply(new StartupRequest { Login = true }, messages.Add);
        Check(!config.NeedsUpgrade && StartupTask.MatchesBroker(brokerTasks.Xml, installation, brokerTasks.UserSid), "migration repairs the early widgets task without disabling login");
        config.Apply(new StartupRequest { Login = false }, messages.Add);
    }
    sealed class Service : ISoftKeyStartupService
    {
        public StartupChoices State = new(true, false);
        public TaskCompletionSource<StartupChoices> Pending;
        public IProgress<StartupProgress> Progress;
        public StartupChoices Requested;
        public int Calls;
        public StartupChoices Read() => State;
        public Task<StartupChoices> ApplyAsync(StartupChoices choices, IProgress<StartupProgress> progress)
        {
            Calls++; Requested = choices; Progress = progress; Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); return Pending.Task;
        }
    }
    static void Pump(int ms = 50)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void FinishTabTransition() => Pump((int)SoftKeyHoverTransition.Duration.TotalMilliseconds + 75);

    static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    static void Interface(string output, bool desktopOnly = false)
    {
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var service = new Service();
        var window = new StartupSettingsWindow(service) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.Show(); Pump();
        var login = Field<SoftKeyCheckBox>(window, "login"); var automatic = Field<SoftKeyCheckBox>(window, "automatic");
        var afterUnlock = Field<SoftKeyCheckBox>(window, "afterUnlock");
        var startupApply = Field<Button>(window, "apply");
        Check(login.IsChecked == true && automatic.IsChecked == false && afterUnlock.IsChecked == false,
            "checkboxes initially display the actual Windows state supplied by the service");
        var startOptions = new[] { login, automatic, afterUnlock };
        Check(startOptions.All(option => option.ActualHeight >= 70
                && Math.Abs(option.ActualWidth - login.ActualWidth) < 1
                && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(option))
                && option.Content is DockPanel { Children: { Count: 2 } } card
                && card.Children[0] is Border
                {
                    IsHitTestVisible: false, CornerRadius.TopLeft: 19, Child: System.Windows.Shapes.Path
                })
            && automatic.TranslatePoint(default, login).Y - login.ActualHeight >= 8
            && afterUnlock.TranslatePoint(default, automatic).Y - automatic.ActualHeight >= 8,
            "Startup uses separated cards with a single line symbol and accessible descriptions");
        Check(window.FontFamily.Source == "Segoe UI" && window.AllowsTransparency, "Startup uses the application's shared dialog styling");
        window.ApplyAsync(false).GetAwaiter().GetResult();
        Check(!startupApply.IsEnabled && service.Calls == 0, "Startup Apply begins disabled and cannot submit unchanged choices");
        Render(window, Path.Combine(output, "startup-dialog.png"));
        afterUnlock.IsChecked = true;
        Check(startupApply.IsEnabled, "changing a Startup option enables Apply");
        afterUnlock.IsChecked = false;
        Check(!startupApply.IsEnabled, "reverting a Startup option disables Apply");
        afterUnlock.IsChecked = true;
        var apply = window.ApplyAsync(false); Pump();
        Check(service.Requested == new StartupChoices(true, false, true), "checking unlock sends its own choice without checking automatic sign-in startup");
        Check(window.IsApplying && window.IsVisible && !Field<Button>(window, "ok").IsEnabled && !login.IsEnabled && !afterUnlock.IsEnabled,
            "Apply keeps the dialog visible and disables duplicate changes during installation");
        window.Close(); Check(window.IsVisible, "the window cannot close while installation is in progress");
        service.Progress.Report(new(65, "Ändrar automatisk start…")); Pump();
        Check(Field<ProgressBar>(window, "progress").Visibility == Visibility.Visible && Field<ProgressBar>(window, "progress").Value == 65,
            "installation progress is shown inside the dialog");
        Render(window, Path.Combine(output, "startup-progress.png"));
        service.State = new(true, false, true); service.Pending.SetResult(service.State);
        while (!apply.IsCompleted) Pump(); apply.GetAwaiter().GetResult();
        Check(window.IsVisible && !window.IsApplying && !startupApply.IsEnabled && afterUnlock.IsEnabled && afterUnlock.IsChecked == true,
            "Verkställ leaves the completed dialog open and shows the verified unlock choice");
        afterUnlock.IsChecked = false;
        var failed = window.ApplyAsync(true); Pump();
        service.Pending.SetException(new IOException("Windows kunde inte slutföra ändringen. Försök igen."));
        while (!failed.IsCompleted) Pump(); failed.GetAwaiter().GetResult();
        Check(window.IsVisible && !window.IsApplying && startupApply.IsEnabled && Field<TextBlock>(window, "status").Foreground == SoftKeyPopupDesign.ErrorText,
            "a failed OK operation leaves the dialog open with a visible error");
        Render(window, Path.Combine(output, "startup-error.png"));
        bool closed = false; window.Closed += (_, _) => closed = true;
        var ok = window.ApplyAsync(true); Pump();
        Check(!closed, "OK does not close before Windows has completed the change");
        service.State = new(true, false); service.Pending.SetResult(service.State);
        while (!ok.IsCompleted) Pump(); ok.GetAwaiter().GetResult();
        Check(closed, "OK closes after successful application and verification");
        var cancelled = new Service(); var cancelWindow = new StartupSettingsWindow(cancelled);
        cancelWindow.ApplyAsync(true).GetAwaiter().GetResult();
        Check(cancelled.Calls == 0, "OK without an edited Startup setting makes no startup changes");
        OtherDialogs(output);
        DialogPlacement();
        DwellPause();
        KeyboardVisibility(output);
        KeyboardSpacing(output);
        DragKeyPower(output);
        DragKeySettings(output);
        SpecificSuggestionSetting(output);
        PhoneNumberDialog(output);
        UpdateUiChecks(output);
        if (desktopOnly) DesktopInput(output);
        if (!desktopOnly)
        {
            SessionWindows();
            SignInToolbar(output);
            SignInShell(output);
            SignInKeyboardMatch(output);
            SecureAttentionInterface(output);
        }
        app.Shutdown();
    }

    static void SpecificSuggestionSetting(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-specific-toggle-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        storage.Save("settings.json", new { DwellMs = 5000 }); // Older settings have no specific-suggestions property.
        storage.Save("words.json", new Dictionary<string, int> { ["hej"] = 1 });
        var saved = new SavedTextStore(storage);
        Check(saved.Save(Guid.NewGuid(), "Privat e-post", SavedTextKind.Email, Encoding.Unicode.GetBytes("prov@example.invalid")),
            "the isolated test has an encrypted saved email");
        string suggestionsFile = Path.Combine(folder, "saved-text.json");
        byte[] before = File.ReadAllBytes(suggestionsFile);
        var keyboard = new KeyboardWindow(storage);
        Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
        bool allowSave = false;
        var dialog = new TabbedWordListWindow(() => new[] { "hej" }, _ => null, _ => null, (_, _) => null,
            saved, hasTarget: false, passwordTarget: false, _ => Task.FromResult<string>(null),
            Field<Settings>(keyboard, "cfg").SpecificSuggestionsEnabled,
            enabled => allowSave && (bool)Call(keyboard, "SaveSpecificSuggestionsEnabled", enabled));
        try
        {
            var email = new DesktopWordField((IntPtr)1, (IntPtr)2, "email", DesktopInputKind.Email);
            var password = new DesktopWordField((IntPtr)1, (IntPtr)3, "password", DesktopInputKind.Password);
            var buttons = Field<Button[]>(keyboard, "sugg");
            var choosePassword = Field<Button>(keyboard, "passwordSuggestionAction");
            var phoneKey = Field<Button>(keyboard, "phoneNumberKey");
            var pageDown = Field<Dictionary<KeyDef, Button>>(keyboard, "btn")[KeyboardLayout.NavigationKeys[^1]];
            Check(phoneKey.Template == pageDown.Template && phoneKey.Margin == pageDown.Margin
                && phoneKey.Padding == pageDown.Padding && phoneKey.Background == pageDown.Background,
                "the phone key shares Page Down's raised template, spacing and surface");
            Call(keyboard, "SetFocusedSuggestionField", email, "p");
            Check(Equals(buttons[0].Content, "prov@example.invalid"), "an existing profile keeps specific suggestions enabled by default");
            ShowOffscreen(dialog);
            var tabs = Field<TabControl>(dialog, "tabs");
            var ordinaryTab = (TabItem)tabs.Items[0];
            var specificTab = (TabItem)tabs.Items[1];
            Border SelectionMark(TabItem tab) => (Border)tab.Template.FindName("SelectionMark", tab);
            Check(Math.Abs(ordinaryTab.ActualWidth - specificTab.ActualWidth) <= 1
                && SelectionMark(ordinaryTab) is { IsVisible: true } && SelectionMark(specificTab) is { IsVisible: false },
                "the word-list tabs have equal targets and mark only the selected tab");
            Render(dialog, Path.Combine(output, "tabbed-word-list.png"));
            var page = Field<SavedSuggestionsPage>(dialog, "specific");
            var toggle = Field<SoftKeyCheckBox>(page, "enableSuggestions");
            var reveal = Field<Border>(page, "listReveal");
            Check(!toggle.IsVisible, "the ordinary word-list tab has no specific-suggestions controls");
            double ordinaryHeight = dialog.ActualHeight, tabWidth = dialog.ActualWidth;
            var tabHeights = new List<double>();
            void RecordTabHeight(object sender, SizeChangedEventArgs args) => tabHeights.Add(args.NewSize.Height);
            dialog.SizeChanged += RecordTabHeight;
            tabs.SelectedIndex = 1;
            FinishTabTransition();
            double specificHeight = dialog.ActualHeight;
            bool HasIntermediateHeight() => tabHeights.Any(height => height > specificHeight + 1 && height < ordinaryHeight - 1);
            Check(specificHeight < ordinaryHeight && dialog.ActualWidth == tabWidth
                && HasIntermediateHeight() == SystemParameters.ClientAreaAnimation,
                "switching to specific suggestions animates the height according to Windows settings and keeps the tab width");
            Check(SelectionMark(ordinaryTab) is { IsVisible: false } && SelectionMark(specificTab) is { IsVisible: true },
                "the word-list selection marker moves with the active tab");
            var position = toggle.PointToScreen(new Point(0, 0));
            Check(toggle.IsVisible && toggle.IsChecked == true && reveal.IsVisible
                && System.Windows.Automation.AutomationProperties.GetName(toggle) == "Aktivera specifika ordförslag", "the specific tab shows the labeled checkbox above the list");
            toggle.IsChecked = false;
            Check(toggle.IsChecked == true && reveal.IsVisible, "a failed save restores the checkbox and leaves the saved suggestions available");

            allowSave = true;
            toggle.IsChecked = false;
            Pump(240);
            Check(toggle.IsChecked == false && reveal.Visibility == Visibility.Collapsed && toggle.PointToScreen(new Point(0, 0)) == position,
                "unchecking collapses the list and keeps the checkbox at the same screen position");
            Render(dialog, Path.Combine(output, "specific-setting-disabled.png"));
            Call(keyboard, "SetFocusedSuggestionField", email, "p");
            Check(buttons.All(button => button.Visibility == Visibility.Collapsed), "disabled specific suggestions do not offer saved emails");
            Call(keyboard, "SetFocusedSuggestionField", password, null);
            Check(choosePassword.Visibility == Visibility.Collapsed, "disabled specific suggestions hide the password picker action");
            var ordinary = new DesktopWordField((IntPtr)1, (IntPtr)4, "ordinary");
            Call(keyboard, "SetFocusedSuggestionField", ordinary, null);
            typeof(KeyboardWindow).GetField("wordTarget", Fields).SetValue(keyboard, (ordinary.Active, ordinary.Focus));
            Call(keyboard, "Track", KeyboardLayout.Rows.SelectMany(row => row).First(key => key.Label == "h"), true, "h");
            Check(Equals(buttons[0].Content, "hej"), "ordinary word suggestions are still available when specific suggestions are off");
            Check(!storage.Load("settings.json", new Settings()).SpecificSuggestionsEnabled
                && before.SequenceEqual(File.ReadAllBytes(suggestionsFile)), "the disabled preference is saved without changing the encrypted entries");

            toggle.IsChecked = true;
            toggle.IsChecked = false;
            toggle.IsChecked = true;
            Pump(240);
            Call(keyboard, "SetFocusedSuggestionField", email, "p");
            Check(reveal.IsVisible && reveal.IsEnabled && reveal.ActualHeight > 0 && Equals(buttons[0].Content, "prov@example.invalid")
                && storage.Load("settings.json", new Settings()).SpecificSuggestionsEnabled, "re-enabling after rapid toggles restores the list, suggestions and saved preference");
            Render(dialog, Path.Combine(output, "specific-setting-enabled.png"));

            var handle = new WindowInteropHelper(dialog).Handle;
            Native.GetWindowRect(handle, out var compactBounds);
            Check(WindowPositioning.TryWorkArea(compactBounds, out var workArea)
                && Native.MoveWithoutActivation(handle, compactBounds.Left, workArea.Bottom - (compactBounds.Bottom - compactBounds.Top)),
                "the compact word-list tab can be moved to the bottom of its work area");
            tabHeights.Clear();
            tabs.SelectedIndex = 0;
            FinishTabTransition();
            Check(dialog.ActualHeight == ordinaryHeight && HasIntermediateHeight() == SystemParameters.ClientAreaAnimation
                && Native.GetWindowRect(handle, out var expandedBounds) && expandedBounds.Bottom <= workArea.Bottom,
                "returning to ordinary words animates back to the original height and stays on screen");
            tabs.SelectedIndex = 1;
            Pump(35);
            tabs.SelectedIndex = 0;
            FinishTabTransition();
            Check(!toggle.IsVisible && dialog.WordsPage.IsVisible && dialog.ActualHeight == ordinaryHeight,
                "a quick reversal settles at the height of the selected word-list tab");
            dialog.SizeChanged -= RecordTabHeight;

            keyboard.Loaded += (_, _) =>
            {
                Field<DispatcherTimer>(keyboard, "desktopTypingTimer").Stop();
                Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
            };
            ShowOffscreen(keyboard);
            Call(keyboard, "SetFocusedSuggestionField", ordinary, null);
            keyboard.UpdateLayout();
            Check(phoneKey.IsVisible && Math.Abs(phoneKey.ActualHeight - pageDown.ActualHeight) <= 1
                && Math.Abs(phoneKey.ActualWidth - pageDown.ActualWidth) <= 1,
                "the rendered phone key is the same size as Page Down within layout rounding");
            Render(keyboard, Path.Combine(output, "phone-key-shadow.png"));
        }
        finally
        {
            dialog.Close();
            keyboard.EnablePreviewClose();
            keyboard.Close();
            Directory.Delete(folder, true);
        }
    }

    static void PhoneNumberDialog(string output)
    {
        var mobile = new SavedPhoneNumber(new(Guid.NewGuid(), "Privat mobil", SavedTextKind.Phone, new byte[] { 1 }), "+46 70-123 45 67");
        var office = new SavedPhoneNumber(new(Guid.NewGuid(), "Jobb", SavedTextKind.Phone, new byte[] { 1 }), "08-555 10 10");
        SavedPhoneNumber selected = null;
        var dialog = new PhoneNumberWindow([mobile, office], number => { selected = number; return Task.FromResult<string>(null); });
        try
        {
            ShowOffscreen(dialog);
            var search = Field<TextBox>(dialog, "search");
            var rows = Field<StackPanel>(dialog, "rows");
            Render(dialog, Path.Combine(output, "phone-number-dialog.png"));
            search.Text = "MOBIL";
            Check(rows.Children.OfType<Button>().Count() == 1, "telephone suggestions are searchable by name regardless of case");
            search.Text = "(08) 5551010";
            Check(System.Windows.Automation.AutomationProperties.GetName(rows.Children.OfType<Button>().Single()) == "Jobb, 08-555 10 10",
                "search compares telephone digits independent of spaces and punctuation");
            search.Text = "9999";
            Check(!rows.Children.OfType<Button>().Any() && Field<TextBlock>(dialog, "status").IsVisible,
                "a nonmatching search shows a clear empty state");
            search.Text = "701234567";
            rows.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(selected == mobile && !dialog.IsVisible, "choosing a phone result returns the exact original number and closes the dialog");
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    }

    static void SecureAttentionLogic()
    {
        var user = new SecureAttentionContext(true, false, false, true, false, true, 3);
        var calls = new List<bool>();
        Check(SecureAttention.Request(user, false, calls.Add) == SecureAttentionResult.Requested && calls.SequenceEqual(new[] { true }),
            "desktop Ctrl+Alt+Delete requests SendSAS in the current user's context");
        calls.Clear();
        Check(SecureAttention.Request(user with { Locked = true }, true, calls.Add) == SecureAttentionResult.Requested && calls.Single(),
            "the restricted locked-desktop keyboard requests SAS as its signed-in user");
        calls.Clear();
        Check(SecureAttention.Request(user with { System = true, SecureDesktop = true, Locked = null }, true, calls.Add) == SecureAttentionResult.Requested && !calls.Single(),
            "the protected SYSTEM sign-in keyboard requests SAS with AsUser false");
        foreach (var scenario in new[] {
            (user with { ActiveDesktop = false }, false, SecureAttentionResult.InactiveDesktop),
            (user with { UIAccess = false }, false, SecureAttentionResult.MissingUIAccess),
            (user with { UacEnabled = false }, false, SecureAttentionResult.UacDisabled),
            (user with { Policy = 0 }, false, SecureAttentionResult.PolicyBlocked),
            (user with { Policy = 1 }, false, SecureAttentionResult.PolicyBlocked),
            (user with { Policy = 99 }, false, SecureAttentionResult.PolicyBlocked),
            (user with { Policy = null }, false, SecureAttentionResult.PolicyBlocked),
            (user with { Locked = null }, false, SecureAttentionResult.WrongSession),
            (user with { Locked = true }, false, SecureAttentionResult.WrongSession),
            (user, true, SecureAttentionResult.WrongSession),
            (user with { System = true }, true, SecureAttentionResult.WrongSession),
            (user with { SecureDesktop = true }, true, SecureAttentionResult.WrongSession) })
        {
            calls.Clear();
            Check(SecureAttention.Request(scenario.Item1, scenario.Item2, calls.Add) == scenario.Item3 && calls.Count == 0,
                "SAS prerequisites reject " + scenario.Item3 + " without calling Windows");
        }
        Check(SecureAttention.CheckSupport(user with { Policy = 2 }) == SecureAttentionResult.Ready
            && SecureAttention.CheckSupport(user with { Policy = null, SecureDesktop = true }) == SecureAttentionResult.Ready,
            "policy checks permit Ease of Access and the Windows secure-desktop default");
        var control = KeyboardLayout.Rows.SelectMany(r => r).First(k => k.Label == "Ctl");
        var alt = KeyboardLayout.Rows.SelectMany(r => r).First(k => k.Label == "Alt");
        var altGr = KeyboardLayout.Rows.SelectMany(r => r).First(k => k.Label == "AltGr");
        var delete = KeyboardLayout.NavigationKeys.Single(k => k.Label == "Del");
        Check(SecureAttention.IsChord(delete, new[] { control, alt })
            && !SecureAttention.IsChord(delete, new[] { control }) && !SecureAttention.IsChord(delete, new[] { control, altGr }),
            "the special chord requires Ctrl plus Alt and does not confuse AltGr or ordinary Ctrl+Delete");
    }

    static void SecureAttentionInterface(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-sas-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var storage = new JsonFileStorage(folder);
        storage.Save("settings.json", new Settings { AutoClick = false, AlwaysOnTop = false, DragKeyPlacement = DragKeyPlacement.Top });
        var previousGuard = Native.AdditionalInputGuard;
        Native.AdditionalInputGuard = () => false;
        try
        {
            foreach (bool restricted in new[] { false, true })
            {
                var window = new KeyboardWindow(restricted ? null : storage, restricted,
                    new Settings { AutoClick = false, AlwaysOnTop = false, DragKeyPlacement = DragKeyPlacement.Top });
                int requests = 0, ordinaryKeys = 0;
                bool releaseAllowed = true;
                var held = Field<HashSet<KeyDef>>(window, "held");
                window.GetType().GetField("secureAttentionInput", Fields).SetValue(window, (Func<bool, SecureAttentionResult>)(mode =>
                {
                    Check(mode == restricted && held.Count == 0, "SAS releases owned modifiers before using the correct keyboard context");
                    requests++; return SecureAttentionResult.Requested;
                }));
                window.GetType().GetField("modifierInput", Fields).SetValue(window, (Func<KeyDef, bool, bool>)((_, up) => !up || releaseAllowed));
                window.GetType().GetField("keyInput", Fields).SetValue(window, (Func<KeyDef, bool>)(_ => { ordinaryKeys++; return true; }));
                window.SetInputGuard(() => false);
                try
                {
                    ShowOffscreen(window);
                    Render(window, Path.Combine(output, restricted ? "sas-logon-keyboard.png" : "sas-desktop-keyboard.png"));
                    Check(restricted || (window.WindowStyle == WindowStyle.SingleBorderWindow
                        && Field<StackPanel>(window, "menuLayer").IsVisible && Field<Button[]>(window, "menuHeaders").All(b => b.IsVisible)),
                        "simplifying sign-in preserves the desktop title bar and all desktop menus");
                    Check(Field<StackPanel>(window, "menuLayer").Children.OfType<Button>().All(b => !Equals(b.Content, "Ctrl + Alt + Del")),
                        "Ctrl+Alt+Delete uses the existing keyboard keys without an extra button in " + (restricted ? "sign-in mode" : "desktop mode"));
                    window.SetInputGuard(() => true);
                    var control = KeyboardLayout.Rows.SelectMany(r => r).First(k => k.Label == "Ctl");
                    var alt = KeyboardLayout.Rows.SelectMany(r => r).First(k => k.Label == "Alt");
                    var delete = KeyboardLayout.NavigationKeys.Single(k => k.Label == "Del");
                    string idleStatus = restricted ? "Inloggning" : "Redo";
                    Call(window, "Press", control); Call(window, "Press", alt);
                    Check(held.Count == 2 && Field<TextBlock>(window, "statusText").Text == idleStatus,
                        "Ctrl and Alt stay active on their keys without being echoed in the status row");
                    Call(window, "Press", delete);
                    Check(requests == 1 && ordinaryKeys == 0 && held.Count == 0 && Field<TextBlock>(window, "statusText").Text == idleStatus,
                        "Ctrl+Alt+Delete uses SAS, clears its modifiers and leaves the status row free of key names");
                    Call(window, "Press", delete);
                    Check(ordinaryKeys == 1 && requests == 1, "ordinary Delete remains an ordinary key");
                    window.GetType().GetField("mainWindowActionsLocked", Fields).SetValue(window, true);
                    Call(window, "Press", control); Call(window, "Press", alt); Call(window, "Press", delete);
                    Check(requests == 2 && ordinaryKeys == 1, "Ctrl+Alt+Delete remains available while mouse or word-list settings lock ordinary window actions");
                    window.GetType().GetField("mainWindowActionsLocked", Fields).SetValue(window, false);
                    Call(window, "Press", control); Call(window, "Press", alt); releaseAllowed = false;
                    Call(window, "Press", delete);
                    Check(requests == 2 && ordinaryKeys == 1 && held.Count == 2, "SAS waits when owned modifier release is rejected");
                    releaseAllowed = true; Call(window, "ReleaseHeldModifiers");
                    window.SetInputGuard(() => false);
                    Call(window, "RequestSecureAttention");
                    Check(requests == 2, "an inactive desktop cannot issue a secure attention request");
                    Check(restricted ? Field<JsonFileStorage>(window, "storage") == null : true, "SAS never opens personal storage in sign-in mode");
                }
                finally { window.GetType().GetField("closeConfirmed", Fields).SetValue(window, true); window.Close(); }
            }
        }
        finally { Native.AdditionalInputGuard = previousGuard; Directory.Delete(folder, true); }
    }

    static void SignInShell(string output)
    {
        var previousGuard = Native.AdditionalInputGuard;
        Native.AdditionalInputGuard = () => false;
        var settings = new Settings { AutoClick = true, DwellMs = 5000, AlwaysOnTop = true, DragKeyPlacement = DragKeyPlacement.Top };
        settings.SmallDragKeyIcons = true;
        KeyboardLayoutSettings.Decode(new byte[] { 0, 7, 125 }).Value.ApplyTo(settings);
        var window = new KeyboardWindow(null, true, settings);
        int ready = 0;
        var display = new KeyboardStartupDisplay(window, () => ready++);
        display.Observe(true);
        bool active = true;
        window.SetInputGuard(() => active);
        int injected = 0;
        window.GetType().GetField("mouseActions", Fields).SetValue(window,
            new MouseActionState(_ => { injected++; return true; }, _ => { injected++; return true; },
                _ => { injected++; return true; }, _ => { injected++; return true; }));
        try
        {
            ShowOffscreen(window);
            display.Observe(false);
            Check(ready == 0, "the secure keyboard is not reported ready before it is displayed on the active desktop");
            display.Observe(true);
            display.Observe(true);
            Check(ready == 1, "the secure keyboard confirms its first active render exactly once");
            Check(window.WindowStyle == WindowStyle.None && !window.ShowInTaskbar,
                "sign-in has no title bar or taskbar controls");
            CheckTitlelessFrame(window, "sign-in");
            Check(Field<StackPanel>(window, "menuLayer").Visibility == Visibility.Collapsed,
                "the complete sign-in menu is hidden");
            Call(window, "OpenMouseSettings");
            Check(Field<MouseSettingsWindow>(window, "mouseWin") == null, "sign-in cannot open the retired mouse settings dialog");
            Call(window, "OpenKeyboardSettings");
            Check(Field<KeyboardSettingsWindow>(window, "keyboardSettingsWin") == null,
                "sign-in cannot open the personal keyboard settings");
            Call(window, "OpenDragKeySettings");
            var secureToolbar = Field<DragKeyWindow>(window, "dragKeyWin");
            double PreferenceScale() => secureToolbar.Surface.LayoutTransform.Value.M11 / Field<double>(window, "dragKeyScale");
            Check(Field<DragKeySettingsWindow>(window, "dragKeySettingsWin") == null
                && Math.Abs(PreferenceScale() - 0.8) < 0.001,
                $"sign-in applies the compact size ({PreferenceScale():0.000}) and blocks desktop DragKey settings");
            var signInAutoClick = Field<DragKeyButton>(secureToolbar, "autoClick");
            var symbol = (Viewbox)signInAutoClick.Content;
            Size Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize)).Size;
            var compactPanel = Bounds(secureToolbar.Surface);
            var compactButton = Bounds(signInAutoClick);
            var compactIcon = Bounds(symbol);
            var previousArea = (KeyboardAreaSize?)Call(window, "CurrentKeyboardArea");
            Call(window, "ApplySignInDragKeySize", false, null);
            Pump();
            static bool Scaled(Size compact, Size normal) => Math.Abs(compact.Width - 0.8 * normal.Width) <= 1
                && Math.Abs(compact.Height - 0.8 * normal.Height) <= 1;
            Check(Math.Abs(PreferenceScale() - 1) < 0.001 && Scaled(compactPanel, Bounds(secureToolbar.Surface))
                && Scaled(compactButton, Bounds(signInAutoClick)) && Scaled(compactIcon, Bounds(symbol)),
                "the sign-in panel, AutoClick hit area, and artwork are all 80 percent of normal on both axes");
            Call(window, "ApplySignInDragKeySize", true, null);
            Pump();
            var currentArea = (KeyboardAreaSize?)Call(window, "CurrentKeyboardArea");
            Check(currentArea is { } fitted && previousArea is { } original && Math.Abs(fitted.Width - original.Width) <= 1
                && Math.Abs(fitted.Height - original.Height) <= 1,
                $"switching the sign-in toolbar size preserves the dimensions of the main keys ({previousArea} to {currentArea})");
            var autoPoint = signInAutoClick.PointToScreen(new Point(signInAutoClick.ActualWidth / 2, signInAutoClick.ActualHeight / 2));
            var autoTarget = secureToolbar.TargetAt(autoPoint);
            Call(window, "PerformDwell", autoTarget);
            Check(autoTarget == DwellTarget.AutoClick && !settings.AutoClick && secureToolbar.TargetAt(autoPoint) == DwellTarget.AutoClick,
                "the compact AutoClick remains a dwell target while paused");
            Call(window, "PerformDwell", autoTarget);
            Check(settings.AutoClick, "the compact AutoClick can resume dwell input");
            var menus = window.GetType().GetMethod("OpenMenu", Fields, null, new[] { typeof(int), typeof(Rect) }, null);
            menus.Invoke(window, new object[] { 3, new Rect() });
            Check(!Field<System.Windows.Controls.Primitives.Popup>(window, "menuPopup").IsOpen,
                "the former mouse menu cannot reopen through an internal action");
            var grip = Field<Border>(window, "signInMoveGrip");
            Point center = grip.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
            Check(grip.IsVisible && (bool)Call(window, "IsSignInMoveAreaAt", center), "the quiet sign-in grip exposes a usable move area");
            Check(Field<Dictionary<KeyDef, Button>>(window, "btn").Keys.ToHashSet().SetEquals(
                KeyboardLayout.Rows.SelectMany(row => row).Concat(KeyboardLayout.NavigationKeys)),
                "the simplified shell retains the shared keyboard layout, including Ctrl, Alt and Delete");
            var keys = Field<Dictionary<KeyDef, Button>>(window, "btn");
            Check(keys.Values.All(key => key.Margin == new Thickness(2, 4, 2, 5)),
                "the sign-in keyboard clamps the legacy spacing and scales both axes with the legend");
            var keyGrid = Field<Grid>(window, "keyboardKeyGrid");
            double baseSize = KeyboardLayout.LegendFontSize(keyGrid.RowDefinitions.Average(row => row.ActualHeight),
                keyGrid.ColumnDefinitions[0].ActualWidth / KeyboardLayout.KeyUnitsPerRow);
            Check(keys.Values.All(key => Math.Abs(key.FontSize - baseSize * 1.25
                * (key.Content is string ? SoftKeyKeyDesign.LegendTextScale : 1)) < 0.01),
                "the sign-in keyboard applies the shared text scale at the maximum setting");
            var letter = keys.Single(pair => pair.Key.Label == "a").Value;
            Check(letter.FontWeight == FontWeights.Bold && letter.Padding.Top == 0 && letter.Padding.Bottom == 0
                && TextElement.GetFontSize(FindKeyContent(letter)) > baseSize * 1.25,
                "sign-in displays larger bold letters with less internal padding");
            var letterFace = (Border)letter.Template.FindName("KeyFace", letter);
            var originalLayout = KeyboardLayoutSettings.From(settings);
            Call(window, "ChangeKeyboardLayout", originalLayout with { FontSizePercent = 75 });
            Pump();
            Check(letterFace.ActualHeight < letter.ActualHeight && letterFace.ActualWidth == letter.ActualWidth
                && letter.InputHitTest(new Point(0.25, 0.25)) != null,
                "the sign-in keyboard reserves the inset on the available axis and keeps the full click target");
            Call(window, "ChangeKeyboardLayout", originalLayout);
            Pump();
            Native.POINT KeyPoint(string label, bool rightEdge)
            {
                var key = keys.Single(pair => pair.Key.Label == label).Value;
                var p = key.PointToScreen(new Point(rightEdge ? key.ActualWidth - 2 : 2, key.ActualHeight / 2));
                return new Native.POINT { X = (int)p.X, Y = (int)p.Y };
            }
            var a = KeyPoint("a", true);
            var s = KeyPoint("s", false);
            var aControl = DesktopDwellTargets.FindLocal(a, null);
            var sControl = DesktopDwellTargets.FindLocal(s, null);
            var tracker = new DwellTracker();
            tracker.Update(a.X, a.Y, DwellTarget.SoftKeyRepeat, 0, 800, 100, false, aControl);
            Check(Math.Abs(a.X - s.X) <= 100 && !Equals(aControl, sControl)
                && !tracker.Update(s.X, s.Y, DwellTarget.SoftKeyRepeat, 800, 800, 100, false, sControl)
                && tracker.Update(s.X, s.Y, DwellTarget.SoftKeyRepeat, 1600, 800, 100, false, sControl),
                "a new sign-in key needs a full dwell even when the pointer stays within the movement tolerance");
            Render(window, Path.Combine(output, "signin-simple-keyboard.png"));
            IntPtr handle = new WindowInteropHelper(window).Handle, foreground = Native.ForegroundWindow;
            Native.GetWindowRect(handle, out var before);
            var point = new Native.POINT { X = (int)center.X, Y = (int)center.Y };
            Check((bool)Call(window, "BeginSignInWindowDrag", point), "HeadMouse can begin a sign-in move without a Drag button");
            Check((bool)Call(window, "UpdateWindowDrag", new Native.POINT { X = point.X + 30, Y = point.Y - 40 }), "sign-in reuses the existing bounded window movement");
            Native.GetWindowRect(handle, out var after);
            Check(after.Left != before.Left || after.Top != before.Top, "the sign-in keyboard actually moves");
            Check((bool)Call(window, "FinishWindowDrag") && !Field<MouseActionState>(window, "mouseActions").IsDragging
                && injected == 0 && Native.ForegroundWindow == foreground, "the move ends without injecting a click or taking typing focus");
            Call(window, "BeginSignInWindowDrag", point); active = false;
            Check(!(bool)Call(window, "UpdateWindowDrag", point) && !Field<MouseActionState>(window, "mouseActions").IsDragging,
                "an inactive input desktop cancels sign-in movement");
            Check(!(bool)Call(window, "BeginSignInWindowDrag", point), "an inactive input desktop cannot start a sign-in move");
            active = true; Call(window, "ToggleAutoClick");
            Check(!(bool)Call(window, "BeginSignInWindowDrag", point), "paused autoclick also pauses dwell movement");
            Check(Field<JsonFileStorage>(window, "storage") == null, "sign-in movement never opens personal storage");
            CheckSmallKeyboard(window, "sign-in", output);
        }
        finally { window.EnablePreviewClose(); window.Close(); Native.AdditionalInputGuard = previousGuard; }
    }

    static void SignInKeyboardMatch(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-matched-keyboard-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        var settings = new Settings { AutoClick = false, AlwaysOnTop = false, WindowWidth = 720, WindowHeight = 270,
            HorizontalKeyGap = 3, VerticalKeyGap = 3, KeyFontSizePercent = 90, HasPersonalKeyboardLayout = true };
        storage.Save("settings.json", settings);
        var desktop = new KeyboardWindow(storage, false, null, useInstalledLayout: true);
        KeyboardWindow signIn = null;
        try
        {
            ShowOffscreen(desktop);
            var desktopArea = (KeyboardAreaSize?)Call(desktop, "CurrentKeyboardArea");
            var personalSettings = Field<Settings>(desktop, "cfg");
            Check(desktopArea != null && personalSettings.KeyboardArea == desktopArea,
                "the desktop measures the laid-out key area independently of its window frame");
            Call(desktop, "SaveSettings");
            Check(storage.Load("settings.json", new Settings()).KeyboardArea == desktopArea,
                "the measured dimensions survive the desktop settings round trip");

            var signInSettings = new Settings { AutoClick = false, KeyboardArea = desktopArea,
                DragKeyPlacement = DragKeyPlacement.Top, SmallDragKeyIcons = true };
            KeyboardLayoutSettings.From(personalSettings).ApplyTo(signInSettings);
            signIn = new KeyboardWindow(null, true, signInSettings);
            ShowOffscreen(signIn);
            static bool Near(double left, double right) => Math.Abs(left - right) <= 1.1;
            var signInArea = (KeyboardAreaSize?)Call(signIn, "CurrentKeyboardArea");
            Check(signInArea is { } fitted && Near(fitted.Width, desktopArea.Value.Width) && Near(fitted.Height, desktopArea.Value.Height),
                $"the compact sign-in frame fits the desktop key area ({desktopArea}, {signInArea})");
            var desktopKeys = Field<Dictionary<KeyDef, Button>>(desktop, "btn");
            var signInKeys = Field<Dictionary<KeyDef, Button>>(signIn, "btn");
            Check(desktopKeys.All(pair => Near(pair.Value.ActualWidth, signInKeys[pair.Key].ActualWidth)
                && Near(pair.Value.ActualHeight, signInKeys[pair.Key].ActualHeight)
                && (pair.Value.Content is not string || Near(TextElement.GetFontSize(FindKeyContent(pair.Value)),
                    TextElement.GetFontSize(FindKeyContent(signInKeys[pair.Key]))))),
                "both views match the proportions and visible legends of the shared keys, including navigation");
            Check(Field<Border>(signIn, "signInMoveGrip").IsVisible && Field<Button>(signIn, "phoneNumberKey") == null
                && Field<DragKeyButton>(Field<DragKeyWindow>(signIn, "dragKeyWin"), "autoClick").IsVisible,
                "the matched sign-in layout keeps only the move grip and Autoclick above the keys");
            Render(desktop, Path.Combine(output, "matched-desktop-keyboard.png"));
            Render(signIn, Path.Combine(output, "matched-signin-keyboard.png"));

            signIn.Width += 48;
            Pump();
            var manualSize = (KeyboardAreaSize?)Call(signIn, "CurrentKeyboardArea");
            Call(signIn, "ApplySignInKeyboardArea", desktopArea);
            Check((KeyboardAreaSize?)Call(signIn, "CurrentKeyboardArea") == manualSize && manualSize != signInArea,
                "returning to the same protected setting preserves a manual sign-in resize");
            CheckInteractiveResizeText(desktop, "desktop");
            CheckInteractiveResizeText(signIn, "sign-in");
        }
        finally
        {
            if (signIn != null) { signIn.EnablePreviewClose(); signIn.Close(); }
            typeof(KeyboardWindow).GetField("closeConfirmed", Fields).SetValue(desktop, true);
            desktop.Close();
            Directory.Delete(folder, true);
        }
    }

    static void CheckInteractiveResizeText(KeyboardWindow keyboard, string mode)
    {
        var settings = Field<Settings>(keyboard, "cfg");
        var grid = Field<Grid>(keyboard, "keyboardKeyGrid");
        var key = Field<Dictionary<KeyDef, Button>>(keyboard, "btn").Single(pair => pair.Key.Label == "a").Value;
        double ExpectedSize() => KeyboardLayout.LegendFontSize(grid.RowDefinitions.Average(row => row.ActualHeight),
            grid.ColumnDefinitions[0].ActualWidth / KeyboardLayout.KeyUnitsPerRow)
            * SoftKeyKeyDesign.LegendTextScale * settings.KeyFontSizePercent / Settings.DefaultKeyFontSizePercent;

        keyboard.Width = keyboard.MinWidth + 180;
        keyboard.Height = keyboard.MinHeight + 90;
        keyboard.UpdateLayout();
        var handle = new WindowInteropHelper(keyboard).Handle;
        double startSize = key.FontSize;

        Call(keyboard, "FilterMainWindowCommands", handle, 0x0231, IntPtr.Zero, IntPtr.Zero, false);
        keyboard.Width = keyboard.MinWidth;
        keyboard.Height = keyboard.MinHeight;
        keyboard.UpdateLayout();
        Check(key.FontSize == startSize && ExpectedSize() < startSize && ClippedKeyLegends(keyboard).Count == 0,
            $"{mode}: text still fits during native resize and defers updating its shared base size");
        Call(keyboard, "FilterMainWindowCommands", handle, 0x0232, IntPtr.Zero, IntPtr.Zero, false);
        keyboard.UpdateLayout();
        Check(Math.Abs(key.FontSize - ExpectedSize()) < 0.01 && key.FontSize < startSize,
            $"{mode}: completing native resize applies the standard size at the final dimensions");

        settings.AutoClick = true;
        keyboard.SetInputGuard(() => true);
        Field<MouseActionState>(keyboard, "mouseActions").Select(MouseAction.Drag);
        Check(Native.GetWindowRect(handle, out var bounds), $"{mode}: dwell resize starts with real window bounds");
        var start = new Native.POINT { X = bounds.Right - 1, Y = bounds.Bottom - 1 };
        startSize = key.FontSize;
        Check((bool)Call(keyboard, "BeginWindowResize", WindowResizeEdges.Right | WindowResizeEdges.Bottom, start),
            $"{mode}: dwell resize starts");
        var end = new Native.POINT { X = start.X + 180, Y = start.Y + 90 };
        Check((bool)Call(keyboard, "UpdateWindowDrag", end), $"{mode}: dwell resize changes window dimensions");
        keyboard.UpdateLayout();
        Check(key.FontSize == startSize && ExpectedSize() > startSize,
            $"{mode}: dwell resize also defers the shared base size during movement");
        Check((bool)Call(keyboard, mode == "desktop" ? "FinishWindowDrag" : "CancelMouseAction"),
            $"{mode}: dwell resize releases its gesture");
        keyboard.UpdateLayout();
        Check(Math.Abs(key.FontSize - ExpectedSize()) < 0.01 && key.FontSize > startSize,
            $"{mode}: ending dwell resize applies the standard size at the final dimensions");
    }

    static void KeyboardAccentLabels()
    {
        Check(Native.RemovePendingAccent("¨q", "¨", false) == "q"
            && Native.RemovePendingAccent("¨¨", "¨", false) == "¨",
            "pending dots are removed from the next key's legend, including an accent key itself");
        Check(Native.RemovePendingAccent("ä", "¨", false) == "a"
            && Native.RemovePendingAccent("Ë", "¨", false) == "E"
            && Native.RemovePendingAccent("u\u0308", "¨", false) == "u",
            "composed labels recover their ordinary lower- or uppercase letters");
        Check(Native.RemovePendingAccent("ä", "¨", true) == "ä"
            && Native.RemovePendingAccent("å", "¨", false) == "å"
            && Native.RemovePendingAccent("é", "´", true) == "é",
            "letters that already belong to a key keep their accents");
        Check(Native.RemovePendingAccent("ê", "^", false) == "e"
            && Native.RemovePendingAccent("ñ", "~", false) == "n"
            && Native.RemovePendingAccent("é", "'", false) == "e"
            && Native.RemovePendingAccent("ö", "\"", false) == "o"
            && Native.RemovePendingAccent("´@", "´", false) == "@",
            "the same correction handles common dead accents and symbols");
    }

    static void SecureKeyboardDesktopReturn()
    {
        long? visit = 1100;
        var restore = new SecureDesktopReturn(() => visit);
        Check(!restore.ShouldAttempt(false, false, 1000) && !restore.ShouldAttempt(false, true, 1250),
            "starting the broker off the user desktop does not arm recovery even with a secure keyboard report");
        Check(!restore.ShouldAttempt(false, true, 1500), "ordinary Task Manager use does not arm SoftKey recovery");
        Check(!restore.ShouldAttempt(false, false, 1750) && !restore.ShouldAttempt(false, true, 1850),
            "an unrelated desktop switch cannot reuse an earlier secure keyboard report");
        restore.ShouldAttempt(false, false, 1900);
        visit = 1950;
        Check(restore.ShouldAttempt(false, true, 2000),
            "returning via Cancel or Task Manager starts recovery from the secure keyboard report independent of foreground focus");
        restore.Attempted(2000);
        Check(!restore.ShouldAttempt(false, true, 2500) && restore.ShouldAttempt(false, true, 3000),
            "a busy launcher can be retried without repeatedly starting SoftKey");
        restore.Complete();
        Check(!restore.ShouldAttempt(false, true, 3250), "a successful secure keyboard return is handled only once");

        restore.ShouldAttempt(false, false, 3500);
        visit = 3600;
        restore.ShouldAttempt(true, false, 3750);
        Check(!restore.ShouldAttempt(false, false, 4000) && !restore.ShouldAttempt(false, true, 4250),
            "returning from a real lock leaves startup to the unlock setting");
        restore.ShouldAttempt(false, false, 4500);
        visit = 4600;
        restore.ShouldAttempt(null, false, 4750);
        Check(!restore.ShouldAttempt(false, true, 5000), "unknown lock status cancels secure keyboard recovery");
        restore.ShouldAttempt(false, false, 5250);
        visit = 5400;
        Check(restore.ShouldAttempt(false, true, 5500) && !restore.ShouldAttempt(false, true, 12500),
            "the same broker handles a later secure keyboard visit and expires the request after its retry window");

        restore.ShouldAttempt(false, false, 12750);
        visit = 12600; // The secure keyboard can report before the broker's first inactive poll.
        Check(restore.ShouldAttempt(false, true, 13000), "a report during the transition can precede the broker's first inactive poll");

        var restartedBroker = new SecureDesktopReturn(() => 15500);
        restartedBroker.ShouldAttempt(false, true, 15000);
        restartedBroker.ShouldAttempt(false, false, 15250);
        Check(!restartedBroker.ShouldAttempt(false, true, 15750), "a signal already present when the broker starts cannot be mistaken for a new visit");

        var fastReturn = new SecureDesktopReturn(() => visit);
        fastReturn.ShouldAttempt(false, true, 16000);
        visit = 16100;
        Check(fastReturn.ShouldAttempt(false, true, 16250),
            "a protected report survives a quick switch away and back between broker checks");
        fastReturn.Complete();
        Check(!fastReturn.ShouldAttempt(false, true, 16500), "the same rapid return cannot start SoftKey twice");
    }

    static void WordFieldReadRecovery()
    {
        var reader = new DesktopWordFieldReader();
        using var allowFirstRead = new ManualResetEventSlim(false);
        var input = new DesktopWordField((IntPtr)1, (IntPtr)2, "email", DesktopInputKind.Email);
        var waiting = reader.BeginProviderRead(1, () => { allowFirstRead.Wait(); return default; });
        try
        {
            Check(reader.BeginProviderRead(1, () => default) == null, "a stalled provider gets no duplicate reads");
            var other = reader.BeginProviderRead(2, () => (input, "a"));
            Check(other != null && other.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == (input, "a"),
                "the next app can supply a fresh field and prefix while another provider is stalled");
        }
        finally { allowFirstRead.Set(); waiting.GetAwaiter().GetResult(); }
        Check(reader.BeginProviderRead(1, () => (input, "b")).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == (input, "b"),
            "a previously stalled provider can be queried again when it responds");
    }

    static void SessionState()
    {
        var state = new DesktopSessionState();
        Check(state.ShouldSuspend(null, true) && state.ShouldSuspend(false, false), "desktop visibility fails closed for an unknown session or inactive desktop");
        Check(!state.ShouldSuspend(false, true), "the desktop keyboard is allowed only on a known unlocked active desktop");
        state.Notify(4);
        Check(state.ShouldSuspend(false, true), "a remote disconnect suspends the desktop keyboard");
        state.Notify(3);
        Check(!state.ShouldSuspend(false, true), "a reconnected unlocked active desktop can resume");
        Check(!LockSession.ShouldStopLockedKeyboard(null, true) && !LockSession.ShouldStopLockedKeyboard(true, null)
            && LockSession.ShouldStopLockedKeyboard(false, true) && LockSession.ShouldStopLockedKeyboard(null, false),
            "the locked keyboard waits through uncertainty and exits on confirmed unlock or removal");
        string marker = "test-" + Guid.NewGuid().ToString("N");
        Check(!DesktopStartupReady.IsSet(marker), "a missing readiness signal is never reported as successful");
        using (var ready = DesktopStartupReady.Create(marker))
        {
            Check(ready != null && !DesktopStartupReady.IsSet(marker), "creating the desktop instance does not confirm that it is visible");
            ready.Set();
            Check(DesktopStartupReady.IsSet(marker), "the start helper can read the desktop app's explicit ready signal");
        }
        Check(!DesktopStartupReady.IsSet(marker), "the ready signal disappears when the desktop app exits");
    }

    static void DesktopLockExit(bool viaNotification)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-lock-exit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var storage = new JsonFileStorage(folder);
        storage.Save("settings.json", new Settings { AutoClick = true, Repeat = true, DwellMs = 5000, DragKeyPlacement = DragKeyPlacement.Floating });
        var app = new Application();
        Native.AdditionalInputGuard = () => false; // The test must not inject real input.
        bool? locked = false;
        var keyboard = new KeyboardWindow(storage) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        keyboard.EnableDesktopSessionTracking(() => locked, () => true);
        bool keyboardClosed = false, toolbarClosed = false, dialogClosed = false, timedOut = false;
        keyboard.Closed += (_, _) => keyboardClosed = true;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timeout.Stop(); timedOut = true; keyboard.EnablePreviewClose(); app.Shutdown(); };
        keyboard.Loaded += (_, _) =>
        {
            var toolbar = Field<DragKeyWindow>(keyboard, "dragKeyWin");
            toolbar.Closed += (_, _) => toolbarClosed = true;
            var dialog = new Window { Owner = keyboard, ShowActivated = false, Width = 120, Height = 90,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            dialog.Closing += (_, e) => e.Cancel = true; // A busy settings dialog may reject normal closing.
            dialog.Closed += (_, _) => dialogClosed = true;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() =>
            {
                if (viaNotification) Call(keyboard, "NotifyDesktopSession", DesktopSessionMonitor.SessionLock);
                else { locked = true; Call(keyboard, "PollDesktopSession"); }
                Check(Field<bool>(keyboard, "desktopSessionClosing") && !Field<DispatcherTimer>(keyboard, "dwellTimer").IsEnabled,
                    "the lock immediately disables desktop input and initiates shutdown");
                locked = false;
                Call(keyboard, "NotifyDesktopSession", 8);
                Check(!Native.WindowIsVisible(new WindowInteropHelper(keyboard).Handle), "a quick unlock cannot restore a closing keyboard");
            }));
            dialog.ShowDialog();
        };
        try
        {
            timeout.Start();
            int exitCode = app.Run(keyboard);
            Check(!timedOut && exitCode == 0 && keyboardClosed && toolbarClosed && dialogClosed,
                "lock shuts down the application, floating DragKey and modal dialog without confirmation");
            var saved = storage.Load<Settings>("settings.json", null);
            Check(saved is { AutoClick: true, Repeat: true }, "shutdown preserves the user's autoclick settings");
        }
        finally { timeout.Stop(); Directory.Delete(folder, true); }
    }

    static void SessionWindows()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-session-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var storage = new JsonFileStorage(folder);
        var cfg = new Settings { AutoClick = true, Repeat = true, DwellMs = 5000, AlwaysOnTop = true, DragKeyPlacement = DragKeyPlacement.Floating };
        storage.Save("settings.json", cfg);
        bool? locked = false; bool active = true;
        var previousGuard = Native.AdditionalInputGuard;
        bool previousDesktopRequirement = Native.RequireActiveDesktop;
        Native.AdditionalInputGuard = () => false; // No simulated transition may inject real input.
        var keyboard = new KeyboardWindow(storage);
        int ready = 0;
        keyboard.EnableDesktopSessionTracking(() => locked, () => active, () => ready++);
        keyboard.Loaded += (_, _) => Check(ready == 0, "loading alone does not confirm desktop startup");
        var dialog = new Window { Width = 120, Height = 90, ShowActivated = false };
        IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;
        bool Visible(Window w) => Native.WindowIsVisible(Handle(w));
        void Poll() => Call(keyboard, "PollDesktopSession");
        void Notify(int reason) => Call(keyboard, "NotifyDesktopSession", reason);
        try
        {
            ShowOffscreen(keyboard); dialog.Owner = keyboard; ShowOffscreen(dialog);
            Poll();
            Check(ready == 1, "the desktop confirms startup after rendering an active and visible keyboard");
            var toolbar = Field<DragKeyWindow>(keyboard, "dragKeyWin");
            var actualSettings = Field<Settings>(keyboard, "cfg");
            Native.GetWindowRect(Handle(keyboard), out var before);
            Check(Visible(keyboard) && Visible(toolbar) && Visible(dialog), "the desktop keyboard, floating DragKey and owned dialog begin visible");
            Notify(4); // Deliberately leave the queried desktop active.
            Check(!Visible(keyboard) && !Visible(toolbar) && !Visible(dialog), "a disconnect event hides all three desktop surfaces before the queried desktop changes");
            Check(keyboard.Visibility == Visibility.Visible && dialog.Visibility == Visibility.Visible,
                "native hiding preserves the WPF visibility state needed by modal dialogs");
            Check(actualSettings.AutoClick && actualSettings.Repeat && actualSettings.AlwaysOnTop
                && !Field<DispatcherTimer>(keyboard, "dwellTimer").IsEnabled && !Field<DispatcherTimer>(keyboard, "topmostTimer").IsEnabled,
                "disconnecting pauses dwell and topmost enforcement without changing the user's autoclick preferences");
            int keys = 0, clicks = 0;
            keyboard.GetType().GetField("keyInput", Fields).SetValue(keyboard, (Func<KeyDef, bool>)(_ => { keys++; return true; }));
            keyboard.GetType().GetField("mouseActions", Fields).SetValue(keyboard,
                new MouseActionState(_ => { clicks++; return true; }, _ => { clicks++; return true; },
                    _ => { clicks++; return true; }, _ => { clicks++; return true; }));
            Call(keyboard, "Press", KeyboardLayout.Rows.SelectMany(row => row).First(k => k.Label == "a"));
            Call(keyboard, "PerformDwell", DwellTarget.External); Call(keyboard, "ToggleAutoClick");
            Check(keys == 0 && clicks == 0 && actualSettings.AutoClick, "the suspended desktop cannot type, dwell-click or change autoclick");
            var messageArguments = new object[] { Handle(keyboard), (int)SingleInstance.RestoreMessage, IntPtr.Zero, IntPtr.Zero, false };
            keyboard.GetType().GetMethod("FilterMainWindowCommands", Fields).Invoke(keyboard, messageArguments);
            Check(!Visible(keyboard), "a duplicate-start restore request cannot reveal the suspended desktop keyboard");
            locked = null; Poll();
            Notify(3); Check(!Visible(keyboard), "reconnection waits for a known unlocked session");
            locked = false; active = false; Poll(); Check(!Visible(keyboard), "reconnection waits for the user's desktop to become active");
            active = true; Poll();
            Check(Visible(keyboard) && Visible(toolbar) && Visible(dialog), "reconnection restores the same desktop keyboard, floating toolbar and dialog");
            Native.GetWindowRect(Handle(keyboard), out var after);
            Check(before.Left == after.Left && before.Top == after.Top && before.Right == after.Right && before.Bottom == after.Bottom,
                "disconnect/reconnect preserves the desktop keyboard's exact window rectangle");
            Check(actualSettings.AutoClick && actualSettings.Repeat && Field<DispatcherTimer>(keyboard, "dwellTimer").IsEnabled,
                "reconnection resumes the original autoclick choices");

            keyboard.WindowState = WindowState.Minimized; Pump();
            Notify(4); Notify(3);
            Check(keyboard.WindowState == WindowState.Minimized && Visible(toolbar), "a minimized keyboard stays minimized while its floating resume toolbar returns after reconnection");
            keyboard.WindowState = WindowState.Normal; Pump();
            Notify(4); dialog.Close(); Notify(3);
            Check(!keyboard.OwnedWindows.Cast<Window>().Contains(dialog), "a dialog closed during suspension is not reopened on reconnection");

            var modal = new CloseConfirmationWindow { Owner = keyboard, ShowActivated = false };
            int stage = 0;
            Exception modalError = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (stage++ == 0)
                    {
                        Notify(4);
                        Check(!Visible(modal), "the confirmation dialog is hidden while its modal loop remains open");
                    }
                    else
                    {
                        Notify(3);
                        Check(Visible(modal), "the same modal confirmation is restored after reconnection");
                        timer.Stop(); modal.DialogResult = false;
                    }
                }
                catch (Exception ex) { modalError = ex; timer.Stop(); modal.Close(); }
            };
            timer.Start(); modal.ShowDialog(); timer.Stop();
            if (modalError != null) throw modalError;
            Check(stage == 2, "native suspension never prematurely ends ShowDialog");
        }
        finally
        {
            keyboard.GetType().GetField("closeConfirmed", Fields).SetValue(keyboard, true);
            dialog.Close(); keyboard.Close();
            Native.AdditionalInputGuard = previousGuard; Native.RequireActiveDesktop = previousDesktopRequirement;
            Directory.Delete(folder, true);
        }
    }

    static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    static void DwellPause()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-dwell-pause-check-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        storage.Save("settings.json", new Settings { AutoClick = true, AlwaysOnTop = false });
        var keyboard = new KeyboardWindow(storage);
        keyboard.EnablePreviewClose();
        try
        {
            int clicks = 0;
            bool releaseAllowed = false;
            var mouse = new MouseActionState(_ => { clicks++; return true; }, down => down || releaseAllowed, _ => false, _ => false);
            typeof(KeyboardWindow).GetField("mouseActions", Fields).SetValue(keyboard, mouse);
            Check(mouse.Select(MouseAction.Drag) && mouse.Perform(), "the pause test starts with a held drag button");
            Call(keyboard, "ToggleAutoClick");
            Call(keyboard, "PerformDwell", DwellTarget.External);
            Call(keyboard, "ToggleAutoClick");
            var settings = Field<Settings>(keyboard, "cfg");
            Check(!settings.AutoClick && !storage.Load("settings.json", new Settings()).AutoClick
                && mouse.HeldDragButton.HasValue && clicks == 0 && Field<DispatcherTimer>(keyboard, "dwellTimer").IsEnabled,
                "pausing prevents dwell clicks and persists even when Windows has not yet released the drag");
            releaseAllowed = true;
            Call(keyboard, "Dwell", null, EventArgs.Empty);
            Check(!settings.AutoClick && !mouse.HeldDragButton.HasValue && clicks == 0,
                "the paused timer releases the remaining drag without resuming autoclick");
            Call(keyboard, "PerformDwell", DwellTarget.AutoClick);
            Check(settings.AutoClick, "the autoclick button remains a dwell target for deliberate resume");
        }
        finally
        {
            keyboard.Close();
            Directory.Delete(folder, true);
        }
    }

    static void KeyboardVisibility(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-visibility-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var storage = new JsonFileStorage(folder);
        var previousGuard = Native.AdditionalInputGuard;
        bool previousDesktopRequirement = Native.RequireActiveDesktop;
        Native.AdditionalInputGuard = () => false;
        try
        {
            foreach (var placement in Enum.GetValues<DragKeyPlacement>())
            {
                bool topDock = placement == DragKeyPlacement.Top;
                storage.Save("settings.json", new Settings { AutoClick = true, DwellMs = 5000, Repeat = true, AlwaysOnTop = true,
                    DragKeyPlacement = placement, DragKeyPosition = new WindowPosition(120, 100), DragKeyTopPosition = 1,
                    SmallDragKeyIcons = topDock, WindowWidth = topDock ? 682 : 860, WindowHeight = topDock ? 261 : 330 });
                bool? locked = false;
                var keyboard = new KeyboardWindow(storage);
                keyboard.EnableDesktopSessionTracking(() => locked, () => true);
                try
                {
                    ShowOffscreen(keyboard);
                    var toolbar = Field<DragKeyWindow>(keyboard, "dragKeyWin");
                    var toolbarGrid = Field<System.Windows.Controls.Primitives.UniformGrid>(toolbar, "buttons");
                    if (placement == DragKeyPlacement.Top)
                    {
                        var band = Field<Grid>(keyboard, "desktopHeaderBand");
                        var host = Field<Border>(keyboard, "dragKeyDockHost");
                        var menu = Field<StackPanel>(keyboard, "menuLayer");
                        var menuBounds = menu.TransformToAncestor(band).TransformBounds(new Rect(menu.RenderSize));
                        var toolbarBounds = host.TransformToAncestor(band).TransformBounds(new Rect(host.RenderSize));
                        Check(host.Parent == band && toolbarBounds.Left > menuBounds.Right
                            && Math.Abs(toolbarBounds.Top + toolbarBounds.Height / 2 - menuBounds.Top - menuBounds.Height / 2) <= 1,
                            "a right-aligned top dock shares the menu band without covering the menu");
                        var target = band.PointToScreen(new Point(toolbarBounds.Right - 30, toolbarBounds.Top + 2));
                        var match = (DragKeyDockMatch)Call(keyboard, "DockMatchFor", new Rect(target, new Size(25, 20)));
                        Check(match.Placement == DragKeyPlacement.Top, "the shared band accepts a top dock at the visible toolbar position");
                        Render(keyboard, Path.Combine(output, "dragkey-menu-band.png"));
                        Call(keyboard, "SetDragKeyPlacementAt", DragKeyPlacement.Top, 0.0); Pump();
                        Check(host.Parent == band && host.TranslatePoint(default, band).X >= menu.ActualWidth + 12,
                            "the beside setting keeps a left-positioned top dock clear of the menu in the same row");
                        Call(keyboard, "SetDragKeyPlacementAt", DragKeyPlacement.Top, 1.0); Pump();
                        Check(host.Parent == band, "moving the top dock back to the right restores the shared band");
                    }
                    bool sideDock = placement is DragKeyPlacement.Left or DragKeyPlacement.Right;
                    Check(sideDock ? toolbarGrid.Columns == 2 && toolbarGrid.Rows == 0
                            : toolbarGrid.Columns == 0 && toolbarGrid.Rows == 1,
                        placement + ": only side docking uses two columns");
                    var toggle = Field<DragKeyButton>(toolbar, "keyboardToggle");
                    var headerToggle = Field<DragKeyButton>(toolbar, "headerToggle");
                    var moveKeyboard = Field<DragKeyButton>(toolbar, "moveKeyboardEdge");
                    var power = Field<DragKeyButton>(toolbar, "power");
                    Check(power.IsVisible && power.IsEnabled && toolbar.IsPowerAt(power.PointToScreen(new Point(power.ActualWidth / 2, power.ActualHeight / 2))),
                        placement + ": the power button is visible and usable in this DragKey placement");
                    var settings = Field<Settings>(keyboard, "cfg");
                    var savedToolbar = settings.DragKeyPosition;
                    var savedKeyboard = settings.KeyboardPosition;
                    Native.GetWindowRect(new WindowInteropHelper(keyboard).Handle, out var before);
                    IntPtr foreground = Native.ForegroundWindow;
                    Call(keyboard, "ToggleAutoClick");
                    var controls = Field<System.Windows.Controls.Primitives.UniformGrid>(toolbar, "buttons").Children.OfType<DragKeyButton>().ToArray();
                    var autoClick = Field<DragKeyButton>(toolbar, "autoClick");
                    Check(!settings.AutoClick && controls.Where(b => b != autoClick).All(b => !b.IsEnabled) && autoClick.IsEnabled,
                        placement + ": paused autoclick disables every other toolbar button, including undock and keyboard visibility");
                    if (placement == DragKeyPlacement.Floating)
                        Check(((Image)((Viewbox)toggle.Content).Child).Source == DragKeySymbols.Image(DragKeySymbol.Keyboard, paused: true),
                            "a paused desktop toolbar uses gray artwork for unavailable actions");
                    Check(toolbar.TargetAt(autoClick.PointToScreen(new Point(autoClick.ActualWidth / 2, autoClick.ActualHeight / 2))) == DwellTarget.AutoClick
                        && toolbar.TargetAt(toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2))) == DwellTarget.None,
                        placement + ": only the AutoClick resume control remains a dwell target while paused");
                    Check(((Image)((Viewbox)autoClick.Content).Child).Source == DragKeySymbols.Image(DragKeySymbol.AutoClickPaused)
                        && Field<TextBlock>(keyboard, "dragKeyStatus").Text == "Autoklick pausat",
                        placement + ": the pause glyph and keyboard status describe the paused state");
                    Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboard);
                    Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboardHeader);
                    Call(keyboard, "PerformDwell", DwellTarget.UndockDragKey);
                    Check(keyboard.IsVisible && keyboard.WindowStyle == WindowStyle.SingleBorderWindow && settings.DragKeyPlacement == placement,
                        placement + ": a stale dwell action cannot hide the keyboard or its header or undock while autoclick is paused");
                    Call(keyboard, "PerformDwell", DwellTarget.AutoClick);
                    Check(settings.AutoClick && controls.Where(b => b.Visibility == Visibility.Visible).All(b => b.IsEnabled),
                        placement + ": AutoClick resumes the available toolbar buttons");
                    CheckKeyboardHeader(keyboard, toolbar, headerToggle, placement, output);
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    Check(!keyboard.IsVisible && !Native.WindowIsVisible(new WindowInteropHelper(keyboard).Handle)
                        && keyboard.WindowState == WindowState.Normal && toolbar.IsVisible,
                        placement + ": hide removes the keyboard without minimizing and leaves DragKey accessible");
                    Check(!headerToggle.IsEnabled, placement + ": the header button waits for the keyboard to become visible");
                    Check(settings.DragKeyPlacement == placement && settings.DragKeyPosition == savedToolbar
                        && settings.KeyboardPosition == savedKeyboard && settings.AutoClick && settings.Repeat,
                        placement + ": hiding preserves docking, window positions and autoclick preferences");
                    Check(System.Windows.Automation.AutomationProperties.GetName(toggle) == "Visa tangentbordet"
                        && toolbar.TargetAt(toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2))) == DwellTarget.ToggleKeyboard,
                        placement + ": the visible resume icon exposes its action to dwell and accessibility");
                    if (placement == DragKeyPlacement.Top) Render(toolbar, Path.Combine(output, "dragkey-keyboard-hidden.png"));
                    Call(keyboard, "NotifyDesktopSession", 4);
                    Check(!Native.WindowIsVisible(new WindowInteropHelper(toolbar).Handle), placement + ": disconnecting also hides the independent resume toolbar");
                    Call(keyboard, "NotifyDesktopSession", 3); Pump();
                    Check(!keyboard.IsVisible && toolbar.IsVisible && Native.WindowIsVisible(new WindowInteropHelper(toolbar).Handle),
                        placement + ": reconnecting respects the user's hidden keyboard choice");
                    Call(keyboard, "ToggleAutoClick");
                    Check(!toggle.IsEnabled && toolbar.TargetAt(toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2))) == DwellTarget.None,
                        placement + ": the highlighted show-keyboard button is disabled too when autoclick is paused");
                    Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboard);
                    Check(!keyboard.IsVisible, placement + ": a paused dwell cannot show the hidden keyboard");
                    Check(Field<TextBlock>(toolbar, "hiddenKeyboardStatus") is { IsVisible: true, Text: "Autoklick pausat" },
                        placement + ": DragKey shows the paused status when the keyboard is hidden");
                    if (placement == DragKeyPlacement.Top) Render(toolbar, Path.Combine(output, "dragkey-autoclick-paused.png"));
                    Call(keyboard, "PerformDwell", DwellTarget.AutoClick);
                    Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboard); Pump();
                    Native.GetWindowRect(new WindowInteropHelper(keyboard).Handle, out var after);
                    Check(keyboard.IsVisible && keyboard.WindowState == WindowState.Normal
                        && (Field<Border>(keyboard, "dragKeyDockHost").Child != null) == (placement != DragKeyPlacement.Floating)
                        && before.Left == after.Left && before.Top == after.Top && before.Right == after.Right && before.Bottom == after.Bottom,
                        placement + ": resume restores the same keyboard frame and its original docking");
                    Check(Native.ForegroundWindow == foreground, placement + ": hide, unlock and resume preserve the typing foreground");
                    if (placement == DragKeyPlacement.Top) Render(keyboard, Path.Combine(output, "dragkey-keyboard-top.png"));
                    if (placement == DragKeyPlacement.Left) Render(keyboard, Path.Combine(output, "dragkey-keyboard-left.png"));
                    keyboard.GetType().GetField("mainWindowActionsLocked", Fields).SetValue(keyboard, true);
                    Call(keyboard, "ToggleKeyboardVisibility");
                    Check(keyboard.IsVisible, placement + ": an open dialog prevents hiding its keyboard owner");
                    keyboard.GetType().GetField("mainWindowActionsLocked", Fields).SetValue(keyboard, false);
                    Call(keyboard, "ToggleKeyboardVisibility");
                    var messageArguments = new object[] { new WindowInteropHelper(keyboard).Handle, (int)SingleInstance.RestoreMessage, IntPtr.Zero, IntPtr.Zero, false };
                    keyboard.GetType().GetMethod("FilterMainWindowCommands", Fields).Invoke(keyboard, messageArguments);
                    Check(keyboard.IsVisible && !Field<bool>(keyboard, "keyboardHidden"), placement + ": reopening SoftKey explicitly restores a hidden keyboard");
                    keyboard.WindowState = WindowState.Minimized; Pump();
                    Call(keyboard, "ToggleKeyboardVisibility"); Pump();
                    Check(keyboard.IsVisible && keyboard.WindowState == WindowState.Normal,
                        placement + ": one press also restores a previously minimized keyboard");
                    var handle = new WindowInteropHelper(keyboard).Handle;
                    Native.GetWindowRect(handle, out var atRest);
                    Check(WindowPositioning.TryWorkArea(atRest, out var area)
                        && toolbar.TargetAt(moveKeyboard.PointToScreen(new Point(moveKeyboard.ActualWidth / 2,
                            moveKeyboard.ActualHeight / 2))) == DwellTarget.MoveKeyboardEdge,
                        placement + ": the quick-move button is a dedicated dwell target");
                    var expected = VerticalWindowSnap.JumpToEdge(atRest, area);
                    void CheckMoveArtwork(Native.RECT destination)
                    {
                        bool toTop = destination.Top == area.Top;
                        var symbol = toTop ? DragKeySymbol.KeyboardToTop : DragKeySymbol.KeyboardToBottom;
                        string label = toTop ? "Flytta tangentbordet till överkanten" : "Flytta tangentbordet till nederkanten";
                        var dockIcon = Field<Border>(Field<DragKeyDockPreview>(keyboard, "dragKeyDockPreview"), "moveKeyboardEdge");
                        Check(((Image)((Viewbox)moveKeyboard.Content).Child).Source == DragKeySymbols.Image(symbol)
                            && ((Image)((Viewbox)dockIcon.Child).Child).Source == DragKeySymbols.Image(symbol)
                            && Equals(moveKeyboard.ToolTip, label)
                            && System.Windows.Automation.AutomationProperties.GetName(moveKeyboard) == label,
                            placement + ": the button and docking preview show the next destination");
                    }
                    CheckMoveArtwork(expected);
                    foreground = Native.ForegroundWindow;
                    moveKeyboard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    Native.GetWindowRect(handle, out var moved);
                    Check(moved.Equals(expected) && Native.ForegroundWindow == foreground,
                        placement + ": one click moves to the screen edge without stealing typing focus");
                    CheckMoveArtwork(VerticalWindowSnap.JumpToEdge(moved, area));
                    Call(keyboard, "PerformDwell", DwellTarget.MoveKeyboardEdge); Pump();
                    Native.GetWindowRect(handle, out var movedAgain);
                    Check(movedAgain.Equals(VerticalWindowSnap.JumpToEdge(moved, area)) && movedAgain.Top != moved.Top,
                        placement + ": a second activation moves to the opposite edge");
                    if (movedAgain.Top != area.Top)
                    {
                        moveKeyboard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    }
                    Native.GetWindowRect(handle, out var snapped);
                    Check(snapped.Top == area.Top && keyboard.WindowStyle == WindowStyle.SingleBorderWindow,
                        placement + ": quick move reaches the upper edge with the header visible");
                    headerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    Native.GetWindowRect(handle, out var compact);
                    Check(keyboard.WindowStyle == WindowStyle.None && compact.Top == area.Top && compact.Bottom < snapped.Bottom
                        && settings.KeyboardPosition?.Y == area.Top,
                        placement + ": hiding the header keeps the keyboard and its saved position at the upper edge");
                    Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboardHeader); Pump();
                    Native.GetWindowRect(handle, out var restored);
                    Check(restored.Equals(snapped) && Native.ForegroundWindow == foreground,
                        placement + ": showing the header restores the upper-edge frame without moving typing focus");
                    if (placement == DragKeyPlacement.Right) CheckSmallKeyboard(keyboard, "desktop", output);
                }
                finally { keyboard.GetType().GetField("closeConfirmed", Fields).SetValue(keyboard, true); keyboard.Close(); }
            }
        }
        finally
        {
            Native.AdditionalInputGuard = previousGuard; Native.RequireActiveDesktop = previousDesktopRequirement;
            Directory.Delete(folder, true);
        }
    }

    static void KeyboardSpacing(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-gap-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // Older settings files had no spacing fields.
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"DwellMs\":5000,\"DragKeyPlacement\":3,\"DragKeyTopPosition\":1}");
        var storage = new JsonFileStorage(folder);
        var previousGuard = Native.AdditionalInputGuard;
        Native.AdditionalInputGuard = () => false;
        var keyboard = new KeyboardWindow(storage);
        keyboard.EnablePreviewClose();
        try
        {
            ShowOffscreen(keyboard);
            var settings = Field<Settings>(keyboard, "cfg");
            var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn");
            Button Key(string label) => keys.Single(pair => pair.Key.Label == label).Value;
            var a = Key("a");
            double originalSize = a.FontSize;
            var normalTemplate = a.Template;
            Check(settings.HorizontalKeyGap == 4 && settings.VerticalKeyGap == 4 && a.Margin == new Thickness(2),
                "older settings keep the previous spacing");

            KeyboardSettingsWindow Open()
            {
                var menu = Field<Button[]>(keyboard, "menuHeaders").Single(button => Equals(button.Content, "Tangentbord"));
                menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var submenu = Field<Border>(keyboard, "menuPanel");
                Descendants<Button>(submenu).Single(button => Equals(button.Content, "Inställningar…"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                return Field<KeyboardSettingsWindow>(keyboard, "keyboardSettingsWin");
            }
            Button ActionButton(Window window, string label) => Descendants<Button>(window).Single(button => Equals(button.Content, label));
            void Select(Window window, string label) => ActionButton(window, label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SoftKeyCheckBox Option(Window window, string label) => Descendants<SoftKeyCheckBox>(window)
                .Single(option => AutomationProperties.GetName(option) == label);
            RadioButton PlacementOption(Window window, string label) => Descendants<RadioButton>(window)
                .Single(option => AutomationProperties.GetName(option) == label);
            TabControl Tabs(Window window) => Descendants<TabControl>(window).Single();
            void OpenOtherSettings(Window window)
            {
                Tabs(window).SelectedIndex = 1;
                FinishTabTransition();
            }
            const string besideLabel = "Bredvid DragKey";
            const string aboveLabel = "Ovanför DragKey";
            const string signInLabel = "Använd samma inställningar vid Windows-inloggning";
            void WaitForUi(Func<bool> ready)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(2)) Pump();
            }

            var dialog = Open();
            Check(dialog is { IsVisible: true } && dialog.Owner == keyboard && !a.IsEnabled,
                "the settings menu opens an owned dialog without leaving typing active behind it");
            var h = Field<Slider>(dialog, "horizontal");
            var v = Field<Slider>(dialog, "vertical");
            var sample = Field<List<Button>>(dialog, "sampleKeys");
            var f = Field<Slider>(dialog, "fontSize");
            Button PreviewKey(string label) => sample.Single(key => Equals(key.Content, label));
            double HorizontalGap(Button left, Button right) => right.TranslatePoint(default, left).X - left.ActualWidth;
            double VerticalGap(Button upper, Button lower) => lower.TranslatePoint(default, upper).Y - upper.ActualHeight;
            void CheckGroups(Func<string, Button> find, string view)
            {
                Check(HorizontalGap(find("F4"), find("F5")) > HorizontalGap(find("F3"), find("F4"))
                    && HorizontalGap(find("F8"), find("F9")) > HorizontalGap(find("F7"), find("F8")),
                    view + " separates the function keys into three groups of four");
                Check(VerticalGap(find("Del"), find("Hme")) > VerticalGap(find("Ins"), find("Del"))
                    && VerticalGap(find("End"), find("PgU")) > VerticalGap(find("Hme"), find("End")),
                    view + " separates the navigation keys into three pairs");
            }
            CheckGroups(Key, "the desktop");
            CheckGroups(PreviewKey, "the layout preview");
            Check(new[] { "a", "å", "ä", "ö", "5" }.All(label => Key(label).FontWeight == FontWeights.Bold
                && PreviewKey(label).FontWeight == FontWeights.Bold && Key(label).Padding == new Thickness(0.5, 0, 0.5, 0)),
                "the desktop and preview use bold letters and digits with compact padding");
            var tabs = Tabs(dialog);
            var keyboardTab = (TabItem)tabs.Items[0];
            var otherTab = (TabItem)tabs.Items[1];
            var compactOption = PlacementOption(dialog, besideLabel);
            Check(tabs.SelectedIndex == 0 && Equals(keyboardTab.Header, "Tangentbord")
                && Equals(otherTab.Header, "Övriga inställningar") && h.IsVisible && !compactOption.IsVisible,
                "the keyboard tab opens first and leaves both menu options on the other tab");
            Border SelectionMark(TabItem tab) => (Border)tab.Template.FindName("SelectionMark", tab);
            Check(Math.Abs(keyboardTab.ActualWidth - otherTab.ActualWidth) <= 1
                && SelectionMark(keyboardTab).IsVisible && !SelectionMark(otherTab).IsVisible,
                "the tab strip has equal targets and marks only the selected tab");
            var applyButton = ActionButton(dialog, "Verkställ");
            Check(!applyButton.IsEnabled && applyButton.Opacity < 1, "Apply begins disabled and is visibly subdued");
            h.Value = 5;
            Check(applyButton.IsEnabled && applyButton.Opacity == 1, "changing a slider enables Apply");
            h.Value = 4;
            Check(!applyButton.IsEnabled, "restoring the last applied slider value disables Apply again");
            var tabCenter = otherTab.PointToScreen(new Point(otherTab.ActualWidth / 2, otherTab.ActualHeight / 2));
            Check((DwellTarget)Call(keyboard, "GetDwellTarget", new Native.POINT { X = (int)tabCenter.X, Y = (int)tabCenter.Y })
                == DwellTarget.SoftKeyControl, "the other settings tab can be targeted by Autoclick");
            double previewBase = sample[0].FontSize;
            var roomy = sample.Single(key => Equals(key.Content, "a"));
            var tight = sample.Single(key => Equals(key.Content, "s"));
            var upper = sample.Single(key => Equals(key.Content, "q"));
            Border Face(Button key) => (Border)key.Template.FindName("KeyFace", key);
            var roomyFace = Face(roomy);
            var tightFace = Face(tight);
            Vector VisibleGaps() => new(
                tightFace.TranslatePoint(new Point(), roomyFace).X - roomyFace.ActualWidth,
                roomyFace.TranslatePoint(new Point(), Face(upper)).Y - Face(upper).ActualHeight - Face(upper).Margin.Bottom);
            h.Value = v.Value = 12;
            f.Value = 75;
            Pump();
            var compactStart = roomyFace.TranslatePoint(new Point(), roomy);
            Check(compactStart.X > 0 && compactStart.Y > 0
                && Math.Abs(compactStart.X - (roomy.ActualWidth - roomyFace.ActualWidth) / 2) < 1,
                "a roomy face stays centered inside its full button");
            var smallGaps = VisibleGaps();
            Check(Math.Abs(smallGaps.X - 9) < 1 && Math.Abs(smallGaps.Y - 9) < 1
                && Descendants<TextBlock>(dialog).Count(label => label.Text == "12 → 9 px") == 2,
                $"the visible gaps include the inset and follow the smaller text on both axes ({smallGaps})");
            tight.Content = "A label that must scale down";
            Pump();
            Check(tightFace.ActualWidth == tight.ActualWidth && tightFace.TranslatePoint(new Point(), tight).X == 0,
                "a key that needs its full slot to fit the legend keeps the original face");
            var longLegend = (ContentPresenter)((KeyboardKeyLegend)tightFace.Child).Child;
            var longBounds = longLegend.TransformToAncestor(tight).TransformBounds(new Rect(longLegend.RenderSize));
            Check(TextElement.GetFontSize(longLegend) < tight.FontSize
                && Math.Abs(longBounds.Width - longLegend.ActualWidth) < 0.2,
                "narrow legends use a smaller native font with no second scaling transform");
            tight.Content = "s";
            Pump();
            Check(tightFace.ActualWidth < tight.ActualWidth, "the face is recalculated if the current legend changes");
            f.Value = 125;
            Pump();
            Check(Math.Abs(sample[0].FontSize - previewBase * 1.25) < 0.01 && a.FontSize == originalSize,
                "the font slider changes the preview immediately before applying");
            var largeGaps = VisibleGaps();
            Check(Math.Abs(largeGaps.X - 12) < 1 && Math.Abs(largeGaps.Y - 12) < 1
                && roomyFace.ActualWidth == roomy.ActualWidth,
                "at 125 percent the visible gaps respect the upper limit and the face uses the full button");
            foreach (var (label, angle, name) in new[]
            {
                ("←", -90, "Vänsterpil"), ("→", 90, "Högerpil"), ("↑", 0, "Uppåtpil"), ("↓", 180, "Nedåtpil")
            })
            {
                var arrow = Key(label);
                var graphic = (Viewbox)arrow.Content;
                var path = (System.Windows.Shapes.Path)((Canvas)graphic.Child).Children[0];
                var previewArrow = sample.Single(key => System.Windows.Automation.AutomationProperties.GetName(key) == name);
                Check(System.Windows.Automation.AutomationProperties.GetName(arrow) == name
                    && path.RenderTransform is RotateTransform turn && turn.Angle == angle
                    && path.Stroke == arrow.Foreground && path.StrokeThickness == 2
                    && previewArrow.Content is Viewbox,
                    $"the keyboard and preview use a distinct directional icon for {name}");
            }
            Check(sample.Count == KeyboardLayout.Rows.Sum(row => row.Length) + KeyboardLayout.NavigationKeys.Length
                && sample.Single(key => Equals(key.Content, "Space")).ActualWidth > sample.Single(key => Equals(key.Content, "a")).ActualWidth * 3
                && sample.All(key => !key.IsEnabled), "the miniature represents the full layout and preserves key proportions without accepting input");
            var track = (System.Windows.Controls.Primitives.Track)h.Template.FindName("PART_Track", h);
            h.Value = 0;
            v.Value = 7;
            Pump();
            var decrement = Descendants<Button>(dialog).Single(button => Equals(button.ToolTip, "Minska horisontellt mellanrum"));
            Check(track.Value == 3 && v.Minimum == 3 && !decrement.IsEnabled
                && sample.All(key => key.Margin == new Thickness(2, 4, 2, 5)) && a.Margin == new Thickness(2),
                "horizontal and vertical changes update the simulator separately before applying");
            var increment = Descendants<Button>(dialog).Single(button => Equals(button.ToolTip, "Öka horisontellt mellanrum"));
            var center = increment.PointToScreen(new Point(increment.ActualWidth / 2, increment.ActualHeight / 2));
            var point = new Native.POINT { X = (int)center.X, Y = (int)center.Y };
            var target = (DwellTarget)Call(keyboard, "GetDwellTarget", point);
            Check(target == DwellTarget.SoftKeyControl, $"slider steps can be targeted by Autoclick ({target})");
            increment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(h.Value == 4 && v.Value == 7 && sample[0].Margin == new Thickness(2, 4, 3, 5),
                "the step button moves its slider by one pixel");
            double dialogHeight = dialog.ActualHeight, dialogWidth = dialog.ActualWidth;
            var dialogHandle = new WindowInteropHelper(dialog).Handle;
            Native.GetWindowRect(dialogHandle, out var initialDialogBounds);
            var tabSizes = new List<Size>();
            void RecordTabSize(object sender, SizeChangedEventArgs change) => tabSizes.Add(change.NewSize);
            dialog.SizeChanged += RecordTabSize;
            OpenOtherSettings(dialog);
            var pendingMenuPlacement = PlacementOption(dialog, aboveLabel);
            double compactWidth = dialog.ActualWidth, compactHeight = dialog.ActualHeight;
            Check(pendingMenuPlacement.IsVisible && !h.IsVisible && dialog.ActualHeight < dialogHeight
                && dialog.ActualWidth <= dialogWidth && (dialogWidth <= 506 || dialog.ActualWidth < dialogWidth),
                "the Other settings tab reduces the dialog to the space needed for its choices");
            bool HasIntermediateSize() => tabSizes.Any(size => size.Height > compactHeight && size.Height < dialogHeight
                && (compactWidth == dialogWidth || (size.Width > compactWidth && size.Width < dialogWidth)));
            Check(HasIntermediateSize() == SystemParameters.ClientAreaAnimation,
                "shrinking animates both dimensions when Windows animations are enabled");
            Check(compactOption.ActualHeight >= 68 && pendingMenuPlacement.ActualHeight >= 68
                && Math.Abs(compactOption.ActualWidth - pendingMenuPlacement.ActualWidth) < 1
                && pendingMenuPlacement.TranslatePoint(default, compactOption).Y - compactOption.ActualHeight >= 8,
                "the menu options use separated full-width rows");
            Check(!SelectionMark(keyboardTab).IsVisible && SelectionMark(otherTab).IsVisible && applyButton.IsEnabled,
                "the selection marker follows the active tab and pending changes stay actionable across tabs");
            pendingMenuPlacement.IsChecked = true;
            Render(dialog, Path.Combine(output, "keyboard-other-settings.png"));
            Check(Native.GetWindowRect(dialogHandle, out var smallDialogBounds)
                && smallDialogBounds.Left == initialDialogBounds.Left && smallDialogBounds.Top == initialDialogBounds.Top,
                "shrinking keeps the title and tab strip at the same screen position");
            Check(WindowPositioning.TryWorkArea(initialDialogBounds, out var dialogWorkArea)
                && Native.MoveWithoutActivation(dialogHandle,
                    dialogWorkArea.Right - (smallDialogBounds.Right - smallDialogBounds.Left),
                    dialogWorkArea.Bottom - (smallDialogBounds.Bottom - smallDialogBounds.Top)),
                "the smaller dialog can be moved to the lower right edge of its monitor");
            Pump();
            tabSizes.Clear();
            tabs.SelectedIndex = 0;
            FinishTabTransition();
            Check(Math.Abs(dialog.ActualHeight - dialogHeight) < 1 && Math.Abs(dialog.ActualWidth - dialogWidth) < 1
                && Native.GetWindowRect(dialogHandle, out var expandedDialogBounds)
                && expandedDialogBounds.Right <= dialogWorkArea.Right && expandedDialogBounds.Bottom <= dialogWorkArea.Bottom,
                "returning to the larger tab restores its dimensions and keeps the buttons on screen");
            Check(HasIntermediateSize() == SystemParameters.ClientAreaAnimation,
                "growing animates both dimensions when Windows animations are enabled");
            dialog.SizeChanged -= RecordTabSize;
            Native.MoveWithoutActivation(dialogHandle, initialDialogBounds.Left, initialDialogBounds.Top);
            tabs.SelectedIndex = 1;
            Pump(35);
            tabs.SelectedIndex = 0;
            FinishTabTransition();
            Check(Math.Abs(dialog.ActualHeight - dialogHeight) < 1 && Math.Abs(dialog.ActualWidth - dialogWidth) < 1,
                "a quick reversal finishes at the size of the last selected tab");
            Check(h.IsVisible && h.Value == 4 && v.Value == 7 && pendingMenuPlacement.IsChecked == true
                && !pendingMenuPlacement.IsVisible, "both tabs keep their pending changes while switching");
            Select(dialog, "Avbryt");
            Check(!dialog.IsVisible && settings.HorizontalKeyGap == 4 && settings.VerticalKeyGap == 4 && settings.KeyFontSizePercent == 100
                && a.FontSize == originalSize && a.IsEnabled && !settings.MenuAboveDragKey
                && keyboard.WindowStyle == WindowStyle.SingleBorderWindow,
                "cancelling keeps the live keyboard and preferences unchanged");

            dialog = Open();
            h = Field<Slider>(dialog, "horizontal");
            v = Field<Slider>(dialog, "vertical");
            f = Field<Slider>(dialog, "fontSize");
            h.Value = v.Value = 3;
            f.Value = 120;
            Pump();
            sample = Field<List<Button>>(dialog, "sampleKeys");
            Check(sample.All(key => key.Margin == new Thickness(2) && key.Template == normalTemplate
                && key.Template.FindName("KeyFace", key) is Border face && face.CornerRadius.Equals(SoftKeyKeyDesign.Radius)),
                "the simulator scales the selected minimum gaps at 120 percent and shows the shared rounded faces");
            var installedLayout = KeyboardLayoutSettings.ReadInstalled();
            Select(dialog, "Verkställ");
            WaitForUi(() => h.IsEnabled);
            var saved = storage.Load("settings.json", new Settings());
            var right = a.TranslatePoint(new Point(a.ActualWidth, 0), keyboard);
            var next = Key("s").TranslatePoint(new Point(), keyboard);
            var keyAbove = Key("q");
            var rowEdge = keyAbove.TranslatePoint(new Point(0, keyAbove.ActualHeight), keyboard);
            var nextRow = a.TranslatePoint(new Point(), keyboard);
            Check(dialog.IsVisible && saved.HasPersonalKeyboardLayout && KeyboardLayoutSettings.ReadInstalled() == installedLayout
                && saved.HorizontalKeyGap == 3 && saved.VerticalKeyGap == 3 && saved.KeyFontSizePercent == 120
                && saved.DwellMs == 5000 && Math.Abs(a.FontSize - originalSize * 1.2) < 0.01
                && Math.Abs(next.X - right.X - 4) < 1 && Math.Abs(nextRow.Y - rowEdge.Y - 4) < 1,
                "applying saves the desktop layout without changing sign-in settings or closing the dialog");
            Check(!ActionButton(dialog, "Verkställ").IsEnabled, "a successful save disables Apply until another change");
            Select(dialog, "OK");
            WaitForUi(() => !dialog.IsVisible);
            Check(!dialog.IsVisible, "OK closes the settings dialog after a successful save");
            var installedDesktop = new KeyboardWindow(storage, false, null, useInstalledLayout: true);
            installedDesktop.EnablePreviewClose();
            Check(KeyboardLayoutSettings.From(Field<Settings>(installedDesktop, "cfg")) == new KeyboardLayoutSettings(3, 3, 120),
                "the installed desktop restores the personal layout after reopening");
            installedDesktop.Close();
            Check(a.InputHitTest(new Point(0.25, 0.25)) != null && Key("Enter").Background != Key("F1").Background
                && Key("F1").Background != a.Background && a.FontSize == Key("Enter").FontSize
                && keys.Where(pair => pair.Key.Label == "Sft").All(pair => pair.Value.Background == Key("Cap").Background),
                "rounded keys keep their corner targets, consistent legend size and distinct color groups");
            dialog = Open();
            Check(Field<Slider>(dialog, "horizontal").Value == 3 && Field<Slider>(dialog, "vertical").Value == 3
                && Field<Slider>(dialog, "fontSize").Value == 120, "reopening the dialog shows all three applied settings");
            Render(keyboard, Path.Combine(output, "keyboard-minimum-spacing.png"));

            OpenOtherSettings(dialog);
            var above = PlacementOption(dialog, aboveLabel);
            var beside = PlacementOption(dialog, besideLabel);
            var menu = Field<StackPanel>(keyboard, "menuLayer");
            var band = Field<Grid>(keyboard, "desktopHeaderBand");
            var dock = Field<Border>(keyboard, "dragKeyDockHost");
            var keyboardArea = Field<Grid>(keyboard, "keyboardKeyGrid");
            double beforeKeyHeight = keyboardArea.ActualHeight;
            var menuApply = ActionButton(dialog, "Verkställ");
            Check(beside.IsChecked == true && above.IsChecked == false && dock.Parent == band && !menuApply.IsEnabled,
                "older profiles show the menu beside a top-docked DragKey by default");
            above.IsChecked = true;
            Check(menuApply.IsEnabled, "changing only the menu placement enables Apply");
            beside.IsChecked = true;
            Check(!menuApply.IsEnabled, "restoring the menu placement disables Apply again");
            above.IsChecked = true;
            Select(dialog, "Verkställ");
            WaitForUi(() => above.IsEnabled);
            Check(above.IsEnabled && beside.IsChecked == false && keyboard.WindowStyle == WindowStyle.SingleBorderWindow
                && menu.IsVisible && dock.Parent == Field<Grid>(keyboard, "dragKeyLayout")
                && dock.TranslatePoint(default, band).Y >= band.ActualHeight
                && Math.Abs(keyboardArea.ActualHeight - beforeKeyHeight) <= 1
                && storage.Load("settings.json", new Settings()).MenuAboveDragKey
                && KeyboardLayoutSettings.ReadInstalled() == installedLayout,
                "applying Above puts the menu on its own row without changing key height or sign-in settings");
            Render(keyboard, Path.Combine(output, "menu-above-dragkey.png"));
            dialog.Close();
            var handle = new WindowInteropHelper(keyboard).Handle;
            Native.GetWindowRect(handle, out var fullFrame);
            var headerToggle = Field<DragKeyButton>(Field<DragKeyWindow>(keyboard, "dragKeyWin"), "headerToggle");
            headerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Native.GetWindowRect(handle, out var compactFrame);
            Check(keyboard.WindowStyle == WindowStyle.None && !menu.IsVisible && band.ActualHeight == 0
                && dock.IsVisible && compactFrame.Bottom - compactFrame.Top < fullFrame.Bottom - fullFrame.Top
                && Math.Abs(keyboardArea.ActualHeight - beforeKeyHeight) <= 1
                && AutomationProperties.GetName(headerToggle) == "Visa titelrad",
                "hiding the title also removes the Above menu row and preserves the dock and key height");
            CheckTitlelessFrame(keyboard, "titleless desktop with a hidden menu");
            Render(keyboard, Path.Combine(output, "menu-above-dragkey-hidden.png"));
            var restored = new KeyboardWindow(storage);
            restored.EnablePreviewClose();
            try
            {
                ShowOffscreen(restored);
                Check(restored.WindowStyle == WindowStyle.None && !Field<StackPanel>(restored, "menuLayer").IsVisible
                    && Field<Settings>(restored, "cfg").MenuAboveDragKey,
                    "restarting restores both the Above placement and the hidden title bar");
            }
            finally { restored.Close(); }
            headerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(menu.IsVisible && keyboard.WindowStyle == WindowStyle.SingleBorderWindow
                && Native.GetWindowRect(handle, out var restoredFrame) && restoredFrame.Equals(fullFrame),
                "restoring the title also brings back the Above menu at its original frame");
            dialog = Open();
            OpenOtherSettings(dialog);
            Check(PlacementOption(dialog, aboveLabel).IsChecked == true, "the menu placement remains selected after toggling the title bar");
            PlacementOption(dialog, besideLabel).IsChecked = true;
            Select(dialog, "OK");
            WaitForUi(() => !dialog.IsVisible);
            headerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(keyboard.WindowStyle == WindowStyle.None && menu.IsVisible && dock.Parent == band
                && !storage.Load("settings.json", new Settings()).MenuAboveDragKey,
                "Beside keeps the menu visible alongside the toolbar in compact mode");
            dialog = Open();
            OpenOtherSettings(dialog);
            above = PlacementOption(dialog, aboveLabel);
            above.IsChecked = true;
            Select(dialog, "Verkställ");
            WaitForUi(() => above.IsEnabled);
            Check(keyboard.WindowStyle == WindowStyle.None && !menu.IsVisible && dock.Parent != band,
                "changing to Above with the title already hidden also hides the menu immediately");
            beside = PlacementOption(dialog, besideLabel);
            beside.IsChecked = true;
            Select(dialog, "OK");
            WaitForUi(() => !dialog.IsVisible);
            Check(keyboard.WindowStyle == WindowStyle.None && menu.IsVisible && dock.Parent == band,
                "switching back to Beside shows the menu without changing title-bar visibility");
            headerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();

            var sharingSettings = JsonSerializer.Deserialize<Settings>("{\"ShareKeyboardLayoutAtSignIn\":true}");
            var remembered = new KeyboardSettingsWindow(sharingSettings, true, (_, _, _) => Task.FromResult(true));
            Check(Option(remembered, signInLabel).IsChecked == true,
                "the installed keyboard settings dialog restores the saved sign-in choice");
            remembered.Close();

            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = new List<(KeyboardLayoutSettings Layout, bool SignIn)>();
            var sharedSettings = new Settings();
            var sharedDialog = new KeyboardSettingsWindow(sharedSettings, true, (layout, signIn, _) =>
            {
                requests.Add((layout, signIn));
                layout.ApplyTo(sharedSettings);
                sharedSettings.ShareKeyboardLayoutAtSignIn = signIn;
                return pending.Task;
            }) { Owner = keyboard };
            var otherDialog = new Window { Owner = keyboard, Topmost = keyboard.Topmost, ShowActivated = false,
                ShowInTaskbar = false, Width = 240, Height = 160 };
            var settingsDialogField = typeof(KeyboardWindow).GetField("keyboardSettingsWin", Fields);
            settingsDialogField.SetValue(keyboard, sharedDialog);
            try
            {
                ShowOffscreen(sharedDialog);
                ShowOffscreen(otherDialog);
                Render(sharedDialog, Path.Combine(output, "keyboard-spacing.png"));
                var signIn = Option(sharedDialog, signInLabel);
                var sharedApply = ActionButton(sharedDialog, "Verkställ");
                Select(sharedDialog, "Verkställ");
                Check(signIn.IsChecked == false && !sharedApply.IsEnabled && requests.Count == 0,
                    "a clean Apply does not request a save");
                signIn.IsChecked = true;
                Check(sharedApply.IsEnabled, "changing only the sign-in checkbox enables Apply");
                signIn.IsChecked = false;
                Check(!sharedApply.IsEnabled, "undoing the sign-in checkbox change disables Apply");
                h = Field<Slider>(sharedDialog, "horizontal");
                v = Field<Slider>(sharedDialog, "vertical");
                f = Field<Slider>(sharedDialog, "fontSize");
                h.Value = v.Value = 3;
                f.Value = 75;
                Pump();
                Check(Field<List<Button>>(sharedDialog, "sampleKeys").All(key => key.Margin == new Thickness(1, 1, 2, 2)),
                    "a small font cannot take the gaps below three pixels");
                h.Value = v.Value = 8;
                Pump();
                Render(sharedDialog, Path.Combine(output, "keyboard-spacing-small-text.png"));
                h.Value = v.Value = 3;
                f.Value = 125;
                Pump();
                Render(sharedDialog, Path.Combine(output, "keyboard-spacing-minimum.png"));
                Select(sharedDialog, "Verkställ");
                WaitForUi(() => requests.Count == 1);
                KeyboardSettingsProgressWindow ActiveCover() => keyboard.OwnedWindows.OfType<KeyboardSettingsProgressWindow>().Single();
                var progress = ActiveCover();
                var keyboardContent = (UIElement)keyboard.Content;
                bool AboveCover(Window dialog) => Native.IsWindowAbove(new WindowInteropHelper(dialog).Handle,
                    new WindowInteropHelper(progress).Handle);
                bool CoversKeyboard()
                {
                    var keyboardHandle = new WindowInteropHelper(keyboard).Handle;
                    var progressHandle = new WindowInteropHelper(progress).Handle;
                    return Native.GetWindowRect(keyboardHandle, out var bounds)
                        && Native.GetWindowRect(progressHandle, out var cover)
                        && bounds.Left == cover.Left && bounds.Top == cover.Top
                        && bounds.Right == cover.Right && bounds.Bottom == cover.Bottom
                        && Native.IsWindowAbove(progressHandle, keyboardHandle);
                }
                Check(requests.Single() == (new KeyboardLayoutSettings(3, 3, 125), false)
                    && progress.IsVisible && !progress.ShowActivated && progress.Owner == keyboard
                    && !h.IsEnabled && !v.IsEnabled && !f.IsEnabled && !signIn.IsEnabled && !sharedApply.IsEnabled,
                    "a desktop save shows progress owned by the keyboard and prevents repeat input");
                var bar = Descendants<ProgressBar>(progress).Single();
                Check(bar.IsIndeterminate && !progress.AllowsTransparency && CoversKeyboard() && keyboardContent.Opacity == 0
                    && AboveCover(sharedDialog) && AboveCover(otherDialog),
                    "the opaque progress bar covers the topmost keyboard without covering either dialog");
                var segment = (Border)bar.Template.FindName("PART_GlowRect", bar);
                var start = segment.Margin.Left;
                keyboard.Width += 48;
                WaitForUi(CoversKeyboard);
                Call(keyboard, "StartTopmostEnforcement");
                Call(keyboard, "KeepSoftKeyOnTop");
                Pump(180);
                Check(CoversKeyboard() && keyboardContent.Opacity == 0 && segment.Margin.Left != start
                    && AboveCover(sharedDialog) && AboveCover(otherDialog),
                    "resizing and resetting SoftKey's topmost order leave the animated cover below both dialogs");
                Render(progress, Path.Combine(output, "keyboard-save-progress.png"));
                pending.SetResult(true);
                WaitForUi(() => !progress.IsVisible && signIn.IsEnabled);
                Check(sharedDialog.IsVisible && !progress.IsVisible && signIn.IsEnabled && !sharedApply.IsEnabled && keyboardContent.Opacity == 1,
                    "the desktop keyboard becomes visible again when the save completes");

                pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                signIn.IsChecked = true;
                settings.AlwaysOnTop = otherDialog.Topmost = sharedDialog.Topmost = keyboard.Topmost = false;
                Select(sharedDialog, "OK");
                WaitForUi(() => requests.Count == 2);
                progress = ActiveCover();
                Check(requests.Count == 2 && requests[1] == (new KeyboardLayoutSettings(3, 3, 125), true)
                    && progress.IsVisible && !signIn.IsEnabled,
                    "the checked choice requests sign-in settings and shows progress until Windows responds");
                Check(!progress.Topmost && CoversKeyboard() && AboveCover(sharedDialog) && AboveCover(otherDialog),
                    "the cover also sits below both dialogs when Always on top is disabled");
                Render(progress, Path.Combine(output, "keyboard-signin-save-progress.png"));
                pending.SetException(new InvalidOperationException("Ändringen avbröts i Windows."));
                WaitForUi(() => !progress.IsVisible && signIn.IsEnabled);
                Check(sharedDialog.IsVisible && !progress.IsVisible && keyboardContent.Opacity == 1 && signIn.IsEnabled && signIn.IsChecked == true
                    && sharedApply.IsEnabled && sharedSettings.ShareKeyboardLayoutAtSignIn
                    && h.IsEnabled && v.IsEnabled && f.IsEnabled && h.Value == 3 && v.Value == 3 && f.Value == 125
                    && Field<TextBlock>(sharedDialog, "error").Text == "Ändringen avbröts i Windows.",
                    "declining approval keeps the preview available for a retry");
                pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Select(sharedDialog, "OK");
                WaitForUi(() => requests.Count == 3);
                progress = ActiveCover();
                pending.SetResult(true);
                WaitForUi(() => !sharedDialog.IsVisible);
                Check(requests.Count == 3 && !sharedDialog.IsVisible && !progress.IsVisible,
                    "a successful sign-in save closes progress and then closes the dialog from OK");
            }
            finally
            {
                settingsDialogField.SetValue(keyboard, null);
                otherDialog.Close();
                sharedDialog.Close();
            }
        }
        finally
        {
            keyboard.Close();
            Native.AdditionalInputGuard = previousGuard;
            Directory.Delete(folder, true);
        }
    }

    static ContentPresenter FindKeyContent(DependencyObject visual)
    {
        if (visual is ContentPresenter content) return content;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (FindKeyContent(VisualTreeHelper.GetChild(visual, i)) is { } child) return child;
        return null;
    }

    static List<string> ClippedKeyLegends(KeyboardWindow keyboard)
    {
        var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn").Values;
        var clipped = new List<string>();
        foreach (var key in keys)
        {
            if (key.Content is not string { Length: > 0 } legend) continue;
            var typeface = new Typeface(key.FontFamily, key.FontStyle, key.FontWeight, key.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(key);
            var content = FindKeyContent(key);
            double actualSize = TextElement.GetFontSize(content);
            var text = new FormattedText(legend, System.Globalization.CultureInfo.CurrentUICulture,
                key.FlowDirection, typeface, actualSize, key.Foreground, null, TextFormattingMode.Display, dpi.PixelsPerDip);
            var bounds = content.TransformToAncestor(key).TransformBounds(new Rect(content.RenderSize));
            if (actualSize > key.FontSize + 0.51 / dpi.PixelsPerDip || text.Width > content.ActualWidth + 1 / dpi.DpiScaleX
                || bounds.Left < 0 || bounds.Right > key.ActualWidth || bounds.Bottom > key.ActualHeight)
                clipped.Add(legend);
        }
        return clipped;
    }

    static void CheckSmallKeyboard(KeyboardWindow keyboard, string mode, string output)
    {
        keyboard.Width = keyboard.MinWidth;
        keyboard.Height = keyboard.MinHeight;
        Pump();
        var clipped = ClippedKeyLegends(keyboard);
        Check(clipped.Count == 0, $"{mode}: legends fit at the smallest keyboard size ({string.Join(", ", clipped)})");
        var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn").Values;

        static double Luminance(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            static double Linear(byte value)
            {
                double s = value / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        var deck = Field<Grid>(keyboard, "keyboardKeyGrid");
        var tray = deck.Children.OfType<Border>().Single(border => border.Background is SolidColorBrush { Color.A: 255 });
        var surround = Luminance(tray.Background);
        Check(keys.All(key => Contrast(surround, Luminance(key.BorderBrush)) >= 1.45
            && Contrast(Luminance(key.Background), Luminance(key.Foreground)) >= 7),
            mode + ": key outlines and legends remain distinct at minimum size");
        Render(keyboard, Path.Combine(output, "keyboard-minimum-" + mode + ".png"));
    }

    static void DragKeyPower(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-drag-power-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        storage.Save("settings.json", new Settings { AutoClick = true, DwellMs = 5000, DragKeyPlacement = DragKeyPlacement.Floating });
        var previousGuard = Native.AdditionalInputGuard;
        Native.AdditionalInputGuard = () => false;
        var keyboard = new KeyboardWindow(storage);
        bool closed = false;
        keyboard.Closed += (_, _) => closed = true;
        try
        {
            ShowOffscreen(keyboard);
            var toolbar = Field<DragKeyWindow>(keyboard, "dragKeyWin");
            var power = Field<DragKeyButton>(toolbar, "power");
            Point PowerPoint() => power.PointToScreen(new Point(power.ActualWidth / 2, power.ActualHeight / 2));
            foreach (var selected in Enum.GetValues<MouseAction>())
            {
                toolbar.Refresh(true, selected, dragging: true, actionsAvailable: false);
                Check(power.IsEnabled && toolbar.TargetAt(PowerPoint()) == DwellTarget.CloseSoftKey && !toolbar.IsMoveAreaAt(PowerPoint()),
                    selected + ": power stays a dedicated dwell action regardless of the mouse mode or an open dialog");
            }
            toolbar.Refresh(false, MouseAction.Left, false, true);
            Check(!power.IsEnabled && !toolbar.IsPowerAt(PowerPoint()) && ((Image)((Viewbox)power.Content).Child).Source == DragKeySymbols.Image(DragKeySymbol.Power, paused: true),
                "the power button uses paused artwork and has no dwell action with Autoclick off");
            Call(keyboard, "RefreshDragKey");
            Render(toolbar, Path.Combine(output, "dragkey-power.png"));

            Window Answer(Action request, bool confirmed, bool fromDwellTick = false)
            {
                Window owner = null;
                var timer = fromDwellTick ? Field<DispatcherTimer>(keyboard, "dwellTimer")
                    : new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                bool requested = !fromDwellTick, answered = false, timedOut = false;
                int modalTicks = 0;
                DwellTarget answerTarget = DwellTarget.None;
                void AnswerTick(object sender, EventArgs args)
                {
                    if (!requested) { requested = true; request(); return; }
                    if (Field<CloseConfirmationWindow>(keyboard, "closeConfirmation") is not { IsVisible: true } dialog) return;
                    if (fromDwellTick && ++modalTicks < 2) return;
                    owner = dialog.Owner;
                    var button = Descendants<Button>(dialog).Single(b => Equals(b.Content, confirmed ? "Stäng" : "Avbryt"));
                    if (fromDwellTick)
                    {
                        Point p = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                        answerTarget = (DwellTarget)Call(keyboard, "GetDwellTarget", new Native.POINT { X = (int)p.X, Y = (int)p.Y });
                    }
                    answered = true;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                watchdog.Tick += (_, _) =>
                {
                    watchdog.Stop();
                    timedOut = true;
                    if (Field<CloseConfirmationWindow>(keyboard, "closeConfirmation") is { IsVisible: true } dialog)
                        dialog.DialogResult = false;
                };
                timer.Tick += AnswerTick;
                if (!fromDwellTick) timer.Start();
                watchdog.Start();
                try
                {
                    if (fromDwellTick) while (!answered && !timedOut) Pump(50);
                    else request();
                }
                finally
                {
                    timer.Tick -= AnswerTick;
                    if (!fromDwellTick) timer.Stop();
                    watchdog.Stop();
                }
                if (fromDwellTick) Check(!timedOut && modalTicks >= 2 && answerTarget == DwellTarget.SoftKeyControl,
                    "the real Autoclick timer keeps ticking in the confirmation and can reach its " + (confirmed ? "Close" : "Cancel") + " button");
                return owner;
            }

            Call(keyboard, "ToggleKeyboardVisibility"); Pump();
            var gestures = new MouseActionState(_ => true, _ => true, _ => true, _ => true);
            typeof(KeyboardWindow).GetField("mouseActions", Fields).SetValue(keyboard, gestures);
            gestures.Select(MouseAction.Drag);
            gestures.BeginWindowDrag();
            var pos = PowerPoint();
            Check((DwellTarget)Call(keyboard, "GetDwellTarget", new Native.POINT { X = (int)pos.X, Y = (int)pos.Y }) == DwellTarget.CloseSoftKey,
                "power takes precedence over the drop action while moving a window");
            Action dwellPower = () => Call(keyboard, "PerformDwell", DwellTarget.CloseSoftKey);
            var dismissed = Answer(dwellPower, confirmed: false, fromDwellTick: true);
            Check(dismissed == toolbar && !gestures.IsDragging && !closed && !keyboard.IsVisible && toolbar.IsVisible,
                "dwell cancels a drag and confirms from the reachable toolbar while the keyboard is hidden");

            Call(keyboard, "ToggleKeyboardVisibility"); Pump();
            Call(keyboard, "OpenDragKeySettings");
            var settings = Field<DragKeySettingsWindow>(keyboard, "dragKeySettingsWin");
            Check(settings is { IsVisible: true } && power.IsEnabled, "the power button stays enabled while DragKey settings are open");
            Action clickPower = () => power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Answer(clickPower, confirmed: false) == settings && settings.IsVisible
                && Field<bool>(keyboard, "mainWindowActionsLocked"), "cancelling the power action returns to the open settings dialog");
            Check(Answer(dwellPower, confirmed: true, fromDwellTick: true) != null && closed && !toolbar.IsVisible && !settings.IsVisible,
                "confirming the power action closes SoftKey, DragKey and its open settings");
        }
        finally
        {
            if (!closed) { keyboard.EnablePreviewClose(); keyboard.Close(); }
            Native.AdditionalInputGuard = previousGuard;
            Directory.Delete(folder, true);
        }
    }

    static void CheckKeyboardHeader(KeyboardWindow keyboard, DragKeyWindow toolbar, DragKeyButton toggle,
        DragKeyPlacement placement, string output)
    {
        var handle = new WindowInteropHelper(keyboard).Handle;
        var settings = Field<Settings>(keyboard, "cfg");
        var menu = Field<StackPanel>(keyboard, "menuLayer");
        var keys = Field<FrameworkElement>(keyboard, "keyboardSurface");
        var dock = Field<Border>(keyboard, "dragKeyDockHost");
        var foreground = Native.ForegroundWindow;
        Native.GetWindowRect(handle, out var before);
        bool atTopEdge = WindowPositioning.TryWorkArea(before, out var area) && before.Top == area.Top;
        double minimum = keyboard.MinHeight, keyHeight = keys.ActualHeight, savedHeight = settings.WindowHeight;
        var savedPosition = settings.KeyboardPosition;
        Point button = toggle.PointToScreen(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
        Check(toolbar.TargetAt(button) == DwellTarget.ToggleKeyboardHeader && !toolbar.IsMoveAreaAt(button),
            placement + ": the header button supports dwell and is separate from the toolbar drag area");
        var mouseMenu = Field<Button[]>(keyboard, "menuHeaders").Single(header => Equals(header.Content, "Mus"));
        mouseMenu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var popup = Field<System.Windows.Controls.Primitives.Popup>(keyboard, "menuPopup");
        Check(popup.IsOpen, placement + ": the desktop menu is available before collapsing the header");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Native.GetWindowRect(handle, out var compact);
        Check(keyboard.WindowStyle == WindowStyle.None && menu.IsVisible && !popup.IsOpen
            && keyboard.IsVisible && keys.IsVisible && compact.Bottom - compact.Top < before.Bottom - before.Top
            && (atTopEdge ? compact.Top == before.Top : compact.Bottom == before.Bottom)
            && compact.Left == before.Left && compact.Right == before.Right,
            placement + ": the click removes the title bar, keeps the menu, and preserves the window anchor");
        CheckTitlelessFrame(keyboard, placement + " compact desktop");
        Check(Math.Abs(keys.ActualHeight - keyHeight) <= 1 && keyboard.MinHeight < minimum
            && Math.Abs(settings.WindowHeight - savedHeight) <= 1 && settings.KeyboardPosition == savedPosition
            && (dock.Child != null) == (placement != DragKeyPlacement.Floating),
            placement + ": compact mode preserves key size, saved full dimensions, and docking");
        Check(System.Windows.Automation.AutomationProperties.GetName(toggle) == "Visa titelrad",
            placement + ": accessibility exposes the action to show the title bar");
        mouseMenu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(popup.IsOpen && popup.PlacementTarget == menu && Native.GetWindowRect(handle, out var menuOpenBounds)
            && menuOpenBounds.Equals(compact), placement + ": the menu opens without shifting the titleless window");
        Call(keyboard, "CloseMenu");
        if (placement == DragKeyPlacement.Top)
        {
            var band = Field<Grid>(keyboard, "desktopHeaderBand");
            Check(dock.Parent == band, "the titleless keyboard keeps the top dock in the menu band");
            Render(keyboard, Path.Combine(output, "dragkey-compact-keyboard.png"));
            var topSurface = toolbar.Surface.PointToScreen(new Point(toolbar.Surface.ActualWidth / 2, 1));
            Check(Native.HitTestAt(new Native.POINT { X = (int)topSurface.X, Y = (int)topSurface.Y }, handle) == Native.HtClient,
                "the compact toolbar receives pointer input up to its top edge");
            var originalSmall = settings.SmallDragKeyIcons;
            try
            {
                foreach (bool small in new[] { true, false })
                {
                    settings.SmallDragKeyIcons = small;
                    Call(keyboard, "ApplyDragKeyButtons");
                    keyboard.Width = keyboard.MinWidth;
                    Pump();
                    Check(dock.Parent == band && dock.TranslatePoint(default, band).X
                        - menu.TranslatePoint(new Point(menu.ActualWidth, 0), band).X >= 12,
                        $"the {(small ? "small" : "normal")} top dock keeps space beside the menu at minimum width");
                }
            }
            finally
            {
                settings.SmallDragKeyIcons = originalSmall;
                Call(keyboard, "ApplyDragKeyButtons");
                Native.ResizeWithoutActivation(handle, compact.Left, compact.Top, compact.Right - compact.Left, compact.Bottom - compact.Top);
                Pump();
            }
            Call(keyboard, "ToggleWordMenu", new Point(0, 20));
            Check(popup.IsOpen && popup.PlacementTarget == Field<Border>(keyboard, "wordMenuAnchor"),
                "the word menu stays anchored to the keyboard in compact mode");
        }
        Call(keyboard, "PerformDwell", DwellTarget.ToggleKeyboardHeader); Pump();
        Native.GetWindowRect(handle, out var after);
        Check(keyboard.WindowStyle == WindowStyle.SingleBorderWindow && menu.IsVisible && after.Equals(before)
            && Math.Abs(keyboard.MinHeight - minimum) < 0.01 && Native.ForegroundWindow == foreground,
            placement + ": a dwell restores the exact frame, menu and title bar without taking focus");
        if (placement != DragKeyPlacement.Right) return;

        keyboard.Height = keyboard.MinHeight; Pump();
        double fullHeight = keyboard.ActualHeight;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        double compactHeight = keyboard.ActualHeight;
        Check(compactHeight < fullHeight, "a keyboard at its minimum height still becomes shorter in compact mode");
        keyboard.Height = compactHeight + 24; Pump();
        Call(keyboard, "SaveSettings");
        var storage = Field<JsonFileStorage>(keyboard, "storage");
        var saved = storage.Load("settings.json", new Settings());
        Check(Math.Abs(saved.WindowHeight - (fullHeight + 24)) <= 1,
            "resizing in compact mode saves the corresponding full keyboard height");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Math.Abs(keyboard.ActualHeight - saved.WindowHeight) <= 1,
            "restoring the header keeps the height added while compact");
        Native.ResizeWithoutActivation(handle, before.Left, before.Top, before.Right - before.Left, before.Bottom - before.Top);
        Pump();
    }

    static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -10000;
        window.Loaded += (_, _) => { window.Left = window.Top = -10000; };
        window.Show(); Pump();
    }

    static void CheckTitlelessFrame(Window window, string mode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        double clientTop = window.PointToScreen(default).Y;
        Check(Native.GetWindowRect(handle, out var bounds) && Native.GetClientRect(handle, out var client)
            && client.Right - client.Left == bounds.Right - bounds.Left
            && client.Bottom - client.Top == bounds.Bottom - bounds.Top && Math.Abs(clientTop - bounds.Top) < 1,
            $"{mode}: SoftKey draws the whole titleless window (client {clientTop:0.##}, window {bounds.Top})");
        var edge = new Native.POINT { X = (bounds.Left + bounds.Right) / 2, Y = bounds.Top + 1 };
        int hit = Native.HitTestAt(edge, handle);
        Check(WindowResizeGeometry.FromHitTest(hit) == WindowResizeEdges.Top,
            $"{mode}: the top edge still supports resizing ({hit})");
        var statusGrid = (Grid)Field<TextBlock>(window, "statusText").Parent;
        var status = (Border)statusGrid.Parent;
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleY;
        double gap = (bounds.Bottom - status.PointToScreen(new Point(0, status.ActualHeight)).Y) / scale;
        Check(gap >= 0 && gap <= 3.5, $"{mode}: the status row ends within 3.5 DIP of the bottom edge ({gap:0.##})");
        edge.Y = bounds.Bottom - 1;
        hit = Native.HitTestAt(edge, handle);
        Check(WindowResizeGeometry.FromHitTest(hit) == WindowResizeEdges.Bottom,
            $"{mode}: the compact bottom edge still supports resizing ({hit})");
        var origin = window.PointToScreen(default);
        foreach (var (message, value) in new[] { (Native.WmNcActivate, 1), (Native.WmNcActivate, 0), (Native.WmNcPaint, 1) })
        {
            object[] args = [handle, message, new IntPtr(value), IntPtr.Zero, false];
            Call(window, "FilterMainWindowCommands", args);
            Check((bool)args[4] && Native.GetWindowRect(handle, out var after) && after.Equals(bounds)
                && window.PointToScreen(default) == origin, $"{mode}: frame message {message:X} ({value}) keeps the titleless bounds and client origin");
        }
    }

    static void DragKeySettings(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-drag-options-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"),
            "{\"DwellMs\":5000,\"AlwaysOnTop\":false,\"DragKeyPlacement\":1,\"LeftDragEnabled\":false,\"MiddleDragEnabled\":true}");
        var previousGuard = Native.AdditionalInputGuard;
        Native.AdditionalInputGuard = () => false;
        var keyboard = new KeyboardWindow(storage);
        var events = new List<string>();
        bool acceptRelease = true;
        var mouse = new MouseActionState(mode => { events.Add(mode == 2 ? "right click" : "click"); return true; },
            down => { events.Add(down ? "left down" : "left up"); return true; },
            down => { events.Add(down ? "middle down" : "middle up"); return down || acceptRelease; },
            down => { events.Add(down ? "right down" : "right up"); return down || acceptRelease; });
        keyboard.GetType().GetField("mouseActions", Fields).SetValue(keyboard, mouse);

        DragKeySettingsWindow Open()
        {
            var header = Field<Button[]>(keyboard, "menuHeaders").Single(button => Equals(button.Content, "Mus"));
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var panel = Field<Border>(keyboard, "menuPanel");
            Descendants<Button>(panel).Single(button => (string)button.Content == "Dragkey…")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            return Field<DragKeySettingsWindow>(keyboard, "dragKeySettingsWin");
        }
        static Button Command(Window dialog, string label) => Descendants<Button>(dialog).Single(button => Equals(button.Content, label));
        try
        {
            ShowOffscreen(keyboard);
            Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
            var toolbar = Field<DragKeyWindow>(keyboard, "dragKeyWin");
            var leftIcon = Field<DragKeyButton>(toolbar, "drag");
            var rightIcon = Field<DragKeyButton>(toolbar, "rightDrag");
            var middleIcon = Field<DragKeyButton>(toolbar, "middleDrag");
            Check(leftIcon.IsVisible && !rightIcon.IsVisible && middleIcon.IsVisible,
                "old middle-only settings are kept and left drag is restored as a permanent button");
            Check(DragKeyIconsFit(toolbar), "normal DragKey artwork has equal visual size, centered with clearance");
            var dialog = Open();
            SoftKeyCheckBox Choice(string name) => Descendants<SoftKeyCheckBox>(dialog).Single(box =>
                System.Windows.Automation.AutomationProperties.GetName(box) == name);
            RadioButton SizeChoice(string name) => Descendants<RadioButton>(dialog).Single(button =>
                System.Windows.Automation.AutomationProperties.GetName(button) == name);
            var initialButtonSize = leftIcon.RenderSize;
            static bool Scaled(FrameworkElement icon, double scale) => icon.LayoutTransform.Value.M11 == scale
                && icon.LayoutTransform.Value.M22 == scale;
            Check(Descendants<RadioButton>(dialog).Count() == 2 && SizeChoice("Normal").IsChecked == true
                && Scaled((Viewbox)leftIcon.Content, 1), "older profiles start with the current normal size");
            var sizeApply = Command(dialog, "Verkställ");
            sizeApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!sizeApply.IsEnabled && dialog.IsVisible, "DragKey Apply begins disabled and does not close or save the clean dialog");
            SizeChoice("Liten").IsChecked = true;
            Check(sizeApply.IsEnabled, "changing DragKey size enables Apply");
            SizeChoice("Normal").IsChecked = true;
            Check(!sizeApply.IsEnabled, "undoing a DragKey size change disables Apply");
            var small = SizeChoice("Liten");
            dialog.Topmost = true;
            Pump();
            var point = small.PointToScreen(new Point(small.ActualWidth / 2, small.ActualHeight / 2));
            var dwellPoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
            var target = (DwellTarget)Call(keyboard, "GetDwellTarget", dwellPoint);
            Check(target == DwellTarget.SoftKeyControl && DesktopDwellTargets.FindLocal(dwellPoint, null)
                is ValueTuple<DependencyObject, object> local && local.Item1 == small,
                $"the whole size choice is a dwell target ({target})");
            Call(small, "OnClick");
            Check(SizeChoice("Normal").IsChecked == false
                && Descendants<Viewbox>(small).All(icon => Scaled(icon, 0.85))
                && Descendants<Viewbox>(small).Count() == 2,
                "the two radio choices are exclusive and the small preview scales both axes to 85 percent");
            Command(dialog, "Avbryt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!storage.Load("settings.json", new Settings()).SmallDragKeyIcons && Scaled((Viewbox)leftIcon.Content, 1),
                "cancelling the size choice keeps the stored and live sizes");
            dialog = Open();
            Check(dialog.IsVisible && Descendants<SoftKeyCheckBox>(dialog).Count() == 2
                && Choice(DragKeyWindow.RightDragLabel).IsChecked == false && Choice(DragKeyWindow.MiddleDragLabel).IsChecked == true,
                "Mus → Dragkey offers independent right and middle drag options");
            var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn");
            Check(keys.Values.All(button => !button.IsEnabled) && Field<Button[]>(keyboard, "sugg").All(button => !button.IsEnabled),
                "Dragkey settings disable typing and word suggestions");
            Choice(DragKeyWindow.MiddleDragLabel).IsChecked = false;
            Check(Command(dialog, "Verkställ").IsEnabled, "both optional drag buttons can be disabled");
            Command(dialog, "Verkställ").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(leftIcon.IsVisible && !rightIcon.IsVisible && !middleIcon.IsVisible, "left drag remains with both optional buttons off");
            dialog = Open();
            Choice(DragKeyWindow.RightDragLabel).IsChecked = Choice(DragKeyWindow.MiddleDragLabel).IsChecked = true;
            SizeChoice("Liten").IsChecked = true;
            Render(dialog, Path.Combine(output, "dragkey-settings.png"));
            Command(dialog, "Verkställ").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var settings = storage.Load("settings.json", new Settings());
            Check(!dialog.IsVisible && settings.RightDragEnabled && settings.MiddleDragEnabled
                && leftIcon.IsVisible && rightIcon.IsVisible && middleIcon.IsVisible,
                "applying both options saves and shows all three drag buttons");
            var power = Field<DragKeyButton>(toolbar, "power");
            var liveGrid = Field<System.Windows.Controls.Primitives.UniformGrid>(toolbar, "buttons");
            var compact = DragKeySize.Small;
            Check(settings.SmallDragKeyIcons && leftIcon.ActualWidth < initialButtonSize.Width && leftIcon.ActualHeight < initialButtonSize.Height
                && toolbar.Surface.Width == compact.SideWidth && toolbar.Surface.Height == compact.SideHeight(true, true)
                && liveGrid.Margin == new Thickness(4, 3, 4, 4)
                && liveGrid.Children.OfType<DragKeyButton>().All(button => Scaled((Viewbox)button.Content, 0.85)),
                "applying Small reduces the side-docked panel and its buttons and persists the 85 percent artwork scale");
            var cells = liveGrid.Children.OfType<Button>().Where(button => button.IsVisible)
                .Select(button => (Border)button.Template.FindName("Frame", button))
                .Select(face => face.TranslatePoint(default, toolbar.Surface)).ToArray();
            Check(cells.Length == 11 && cells.Select(cell => cell.X).Distinct().Count() == 2 && cells.Select(cell => cell.Y).Distinct().Count() == 6,
                "all eleven buttons are laid out in two columns on the side");
            Check(power.TranslatePoint(new Point(0, power.ActualHeight), toolbar.Surface).Y <= toolbar.Surface.ActualHeight,
                "all three drag buttons and the final power button fit in the vertical dock");
            Check(DragKeyIconsFit(toolbar), "the artwork has the same clearance in the compact side dock");
            var auto = Field<DragKeyButton>(toolbar, "autoClick");
            var autoFace = (Border)auto.Template.FindName("Frame", auto);
            var pointInCellInset = new Point(0.5, auto.ActualHeight / 2);
            Check(!new Rect(autoFace.RenderSize).Contains(auto.TranslatePoint(pointInCellInset, autoFace))
                && toolbar.TargetAt(auto.PointToScreen(pointInCellInset)) == DwellTarget.AutoClick
                && !toolbar.IsMoveAreaAt(auto.PointToScreen(pointInCellInset)) && auto.InputHitTest(pointInCellInset) != null,
                "the space beside the AutoClick face is part of its button for dwell and mouse input");
            Check(auto.Background == SoftKeyKeyDesign.AccentFace && power.Background == SoftKeyKeyDesign.FunctionFace
                && leftIcon.Background == SoftKeyKeyDesign.LetterFace
                && liveGrid.Children.OfType<DragKeyButton>().Where(button => button.IsVisible).All(button =>
                    button.Template.FindName("Frame", button) is Border face && face.CornerRadius == default
                    && button.Template.FindName("PointerHighlight", button) is Border highlight && highlight.CornerRadius == default),
                "the live toolbar has square faces and highlights with the existing role colors");
            var preview = Field<DragKeyDockPreview>(keyboard, "dragKeyDockPreview");
            preview.SetPlacement(DragKeyPlacement.Right);
            var previewGrid = Field<System.Windows.Controls.Primitives.UniformGrid>(preview, "symbols");
            var previewSurface = Field<Border>(preview, "panelSurface");
            Check(previewGrid.Columns == 2 && previewSurface.Height == toolbar.Surface.Height
                && previewSurface.Width == toolbar.Surface.Width && previewGrid.Margin == liveGrid.Margin,
                "the side-dock preview uses the same compact spacing and dimensions as the live toolbar");
            bool nextDestination = !Field<bool>(toolbar, "keyboardMovesToTop");
            toolbar.SetKeyboardMoveDestination(nextDestination);
            preview.SetKeyboardMoveDestination(nextDestination);
            Check(Scaled((Viewbox)Field<DragKeyButton>(toolbar, "moveKeyboardEdge").Content, 0.85)
                && previewGrid.Children.OfType<Border>().All(icon => icon.CornerRadius == default && Scaled((Viewbox)icon.Child, 0.85)),
                "the dock preview uses square faces and retains the selected icon size");
            Render(keyboard, Path.Combine(output, "dragkey-all-buttons.png"));
            Call(keyboard, "SetDragKeyPlacement", DragKeyPlacement.Right);
            Pump();
            Render(keyboard, Path.Combine(output, "dragkey-all-buttons-right.png"));
            Call(keyboard, "SetDragKeyPlacement", DragKeyPlacement.Top);
            Pump();
            Check(liveGrid.Rows == 1 && toolbar.Surface.Width == compact.HorizontalWidth && toolbar.Surface.Height == compact.TopHeight,
                "the compact top dock keeps a single row in a smaller panel");
            Render(keyboard, Path.Combine(output, "dragkey-all-buttons-top.png"));
            Call(keyboard, "SetDragKeyPlacement", DragKeyPlacement.Floating);
            Pump();
            Check(toolbar.IsVisible && toolbar.ActualWidth == compact.HorizontalWidth && toolbar.ActualHeight == compact.FloatingHeight,
                "the floating toolbar occupies less space and includes a compact title row");
            Check(DragKeyIconsFit(toolbar), "compact floating DragKey artwork has equal visual size, centered with clearance");
            Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
            Render(toolbar, Path.Combine(output, "dragkey-floating.png"));
            CheckDragKeyHover(toolbar, Field<DragKeyButton>(toolbar, "moveKeyboardEdge"), output);
            Call(keyboard, "SetDragKeyPlacement", DragKeyPlacement.Left);
            Pump();

            var rightCenter = rightIcon.PointToScreen(new Point(rightIcon.ActualWidth / 2, rightIcon.ActualHeight / 2));
            Check(toolbar.TargetAt(rightCenter) == DwellTarget.RightDrag, "the right drag icon has a distinct dwell target");
            Call(keyboard, "PerformDwell", DwellTarget.RightDrag);
            Call(keyboard, "PerformDwell", DwellTarget.External);
            acceptRelease = false;
            Call(keyboard, "PerformDwell", DwellTarget.DragDrop);
            Check(mouse.HeldDragButton == MouseAction.RightDrag && !leftIcon.IsEnabled && rightIcon.IsEnabled,
                "a rejected right release keeps the right drag button available for retry");
            acceptRelease = true;
            rightIcon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(events.SequenceEqual(new[] { "right down", "right up", "right up" }) && !mouse.IsDragging,
                "right drag holds and releases only the right mouse button, including a retried release");
            events.Clear();
            Call(keyboard, "PerformDwell", DwellTarget.RightClick);
            var dragStatus = Field<TextBlock>(keyboard, "dragKeyStatus");
            Check(dragStatus.Visibility == Visibility.Visible && dragStatus.Text == "Högerklick valt",
                "a selected click mode appears in the keyboard status row");
            Call(keyboard, "PerformDwell", DwellTarget.External);
            Check(events.SequenceEqual(new[] { "right click" }) && !mouse.IsDragging,
                "ordinary right click still uses its single-click action");
            Check(dragStatus.Visibility == Visibility.Collapsed, "the click mode disappears after the single click");
            events.Clear();

            leftIcon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Call(keyboard, "PerformDwell", DwellTarget.External);
            Check(dragStatus.Text == "Drag pågår · vila för att släppa", "the status row shows how to release an active drag");
            Call(keyboard, "PerformDwell", DwellTarget.DragDrop);
            Check(events.SequenceEqual(new[] { "left down", "left up" }), "left drag uses only the left mouse button");
            events.Clear();
            Call(keyboard, "PerformDwell", DwellTarget.MiddleDrag);
            Call(keyboard, "PerformDwell", DwellTarget.External);
            acceptRelease = false;
            Call(keyboard, "PerformDwell", DwellTarget.DragDrop);
            Check(mouse.HeldDragButton == MouseAction.MiddleDrag && !leftIcon.IsEnabled && middleIcon.IsEnabled,
                "a rejected middle release keeps the right release action available for retry");
            acceptRelease = true;
            Call(keyboard, "PerformDwell", DwellTarget.DragDrop);
            Check(events.SequenceEqual(new[] { "middle down", "middle up", "middle up" }) && !mouse.IsDragging,
                "middle drag uses only the middle mouse button, including a retried release");

            dialog = Open();
            Check(SizeChoice("Liten").IsChecked == true, "the settings dialog reopens with Small selected");
            SizeChoice("Normal").IsChecked = true;
            Choice(DragKeyWindow.RightDragLabel).IsChecked = false;
            Command(dialog, "Avbryt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(leftIcon.IsVisible && rightIcon.IsVisible && middleIcon.IsVisible && Scaled((Viewbox)leftIcon.Content, 0.85),
                "cancel leaves the saved button selection and size in place");
            dialog = Open();
            SizeChoice("Normal").IsChecked = true;
            Choice(DragKeyWindow.RightDragLabel).IsChecked = false;
            Command(dialog, "Verkställ").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Call(keyboard, "PerformDwell", DwellTarget.RightDrag);
            settings = storage.Load("settings.json", new Settings());
            Check(!settings.RightDragEnabled && settings.MiddleDragEnabled && leftIcon.IsVisible && !rightIcon.IsVisible && middleIcon.IsVisible
                && mouse.Selected == MouseAction.Left && !settings.SmallDragKeyIcons && Scaled((Viewbox)leftIcon.Content, 1)
                && leftIcon.RenderSize == initialButtonSize && toolbar.Surface.Width == DragKeySize.Normal.SideWidth
                && liveGrid.Margin == new Thickness(3, 1, 3, 3),
                "disabling right drag hides and disarms only that button, and Normal restores the original toolbar size");

            int forwarded = 0;
            keyboard.GetType().GetField("keyInput", Fields).SetValue(keyboard, (Func<KeyDef, bool>)(_ => { forwarded++; return true; }));
            Call(keyboard, "OpenMouseSettings");
            var mouseSettings = Field<MouseSettingsWindow>(keyboard, "mouseWin");
            Pump();
            var dwell = Field<TextBox>(mouseSettings, "dwell");
            var digit = keys.Keys.First(key => key.Label == "5");
            var backspace = keys.Keys.First(key => key.Label == "BkSp");
            Check(keys[digit].IsEnabled && !keys[keys.Keys.First(key => key.Label == "a")].IsEnabled,
                "autoclick settings keep useful keys available and gray out letters");
            Call(keyboard, "Press", digit);
            Check(dwell.Text == "5" && forwarded == 0, "digits from the keyboard go directly into the selected dwell field");
            Call(mouseSettings, "Select", new object[] { null });
            Call(keyboard, "Press", backspace);
            Check(dwell.Text == "5" && forwarded == 0 && !keys[backspace].IsEnabled,
                "with no selected field backspace cannot reach the previous application");
            mouseSettings.Close();
            Check(keys.Values.All(button => button.IsEnabled), "closing autoclick restores the writing keys");

            var keyboardMenu = Field<Button[]>(keyboard, "menuHeaders").Single(button => Equals(button.Content, "Tangentbord"));
            var popup = Field<System.Windows.Controls.Primitives.Popup>(keyboard, "menuPopup");
            var menuPanel = Field<Border>(keyboard, "menuPanel");
            string MenuLabel(Button button) => button.Content?.ToString();
            Button MenuItem(string label) => Descendants<Button>(menuPanel).Single(button => MenuLabel(button) == label);
            keyboardMenu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Descendants<Button>(menuPanel).Select(MenuLabel).SequenceEqual(new[] { "Inställningar…", "Ordlista…" }),
                "the keyboard menu offers settings and one combined word list");
            MenuItem("Ordlista…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var wordBook = Field<TabbedWordListWindow>(keyboard, "wordListWin");
            var tabs = Field<TabControl>(wordBook, "tabs");
            Check(wordBook.IsVisible && wordBook.Owner == keyboard && !popup.IsOpen && !wordBook.IsSpecific
                && tabs.Items.Cast<TabItem>().Select(tab => tab.Header).SequenceEqual(new object[] { "Vanliga ord", "Specifika förslag" }),
                "Ordlista opens on the ordinary tab with a second tab for specific suggestions");
            tabs.SelectedIndex = 1;
            Check(wordBook.IsSpecific && !keys[digit].IsEnabled, "the specific list does not route typing to an empty form");
            tabs.SelectedIndex = 0;
            Check(wordBook.CanType && keys[digit].IsEnabled, "switching back restores the ordinary word list's writing keys");
            wordBook.Close();
        }
        finally
        {
            keyboard.EnablePreviewClose();
            keyboard.Close();
            Native.AdditionalInputGuard = previousGuard;
            Directory.Delete(folder, true);
        }
    }

    static void CheckDragKeyHover(DragKeyWindow toolbar, DragKeyButton button, string output)
    {
        var state = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var frame = (Border)button.Template.FindName("Frame", button);
        var highlight = (Border)button.Template.FindName("PointerHighlight", button);
        var restingBorder = frame.BorderBrush;
        var restingSize = button.RenderSize;
        try
        {
            button.SetValue(state, true);
            toolbar.UpdateLayout();
            Check(frame.BorderBrush == SoftKeyKeyDesign.SlateAccent && highlight.Opacity > 0 && button.RenderSize == restingSize,
                "DragKey hover visibly changes the face and border without resizing the click target");
            Render(toolbar, Path.Combine(output, "dragkey-hover.png"));
            button.IsEnabled = false;
            Check(highlight.Opacity == 0 && frame.BorderBrush == restingBorder, "disabled DragKey actions do not look actionable on hover");
        }
        finally
        {
            button.SetValue(state, false);
            button.IsEnabled = true;
        }
        Check(highlight.Opacity == 0 && frame.BorderBrush == restingBorder, "leaving the DragKey button restores its normal appearance");
    }

    static bool DragKeyIconsFit(DragKeyWindow toolbar)
    {
        var grid = Field<System.Windows.Controls.Primitives.UniformGrid>(toolbar, "buttons");
        double? firstSpan = null;
        foreach (var button in grid.Children.OfType<DragKeyButton>().Where(button => button.IsVisible))
        {
            var face = (Border)button.Template.FindName("Frame", button);
            var icon = (Image)((Viewbox)button.Content).Child;
            var drawing = (DrawingGroup)((DrawingImage)icon.Source).Drawing;
            var ink = icon.TransformToAncestor(face).TransformBounds(drawing.Children[1].Bounds);
            var safeArea = new Rect(face.RenderSize);
            safeArea.Inflate(-3, -3);
            double span = Math.Max(ink.Width, ink.Height);
            firstSpan ??= span;
            if (!safeArea.Contains(ink) || span is < 14 or > 19 || Math.Abs(span - firstSpan.Value) > 0.8
                || Math.Abs(ink.Left + ink.Width / 2 - face.ActualWidth / 2) > 0.8
                || Math.Abs(ink.Top + ink.Height / 2 - face.ActualHeight / 2) > 0.8) return false;
        }
        return true;
    }

    static void DialogPlacement()
    {
        var screen = SystemParameters.WorkArea;
        var owner = new Window
        {
            WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None,
            ShowActivated = false, ShowInTaskbar = false, Width = 320, Height = 140,
            Left = screen.Left + 12, Top = screen.Bottom - 160
        };
        owner.Show();
        try
        {
            Check(Native.GetWindowRect(new WindowInteropHelper(owner).Handle, out var ownerBounds), "the dialog owner has native screen bounds");
            Check(WindowPositioning.TryWorkArea(ownerBounds, out var area), "the dialog owner supplies its monitor work area");
            var settings = new Settings { AlwaysOnTop = false };
            var list = new WordListWindow(() => Array.Empty<string>(), _ => null, _ => null, (_, _) => null);
            foreach (var dialog in new Window[]
            {
                new StartupSettingsWindow(new Service()), new MouseSettingsWindow(settings, () => true),
                new DragKeySettingsWindow(settings, (_, _, _) => Task.FromResult(true)), new CloseConfirmationWindow(),
                new SoftKeyNoticeWindow("SoftKey", "Kontroll av placering."),
                new PasswordSuggestionWindow(_ => Task.FromResult<string>(null)),
                new PhoneNumberWindow([], _ => Task.FromResult<string>(null)),
                new SoftKeyUpdateWindow(new SoftKeyUpdates.Preview(42, new string('a', 40), "2026-11-27T12:00:00Z",
                    "• Tydligare tangenttext.", new SoftKeyUpdates.Asset(42, "SoftKey-update-42.zip", "uploaded", 1784688, "sha256:" + new string('a', 64))),
                    () => Task.FromResult<SoftKeyUpdates.Preview>(null), (_, _) => Task.CompletedTask),
                list
            })
            {
                try
                {
                    dialog.Owner = owner;
                    dialog.Show(); Pump();
                    CheckDialogFrame(dialog);
                    if (dialog is StartupSettingsWindow startup)
                    {
                        foreach (var field in new[] { "login", "automatic", "afterUnlock" })
                        {
                            var option = Field<SoftKeyCheckBox>(startup, field);
                            var preview = (FrameworkElement)((DockPanel)option.Content).Children[0];
                            var center = preview.TranslatePoint(new Point(preview.ActualWidth / 2, preview.ActualHeight / 2), option);
                            Check(option.InputHitTest(center) != null,
                                "the preview belongs to the clickable area of the Startup option");
                        }
                    }
                    if (dialog == list)
                    {
                        var confirmation = new RemoveWordConfirmationWindow("ord") { Owner = list };
                        try { confirmation.Show(); Pump(); CheckDialogFrame(confirmation); }
                        finally { confirmation.Close(); }
                    }
                }
                finally { dialog.Close(); }
            }

            var ownerHandle = new WindowInteropHelper(owner).Handle;
            var suggestionStore = new SavedTextStore(new JsonFileStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
            (Size Ordinary, Size Specific) WordListSizesAt(int y)
            {
                Native.MoveWithoutActivation(ownerHandle, ownerBounds.Left, y);
                var book = new TabbedWordListWindow(() => new[] { "arbete", "hemma" }, _ => null, _ => null, (_, _) => null,
                    suggestionStore, false, false, _ => Task.FromResult<string>(null), true, _ => true) { Owner = owner };
                try
                {
                    book.Show(); Pump();
                    CheckDialogFrame(book);
                    var ordinary = new Size(book.ActualWidth, book.ActualHeight);
                    Field<TabControl>(book, "tabs").SelectedIndex = 1;
                    FinishTabTransition();
                    return (ordinary, new Size(book.ActualWidth, book.ActualHeight));
                }
                finally { book.Close(); }
            }
            var bottomSizes = WordListSizesAt(area.Bottom - (ownerBounds.Bottom - ownerBounds.Top));
            var topSizes = WordListSizesAt(area.Top);
            Check(bottomSizes == topSizes, "both word-list tabs have the same opening size for a keyboard at the top or bottom");

            void CheckDialogFrame(Window dialog)
            {
                Check(Native.GetWindowRect(new WindowInteropHelper(dialog).Handle, out var bounds)
                    && Math.Abs(bounds.Left + bounds.Right - area.Left - area.Right) <= 2
                    && Math.Abs(bounds.Top + bounds.Bottom - area.Top - area.Bottom) <= 2,
                    dialog.Title + " opens at the center of SoftKey's monitor work area");
                Check(dialog.AllowsTransparency && dialog.Content is Border { Child: StackPanel frame } border
                    && border.CornerRadius.TopLeft > 0 && border.Effect is System.Windows.Media.Effects.DropShadowEffect
                    && frame.Children[0] is DockPanel titleBar
                    && titleBar.Children.OfType<TextBlock>().Single().FontSize == SoftKeyPopupDesign.TitleFontSize,
                    dialog.Title + " has the shared rounded frame, shadow and title size");
                Check(dialog.Content is Border { Child: StackPanel themedFrame }
                    && themedFrame.Children[0] is DockPanel title && title.Background == SoftKeyKeyDesign.FunctionFace
                    && themedFrame.Children[1] is Border body && body.Background == SoftKeyKeyDesign.WindowSurface,
                    dialog.Title + " uses the warm stone surfaces from the keyboard");
            }
        }
        finally { owner.Close(); }
    }

    const string PasswordFixture = "Testlösen123!";

    sealed class GroupTextEditor : RichTextBox
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new GroupTextEditorPeer(this);
    }

    sealed class GroupTextEditorPeer(RichTextBox owner) : RichTextBoxAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }

    static void DesktopInputTarget()
    {
        var app = new Application();
        var field = new TextBox { Height = 28 };
        var password = new PasswordBox { Height = 28 };
        var email = new TextBox { Height = 28 };
        var richText = new Run("Före:  efter");
        var rich = new GroupTextEditor { Height = 42, Document = new FlowDocument(new Paragraph(richText)) };
        System.Windows.Automation.AutomationProperties.SetName(email, "Email or phone");
        var left = new Button { Content = "Vänster", Width = 92, Height = 28 };
        var right = new Button { Content = "Höger", Width = 92, Height = 28 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(left);
        buttons.Children.Add(right);
        var content = new StackPanel();
        content.Children.Add(buttons);
        content.Children.Add(field);
        content.Children.Add(password);
        content.Children.Add(email);
        content.Children.Add(rich);
        var window = new Window
        {
            Title = "SoftKey test", Content = content, Width = 240, Height = 226, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.ContentRendered += (_, _) =>
        {
            var edge = right.PointToScreen(new Point(0, 14));
            var ordinary = field.PointToScreen(new Point(30, 14));
            var secret = password.PointToScreen(new Point(30, 14));
            var mail = email.PointToScreen(new Point(30, 14));
            var editor = rich.PointToScreen(new Point(30, 14));
            Console.WriteLine(JsonSerializer.Serialize(new[] { (int)edge.X, (int)edge.Y, (int)ordinary.X, (int)ordinary.Y,
                (int)secret.X, (int)secret.Y, (int)mail.X, (int)mail.Y, (int)editor.X, (int)editor.Y }));
            _ = Task.Run(() =>
            {
                while (Console.ReadLine() is { } command && command != "close")
                    Console.WriteLine(window.Dispatcher.Invoke(() =>
                    {
                        switch (command)
                        {
                            case "focus-password": window.Activate(); password.Focus(); return "ready";
                            case "focus-ordinary": window.Activate(); field.Focus(); return "ready";
                            case "focus-rich":
                                window.Activate();
                                rich.Focus();
                                rich.CaretPosition = richText.ContentStart.GetPositionAtOffset("Före: ".Length);
                                return "ready";
                            case "readonly-rich": rich.IsReadOnly = true; return "ready";
                            case "writable-rich": rich.IsReadOnly = false; return "ready";
                            case "rich-content": return JsonSerializer.Serialize(
                                new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.TrimEnd('\r', '\n'));
                            case "readonly-ordinary": field.IsReadOnly = true; return "ready";
                            case "writable-ordinary": field.IsReadOnly = false; return "ready";
                            case "phone-filled": return field.Text == "+46 70-123 45 67" ? "yes" : "no";
                            case "ordinary-content": return JsonSerializer.Serialize(field.Text);
                            case "clear-ordinary": field.Clear(); return "ready";
                            case "seed-password": password.Password = "påbörjat"; return "ready";
                            case "password-unmodified": return password.Password == "påbörjat" && field.Text == "" ? "yes" : "no";
                            case "password-filled": return password.Password == PasswordFixture && field.Text == "" ? "yes" : "no";
                            default: return "unknown";
                        }
                    }));
                window.Dispatcher.Invoke(window.Close);
            });
        };
        app.Run(window);
    }

    static void DesktopInput(string output)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--desktop-input-target");
        using var targetProcess = System.Diagnostics.Process.Start(start)!;
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-input-check-" + Guid.NewGuid().ToString("N"));
        var storage = new JsonFileStorage(folder);
        string ReadTarget() => targetProcess.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        try
        {
            int[] edge = JsonSerializer.Deserialize<int[]>(ReadTarget());
            var left = new Native.POINT { X = edge[0] - 1, Y = edge[1] };
            var right = new Native.POINT { X = edge[0] + 1, Y = edge[1] };
            DesktopDwellTargets.Sample? External(Native.POINT point)
                => Task.Run(() => DesktopDwellTargets.FindExternal(point, Native.WindowFromPoint(point)))
                    .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var first = External(left);
            var next = External(right);
            Check(first?.RuntimeId != null && next?.RuntimeId != null && first.Value.Window == next.Value.Window
                && !Equals(first.Value.Identity, next.Value.Identity),
                "two adjacent controls in the same external WPF window have separate dwell identities");
            var tracker = new DwellTracker();
            bool Update(int x, double time, DesktopDwellTargets.Sample sample)
                => tracker.Update(x, edge[1], DwellTarget.External, time, 800, 10, false, sample.Identity);
            Check(!Update(left.X, 0, first.Value) && !Update(left.X, 760, first.Value)
                && !Update(right.X, 800, next.Value) && Update(right.X, 1600, next.Value),
                "external controls start a full dwell after a two-pixel crossing inside the movement tolerance");

            var keyboard = new KeyboardWindow(storage);
            Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
            var keys = Field<Dictionary<KeyDef, Button>>(keyboard, "btn");
            keyboard.GetType().GetField("keyInput", Fields).SetValue(keyboard, (Func<KeyDef, bool>)(_ => true));
            try
            {
                void Type(string labels)
                {
                    foreach (char label in labels)
                        Call(keyboard, "Press", keys.Keys.First(key => key.Label == label.ToString()));
                }
                DesktopWordField FieldAt(int x, int y, bool includePassword = false) => Task.Run(() => DesktopWordFieldReader.ReadElement(
                    () => System.Windows.Automation.AutomationElement.FromPoint(new Point(x, y)),
                    first.Value.Window, first.Value.Window, (uint)targetProcess.Id, includePassword))
                    .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                var field = FieldAt(edge[2], edge[3]);
                Check(field != null, "the ordinary external field is available for word feedback");
                keyboard.GetType().GetField("readDesktopWordField", Fields).SetValue(keyboard, (Func<DesktopWordField>)(() => field));
                Type("provord");
                var status = Field<TextBlock>(keyboard, "statusText");
                Check(status.Parent is Grid statusRow && statusRow.Children.OfType<Button>().Count() == 0
                    && string.Equals(status.Text, "provord", StringComparison.OrdinalIgnoreCase),
                    "the status row displays the current word without a button");
                keyboard.GetType().GetField("modifierInput", Fields).SetValue(keyboard, (Func<KeyDef, bool, bool>)((_, _) => true));
                foreach (string label in new[] { "Cap", "Sft", "Alt", "Ctl", "Win", "AltGr" })
                    Call(keyboard, "Press", keys.Keys.First(key => key.Label == label));
                var modifier = Field<TextBlock>(keyboard, "modifierStatus");
                Check(modifier.Text.Contains("Caps Lock") == Field<bool>(keyboard, "capsLockOn"),
                    "the status row reflects Caps Lock");
                Check(new[] { "Shift", "Alt", "Ctrl", "Win", "AltGr" }.All(label => modifier.Text.Split("  ·  ").Contains(label)),
                    "the status row shows all selected desktop modifiers");
                Call(keyboard, "ReleaseHeldModifiers");
                var foreground = Native.ForegroundWindow;
                var wordMenu = (StackPanel)Call(keyboard, "BuildMenuPanel", 4);
                ((Button)wordMenu.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(storage.Load("words.json", new Dictionary<string, int>()).ContainsKey("provord")
                    && Native.ForegroundWindow == foreground, "the existing word menu saves the current word without taking focus");

                field = FieldAt(edge[4], edge[5]);
                Check(field == null, "the password field in that same window is excluded");
                Check(FieldAt(edge[4], edge[5], includePassword: true)?.IsPassword == true,
                    "saved password suggestions can identify that password field without reading its contents");
                Type("hemligtprov");
                Call(keyboard, "Press", keys.Keys.First(key => key.Label == "Enter"));
                Check(status.Text == "Redo"
                    && !Field<Dictionary<string, int>>(keyboard, "words").ContainsKey("hemligtprov"),
                    "password input never becomes a visible or learned word");

                var saved = new SavedTextStore(storage);
                Check(DesktopInputType.FromAttributes("id:contact;tag:input;text-input-type:email;") == DesktopInputKind.Email
                    && DesktopInputType.FromAttributes(@"description:prefilled\;text-input-type:email;") == null
                    && DesktopInputType.FromAttributes("text-input-type:tel;") == DesktopInputKind.Phone,
                    "HTML input metadata identifies email and telephone fields without mistaking escaped label text for a type");
                Check(DesktopInputType.FromLabel("Email or phone") == DesktopInputKind.Email
                    && DesktopInputType.FromLabel("E-postadress eller telefonnummer") == DesktopInputKind.Email
                    && DesktopInputType.FromLabel("Telefonnummer") == DesktopInputKind.Phone,
                    "a mixed sign-in label prefers email without changing phone-only fields");
                saved.Save(Guid.NewGuid(), "Privat e-post", SavedTextKind.Email, Encoding.Unicode.GetBytes("Privat@example.invalid"));
                saved.Save(Guid.NewGuid(), "Jobb", SavedTextKind.Email, Encoding.Unicode.GetBytes("Jobb@example.invalid"));
                for (int i = 0; i < 8; i++)
                    saved.Save(Guid.NewGuid(), "A" + i, SavedTextKind.Email, Encoding.Unicode.GetBytes($"annan{i}@example.invalid"));
                string longAddress = "Bertil.Med.En.Lang.Adress.For.Att.Prova.Radbrytningen@example.invalid";
                saved.Save(Guid.NewGuid(), "Sist", SavedTextKind.Email, Encoding.Unicode.GetBytes(longAddress));
                saved.Save(Guid.NewGuid(), "Mobil", SavedTextKind.Phone, Encoding.Unicode.GetBytes("+46 70-123 45 67"));
                saved.Save(Guid.NewGuid(), "Hemligt", SavedTextKind.Password, Encoding.Unicode.GetBytes("testsecret"));
                field = FieldAt(edge[6], edge[7]);
                Check(field?.Kind == DesktopInputKind.Email, "an external combined email or phone field is recognized as an email target");
                var suggestions = Field<Button[]>(keyboard, "sugg");
                string[] VisibleSuggestions() => suggestions.Where(button => button.Visibility == Visibility.Visible)
                    .Select(button => (string)button.Content).ToArray();
                string ReadTypedPrefix(string typed) => Task.Run(() =>
                {
                    var element = System.Windows.Automation.AutomationElement.FromPoint(new Point(edge[6], edge[7]));
                    var pattern = (System.Windows.Automation.ValuePattern)element.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                    pattern.SetValue(typed);
                    return DesktopWordFieldReader.ReadEmailPrefix(field, element);
                }).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix(""));
                Check(VisibleSuggestions().Length == 0, "an empty email field shows no saved addresses");
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix("b"));
                Check(VisibleSuggestions().SequenceEqual(new[] { longAddress }),
                    "one lowercase letter finds the matching address beyond the first eight saved addresses");
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix("BJ"));
                Check(VisibleSuggestions().Length == 0, "nonmatching letters hide the previous email suggestion");
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix("Ber"));
                Check(VisibleSuggestions().SequenceEqual(new[] { longAddress }), "editing the email prefix updates the suggestions");
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix(""));
                Check(VisibleSuggestions().Length == 0, "clearing the prefix hides the email suggestions");
                Call(keyboard, "SetFocusedSuggestionField", field, ReadTypedPrefix("J"));
                Type("provord");
                Check(status.Text == "Redo"
                    && VisibleSuggestions().SequenceEqual(new[] { "Jobb@example.invalid" }),
                    "typing in an email field leaves ordinary word feedback and learning off");
                Call(keyboard, "SetFocusedSuggestionField", FieldAt(edge[4], edge[5], includePassword: true), null);
                Check(VisibleSuggestions().Length == 0, "moving to a password field hides email suggestions");
                var replaced = Task.Run(() =>
                {
                    var element = System.Windows.Automation.AutomationElement.FromPoint(new Point(edge[6], edge[7]));
                    var pattern = (System.Windows.Automation.ValuePattern)element.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                    return !DesktopWordFieldReader.SetEmailValue(element, "Jobb@example.invalid", () => false)
                        && pattern.Current.Value == "J"
                        && DesktopWordFieldReader.SetEmailValue(element, "Jobb@example.invalid", () => true)
                        && pattern.Current.Value == "Jobb@example.invalid";
                }).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Check(replaced, "a validated email suggestion replaces the field with just that address");

                keyboard.Width = keyboard.MinWidth;
                keyboard.Height = keyboard.MinHeight;
                ShowOffscreen(keyboard);
                Field<DispatcherTimer>(keyboard, "desktopTypingTimer").Stop();
                Call(keyboard, "SetFocusedSuggestionField", field, "B");
                Render(keyboard, Path.Combine(output, "desktop-wrapped-suggestion.png"));
                TextBlock FindText(DependencyObject element)
                {
                    if (element is TextBlock text) return text;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                        if (FindText(VisualTreeHelper.GetChild(element, i)) is TextBlock child) return child;
                    return null;
                }
                var labelText = FindText(suggestions[0]);
                Check(labelText != null && labelText.Text == longAddress && labelText.TextTrimming == TextTrimming.None
                    && labelText.TextAlignment == TextAlignment.Center && labelText.ActualHeight > labelText.FontSize * 2
                    && suggestions[0].ActualHeight > labelText.ActualHeight,
                    "a long address wraps at the narrowest desktop width and the click target grows around the centered text");
                Call(keyboard, "ClearFocusedSuggestions");

                const string chosenAddress = "Jobb@example.invalid";
                const string longerAddress = "Jobb@example.invalid.se";
                saved.Save(Guid.NewGuid(), "Z andra jobb", SavedTextKind.Email, Encoding.Unicode.GetBytes(longerAddress));
                ReadTypedPrefix("J");
                Task.Run(() => System.Windows.Automation.AutomationElement.FromPoint(new Point(edge[6], edge[7])).SetFocus())
                    .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                var reader = new DesktopWordFieldReader();
                (DesktopWordField Field, string EmailPrefix) ReadFocusedEmail()
                {
                    var read = reader.BeginRead(Native.InputTarget(), includeEmailPrefix: true);
                    return read?.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() ?? default;
                }
                var focused = ReadFocusedEmail();
                Check(focused.Field?.Kind == DesktopInputKind.Email && focused.Field.ElementId == field.ElementId
                    && focused.EmailPrefix == "J", "the external test email field has the writing focus");
                Call(keyboard, "SetFocusedSuggestionField", focused.Field, focused.EmailPrefix);
                var emptyIcon = Field<Image>(keyboard, "emptySuggestionsIcon");
                Check(VisibleSuggestions().SequenceEqual(new[] { chosenAddress, longerAddress })
                    && emptyIcon.Visibility == Visibility.Collapsed, "both matching addresses start out visible");
                suggestions[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 30 && Field<CancellationTokenSource>(keyboard, "emailInsertion") != null; i++) Pump(50);
                var completed = ReadFocusedEmail();
                Check(completed == (focused.Field, chosenAddress) && VisibleSuggestions().Length == 0
                    && emptyIcon.Visibility == Visibility.Visible, "choosing an email fills the field and restores the empty icon");
                Call(keyboard, "SetFocusedSuggestionField", null, null);
                Call(keyboard, "SetFocusedSuggestionField", completed.Field, completed.EmailPrefix);
                Check(VisibleSuggestions().Length == 0 && emptyIcon.Visibility == Visibility.Visible,
                    "refreshing the same completed field keeps the icon and hides both addresses");
                Call(keyboard, "SetFocusedSuggestionField", completed.Field, ReadTypedPrefix("Jo"));
                Check(VisibleSuggestions().SequenceEqual(new[] { chosenAddress, longerAddress })
                    && emptyIcon.Visibility == Visibility.Collapsed, "editing the completed address brings matching suggestions back");
                Call(keyboard, "ClearFocusedSuggestions");

                string TargetCommand(string command)
                {
                    targetProcess.StandardInput.WriteLine(command);
                    return ReadTarget();
                }
                var phoneKey = Field<Button>(keyboard, "phoneNumberKey");
                Check(phoneKey.Visibility == Visibility.Hidden && Grid.GetRow(phoneKey) == Grid.GetRow(keys[KeyboardLayout.NavigationKeys[^1]]) + 2,
                    "the desktop phone key sits after Page Down and a separator and starts hidden with no focus sample");
                var ordinaryElement = Task.Run(() => System.Windows.Automation.AutomationElement.FromPoint(new Point(edge[2], edge[3])))
                    .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                var ordinaryId = string.Join(".", ordinaryElement.GetRuntimeId());
                DesktopWordField FocusOrdinary()
                {
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        TargetCommand("focus-ordinary");
                        Task.Run(ordinaryElement.SetFocus).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        Pump(75);
                        var active = reader.ReadInsertionField();
                        if (active?.Kind == DesktopInputKind.Text && active.ElementId == ordinaryId) return active;
                    }
                    return null;
                }
                TargetCommand("readonly-ordinary");
                var readonlyField = FocusOrdinary();
                Call(keyboard, "SetFocusedSuggestionField", readonlyField, null);
                Check(readonlyField is { CanInsertText: false } && phoneKey.Visibility == Visibility.Hidden,
                    "the phone key stays hidden in a read-only field");
                TargetCommand("writable-ordinary");
                TargetCommand("clear-ordinary");
                var phoneTarget = FocusOrdinary();
                Check(phoneTarget is { CanInsertText: true }, "the ordinary text field is available for the phone key");
                Call(keyboard, "SetFocusedSuggestionField", phoneTarget, null);
                Check(phoneKey.IsVisible, "a writable field of ordinary text shows the phone key");
                phoneKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                var phoneDialog = Field<PhoneNumberWindow>(keyboard, "phoneNumberWindow");
                Check(phoneDialog?.IsVisible == true && keys.Keys.Where(k => k.Label == "a").All(k => keys[k].IsEnabled),
                    "the phone key opens the number list and leaves the on-screen search keys available");
                var phoneRows = Field<StackPanel>(phoneDialog, "rows");
                var matches = phoneRows.Children.OfType<Button>().ToArray();
                Check(matches.Length == 1 && System.Windows.Automation.AutomationProperties.GetName(matches[0]) == "Mobil, +46 70-123 45 67",
                    "only the saved phone numbers are offered by the keyboard dialog");
                matches[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 20 && phoneDialog.IsVisible; i++) Pump(50);
                Pump(175);
                string phoneFeedback = phoneDialog.IsVisible ? Field<TextBlock>(phoneDialog, "status").Text : "dialog closed";
                string phoneValue = TargetCommand("ordinary-content");
                Check(!phoneDialog.IsVisible && TargetCommand("phone-filled") == "yes",
                    "the selected number is inserted in the original ordinary text field (" + phoneFeedback + ", value: " + phoneValue + ")");
                TargetCommand("clear-ordinary");

                TargetCommand("readonly-rich");
                Call(keyboard, "SetFocusedSuggestionField", FieldAt(edge[8], edge[9]), null);
                Check(phoneKey.Visibility == Visibility.Hidden, "a read-only rich text group does not offer the phone key");
                TargetCommand("writable-rich");
                TargetCommand("focus-rich");
                Pump(100);
                var richTarget = reader.ReadInsertionField();
                Check(richTarget is { Kind: DesktopInputKind.Text, CanInsertText: true }
                    && richTarget.ElementId == FieldAt(edge[8], edge[9])?.ElementId,
                    "a focused rich text group is recognized like a browser contenteditable host");
                Call(keyboard, "SetFocusedSuggestionField", richTarget, null);
                Check(phoneKey.IsVisible, "the phone key is available from the rich text group");
                phoneKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                phoneDialog = Field<PhoneNumberWindow>(keyboard, "phoneNumberWindow");
                Check(phoneDialog is { IsVisible: true }, "the phone picker opens for the rich text group");
                Field<StackPanel>(phoneDialog, "rows").Children.OfType<Button>().Single()
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 20 && phoneDialog.IsVisible; i++) Pump(50);
                Pump(175);
                Check(!phoneDialog.IsVisible
                    && JsonSerializer.Deserialize<string>(TargetCommand("rich-content")) == "Före: +46 70-123 45 67 efter",
                    "the number is inserted at the rich text caret while preserving the surrounding text");

                var accentTarget = FocusOrdinary();
                var accentLayout = Native.DesktopInputLayout();
                if (accentTarget != null && Native.PendingKeyboardAccent(accentLayout) == null
                    && Native.TranslateKey(accentLayout, 0x1B, false, false, false, false) == "¨"
                    && Native.TranslateKey(accentLayout, 0x1E, false, false, false, false) == "a")
                {
                    // Only the temporary fixture can receive these two sample keystrokes.
                    var previousGuard = Native.AdditionalInputGuard;
                    Native.AdditionalInputGuard = () => DesktopContext.IsCurrentInputDesktop
                        && Native.InputTarget() == (accentTarget.Active, accentTarget.Focus);
                    bool sentAccent = false;
                    try
                    {
                        Check(sentAccent = Native.PressKey(0x1B, false), "the Swedish accent key reaches the ordinary test field");
                        Pump(100);
                        Check(Native.PendingKeyboardAccent(accentLayout) == "¨", "Windows reports the pending diaeresis without consuming it");
                        var translationCache = (System.Collections.IDictionary)typeof(Native)
                            .GetField("CharacterCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        translationCache.Clear();
                        typeof(KeyboardWindow).GetField("displayedKeyboardLayer", Fields).SetValue(keyboard, -1);
                        Call(keyboard, "RefreshKeyboardLabels");
                        string Legend(string label) => (string)keys.Single(pair => pair.Key.Label == label).Value.Content;
                        Check(string.Equals(Legend("q"), "q", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Legend("a"), "a", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Legend("ä"), "ä", StringComparison.OrdinalIgnoreCase)
                            && translationCache.Count == 0,
                            "a fresh keyboard draws the correct Swedish labels without caching a pending accent");
                        Check(Native.PendingKeyboardAccent(accentLayout) == "¨" && Native.PressKey(0x1E, false),
                            "refreshing the labels leaves the pending accent available to real typing");
                        Pump(100);
                        Check(string.Equals(JsonSerializer.Deserialize<string>(TargetCommand("ordinary-content")), "ä", StringComparison.OrdinalIgnoreCase)
                            && Native.PendingKeyboardAccent(accentLayout) == null,
                            "real typing composes the accent into ä in the test field");
                        Call(keyboard, "RefreshKeyboardLabels");
                        Check(translationCache.Count > 0, "clean Swedish legends are cached after the accent is consumed");
                    }
                    finally
                    {
                        if (sentAccent && Native.PendingKeyboardAccent(accentLayout) == "¨") Native.PressKey(0x39, false);
                        Native.AdditionalInputGuard = previousGuard;
                        TargetCommand("clear-ordinary");
                    }
                }
                else Console.WriteLine("The native Swedish accent check requires a focused field and a keyboard with the test accent key.");

                var passwordElement = Task.Run(() =>
                    System.Windows.Automation.AutomationElement.FromPoint(new Point(edge[4], edge[5])))
                    .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                var passwordId = string.Join(".", passwordElement.GetRuntimeId());
                DesktopWordField FocusPasswordField()
                {
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        TargetCommand("focus-password");
                        Task.Run(passwordElement.SetFocus).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        Pump(75);
                        var active = reader.ReadInsertionField();
                        if (active is { IsPassword: true } && active.ElementId == passwordId) return active;
                    }
                    return null;
                }
                saved.Save(Guid.NewGuid(), "Gmail", SavedTextKind.Password, Encoding.Unicode.GetBytes(PasswordFixture));
                saved.Save(Guid.NewGuid(), "Outlook", SavedTextKind.Password, Encoding.Unicode.GetBytes("Andra testvärdet"));
                TargetCommand("seed-password");
                var protectedField = FocusPasswordField();
                var passwordAction = Field<Button>(keyboard, "passwordSuggestionAction");
                Check(protectedField?.IsPassword == true, "the external password field is recognized from actual focus");
                Call(keyboard, "SetFocusedSuggestionField", protectedField, null);
                Check(passwordAction.Visibility == Visibility.Visible && emptyIcon.Visibility == Visibility.Collapsed
                    && VisibleSuggestions().Length == 0, "the password field shows a dedicated choice instead of ordinary suggestions");
                Render(keyboard, Path.Combine(output, "desktop-password-action.png"));

                PasswordSuggestionWindow OpenPicker()
                {
                    passwordAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump();
                    return Field<PasswordSuggestionWindow>(keyboard, "passwordPicker");
                }
                IEnumerable<T> PickerElements<T>(DependencyObject item) where T : DependencyObject
                {
                    if (item is T match) yield return match;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                        foreach (var descendant in PickerElements<T>(VisualTreeHelper.GetChild(item, i))) yield return descendant;
                }
                Button Gmail(PasswordSuggestionWindow picker) => PickerElements<Button>(picker).Single(button =>
                    System.Windows.Automation.AutomationProperties.GetName(button) == "Gmail");
                var pendingHello = new TaskCompletionSource<WindowsHelloResult>();
                Task<WindowsHelloResult> nextVerification = pendingHello.Task;
                IntPtr verificationOwner = IntPtr.Zero;
                keyboard.GetType().GetField("verifyPassword", Fields).SetValue(keyboard,
                    (Func<IntPtr, System.Threading.CancellationToken, Task<WindowsHelloResult>>)((owner, _) =>
                    {
                        verificationOwner = owner;
                        return nextVerification;
                    }));
                var dwell = Field<DwellTracker>(keyboard, "dwellTracker");
                int visitBeforeHello = dwell.VisitVersion;
                var picker = OpenPicker();
                Check(picker is { IsVisible: true, IsUnlocked: false }
                    && verificationOwner == new WindowInteropHelper(picker).Handle
                    && PickerElements<TextBlock>(picker).All(text => text.Text is not ("Gmail" or "Hemligt" or "Outlook")),
                    "Windows Hello is requested before any credential names appear");
                Check(new[] { "0", "1", "BkSp", "Enter", "Sft" }.All(label =>
                    keys.Any(pair => pair.Key.Label == label && pair.Value.IsEnabled)),
                    "SoftKey keeps PIN typing keys usable throughout Windows Hello verification");
                Check(dwell.VisitVersion != visitBeforeHello, "opening verification invalidates a previously queued dwell click");
                Native.KeepOnTop(protectedField.Active);
                var pinPoint = new Native.POINT { X = edge[4], Y = edge[5] };
                var dwellTarget = (DwellTarget)Call(keyboard, "GetDwellTarget", pinPoint);
                Check(Native.IsWindowAt(pinPoint, protectedField.Active) && dwellTarget == DwellTarget.External,
                    "a password field in a separate window remains a dwell target during verification");
                int dwellClicks = 0;
                var mouseField = keyboard.GetType().GetField("mouseActions", Fields);
                var originalMouse = Field<MouseActionState>(keyboard, "mouseActions");
                try
                {
                    mouseField.SetValue(keyboard, new MouseActionState(_ => { dwellClicks++; return true; }, _ => false, _ => false, _ => false));
                    Call(keyboard, "PerformDwell", dwellTarget);
                    Check(dwellClicks == 1, "a completed dwell still issues a mouse click while verification is pending");
                }
                finally { mouseField.SetValue(keyboard, originalMouse); }
                int unintendedPresses = 0;
                var inputField = keyboard.GetType().GetField("keyInput", Fields);
                inputField.SetValue(keyboard, (Func<KeyDef, bool>)(_ => { unintendedPresses++; return true; }));
                Call(keyboard, "Press", keys.Keys.First(key => key.Label == "1"));
                Check(unintendedPresses == 0, "a PIN key sends no input to an ordinary window during verification");
                inputField.SetValue(keyboard, (Func<KeyDef, bool>)(_ => true));
                pendingHello.SetResult(WindowsHelloResult.Cancelled);
                Pump(100);
                Check(!picker.IsVisible && TargetCommand("password-unmodified") == "yes",
                    "cancelling Windows Hello closes the picker without filling a password");
                Check(!WindowsHelloResult.Unavailable.AllowsPasswordUse() && !WindowsHelloResult.Cancelled.AllowsPasswordUse(),
                    "an unavailable or cancelled prompt cannot authorize password access");

                protectedField = FocusPasswordField();
                Call(keyboard, "SetFocusedSuggestionField", protectedField, null);
                nextVerification = Task.FromResult(WindowsHelloResult.Verified);
                picker = OpenPicker();
                Check(picker is { IsVisible: true } && picker.Owner == keyboard && Field<bool>(keyboard, "mainWindowActionsLocked")
                    && PickerElements<Button>(picker).Where(button => button.Content is DockPanel)
                        .Select(System.Windows.Automation.AutomationProperties.GetName)
                        .SequenceEqual(new[] { "Gmail", "Hemligt", "Outlook" })
                    && PickerElements<TextBlock>(picker).All(text => !text.Text.Contains(PasswordFixture)),
                    "verification unlocks the picker with only the saved password names");
                Render(picker, Path.Combine(output, "desktop-password-picker.png"));
                Check(picker.ActualWidth <= 500 && picker.ActualHeight <= 245
                    && Gmail(picker).ActualWidth >= picker.ActualWidth - 55 && Gmail(picker).ActualHeight >= 46,
                    "the password cards keep a full-width dwell target for each name");

                var differentField = FocusOrdinary();
                Check(differentField != null && differentField.Active == protectedField.Active
                    && differentField.ElementId != protectedField.ElementId, "the external focus moved inside the same window");
                Gmail(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(200);
                Check(picker.IsVisible && TargetCommand("password-unmodified") == "yes"
                    && PickerElements<TextBlock>(picker).Any(text => text.Text == "Välj lösenordsfältet och öppna listan igen."),
                    "a picker never fills a different field in the same window");
                picker.Close();
                Pump();

                protectedField = FocusPasswordField();
                Check(protectedField?.IsPassword == true, "the password field can be focused again after closing the picker");
                Call(keyboard, "SetFocusedSuggestionField", protectedField, null);
                nextVerification = Task.FromResult(WindowsHelloResult.NotConfigured);
                picker = OpenPicker();
                Check(picker is { IsVisible: true, IsUnlocked: true }, "the existing session unlocks the picker when Windows Hello is not configured");
                Gmail(picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 30 && picker.IsVisible; i++) Pump(50);
                string fillFeedback = picker.IsVisible
                    ? PickerElements<TextBlock>(picker).FirstOrDefault(text => text.IsVisible && text.Text is
                        "Välj lösenordsfältet och öppna listan igen." or "Windows kunde inte fylla i lösenordet." or
                        "Släpp de aktiva tangenterna och försök igen.")?.Text
                    : null;
                Check(!picker.IsVisible, "choosing a saved password closes the picker" + (fillFeedback == null ? "" : " (" + fillFeedback + ")"));
                Pump(150);
                Check(TargetCommand("password-filled") == "yes", "the selected password replaces the previous characters");
                Call(keyboard, "SetFocusedSuggestionField", protectedField, null);
                Check(passwordAction.Visibility == Visibility.Collapsed && emptyIcon.Visibility == Visibility.Visible,
                    "the empty icon returns after a saved password is filled");
                Call(keyboard, "ClearFocusedSuggestions");

                var legacy = new SavedTextEntry(Guid.NewGuid(), "Gammal text", SavedTextKind.Text, new byte[] { 1, 2, 3 });
                storage.Save("saved-text.json", new[] { legacy }, keepBackup: false);
                var secret = Encoding.Unicode.GetBytes("Testlösen123!");
                SavedTextEntry chosen = null;
                string[] ordinaryWords = ["arbete", "familj", "frihet", "hemma", "musik", "tack", "välkommen"];
                var original = new WordListWindow(() => ordinaryWords, _ => null, _ => null, (_, _) => null);
                ShowOffscreen(original);
                Size originalSize = original.WordsPage.RenderSize;
                original.Close();

                var dialog = new TabbedWordListWindow(() => ordinaryWords, _ => null, _ => null, (_, _) => null,
                    saved, hasTarget: true, passwordTarget: true,
                    item => { chosen = item; return Task.FromResult<string>(null); }, specificSuggestionsEnabled: true, _ => true);
                try
                {
                    ShowOffscreen(dialog);
                    var tabs = Field<TabControl>(dialog, "tabs");
                    Check(dialog.WordsPage.RenderSize == originalSize, "the ordinary word-list body keeps its original dimensions");
                    Render(dialog, Path.Combine(output, "ordinary-words-tab.png"));
                    ((System.Windows.Automation.Provider.ISelectionItemProvider)
                        new System.Windows.Automation.Peers.TabItemAutomationPeer(tabs.Items[1],
                            new System.Windows.Automation.Peers.TabControlAutomationPeer(tabs))
                            .GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
                    FinishTabTransition();
                    var editor = Field<SavedSuggestionsPage>(dialog, "specific");
                    var entries = Field<ListBox>(editor, "entries");
                    Check(dialog.IsSpecific && entries.Items.Count == 0 && !editor.IsEditing,
                        "the specific tab opens as a list and excludes previously saved free text");
                    var kinds = Field<Dictionary<SavedTextKind, RadioButton>>(editor, "kinds");

                    void NewSuggestion(SavedTextKind kind, string name, string value)
                    {
                        Call(editor, "OpenEditor", new object[] { null });
                        ((System.Windows.Automation.Provider.ISelectionItemProvider)
                            new System.Windows.Automation.Peers.RadioButtonAutomationPeer(kinds[kind])
                                .GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
                        Field<TextBox>(editor, "name").Text = name;
                        if (kind == SavedTextKind.Password) Field<PasswordBox>(editor, "secret").Password = value;
                        else Field<TextBox>(editor, "text").Text = value;
                    }

                    NewSuggestion(SavedTextKind.Email, "Privat e-post", "prov@example.invalid");
                    Call(editor, "SaveEntry");
                    Check(saved.Read().Single().Kind == SavedTextKind.Email && !editor.IsEditing,
                        "saving an email returns to the list");

                    NewSuggestion(SavedTextKind.Password, "Mitt konto", "Testlösen123!");
                    var password = Field<PasswordBox>(editor, "secret");
                    Check(password.IsVisible && !Field<TextBox>(editor, "text").IsVisible,
                        "a saved password is entered in the masked field");
                    Call(editor, "SaveEntry");
                    Check(password.SecurePassword.Length == 0, "saving clears the password field");

                    NewSuggestion(SavedTextKind.Phone, "", "inte ett nummer");
                    Call(editor, "SaveEntry");
                    Check(editor.IsEditing && saved.Read().Length == 2, "a non-number is rejected");
                    Field<TextBox>(editor, "text").Text = "+46 70-123 45 67";
                    Render(dialog, Path.Combine(output, "specific-phone-editor.png"));
                    Call(editor, "SaveEntry");
                    Check(saved.Read().Length == 3 && !editor.IsEditing
                        && saved.Read().Single(item => item.Kind == SavedTextKind.Phone).Name == "+46 70-123 45 67"
                        && storage.Load("saved-text.json", Array.Empty<SavedTextEntry>()).Any(item => item.Id == legacy.Id),
                        "a formatted phone number can be saved without a name while legacy entries are preserved");

                    entries.UpdateLayout();
                    var phoneRow = (ListBoxItem)entries.ItemContainerGenerator.ContainerFromIndex(2);
                    ((System.Windows.Automation.Provider.ISelectionItemProvider)
                        new System.Windows.Automation.Peers.ListBoxItemAutomationPeer(entries.Items[2],
                            new System.Windows.Automation.Peers.ListBoxAutomationPeer(entries))
                            .GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
                    Check(phoneRow.ActualHeight >= 47 && phoneRow.IsSelected && !editor.IsEditing,
                        "the full-size phone row is selectable without opening the editor");
                    Render(dialog, Path.Combine(output, "specific-suggestions-tab.png"));
                    Field<Button>(editor, "use").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(chosen?.Kind == SavedTextKind.Phone && !dialog.IsVisible
                        && Encoding.Unicode.GetString(SavedTextStore.Open(chosen)) == "+46 70-123 45 67",
                        "using a phone suggestion returns the original number and closes the dialog");
                }
                finally { if (dialog.IsVisible) dialog.Close(); }
                var entry = saved.Read().Single(item => item.Kind == SavedTextKind.Password);
                Check(SavedTextStore.Open(entry).SequenceEqual(secret) && !entry.Encrypted.SequenceEqual(secret),
                    "only the chosen password is decrypted on demand and stored as encrypted data");
                Check(saved.Remove(entry.Id) && saved.Read().All(item => item.Id != entry.Id)
                    && !File.Exists(Path.Combine(folder, "saved-text.json.bak")),
                    "removed saved passwords are absent from the list and its storage backups");
            }
            finally { keyboard.EnablePreviewClose(); keyboard.Close(); }
        }
        finally
        {
            if (!targetProcess.HasExited)
            {
                targetProcess.StandardInput.WriteLine("close");
                targetProcess.WaitForExit(5000);
            }
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    static void SignInToolbar(string output)
    {
        var logonToolbar = new DragKeyWindow(() => { }, () => { }, () => { }, () => { }, true);
        try
        {
            logonToolbar.SetDockLayout(true, false);
            logonToolbar.Refresh(true, MouseAction.Left, false, true);
            logonToolbar.Width = 80; logonToolbar.Height = 38;
            ShowOffscreen(logonToolbar); Render(logonToolbar, Path.Combine(output, "logon-autoclick-only.png"));
            Check(Field<System.Windows.Controls.Primitives.UniformGrid>(logonToolbar, "buttons").Children.Count == 1,
                "the rendered sign-in toolbar has exactly one autoclick control");
            var autoClick = Field<DragKeyButton>(logonToolbar, "autoClick");
            var artwork = (Viewbox)autoClick.Content;
            Check(artwork.Child is Image icon && icon.Source == DragKeySymbols.Image(DragKeySymbol.AutoClick)
                && artwork.Width == 18 && artwork.Height == 18 && autoClick.Padding == new Thickness(3),
                "the sign-in panel shares the desktop AutoClick drawing inside its original icon bounds");
            var signInIcon = (DragKeyIconImage)artwork.Child;
            Check(signInIcon.UsesCompactArtwork == (18 * VisualTreeHelper.GetDpi(signInIcon).DpiScaleX < DragKeyIconImage.CompactPixelLimit),
                "sign-in chooses the AutoClick drawing for the actual screen pixel size");
        }
        finally { logonToolbar.CloseWithApplication(); }
    }

    static void OtherDialogs(string output)
    {
        var normalToolbar = new DragKeyWindow(() => { }, () => { }, () => { }, () => { });
        normalToolbar.SetDockLayout(true, false);
        Check(Field<System.Windows.Controls.Primitives.UniformGrid>(normalToolbar, "buttons").Children.Cast<UIElement>()
                .Count(button => button.Visibility == Visibility.Visible) == 9,
            "the desktop toolbar includes power and quick move alongside the existing controls");
        normalToolbar.CloseWithApplication();
        // All samples and callbacks are isolated; these dialogs never open the user's word files.
        var cfg = new Settings { AutoClick = false, Repeat = true, DwellMs = 800, JitterPx = 20, AlwaysOnTop = false };
        int changes = 0;
        bool writable = false;
        var mouse = new MouseSettingsWindow(cfg, () => { changes++; return writable; });
        try
        {
            ShowOffscreen(mouse); Render(mouse, Path.Combine(output, "dialog-autoclick.png"));
            var mouseApply = Field<Button>(mouse, "apply");
            mouseApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!mouseApply.IsEnabled && changes == 0 && mouse.IsVisible, "Autoclick Apply starts disabled and ignores clean requests");
            var repeat = Field<SoftKeyCheckBox>(mouse, "repeat");
            repeat.IsChecked = false;
            Check(mouseApply.IsEnabled, "changing only Autoclick repeat enables Apply");
            repeat.IsChecked = true;
            Check(!mouseApply.IsEnabled, "undoing an Autoclick option disables Apply");
            var handle = new WindowInteropHelper(mouse).Handle;
            Native.GetWindowRect(handle, out var before);
            Check(WindowPositioning.TryWorkArea(before, out var area), "the autoclick preview has a visible work area");
            mouse.Topmost = true;
            Native.MoveWithoutActivation(handle, area.Left + 15, area.Top + 15); Pump();
            var preview = Field<Border>(mouse, "preview");
            var point = preview.PointToScreen(new Point(preview.ActualWidth / 2, preview.ActualHeight / 2));
            var sample = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
            var arc = Field<System.Windows.Shapes.Path>(mouse, "previewArc");
            var checkmark = Field<System.Windows.Shapes.Path>(mouse, "previewCheck");
            Check(mouse.UpdateDwellPreview(sample, 0) && mouse.UpdateDwellPreview(sample, 400)
                && Field<double>(mouse, "previewProgress") == 0.5 && !arc.Data.IsEmpty() && !checkmark.IsVisible,
                "the preview runs while live autoclick is paused");
            Render(mouse, Path.Combine(output, "dialog-autoclick-countdown.png"));
            var moved = new Native.POINT { X = sample.X + 21, Y = sample.Y };
            Check(mouse.UpdateDwellPreview(moved, 800) && arc.Data.IsEmpty(),
                "movement outside the trial tolerance restarts its waiting time");
            Check(mouse.UpdateDwellPreview(moved, 1600) && arc.Data is EllipseGeometry && checkmark.IsVisible && changes == 0 && !cfg.AutoClick,
                "the completed ring shows a checkmark without saving or enabling autoclick");
            Render(mouse, Path.Combine(output, "dialog-autoclick-complete.png"));
            Check(!mouse.UpdateDwellPreview(new Native.POINT { X = area.Left, Y = area.Top }, 1700) && arc.Data.IsEmpty() && !checkmark.IsVisible,
                "leaving the preview clears the trial");
            var dwell = Field<TextBox>(mouse, "dwell");
            dwell.Text = "0800";
            Check(!mouseApply.IsEnabled, "equivalent numeric formatting is not a new Autoclick value");
            Call(mouse, "Select", dwell); dwell.Clear();
            foreach (string label in new[] { "1", "2" }) mouse.TryType(KeyboardLayout.Rows.SelectMany(row => row).First(k => k.Label == label));
            Check(dwell.Text == "12", "the redesigned autoclick dialog accepts SoftKey numeric input");
            Field<TextBox>(mouse, "jitter").Text = "999";
            Check(!(bool)Call(mouse, "Apply") && changes == 0 && mouse.IsVisible
                && Field<TextBlock>(mouse, "dwellError").IsVisible && Field<TextBlock>(mouse, "jitterError").IsVisible,
                "invalid numbers leave both field errors in the open autoclick dialog");
            dwell.Text = "650";
            Field<TextBox>(mouse, "jitter").Text = "15";
            Check(!(bool)Call(mouse, "Apply") && changes == 1 && cfg.DwellMs == 800 && cfg.JitterPx == 20
                && Field<TextBlock>(mouse, "saveError").IsVisible,
                "a failed save leaves the previous dwell settings active and the new values available for retry");
            writable = true;
            Check((bool)Call(mouse, "Apply") && changes == 2 && cfg.DwellMs == 650 && cfg.JitterPx == 15 && !mouseApply.IsEnabled,
                "retrying a valid autoclick change saves and activates both values");
        }
        finally { mouse.Close(); }

        var requestedTiming = new List<AutoclickTimingSettings>();
        var pendingTiming = new TaskCompletionSource();
        var sharedMouse = new MouseSettingsWindow(cfg, () => true, timing =>
        {
            requestedTiming.Add(timing);
            return pendingTiming.Task;
        });
        try
        {
            ShowOffscreen(sharedMouse);
            const string signInHelp = "Den här inställningen sparas också för Windows-inloggningen.";
            var helpIcons = Descendants<Border>(sharedMouse).Where(border =>
                border.ToolTip is ToolTip { Content: Border { Child: TextBlock hint } } && hint.Text == signInHelp).ToArray();
            var delayInput = Field<TextBox>(sharedMouse, "dwell");
            var toleranceInput = Field<TextBox>(sharedMouse, "jitter");
            bool BesideField(Border icon, TextBox field) => icon.IsVisible && ToolTipService.GetIsEnabled(icon)
                && icon.TranslatePoint(new Point(icon.ActualWidth, 0), sharedMouse).X <= field.TranslatePoint(default, sharedMouse).X
                && AutomationProperties.GetHelpText(field).Contains(signInHelp);
            Check(helpIcons.Length == 2 && BesideField(helpIcons[0], delayInput) && BesideField(helpIcons[1], toleranceInput)
                && Math.Abs(helpIcons[0].TranslatePoint(default, sharedMouse).X - helpIcons[1].TranslatePoint(default, sharedMouse).X) < 1
                && Descendants<TextBlock>(sharedMouse).All(block => !block.Text.Contains("Windows-inloggning")),
                "both aligned autoclick help icons have hover tips and accessible field descriptions");
            Render(sharedMouse, Path.Combine(output, "dialog-autoclick-signin-help.png"));
            var helpTip = (ToolTip)helpIcons[0].ToolTip;
            try
            {
                helpTip.PlacementTarget = helpIcons[0];
                helpTip.IsOpen = true;
                Pump(200);
                Check(PresentationSource.FromVisual(helpTip) != null && helpTip.ActualWidth > 0 && helpTip.ActualHeight > 0,
                    "the rounded help tip opens as a visible popup");
                Render(helpTip, Path.Combine(output, "dialog-autoclick-signin-hover.png"));
            }
            finally { helpTip.IsOpen = false; }
            Field<TextBox>(sharedMouse, "dwell").Text = "750";
            Field<TextBox>(sharedMouse, "jitter").Text = "35";
            var applyTiming = Field<Button>(sharedMouse, "apply");
            applyTiming.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(requestedTiming.Single() == new AutoclickTimingSettings(750, 35) && !applyTiming.IsEnabled,
                "applying autoclick forwards both values together and waits for Windows confirmation");
            pendingTiming.SetException(new InvalidOperationException("Ändringen avbröts i Windows."));
            Pump();
            Check(sharedMouse.IsVisible && applyTiming.IsEnabled && Field<TextBlock>(sharedMouse, "saveError").IsVisible,
                "declining confirmation leaves the autoclick dialog available for retry");
            pendingTiming = new TaskCompletionSource();
            applyTiming.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pendingTiming.SetResult();
            Pump();
            Check(!sharedMouse.IsVisible && requestedTiming.SequenceEqual(new[] { new AutoclickTimingSettings(750, 35), new(750, 35) }),
                "retrying forwards the same timing and closes the autoclick dialog on success");
        }
        finally { if (sharedMouse.IsVisible) sharedMouse.Close(); }

        var words = new HashSet<string> { "arbete", "familj", "frihet", "hemma", "musik", "tack", "välkommen" };
        var list = new WordListWindow(() => words,
            word => { words.Add(word.Trim().ToLower()); return null; },
            word => { words.Remove(word); return null; },
            (original, replacement) => { words.Remove(original); words.Add(replacement.Trim().ToLower()); return null; });
        try
        {
            ShowOffscreen(list); Render(list, Path.Combine(output, "dialog-word-list.png"));
            var searchButton = Field<Button>(list, "searchButton");
            Check(searchButton.ActualWidth >= 40 && searchButton.ActualHeight >= 40, "word-list search has a 40-DIP target");
            Field<TextBox>(list, "wordInput").Text = "nyttord";
            Field<Button>(list, "add").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(words.Contains("nyttord") && Field<TextBox>(list, "wordInput").Text == "", "adding a word still works after the word-list layout change");
            Call(list, "SetSearchOpen", true); Field<TextBox>(list, "searchInput").Text = "fri"; Call(list, "UpdateSearch"); Pump(200);
            var rows = Field<ListBox>(list, "wordList");
            Check(rows.Items.Count == 1 && ((WordListItem)rows.Items[0]).Word == "frihet", "search still filters the redesigned word list");
            Render(list, Path.Combine(output, "dialog-word-search.png"));
            rows.SelectedIndex = 0; Call(list, "BeginEditing");
            Field<WordListItem>(list, "editingItem").Draft = "friheten";
            Render(list, Path.Combine(output, "dialog-word-edit.png"));
            Call(list, "SaveEditing");
            Check(words.Contains("friheten") && !words.Contains("frihet"), "inline word editing still uses the existing rename callback");
        }
        finally { list.Close(); }

        foreach (var sample in new (Window Window, string File)[]
        {
            (new CloseConfirmationWindow(), "dialog-close.png"),
            (new RemoveWordConfirmationWindow("friheten"), "dialog-remove-word.png"),
            (new RemoveWordConfirmationWindow(new string('å', 300)), "dialog-remove-long-word.png"),
            (new SoftKeyNoticeWindow("SoftKey", "SoftKey kunde inte kontrollera om programmet redan körs. Stäng en eventuell befintlig instans och försök igen."), "dialog-notice.png")
        })
        {
            try { ShowOffscreen(sample.Window); Render(sample.Window, Path.Combine(output, sample.File)); }
            finally { sample.Window.Close(); }
        }
    }

    static void UpdateUiChecks(string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-update-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new JsonFileStorage(folder);
            var keyboard = new KeyboardWindow(storage);
            Field<DispatcherTimer>(keyboard, "dwellTimer").Stop();
            try
            {
                ShowOffscreen(keyboard);
                var indicator = Field<Border>(keyboard, "updateIndicator");
                var headers = Field<Button[]>(keyboard, "menuHeaders");
                var menu = Field<StackPanel>(keyboard, "menuLayer");
                Check(indicator.Visibility == Visibility.Collapsed,
                    "Arkiv update indicator is collapsed when no update is available");
                double arkivWidthWithoutUpdate = headers[0].ActualWidth;
                double gapWithoutUpdate = headers[1].TranslatePoint(default, menu).X - (headers[0].TranslatePoint(default, menu).X + headers[0].ActualWidth);
                double standardGap = headers[2].TranslatePoint(default, menu).X - (headers[1].TranslatePoint(default, menu).X + headers[1].ActualWidth);
                Check(Math.Abs(gapWithoutUpdate - standardGap) <= 0.5,
                    "no extra menu gap appears between Arkiv and Tangentbord without an update");
                Render(menu, Path.Combine(output, "update-menu-none.png"));

                var preview = new SoftKeyUpdates.Preview(42, new string('a', 40), "2026-11-27T12:00:00Z",
                    "• Tydligare tangenttext och smidigare storleksändring.\n• Uppdaterad menyindikator och ny uppdateringsdialog.",
                    new SoftKeyUpdates.Asset(42, "SoftKey-update-42.zip", "uploaded", 1784688, "sha256:" + new string('a', 64)));
                var pending = new TaskCompletionSource<SoftKeyUpdates.Preview>();
                int requests = 0;
                var suspended = typeof(KeyboardWindow).GetField("desktopSessionSuspended", Fields);
                suspended.SetValue(keyboard, true);
                keyboard.EnableUpdateChecks(_ => ++requests == 1
                    ? Task.FromException<SoftKeyUpdates.Preview>(new IOException("offline"))
                    : pending.Task);
                var timer = Field<DispatcherTimer>(keyboard, "updateTimer");
                Check(!timer.IsEnabled && requests == 0, "a suspended launch defers the initial update check");
                suspended.SetValue(keyboard, false);
                Call(keyboard, "ResumeUpdateChecks");
                Check(timer.IsEnabled && timer.Interval == TimeSpan.FromSeconds(2),
                    "resuming schedules the initial background check");
                timer.Interval = TimeSpan.FromMilliseconds(1);
                Pump();
                Check(requests == 1 && timer.IsEnabled && timer.Interval == TimeSpan.FromMinutes(2),
                    "a failed background check schedules a quick retry");
                var arkivItems = (StackPanel)Call(keyboard, "BuildMenuPanel", 0);
                typeof(KeyboardWindow).GetField("openMenu", Fields).SetValue(keyboard, 0);
                var updateMenuItem = (Button)arkivItems.Children[0];
                var badge = ((DockPanel)updateMenuItem.Content).Children.OfType<Border>().Single();
                Check(badge.Visibility == Visibility.Collapsed, "Arkiv initially has no update badge");
                timer.Interval = TimeSpan.FromMilliseconds(1);
                Pump();
                Check(requests == 2 && !timer.IsEnabled, "the retry runs in the background without opening the update dialog");
                pending.SetResult(preview);
                Pump();
                Check(indicator.Visibility == Visibility.Visible && indicator.Background == SoftKeyKeyDesign.SlateAccent
                    && Math.Abs(headers[0].ActualWidth - arkivWidthWithoutUpdate) <= 0.5,
                    "Arkiv displays the corner status dot without shifting the menu bar");
                Check(badge.Visibility == Visibility.Visible && badge.Child is TextBlock { Text: "Ny" }
                    && timer.IsEnabled && timer.Interval == TimeSpan.FromMinutes(15),
                    "the existing Arkiv row gains the Ny badge and the regular polling interval resumes");
                Render(menu, Path.Combine(output, "update-menu-available.png"));
                Call(keyboard, "CloseMenu");
            }
            finally { keyboard.EnablePreviewClose(); keyboard.Close(); }

            var parsedNotes = SoftKeyReleaseNotes.Parse(
                "Tryggare uppdatering och tydligare statusrad.\n" +
                "• [Nyhet] Inbyggd uppdatering i ren C# – Hämtar och verifierar direkt utan externa skript.\n" +
                "• Tydligare versionsbeskrivning\n  Visar kategorier, verifieringsstatus och vad som bevaras.\n" +
                "• Fix: Windows-tangenten på statusraden – Visar nu Win när tangenten är nedtryckt.");
            Check(parsedNotes.Summary == "Tryggare uppdatering och tydligare statusrad."
                && parsedNotes.Entries.Count == 3
                && parsedNotes.Entries[0].Category == SoftKeyReleaseNotes.Category.Nyhet
                && parsedNotes.Entries[0].Title == "Inbyggd uppdatering i ren C#"
                && parsedNotes.Entries[0].Detail == "Hämtar och verifierar direkt utan externa skript."
                && parsedNotes.Entries[1].Category == SoftKeyReleaseNotes.Category.Förbättring
                && parsedNotes.Entries[1].Detail == "Visar kategorier, verifieringsstatus och vad som bevaras."
                && parsedNotes.Entries[2].Category == SoftKeyReleaseNotes.Category.Fix,
                "release notes parser extracts lead summary, categories, titles and detail lines");

            var pendingFetch = new TaskCompletionSource<SoftKeyUpdates.Preview>();
            var searching = new SoftKeyUpdateWindow(null, () => pendingFetch.Task, (_, _) => Task.CompletedTask);
            try
            {
                ShowOffscreen(searching);
                Check(!searching.ShowInTaskbar && !searching.ShowActivated,
                    "update dialog opens without stealing taskbar space or focus");
                var searchBar = Field<ProgressBar>(searching, "searchProgress");
                Check(searchBar.Visibility == Visibility.Visible
                    && Field<TextBlock>(searching, "title").Text.Contains("Söker"),
                    "update dialog shows progress feedback while checking for updates");
                Check(searchBar.Template.FindName("PART_GlowRect", searchBar) is Border
                    { Visibility: Visibility.Visible } glow && glow.Background == SoftKeyKeyDesign.SlateAccent
                    && searchBar.Template.FindName("Fill", searchBar) is Border { Visibility: Visibility.Collapsed },
                    "the shared progress template shows a slate segment during an indeterminate check");
                Render(searching, Path.Combine(output, "dialog-update-searching.png"));

                pendingFetch.SetResult(null);
                Pump();
                Check(Field<ProgressBar>(searching, "searchProgress").Visibility == Visibility.Collapsed
                    && Field<Border>(searching, "description").Visibility == Visibility.Collapsed
                    && Field<Border>(searching, "reassuranceBanner").Visibility == Visibility.Collapsed
                    && Field<Button>(searching, "check").Visibility == Visibility.Visible
                    && Field<Button>(searching, "install").Visibility == Visibility.Collapsed,
                    "up-to-date state hides release notes and shows the check button");
                Render(searching, Path.Combine(output, "dialog-update-uptodate.png"));

                var failedFetch = new SoftKeyUpdateWindow(null, () => Task.FromException<SoftKeyUpdates.Preview>(new IOException("offline")),
                    (_, _) => Task.CompletedTask);
                try
                {
                    ShowOffscreen(failedFetch);
                    Pump();
                    Check(Field<Border>(failedFetch, "feedbackCard").Visibility == Visibility.Visible
                        && Field<Button>(failedFetch, "check").IsEnabled,
                        "failed update check shows feedback card and allows retry");
                }
                finally { failedFetch.Close(); }
            }
            finally { searching.Close(); }

            var previewUpdate = new SoftKeyUpdates.Preview(9, new string('b', 40), "2026-11-27T14:00:00Z",
                "Tryggare inbyggd uppdatering, tydligare dialog och buggfix för statusraden.\n" +
                "• [Nyhet] Inbyggd uppdatering i ren C# – Hämtar och verifierar uppdateringar direkt utan externa skript.\n" +
                "• [Förbättring] Tydligare versionsbeskrivning – Visar kategorier, verifieringsstatus och vad som bevaras.\n" +
                "• [Fix] Windows-tangenten på statusraden – Visar nu när Windows-tangenten är nedtryckt, precis som Ctrl.",
                new SoftKeyUpdates.Asset(9, "SoftKey-update-9.zip", "uploaded", 1_153_434, "sha256:" + new string('b', 64)));
            var availableDialog = new SoftKeyUpdateWindow(previewUpdate, () => Task.FromResult(previewUpdate), (_, _) => Task.CompletedTask);
            try
            {
                ShowOffscreen(availableDialog);
                Check(Field<Border>(availableDialog, "description").Visibility == Visibility.Visible
                    && Field<Border>(availableDialog, "versionBadge").Visibility == Visibility.Visible
                    && Field<Border>(availableDialog, "reassuranceBanner").Visibility == Visibility.Visible
                    && Field<Border>(availableDialog, "changeCountBadge").Visibility == Visibility.Visible
                    && Field<TextBlock>(availableDialog, "changeCountText").Text == "3 ändringar"
                    && Field<Button>(availableDialog, "install").Visibility == Visibility.Visible
                    && Field<Button>(availableDialog, "check").Visibility == Visibility.Collapsed,
                    "available update state shows structured release notes, verification metadata and reassurance banner");
                Render(availableDialog, Path.Combine(output, "dialog-update-available.png"));

                var progressView = Field<SoftKeyUpdateProgressView>(availableDialog, "progress");
                Field<StackPanel>(availableDialog, "summary").Visibility = Visibility.Collapsed;
                progressView.Content.Visibility = Visibility.Visible;
                progressView.Show(new SoftKeyUpdates.Progress(58, "Hämtar uppdateringen", "SoftKey-update-9.zip  ·  640 KB av 1,1 MB"));
                Pump();
                var downloadBar = Field<ProgressBar>(progressView, "bar");
                Check(downloadBar.Template.FindName("Fill", downloadBar) is Border { Visibility: Visibility.Visible } fill
                    && fill.Background == SoftKeyKeyDesign.SlateAccent && downloadBar.ActualWidth > 0
                    && Math.Abs(fill.ActualWidth - downloadBar.ActualWidth * 0.58) <= 2,
                    "the shared progress template fills the reported share of the download track");
                Render(availableDialog, Path.Combine(output, "dialog-update-progress.png"));
            }
            finally { availableDialog.Close(); }

            string xaml = SoftKeyUpdateProgressView.InstallationContent(488);
            var parsed = (DependencyObject)System.Windows.Markup.XamlReader.Parse(xaml);
            Check(LogicalTreeHelper.FindLogicalNode(parsed, "UpdateStatus") is TextBlock
                && LogicalTreeHelper.FindLogicalNode(parsed, "UpdateDetail") is TextBlock
                && LogicalTreeHelper.FindLogicalNode(parsed, "UpdateBar") is ProgressBar,
                "installation progress XAML preserves UpdateStatus, UpdateDetail and UpdateBar");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
