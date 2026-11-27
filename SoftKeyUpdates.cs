using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Native C# update client: checks public GitHub Releases, downloads and cryptographically
// verifies the signed archive, and launches signed SoftKey.Startup.exe without PowerShell.
static class SoftKeyUpdates
{
    internal sealed record Asset(long Id, string Name, string State, long Size, string Digest);
    internal sealed record Preview(long BuildNumber, string Commit, string Published, string Notes, Asset Asset);
    internal sealed record Progress(double Percent, string Status, string Detail);

    const string Repository = "nitti87/Softkey";
    const string ReleasesApi = "https://api.github.com/repos/" + Repository + "/releases";
    const long MaxArchiveBytes = 64L * 1024 * 1024;

    static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftKey", "Updates");
    static readonly Regex TagPattern = new("^softkey-preview-([1-9][0-9]*)$", RegexOptions.Compiled);
    static readonly Regex CommitPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    static readonly Regex DigestPattern = new("^sha256:[0-9a-f]{64}$", RegexOptions.Compiled);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    static readonly HttpClient Client = CreateHttpClient();

    static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.Add("User-Agent", "SoftKey-Updates");
        return client;
    }

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

    sealed class GitHubRelease
    {
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public string Tag_Name { get; set; }
        public string Target_Commitish { get; set; }
        public string Body { get; set; }
        public string Published_At { get; set; }
        public GitHubAsset[] Assets { get; set; }
    }

    sealed class GitHubAsset
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string State { get; set; }
        public long Size { get; set; }
        public string Digest { get; set; }
    }

    public static async Task<Preview> CheckAsync(CancellationToken cancel)
    {
        long installedBuild = InstalledBuild();
        var releases = new List<GitHubRelease>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));

        int page = 1;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ReleasesApi}?per_page=100&page={page}");
            request.Headers.Add("Accept", "application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IOException("Det gick inte att söka efter uppdateringar. Försök igen.");
            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            var batch = JsonSerializer.Deserialize<GitHubRelease[]>(json, JsonOptions) ?? Array.Empty<GitHubRelease>();
            releases.AddRange(batch);
            if (batch.Length < 100 || page >= 20) break;
            page++;
        }

        return SelectLatestPreview(releases, installedBuild);
    }

    internal static Preview ParsePreviewJson(string releasesJson, long installedBuild)
    {
        var releases = JsonSerializer.Deserialize<GitHubRelease[]>(releasesJson, JsonOptions) ?? Array.Empty<GitHubRelease>();
        return SelectLatestPreview(releases, installedBuild);
    }

    static Preview SelectLatestPreview(IEnumerable<GitHubRelease> releases, long installedBuild)
    {
        long newestBuild = installedBuild;
        GitHubRelease candidate = null;
        Asset candidateAsset = null;

        foreach (var release in releases)
        {
            if (release == null || release.Draft || !release.Prerelease
                || string.IsNullOrEmpty(release.Target_Commitish) || !CommitPattern.IsMatch(release.Target_Commitish)
                || string.IsNullOrEmpty(release.Tag_Name))
                continue;

            var match = TagPattern.Match(release.Tag_Name);
            if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long build)
                || build <= newestBuild)
                continue;

            var asset = ExtractValidAsset(release.Assets, build);
            if (asset == null) continue;

            newestBuild = build;
            candidate = release;
            candidateAsset = asset;
        }

        if (candidate == null || candidateAsset == null) return null;
        string notes = candidate.Body ?? "";
        if (notes.Length > 12000) notes = notes.Substring(0, 12000);
        return new Preview(newestBuild, candidate.Target_Commitish, candidate.Published_At ?? "", notes, candidateAsset);
    }

    static Asset ExtractValidAsset(GitHubAsset[] assets, long build)
    {
        if (assets == null) return null;
        string expectedName = PackageName(build);
        var matching = assets.Where(a => a != null && string.Equals(a.Name, expectedName, StringComparison.Ordinal)).ToArray();
        if (matching.Length == 0) return null;
        if (matching.Length != 1) throw new IOException("Uppdateringens paketmetadata är ogiltig.");
        var a = matching[0];
        if (!string.Equals(a.State, "uploaded", StringComparison.Ordinal) || a.Id < 1 || a.Size < 1 || a.Size > MaxArchiveBytes
            || string.IsNullOrEmpty(a.Digest) || !DigestPattern.IsMatch(a.Digest))
            throw new IOException("Uppdateringens paketmetadata är ogiltig.");
        return new Asset(a.Id, a.Name, a.State, a.Size, a.Digest);
    }

    internal static bool IsAllowedDownloadRedirect(Uri location) =>
        location != null && location.IsAbsoluteUri
        && string.Equals(location.Scheme, "https", StringComparison.OrdinalIgnoreCase)
        && location.IsDefaultPort && string.IsNullOrEmpty(location.UserInfo)
        && (string.Equals(location.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(location.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static async Task<string> DownloadAsync(Preview preview, IProgress<Progress> progress, CancellationToken cancel)
    {
        long installedBuild = InstalledBuild();
        if (!CanInstall || preview == null || preview.Asset == null || preview.BuildNumber <= installedBuild
            || string.IsNullOrEmpty(preview.Commit) || !CommitPattern.IsMatch(preview.Commit)
            || !string.Equals(preview.Asset.Name, PackageName(preview.BuildNumber), StringComparison.Ordinal)
            || preview.Asset.Size < 1 || preview.Asset.Size > MaxArchiveBytes
            || string.IsNullOrEmpty(preview.Asset.Digest) || !DigestPattern.IsMatch(preview.Asset.Digest))
            throw new InvalidOperationException("Använd den installerade versionen av SoftKey för att uppdatera.");

        string file = PackageName(preview.BuildNumber);
        Directory.CreateDirectory(Cache);
        foreach (string dir in new[] { Path.GetDirectoryName(Cache), Cache })
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Uppdateringsmappen kan inte vara en länk.");
        }

        string cachedPath = Path.Combine(Cache, file);
        if (File.Exists(cachedPath))
        {
            if ((File.GetAttributes(cachedPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Uppdateringsmappen innehåller en ogiltig länk.");
            string cachedDigest = "sha256:" + ComputeFileSha256Lower(cachedPath);
            if (new FileInfo(cachedPath).Length != preview.Asset.Size || !string.Equals(cachedDigest, preview.Asset.Digest, StringComparison.Ordinal))
                File.Delete(cachedPath);
        }

        using var trustedCert = SoftKeyUpdateArchive.LoadTrustedCertificate();
        if (File.Exists(cachedPath))
        {
            progress?.Report(new Progress(56, "Verifierar paketets signatur", file));
            var cachedManifest = await Task.Run(() => SoftKeyUpdateArchive.ReadVerified(cachedPath, trustedCert, installedBuild), cancel);
            if (cachedManifest.BuildNumber != preview.BuildNumber || !string.Equals(cachedManifest.Commit, preview.Commit, StringComparison.Ordinal))
                throw new IOException("Det hämtade paketets identitet kunde inte bekräftas.");
            return cachedPath;
        }

        progress?.Report(new Progress(0, "Hämtar uppdateringen", file));
        string tempPath = cachedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string url = FormattableString.Invariant($"https://github.com/{Repository}/releases/download/softkey-preview-{preview.BuildNumber}/{file}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/octet-stream");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var initialResponse = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            HttpResponseMessage downloadResponse = initialResponse;
            HttpResponseMessage redirectedResponse = null;
            try
            {
                if (initialResponse.StatusCode == HttpStatusCode.Found)
                {
                    var location = initialResponse.Headers.Location;
                    if (!IsAllowedDownloadRedirect(location))
                        throw new IOException("GitHub returnerade en oväntad nedladdningsadress.");
                    redirectedResponse = await Client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    downloadResponse = redirectedResponse;
                }
                if (downloadResponse.StatusCode != HttpStatusCode.OK)
                    throw new IOException("Uppdateringen kunde inte hämtas. Försök igen.");

                string actualDigest;
                long totalWritten = 0;
                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                using (var stream = await downloadResponse.Content.ReadAsStreamAsync(timeout.Token))
                using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536];
                    int lastPercent = -1;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
                    {
                        if (totalWritten + read > preview.Asset.Size)
                            throw new IOException("Hämtningen överskred den angivna filstorleken.");
                        await output.WriteAsync(buffer, 0, read, timeout.Token);
                        hasher.AppendData(buffer, 0, read);
                        totalWritten += read;
                        int percent = (int)Math.Floor(55.0 * totalWritten / preview.Asset.Size);
                        if (percent > lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(new Progress(55.0 * totalWritten / preview.Asset.Size, "Hämtar uppdateringen",
                                $"{file}  ·  {FormatSize(totalWritten)} av {FormatSize(preview.Asset.Size)}"));
                        }
                    }
                    actualDigest = "sha256:" + Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                }

                progress?.Report(new Progress(56, "Verifierar paketets signatur", file));
                if (totalWritten != preview.Asset.Size || !string.Equals(actualDigest, preview.Asset.Digest, StringComparison.Ordinal))
                    throw new IOException("Det hämtade paketet matchar inte GitHubs kontrollsumma.");

                var manifest = await Task.Run(() => SoftKeyUpdateArchive.ReadVerified(tempPath, trustedCert, installedBuild), cancel);
                if (manifest.BuildNumber != preview.BuildNumber || !string.Equals(manifest.Commit, preview.Commit, StringComparison.Ordinal))
                    throw new IOException("Det verifierade paketet matchar inte den valda releasen.");

                if (File.Exists(cachedPath))
                {
                    if (!string.Equals("sha256:" + ComputeFileSha256Lower(cachedPath), actualDigest, StringComparison.Ordinal))
                        throw new IOException("Målmappen innehåller redan ett annat paket.");
                }
                else
                {
                    File.Move(tempPath, cachedPath);
                }
                return cachedPath;
            }
            finally
            {
                redirectedResponse?.Dispose();
            }
        }
        finally
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
        }
    }

    public static async Task StartInstallAsync(Preview preview, string packagePath, double left, double top, double width)
    {
        long installedBuild = InstalledBuild();
        if (!CanInstall || preview == null || preview.BuildNumber <= installedBuild
            || !string.Equals(packagePath, Path.Combine(Cache, PackageName(preview.BuildNumber)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Använd den installerade versionen av SoftKey för att uppdatera.");

        await Task.Run(() =>
        {
            string root = AppContext.BaseDirectory;
            string installedExe = Path.Combine(root, "SoftKey.exe");
            string startupExe = Path.Combine(root, "SoftKey.Startup.exe");
            StartupTrust.ProtectedDirectory(root);
            StartupTrust.Verify(installedExe);
            StartupTrust.Verify(Path.Combine(root, "SoftKey.dll"));
            StartupTrust.Verify(startupExe);
            StartupTrust.Verify(Path.Combine(root, "SoftKey.Startup.dll"));

            using var desktop = Process.GetCurrentProcess();
            string arguments = FormattableString.Invariant(
                $"{SoftKeyUpdateInstaller.ApplyArgument} {preview.BuildNumber} {preview.Commit} {installedBuild} {desktop.Id} {desktop.StartTime.ToUniversalTime().Ticks} {left:R} {top:R} {width:R}");

            var start = new ProcessStartInfo(startupExe, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = root,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var worker = Process.Start(start) ?? throw new IOException("Windows kunde inte starta uppdateringen.");
            if (!worker.WaitForExit(15000) || worker.ExitCode != 0)
                throw new IOException("Uppdateringsprogrammet kunde inte förbereda installationen.");
        });
    }

    static string ComputeFileSha256Lower(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string PackageName(long build) => FormattableString.Invariant($"SoftKey-update-{build}.zip");

    internal static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024):0.0} MB"
        : $"{bytes / 1024.0:0.#} kB";
}
