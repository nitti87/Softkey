using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

enum StartupHive { Machine, User, Logon }
sealed record StartupValue(object Value, RegistryValueKind Kind);

interface IStartupStore
{
    StartupValue Read(StartupHive hive, string path, string name);
    Dictionary<string, StartupValue> ReadKey(StartupHive hive, string path);
    void Write(StartupHive hive, string path, string name, StartupValue value);
    void DeleteValue(StartupHive hive, string path, string name);
    void DeleteKey(StartupHive hive, string path);
}

sealed class WindowsStartupStore : IStartupStore, IDisposable
{
    readonly RegistryKey machine, users;
    readonly string userSid;
    public WindowsStartupStore(string userSid)
    {
        this.userSid = new System.Security.Principal.SecurityIdentifier(userSid).Value;
        users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        try
        {
            using var user = users.OpenSubKey(this.userSid) ?? throw new InvalidOperationException("Användarens Windows-inställningar är inte tillgängliga.");
            machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }
        catch { users.Dispose(); throw; }
    }
    RegistryKey Hive(StartupHive hive) => hive == StartupHive.Machine ? machine : users;
    string KeyPath(StartupHive hive, string path) => hive switch
    {
        StartupHive.User => userSid + @"\" + path,
        StartupHive.Logon => @".DEFAULT\" + path,
        _ => path
    };
    public StartupValue Read(StartupHive hive, string path, string name)
    {
        using var key = Hive(hive).OpenSubKey(KeyPath(hive, path));
        object value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value == null ? null : new(value, key.GetValueKind(name));
    }
    public Dictionary<string, StartupValue> ReadKey(StartupHive hive, string path)
    {
        using var key = Hive(hive).OpenSubKey(KeyPath(hive, path));
        return key?.GetValueNames().ToDictionary(n => n, n => new StartupValue(key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind(n)));
    }
    public void Write(StartupHive hive, string path, string name, StartupValue value)
    {
        using var key = Hive(hive).CreateSubKey(KeyPath(hive, path));
        key.SetValue(name, value.Value, value.Kind);
    }
    public void DeleteValue(StartupHive hive, string path, string name)
    {
        using var key = Hive(hive).OpenSubKey(KeyPath(hive, path), true);
        key?.DeleteValue(name, false);
    }
    public void DeleteKey(StartupHive hive, string path) => Hive(hive).DeleteSubKeyTree(KeyPath(hive, path), false);
    public void Dispose() { machine.Dispose(); users.Dispose(); }
}

// All paths and executable names are fixed by the installed package, never supplied by the UI or pipe.
sealed class StartupConfiguration
{
    internal const string AtName = "SoftKey_Logon_v1";
    internal const string Accessibility = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility";
    internal const string AtPath = Accessibility + @"\ATs\" + AtName;
    internal const string SofTypePath = Accessibility + @"\ATs\oic_softype_45";
    internal const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    readonly IStartupStore store;
    readonly StartupInstallation installation;
    readonly IStartupTasks tasks;
    readonly IStartupTasks brokerTasks;
    readonly List<Action> undo = new();
    public StartupConfiguration(IStartupStore store, StartupInstallation installation, IStartupTasks tasks, IStartupTasks brokerTasks)
    { this.store = store; this.installation = installation; this.tasks = tasks; this.brokerTasks = brokerTasks; }
    bool LegacyAutomatic => store.Read(StartupHive.User, RunPath, "SoftKey") is { Kind: RegistryValueKind.String, Value: string command }
        && string.Equals(command, installation.RunCommand, StringComparison.OrdinalIgnoreCase);
    bool RegisteredBeforeSignIn => Contains(store.Read(StartupHive.Logon, Accessibility, "Configuration"));
    public bool NeedsUpgrade
    {
        get
        {
            string brokerXml = brokerTasks.Read();
            bool login = Read().Login;
            return LegacyAutomatic || (tasks.Read() is string xml && StartupTask.ReadDesktop(xml, installation, tasks.UserSid) == null)
                || RegisteredBeforeSignIn != login
                || (login ? !StartupTask.MatchesBroker(brokerXml, installation, brokerTasks.UserSid) : brokerXml != null);
        }
    }

    internal static List<string> List(StartupValue value)
    {
        if (value == null) return new();
        if (value.Kind != RegistryValueKind.String || value.Value is not string text) throw new InvalidOperationException("Windows startinställningar har ett oväntat format.");
        return text.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }
    internal static bool Contains(StartupValue value) => List(value).Any(s => s.Trim().Equals(AtName, StringComparison.OrdinalIgnoreCase));
    public StartupChoices Read(bool includeLegacyRun = true)
    {
        bool login = string.Equals(store.Read(StartupHive.Machine, AtPath, "StartExe")?.Value as string, installation.LogonExecutable, StringComparison.OrdinalIgnoreCase)
            && Contains(store.Read(StartupHive.Machine, Accessibility, "Configuration"))
            && Contains(store.Read(StartupHive.User, Accessibility, "Configuration"));
        var triggers = StartupTask.ReadDesktop(tasks.Read(), installation, tasks.UserSid);
        bool automatic = triggers?.Automatic == true || (includeLegacyRun && LegacyAutomatic);
        return new(login, automatic, triggers?.AfterUnlock == true);
    }
    void Set(StartupHive hive, string path, string name, StartupValue value)
    {
        var previous = store.Read(hive, path, name);
        undo.Add(() => { if (previous == null) store.DeleteValue(hive, path, name); else store.Write(hive, path, name, previous); });
        if (value == null) store.DeleteValue(hive, path, name); else store.Write(hive, path, name, value);
    }
    void Membership(StartupHive hive, bool enabled, bool record = true)
    {
        var list = List(store.Read(hive, Accessibility, "Configuration"));
        bool previous = list.Any(s => s.Trim().Equals(AtName, StringComparison.OrdinalIgnoreCase));
        if (previous == enabled) return;
        if (record) undo.Add(() => Membership(hive, previous, false));
        if (enabled) list.Add(AtName);
        else list.RemoveAll(s => s.Trim().Equals(AtName, StringComparison.OrdinalIgnoreCase));
        store.Write(hive, Accessibility, "Configuration", new(string.Join(",", list), RegistryValueKind.String));
    }
    void RemoveAt()
    {
        var previous = store.ReadKey(StartupHive.Machine, AtPath);
        if (previous == null) return;
        undo.Add(() => { foreach (var entry in previous) store.Write(StartupHive.Machine, AtPath, entry.Key, entry.Value); });
        store.DeleteKey(StartupHive.Machine, AtPath);
    }
    public StartupChoices Apply(StartupRequest request, Action<StartupProgress> progress)
    {
        undo.Clear();
        var desired = request.Choices;
        var original = Read(); // Validate both lists before any change.
        _ = RegisteredBeforeSignIn;
        try
        {
            progress(new(5, "Kontrollerar installationen…"));
            installation.Validate(desired.Login, desired.StartsOnDesktop);
            if (original.Login != desired.Login || (!desired.Login && (store.ReadKey(StartupHive.Machine, AtPath) != null
                || Contains(store.Read(StartupHive.Machine, Accessibility, "Configuration")) || Contains(store.Read(StartupHive.User, Accessibility, "Configuration")))))
            {
                progress(new(30, "Ändrar start vid Windows-inloggning…"));
                if (desired.Login)
                {
                    undo.Add(installation.EnsureSettings(request, store));
                    if (store.ReadKey(StartupHive.Machine, SofTypePath) != null)
                    {
                        undo.Add(installation.EnsureSofTypeRecovery(store));
                        Set(StartupHive.Machine, SofTypePath, "SecureDesktopAccommodation", new(AtName, RegistryValueKind.String));
                    }
                    bool atExisted = store.ReadKey(StartupHive.Machine, AtPath) != null;
                    if (!atExisted) undo.Add(() => store.DeleteKey(StartupHive.Machine, AtPath));
                    foreach (var entry in new Dictionary<string, string>
                    {
                        ["ApplicationName"] = "SoftKey – Inloggning", ["Description"] = "Tangentbord och viloklick vid Windows-inloggning.",
                        ["ATExe"] = "SoftKey.Logon.exe", ["StartExe"] = installation.LogonExecutable, ["StartParams"] = "--ease-of-access",
                        ["SimpleProfile"] = "On-screen keyboard", ["Profile"] = "<HCIModel><Accommodation type=\"severe dexterity\"/></HCIModel>"
                    }) Set(StartupHive.Machine, AtPath, entry.Key, new(entry.Value, RegistryValueKind.String));
                    Set(StartupHive.Machine, AtPath, "TerminateOnDesktopSwitch", new(0, RegistryValueKind.DWord));
                    Set(StartupHive.Machine, AtPath, "CopySettingsToLockedDesktop", new(0, RegistryValueKind.DWord));
                    Membership(StartupHive.Machine, true); Membership(StartupHive.User, true);
                }
                else
                {
                    var accommodation = store.Read(StartupHive.Machine, SofTypePath, "SecureDesktopAccommodation");
                    if (string.Equals(accommodation?.Value as string, AtName, StringComparison.OrdinalIgnoreCase))
                    {
                        var recovery = installation.ReadSofTypeRecovery();
                        Set(StartupHive.Machine, SofTypePath, "SecureDesktopAccommodation", recovery.Present ? new(recovery.Value, RegistryValueKind.String) : null);
                    }
                    Membership(StartupHive.Machine, false); Membership(StartupHive.User, false);
                    RemoveAt();
                }
            }
            Membership(StartupHive.Logon, desired.Login);
            progress(new(65, "Ändrar automatisk start…"));
            string oldTask = tasks.Read();
            var desiredTriggers = (desired.Automatic, desired.AfterUnlock);
            if (desired.StartsOnDesktop ? StartupTask.ReadDesktop(oldTask, installation, tasks.UserSid) != desiredTriggers : oldTask != null)
            {
                undo.Add(() => { if (oldTask == null) tasks.Delete(); else tasks.Write(oldTask); });
                if (desired.StartsOnDesktop) tasks.Write(StartupTask.CreateDesktop(installation, tasks.UserSid, desired.Automatic, desired.AfterUnlock));
                else tasks.Delete();
            }
            // Migrate only our old Run command. The verified task is installed first.
            if (LegacyAutomatic) Set(StartupHive.User, RunPath, "SoftKey", null);
            string oldBrokerTask = brokerTasks.Read();
            if (desired.Login ? !StartupTask.MatchesBroker(oldBrokerTask, installation, brokerTasks.UserSid) : oldBrokerTask != null)
            {
                undo.Add(() => { if (oldBrokerTask == null) brokerTasks.Delete(); else brokerTasks.Write(oldBrokerTask); });
                if (desired.Login) brokerTasks.Write(StartupTask.CreateBroker(installation, brokerTasks.UserSid)); else brokerTasks.Delete();
            }
            progress(new(90, "Kontrollerar inställningarna…"));
            var actual = Read(includeLegacyRun: false);
            if (actual != desired || RegisteredBeforeSignIn != desired.Login
                || (desired.Login && !StartupTask.MatchesBroker(brokerTasks.Read(), installation, brokerTasks.UserSid)))
            {
                var error = new InvalidOperationException("Windows kunde inte bekräfta startinställningarna.");
                error.Data["StartupTaskXml"] = tasks.Read();
                throw error;
            }
            if (desired.Login) brokerTasks.Run();
            undo.Clear();
            return actual;
        }
        catch (Exception error)
        {
            // Progress delivery must never prevent rollback after a disconnected UI.
            try { progress(new(0, "Återställer tidigare inställningar…")); } catch { }
            var failures = new List<Exception>();
            for (int i = undo.Count - 1; i >= 0; i--) { try { undo[i](); } catch (Exception ex) { failures.Add(ex); } }
            undo.Clear();
            if (failures.Count > 0) throw new InvalidOperationException("Ändringen misslyckades och tidigare inställningar kunde inte återställas helt. Öppna Startup igen för att kontrollera dem.", error);
            throw;
        }
    }
}
