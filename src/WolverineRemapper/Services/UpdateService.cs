using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// What the newest GitHub release offers: its version and page, plus the
    /// installer asset and the SHA256SUMS.txt asset when the release has them.
    /// </summary>
    public sealed record UpdateInfo(
        Version Latest,
        string Url,
        string? InstallerUrl,
        string? InstallerName,
        long InstallerSize,
        string? ChecksumUrl);

    /// <summary>
    /// Update pipeline: ask the GitHub releases API for the newest tag, download
    /// the installer, verify it against the published SHA256SUMS.txt, and start
    /// it silently. The app exits right after so the installer can replace it;
    /// the installer relaunches the app when it is done.
    /// </summary>
    public static class UpdateService
    {
        public const string RepoUrl = "https://github.com/LupusTempestas/ControllerRemapper";
        private const string LatestApi = "https://api.github.com/repos/LupusTempestas/ControllerRemapper/releases/latest";
        private const string ChecksumAssetName = "SHA256SUMS.txt";

        public enum Verify { Ok, Mismatch, NoChecksum }

        /// <summary>Running version, trimmed to major.minor.patch.</summary>
        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        private static HttpClient NewClient(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WolverineRemapper/" + Current);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static async Task<UpdateInfo?> GetLatestAsync()
        {
            using var http = NewClient(TimeSpan.FromSeconds(10));
            string json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? RepoUrl : RepoUrl;

            string? installerUrl = null, installerName = null, checksumUrl = null;
            long installerSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string? dl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    if (dl == null) continue;
                    if (name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksumUrl = dl;
                    else if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = dl;
                        installerName = name;
                        installerSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                    }
                }
            }

            var version = ParseVersion(tag);
            return version == null ? null : new UpdateInfo(version, url, installerUrl, installerName, installerSize, checksumUrl);
        }

        /// <summary>"v1.2.3" / "1.2.3" / "1.2" → Version(1,2,3).</summary>
        public static Version? ParseVersion(string tag)
        {
            string s = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(s, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>Download the installer to %TEMP%; progress is 0..1.</summary>
        public static async Task<string> DownloadInstallerAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (info.InstallerUrl == null || info.InstallerName == null) throw new InvalidOperationException("this release has no installer asset");
            string dir = Path.Combine(Path.GetTempPath(), "WolverineRemapper-Update");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, info.InstallerName);

            using var http = NewClient(TimeSpan.FromMinutes(10));
            using var resp = await http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.InstallerSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
            }
            return target;
        }

        /// <summary>
        /// Compare the file's SHA-256 with the line for it in the release's
        /// SHA256SUMS.txt ("hash  filename" per line, sha256sum format).
        /// </summary>
        public static async Task<Verify> VerifyAsync(UpdateInfo info, string path)
        {
            if (info.ChecksumUrl == null) return Verify.NoChecksum;
            using var http = NewClient(TimeSpan.FromSeconds(30));
            string sums = await http.GetStringAsync(info.ChecksumUrl).ConfigureAwait(false);
            string? expected = null;
            string fileName = Path.GetFileName(path);
            foreach (var raw in sums.Split('\n'))
            {
                string line = raw.Trim();
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                string hash = line[..sp].Trim();
                string name = line[sp..].Trim().TrimStart('*');
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) { expected = hash; break; }
            }
            if (expected == null) return Verify.NoChecksum;

            string actual;
            await using (var fs = File.OpenRead(path))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs).ConfigureAwait(false));
            }
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase) ? Verify.Ok : Verify.Mismatch;
        }

        /// <summary>
        /// Start the Inno Setup installer silently as an in-place upgrade. The
        /// language keeps the app's current one, /UPDATE=1 tells the script not
        /// to overwrite the user's language and controller choices and to
        /// relaunch the app afterwards. The installer asks for elevation itself.
        /// </summary>
        public static void StartInstaller(string path, string appLanguageCode)
        {
            string lang = appLanguageCode switch
            {
                "fr" => "french", "de" => "german", "es" => "spanish", "nl" => "dutch", "pt" => "portuguese", _ => "english"
            };
            var psi = new ProcessStartInfo(path, $"/SILENT /NORESTART /SP- /UPDATE=1 /LANG={lang}") { UseShellExecute = true };
            Process.Start(psi);
        }
    }
}
