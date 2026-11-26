using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

static class SoftKeyUpdates
{
    internal sealed record Preview(long BuildNumber, string Commit, string VerifiedDownload);
    static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftKey", "Updates");
    static readonly string Credential = Path.Combine(Cache, "github-token.dpapi");
    static readonly string PowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    static readonly UTF8Encoding Utf8 = new(false, true);

    public static long BuildNumber => long.TryParse(typeof(SoftKeyUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "SoftKeyUpdateBuild")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build) ? build : 0;

    public static bool CanInstall => string.Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SoftKey-UIAccess-Test"), StringComparison.OrdinalIgnoreCase);

    public static bool HasCredential => File.Exists(Credential);

    public static void SetCredential(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024 || token.Any(c => c <= 32 || c >= 127))
            throw new ArgumentException("Klistra in ett GitHub-token med läsbehörighet till Contents.");
        Directory.CreateDirectory(Cache);
        var plain = Utf8.GetBytes(token);
        try
        {
            var encoded = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            var temporary = Path.Combine(Cache, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, encoded); File.Move(temporary, Credential, overwrite: true); }
            finally { File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static void ForgetCredential() => File.Delete(Credential);

    public static async Task<Preview> CheckAsync(CancellationToken cancel)
    {
        if ((new FileInfo(Credential)).Length > 8192) throw new IOException("GitHub-tokenet kunde inte läsas.");
        byte[] token = ProtectedData.Unprotect(File.ReadAllBytes(Credential), null, DataProtectionScope.CurrentUser);
        try
        {
            string command = Parameters(new { BuildNumber, Cache }) + Resource("SoftKey.UpdatePackage.ps1")
                + Resource("SoftKey.UpdateDistribution.ps1").Replace(". (Join-Path $PSScriptRoot 'SoftKey.UpdatePackage.ps1')", "", StringComparison.Ordinal)
                + """

                $headers = @{
                    Authorization = 'Bearer ' + [Console]::ReadLine()
                    Accept = 'application/vnd.github+json'
                    'X-GitHub-Api-Version' = '2022-11-28'
                }
                try {
                    $preview = Receive-SoftKeyPreview $updateParameters.BuildNumber $updateParameters.Cache $headers (Get-SoftKeyReleaseCertificate)
                    if ($preview) { $preview | ConvertTo-Json -Compress }
                } finally { $headers.Clear() }
                """;
            var start = StartInfo(command);
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            using var process = Process.Start(start) ?? throw new IOException("Kunde inte starta uppdateringskontrollen.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(token, timeout.Token);
                await process.StandardInput.BaseStream.WriteAsync(new byte[] { (byte)'\n' }, timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                string json = await output;
                string diagnostics = await error;
                if (process.ExitCode != 0)
                {
                    string message = diagnostics.Contains("(401)", StringComparison.Ordinal) || diagnostics.Contains("(403)", StringComparison.Ordinal)
                        || diagnostics.Contains("(404)", StringComparison.Ordinal)
                        ? "GitHub nekade åtkomst. Kontrollera tokenets läsbehörighet till SoftKey."
                        : "Uppdateringen kunde inte hämtas eller verifieras. Försök igen.";
                    throw new IOException(message);
                }
                if (string.IsNullOrWhiteSpace(json)) return null;
                var preview = JsonSerializer.Deserialize<Preview>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (preview == null || preview.BuildNumber <= BuildNumber || preview.VerifiedDownload != Path.Combine(Cache, $"SoftKey-update-{preview.BuildNumber}.zip"))
                    throw new IOException("Uppdateringspaketets identitet kunde inte bekräftas.");
                return preview;
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                throw;
            }
        }
        finally { CryptographicOperations.ZeroMemory(token); }
    }

    public static async Task StartInstallAsync(Preview preview)
    {
        if (!CanInstall || preview.BuildNumber <= BuildNumber)
            throw new InvalidOperationException("Använd den installerade versionen av SoftKey för att uppdatera.");
        await Task.Run(() =>
        {
            string root = AppContext.BaseDirectory;
            var installed = Path.Combine(root, "SoftKey.exe");
            StartupTrust.ProtectedDirectory(root);
            StartupTrust.Verify(installed);
            StartupTrust.Verify(Path.Combine(root, "SoftKey.dll"));
            using var desktop = Process.GetCurrentProcess();
            string command = Parameters(new
            {
                PackagePath = preview.VerifiedDownload, preview.BuildNumber, InstalledBuild = BuildNumber,
                DesktopProcess = desktop.Id, DesktopStarted = desktop.StartTime.ToUniversalTime().Ticks
            }) + Resource("SoftKey.UpdatePackage.ps1") + Resource("SoftKey.ApplyUpdate.ps1");
            var start = StartInfo(command);
            start.UseShellExecute = true;
            start.Verb = "runas";
            using var worker = Process.Start(start) ?? throw new IOException("Windows kunde inte starta uppdateringen.");
            string restart = Parameters(new { Process = worker.Id, Installed = installed, Root = root, Desktop = desktop.Id }) + """

                $clock = [Diagnostics.Stopwatch]::StartNew()
                while ((Get-Process -Id $updateParameters.Process -ErrorAction SilentlyContinue) -and $clock.Elapsed.TotalSeconds -lt 180) {
                    Start-Sleep -Milliseconds 250
                }
                if (-not (Get-Process -Id $updateParameters.Process -ErrorAction SilentlyContinue) -and
                    -not (Get-Process -Id $updateParameters.Desktop -ErrorAction SilentlyContinue)) {
                    Start-Process -FilePath $updateParameters.Installed -WorkingDirectory $updateParameters.Root
                }
                """;
            using var launcher = Process.Start(StartInfo(restart)) ?? throw new IOException("SoftKey kunde inte förbereda omstarten.");
        });
    }

    static string Resource(string name)
    {
        using var source = typeof(SoftKeyUpdates).Assembly.GetManifestResourceStream("SoftKey." + name)
            ?? throw new InvalidOperationException("Uppdateringsfunktionen saknas i den här versionen.");
        using var reader = new StreamReader(source, Utf8);
        return "\n" + reader.ReadToEnd() + "\n";
    }

    static string Parameters(object data)
    {
        string json = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(data));
        return "$ErrorActionPreference='Stop'; $updateParameters=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"
            + json + "')) | ConvertFrom-Json;\n";
    }

    static ProcessStartInfo StartInfo(string script)
    {
        // Compress the embedded scripts to stay within Windows' command-line length limit.
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(Utf8.GetBytes(script));
        string inner = "$b=[Convert]::FromBase64String('" + Convert.ToBase64String(bytes.ToArray()) + "');"
            + "$s=[IO.Compression.GZipStream]::new([IO.MemoryStream]::new($b),[IO.Compression.CompressionMode]::Decompress);"
            + "$r=[IO.StreamReader]::new($s);try{& ([scriptblock]::Create($r.ReadToEnd()))}finally{$r.Dispose()}";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(inner));
        return new ProcessStartInfo(PowerShell, "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    }
}
