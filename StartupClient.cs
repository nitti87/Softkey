using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

sealed class StartupClient : ISoftKeyStartupService
{
    readonly Settings settings;
    public StartupClient(Settings settings = null) => this.settings = settings ?? new Settings();
    public StartupChoices Read()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var store = new WindowsStartupStore(identity.User.Value);
        return new StartupConfiguration(store, StartupInstallation.Current(store), new WindowsStartupTasks(identity.User.Value), new WindowsStartupTasks(identity.User.Value, true)).Read();
    }
    public async Task<StartupChoices> ApplyAsync(StartupChoices choices, IProgress<StartupProgress> progress)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using (var store = new WindowsStartupStore(identity.User.Value))
        {
            var current = new StartupConfiguration(store, StartupInstallation.Current(store), new WindowsStartupTasks(identity.User.Value), new WindowsStartupTasks(identity.User.Value, true));
            if (current.Read() == choices && !current.NeedsUpgrade) return choices;
        }
        var request = new StartupRequest { Login = choices.Login, Automatic = choices.Automatic, AfterUnlock = choices.AfterUnlock,
            DwellMs = settings.DwellMs, JitterPx = settings.JitterPx, WindowWidth = settings.WindowWidth, WindowHeight = settings.WindowHeight };
        var final = await RequestAsync(request, progress);
        var actual = Read();
        if (actual != choices || final.State != actual) throw new InvalidOperationException("Windows kunde inte bekräfta de valda inställningarna.");
        return actual;
    }

    public static async Task ApplyKeyboardLayoutAsync(KeyboardLayoutSettings layout, KeyboardAreaSize? area)
    {
        bool InstalledMatches() => KeyboardLayoutSettings.ReadInstalled() == layout
            && (area == null || KeyboardAreaSize.ReadInstalled() == area);
        if (InstalledMatches()) return;
        await RequestAsync(new StartupRequest { KeyboardLayout = layout, KeyboardArea = area }, null);
        if (!InstalledMatches())
            throw new InvalidOperationException("Windows kunde inte bekräfta de sparade tangentbordsinställningarna.");
    }

    public static async Task ApplyAutoclickTimingAsync(AutoclickTimingSettings timing)
    {
        if (AutoclickTimingSettings.ReadInstalled() == timing) return;
        await RequestAsync(new StartupRequest { AutoclickTiming = timing }, null);
        if (AutoclickTimingSettings.ReadInstalled() != timing)
            throw new InvalidOperationException("Windows kunde inte bekräfta de sparade autoklickinställningarna.");
    }

    public static async Task ApplyDragKeySizeAsync(bool small)
    {
        if (SignInDragKeySettings.ReadSmallSize() == small) return;
        await RequestAsync(new StartupRequest { SmallDragKeyIcons = small }, null);
        if (SignInDragKeySettings.ReadSmallSize() != small)
            throw new InvalidOperationException("Windows kunde inte bekräfta Dragkeys sparade storlek.");
    }

    static async Task<StartupProgress> RequestAsync(StartupRequest request, IProgress<StartupProgress> progress)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string root = AppContext.BaseDirectory, helper = Path.Combine(root, "SoftKey.Startup.exe");
        await Task.Run(() => { StartupTrust.ProtectedDirectory(root); StartupTrust.Verify(helper); StartupTrust.Verify(Path.Combine(root, "SoftKey.Startup.dll")); });
        string name = "SoftKey.Startup." + Guid.NewGuid().ToString("N");
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        Process process;
        progress?.Report(new(0, "Väntar på Windows godkännande…"));
        try
        {
            process = await Task.Run(() => Process.Start(new ProcessStartInfo(helper)
            {
                Arguments = "--configure " + name + " " + Environment.ProcessId,
                UseShellExecute = true, Verb = "runas", WorkingDirectory = root, WindowStyle = ProcessWindowStyle.Hidden
            }));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Ändringen avbröts i Windows."); }
        using (process)
        {
            var connect = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connect, exited) != connect && !pipe.IsConnected)
                throw new InvalidOperationException("Windows kunde inte starta ändringen.");
            await connect;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != process.Id)
                throw new InvalidOperationException("Installationsprocessen kunde inte bekräftas.");
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            StartupProgress final = null;
            while (true)
            {
                string line = await reader.ReadLineAsync(timeout.Token);
                if (line == null) break;
                if (line.Length > 8192) throw new InvalidOperationException("Installationen gav ett oväntat svar.");
                var update = JsonSerializer.Deserialize<StartupProgress>(line) ?? throw new InvalidOperationException("Installationen gav ett tomt svar.");
                if (update.Message?.Length > 1024) throw new InvalidOperationException("Installationen gav ett oväntat svar.");
                progress?.Report(update);
                if (update.Complete) { final = update; break; }
            }
            await exited;
            if (process.ExitCode != 0 || final?.Success != true) throw new InvalidOperationException(final?.Message ?? "Inställningarna kunde inte ändras.");
            return final;
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
}
