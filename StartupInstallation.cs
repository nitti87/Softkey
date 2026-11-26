using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

sealed record SofTypeStartupRecovery(int Version, bool Present, string Value);
class StartupInstallation
{
    public string Root { get; }
    public string LogonDirectory { get; }
    public string MainExecutable => Path.Combine(Root, "SoftKey.exe");
    public string HelperExecutable => Path.Combine(Root, "SoftKey.Startup.exe");
    public string LogonExecutable => Path.Combine(LogonDirectory, "SoftKey.Logon.exe");
    public string RunCommand => "\"" + HelperExecutable + "\" --launch";
    public StartupInstallation(string root, string logonDirectory) { Root = Path.GetFullPath(root); LogonDirectory = Path.GetFullPath(logonDirectory); }
    public static StartupInstallation Current(IStartupStore store, string root = null)
    {
        root ??= AppContext.BaseDirectory;
        string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SoftKey-Logon-Test");
        string configured = store.Read(StartupHive.Machine, StartupConfiguration.AtPath, "StartExe")?.Value as string;
        string logon = new[] { root, legacy }.FirstOrDefault(p => string.Equals(Path.Combine(p, "SoftKey.Logon.exe"), configured, StringComparison.OrdinalIgnoreCase));
        logon ??= File.Exists(Path.Combine(legacy, "SoftKey.Logon.exe")) ? legacy : root;
        return new(root, logon);
    }
    public virtual void Validate(bool login, bool automatic)
    {
        StartupTrust.ProtectedDirectory(Root);
        StartupTrust.Verify(Path.Combine(Root, "SoftKey.Startup.exe"));
        StartupTrust.Verify(Path.Combine(Root, "SoftKey.Startup.dll"));
        if (automatic) { StartupTrust.Verify(MainExecutable); StartupTrust.Verify(Path.Combine(Root, "SoftKey.dll")); }
        if (login)
        {
            StartupTrust.ProtectedDirectory(LogonDirectory);
            foreach (string name in new[] { "SoftKey.Logon.exe", "SoftKey.Logon.dll", "SoftKey.Locked.exe", "SoftKey.Locked.dll" }) StartupTrust.Verify(Path.Combine(LogonDirectory, name));
        }
    }
    public virtual Action EnsureSettings(StartupRequest request, IStartupStore store)
    {
        string path = Path.Combine(LogonDirectory, "logon-settings.json");
        if (File.Exists(path)) return () => { };
        var safe = new LogonSettings { DwellMs = request.DwellMs, JitterPx = request.JitterPx, WindowWidth = request.WindowWidth, WindowHeight = request.WindowHeight };
        var normalized = safe.ToSettings();
        safe.DwellMs = normalized.DwellMs; safe.JitterPx = normalized.JitterPx; safe.WindowWidth = normalized.WindowWidth; safe.WindowHeight = normalized.WindowHeight;
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, safe);
        return () => File.Delete(path);
    }
    public virtual Action EnsureSofTypeRecovery(IStartupStore store)
    {
        StartupTrust.ProtectedDirectory(LogonDirectory);
        string path = Path.Combine(LogonDirectory, "softype-secure-desktop-backup.json");
        if (File.Exists(path)) { ReadSofTypeRecovery(); return () => { }; }
        var old = store.Read(StartupHive.Machine, StartupConfiguration.SofTypePath, "SecureDesktopAccommodation");
        if (old != null && (old.Kind != Microsoft.Win32.RegistryValueKind.String || old.Value is not string value
            || value.Length > 255 || value.Equals(StartupConfiguration.AtName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("SofTypes tidigare inloggningsinställning kunde inte säkerhetskopieras.");
        var recovery = new SofTypeStartupRecovery(1, old != null, old?.Value as string);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, recovery);
        return () => File.Delete(path);
    }
    public virtual SofTypeStartupRecovery ReadSofTypeRecovery()
    {
        StartupTrust.ProtectedDirectory(LogonDirectory);
        string path = Path.Combine(LogonDirectory, "softype-secure-desktop-backup.json");
        using var stream = File.OpenRead(path);
        if (stream.Length > 4096) throw new InvalidOperationException("SofTypes återställningsuppgifter kunde inte läsas.");
        var recovery = JsonSerializer.Deserialize<SofTypeStartupRecovery>(stream);
        if (recovery == null || recovery.Version != 1 || (recovery.Present && (recovery.Value == null || recovery.Value.Length > 255)))
            throw new InvalidOperationException("SofTypes återställningsuppgifter kunde inte läsas.");
        return recovery;
    }
}
