using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

static class SoftKeyUpdates
{
    internal sealed record Asset(long Id, string Name, string State, long Size, string Digest);
    internal sealed record Preview(long BuildNumber, string Commit, string Published, string Notes, Asset Asset);
    internal sealed record Progress(double Percent, string Status, string Detail);
    sealed record VerifiedPackage(long BuildNumber, string Commit, string VerifiedDownload);

    static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftKey", "Updates");
    static readonly string PowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    static readonly UTF8Encoding Utf8 = new(false, true);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static long BuildNumber => long.TryParse(typeof(SoftKeyUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "SoftKeyUpdateBuild")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build) ? build : 0;

    internal static long CurrentBuildNumber
    {
        get
        {
            try { return InstalledBuild(); }
            catch (IOException) { return BuildNumber; }
        }
    }

    public static bool CanInstall => string.Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SoftKey-UIAccess-Test"), StringComparison.OrdinalIgnoreCase);

    static long InstalledBuild()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "installed-update-build.txt");
        if (!CanInstall || !File.Exists(path)) return BuildNumber;
        if (new FileInfo(path).Length > 32 || !long.TryParse(File.ReadAllText(path).Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out long installed))
            throw new IOException("Det installerade versionsnumret kunde inte läsas.");
        return Math.Max(BuildNumber, installed);
    }

    public static async Task<Preview> CheckAsync(CancellationToken cancel)
    {
        long installedBuild = InstalledBuild();
        string command = Parameters(new { BuildNumber = installedBuild }) + DistributionScript() + """

            $preview = Find-SoftKeyPreview $updateParameters.BuildNumber (Get-SoftKeyPublicReleaseHeaders)
            if ($preview) { $preview | ConvertTo-Json -Compress -Depth 4 }
            """;
        string json = await RunWorkerAsync(command, "Det gick inte att söka efter uppdateringar. Försök igen.", cancel);
        if (string.IsNullOrWhiteSpace(json)) return null;
        var preview = JsonSerializer.Deserialize<Preview>(json, JsonOptions);
        if (preview?.Asset == null || preview.BuildNumber <= installedBuild || preview.Asset.Name != PackageName(preview.BuildNumber))
            throw new IOException("Uppdateringens identitet kunde inte bekräftas.");
        return preview;
    }

    public static async Task<string> DownloadAsync(Preview preview, IProgress<Progress> progress, CancellationToken cancel)
    {
        long installedBuild = InstalledBuild();
        if (!CanInstall || preview.BuildNumber <= installedBuild)
            throw new InvalidOperationException("Använd den installerade versionen av SoftKey för att uppdatera.");

        string file = PackageName(preview.BuildNumber);
        progress.Report(new Progress(0, "Hämtar uppdateringen", file));
        string command = Parameters(new { BuildNumber = installedBuild, Cache, Preview = preview }) + DistributionScript() + """

            $display = @{Percent=-1}
            $result = Receive-SoftKeyPreview -Preview $updateParameters.Preview -InstalledBuildNumber $updateParameters.BuildNumber `
                -Cache $updateParameters.Cache -Headers (Get-SoftKeyPublicReleaseHeaders) -Certificate (Get-SoftKeyReleaseCertificate) `
                -OnBytes {
                    param($received, $total)
                    $percent = [int][Math]::Floor(55.0 * $received / $total)
                    if ($percent -gt $display.Percent) {
                        $display.Percent = $percent
                        [Console]::WriteLine('bytes:' + $received)
                    }
                } -OnVerify { [Console]::WriteLine('verify') }
            $result | ConvertTo-Json -Compress
            """;
        string json = await RunWorkerAsync(command, "Uppdateringen kunde inte hämtas eller verifieras. Försök igen.", cancel, line =>
        {
            if (line == "verify")
            {
                progress.Report(new Progress(56, "Verifierar paketets signatur", file));
            }
            else if (long.TryParse(line.AsSpan("bytes:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out long bytes)
                && bytes >= 0 && bytes <= preview.Asset.Size)
            {
                progress.Report(new Progress(55.0 * bytes / preview.Asset.Size, "Hämtar uppdateringen",
                    $"{file}  ·  {FormatSize(bytes)} av {FormatSize(preview.Asset.Size)}"));
            }
        });
        var package = JsonSerializer.Deserialize<VerifiedPackage>(json, JsonOptions);
        if (package == null || package.BuildNumber != preview.BuildNumber || package.Commit != preview.Commit ||
            !string.Equals(package.VerifiedDownload, Path.Combine(Cache, file), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Det hämtade paketets identitet kunde inte bekräftas.");
        return package.VerifiedDownload;
    }

    public static async Task StartInstallAsync(Preview preview, string packagePath, string progressContent, double left, double top)
    {
        long installedBuild = InstalledBuild();
        if (!CanInstall || preview.BuildNumber <= installedBuild ||
            !string.Equals(packagePath, Path.Combine(Cache, PackageName(preview.BuildNumber)), StringComparison.OrdinalIgnoreCase))
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
                PackagePath = packagePath, preview.BuildNumber, preview.Commit, InstalledBuild = installedBuild,
                DesktopProcess = desktop.Id, DesktopStarted = desktop.StartTime.ToUniversalTime().Ticks,
                ProgressContent = progressContent, Left = left, Top = top
            }) + Resource("SoftKey.UpdatePackage.ps1") + Resource("SoftKey.InstallProgress.ps1")
               + "\nInvoke-SoftKeyInstallWithProgress {\n" + Resource("SoftKey.ApplyUpdate.ps1") + "\n}\n";
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

    static async Task<string> RunWorkerAsync(string command, string errorMessage, CancellationToken cancel, Action<string> onProgress = null)
    {
        var start = StartInfo(command);
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        start.StandardOutputEncoding = Utf8;
        using var process = Process.Start(start) ?? throw new IOException(errorMessage);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            string json = "";
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is string line)
            {
                if (onProgress != null && (line == "verify" || line.StartsWith("bytes:", StringComparison.Ordinal))) onProgress(line);
                else if (!string.IsNullOrWhiteSpace(line)) json = line;
            }
            await process.WaitForExitAsync(timeout.Token);
            _ = await error;
            if (process.ExitCode != 0) throw new IOException(errorMessage);
            return json;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            throw;
        }
    }

    static string PackageName(long build) => FormattableString.Invariant($"SoftKey-update-{build}.zip");

    internal static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024):0.0} MB"
        : $"{bytes / 1024.0:0.#} kB";

    static string DistributionScript() => Resource("SoftKey.UpdatePackage.ps1") +
        Resource("SoftKey.UpdateDistribution.ps1").Replace(". (Join-Path $PSScriptRoot 'SoftKey.UpdatePackage.ps1')", "", StringComparison.Ordinal);

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
        return "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); "
            + "$updateParameters=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"
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
