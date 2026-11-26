using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

interface IStartupTasks
{
    string UserSid { get; }
    string Read();
    void Write(string xml);
    void Delete();
    void Run();
}

// Only our per-user task is accessed. It runs with the signed-in user's interactive token.
sealed class WindowsStartupTasks : IStartupTasks
{
    public string UserSid { get; }
    readonly bool loginBroker;
    public string Name => (loginBroker ? "SoftKey-LoginBroker-" : "SoftKey-Autostart-") + UserSid;
    public WindowsStartupTasks(string userSid, bool loginBroker = false)
    { UserSid = new SecurityIdentifier(userSid).Value; this.loginBroker = loginBroker; }
    T WithFolder<T>(Func<dynamic, T> action)
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
        object folder = null;
        try
        {
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(service);
        }
    }
    public string Read() => WithFolder<string>(folder =>
    {
        object task = null;
        try { task = folder.GetTask(Name); return (string)((dynamic)task).Xml; }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
        finally { if (task != null) Marshal.FinalReleaseComObject(task); }
    });
    public void Write(string xml) => WithFolder<int>(folder =>
    {
        // Administrators and SYSTEM manage this task; its user may read and run it.
        string acl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;" + UserSid + ")";
        object task = folder.RegisterTask(Name, xml, 6, UserSid, null, 3, acl);
        Marshal.FinalReleaseComObject(task);
        return 0;
    });
    public void Delete() => WithFolder<int>(folder =>
    {
        try { folder.DeleteTask(Name, 0); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { }
        return 0;
    });
    public void Run() => WithFolder<int>(folder =>
    {
        object task = folder.GetTask(Name), running = null;
        try { running = ((dynamic)task).Run(null); return 0; }
        finally
        {
            if (running != null) Marshal.FinalReleaseComObject(running);
            Marshal.FinalReleaseComObject(task);
        }
    });
}

static class StartupTask
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    static bool SameDirectory(string value, string expected) => value != null && string.Equals(
        System.IO.Path.TrimEndingDirectorySeparator(value), System.IO.Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase);
    static bool SameUser(string value, string sid)
    {
        if (string.Equals(value, sid, StringComparison.OrdinalIgnoreCase)) return true;
        // Task Scheduler rewrites the trigger SID to its account name on registration.
        // Resolve only our known SID, never a name supplied by task XML.
        try { return string.Equals(value, new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value, StringComparison.OrdinalIgnoreCase); }
        catch (IdentityNotMappedException) { return false; }
    }
    public static string CreateDesktop(StartupInstallation installation, string sid, bool automatic, bool afterUnlock) =>
        Create(installation, sid, automatic, afterUnlock, loginBroker: false);

    public static string CreateBroker(StartupInstallation installation, string sid) =>
        Create(installation, sid, automatic: true, afterUnlock: false, loginBroker: true);

    static string Create(StartupInstallation installation, string sid, bool automatic, bool afterUnlock, bool loginBroker)
    {
        XElement E(string name, object value) => new(Ns + name, value);
        var triggers = new XElement(Ns + "Triggers");
        if (automatic)
            triggers.Add(E("LogonTrigger", new[] { E("Enabled", "true"), E("UserId", sid), E("Delay", "PT0S") }));
        if (afterUnlock)
            triggers.Add(E("SessionStateChangeTrigger", new[] { E("Enabled", "true"), E("UserId", sid), E("Delay", "PT0S"), E("StateChange", "SessionUnlock") }));
        return new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", loginBroker ? "Håll SoftKeys låsskärmstangentbord tillgängligt på widgetsytan." : "Starta SoftKey på skrivbordet enligt appens startinställningar.")),
            triggers,
            E("Principals", new XElement(Ns + "Principal", new XAttribute("id", "User"),
                E("UserId", sid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new[] {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"), E("StopIfGoingOnBatteries", "false"),
                E("AllowHardTerminate", "true"), E("StartWhenAvailable", "true"), E("RunOnlyIfNetworkAvailable", "false"),
                E("IdleSettings", new[] { E("StopOnIdleEnd", "false"), E("RestartOnIdle", "false") }),
                E("AllowStartOnDemand", "true"), E("Enabled", "true"), E("Hidden", "false"), E("RunOnlyIfIdle", "false"),
                E("WakeToRun", "false"), E("ExecutionTimeLimit", loginBroker ? "PT0S" : "PT12M"), E("Priority", loginBroker ? "4" : "1"),
                E("RestartOnFailure", new[] { E("Interval", "PT1M"), E("Count", "3") }) }),
            new XElement(Ns + "Actions", new XAttribute("Context", "User"), E("Exec", new[] {
                E("Command", installation.HelperExecutable), E("Arguments", loginBroker ? "--login-broker" : "--launch"), E("WorkingDirectory", installation.Root) })))
            .ToString(SaveOptions.DisableFormatting);
    }
    public static string InstallationRoot(string xml)
    {
        if (string.IsNullOrEmpty(xml) || xml.Length > 65536) return null;
        try
        {
            var command = XDocument.Parse(xml).Root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command")?.Value;
            return string.Equals(System.IO.Path.GetFileName(command), "SoftKey.Startup.exe", StringComparison.OrdinalIgnoreCase)
                ? System.IO.Path.GetDirectoryName(command) : null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException) { return null; }
    }
    public static (bool Automatic, bool AfterUnlock)? ReadDesktop(string xml, StartupInstallation installation, string sid) =>
        ReadTriggers(xml, installation, sid, loginBroker: false);

    public static bool MatchesBroker(string xml, StartupInstallation installation, string sid) =>
        ReadTriggers(xml, installation, sid, loginBroker: true).HasValue;

    static (bool Automatic, bool AfterUnlock)? ReadTriggers(string xml, StartupInstallation installation, string sid, bool loginBroker)
    {
        if (string.IsNullOrEmpty(xml) || xml.Length > 65536) return null;
        try
        {
            var task = XDocument.Parse(xml).Root;
            var triggers = task?.Element(Ns + "Triggers");
            var logon = triggers?.Element(Ns + "LogonTrigger");
            var unlock = triggers?.Element(Ns + "SessionStateChangeTrigger");
            var principals = task?.Element(Ns + "Principals");
            var principal = principals?.Element(Ns + "Principal");
            var settings = task?.Element(Ns + "Settings");
            var actions = task?.Element(Ns + "Actions");
            var exec = actions?.Element(Ns + "Exec");
            string V(XElement parent, string name) => parent?.Element(Ns + name)?.Value;
            bool Enabled(XElement element) => V(element, "Enabled") is not string enabled || System.Xml.XmlConvert.ToBoolean(enabled);
            bool MatchesUserTrigger(XElement trigger) => SameUser(V(trigger, "UserId"), sid) && Enabled(trigger)
                && (V(trigger, "Delay") is not string delay || System.Xml.XmlConvert.ToTimeSpan(delay) == TimeSpan.Zero);
            bool automatic = logon != null, afterUnlock = unlock != null;
            int knownTriggerCount = (automatic ? 1 : 0) + (afterUnlock ? 1 : 0);
            if (knownTriggerCount == 0 || triggers.Elements().Count() != knownTriggerCount
                || (loginBroker && (!automatic || afterUnlock))
                || (automatic && !MatchesUserTrigger(logon))
                || (afterUnlock && (V(unlock, "StateChange") != "SessionUnlock" || !MatchesUserTrigger(unlock)))) return null;

            bool matchesTask = principals?.Elements().Count() == 1 && actions?.Elements().Count() == 1
                && SameUser(V(principal, "UserId"), sid) && V(principal, "LogonType") == "InteractiveToken"
                && (V(principal, "RunLevel") == null || V(principal, "RunLevel") == "LeastPrivilege")
                && Enabled(settings) && V(settings, "Priority") == (loginBroker ? "4" : "1")
                && (!loginBroker || V(settings, "ExecutionTimeLimit") == "PT0S")
                && V(settings, "DisallowStartIfOnBatteries") == "false" && V(settings, "StopIfGoingOnBatteries") == "false"
                && V(settings, "RunOnlyIfIdle") != "true" && V(settings, "RunOnlyIfNetworkAvailable") != "true"
                && string.Equals(V(exec, "Command"), installation.HelperExecutable, StringComparison.OrdinalIgnoreCase)
                && V(exec, "Arguments") == (loginBroker ? "--login-broker" : "--launch") && SameDirectory(V(exec, "WorkingDirectory"), installation.Root);
            return matchesTask ? (automatic, afterUnlock) : null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or OverflowException) { return null; }
    }
}
