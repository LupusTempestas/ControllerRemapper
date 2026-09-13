using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace WolverineRemapper.Services
{
    public sealed record UpdateInfo(Version Latest, string Url);

    /// <summary>Asks the GitHub releases API for the newest tag. No auto-download: the user gets a link.</summary>
    public static class UpdateService
    {
        public const string RepoUrl = "https://github.com/LupusTempestas/ControllerRemapper";
        private const string LatestApi = "https://api.github.com/repos/LupusTempestas/ControllerRemapper/releases/latest";

        /// <summary>Running version, trimmed to major.minor.patch.</summary>
        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static async Task<UpdateInfo?> GetLatestAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WolverineRemapper/" + Current);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? RepoUrl : RepoUrl;

            var version = ParseVersion(tag);
            return version == null ? null : new UpdateInfo(version, url);
        }

        /// <summary>"v1.2.3" / "1.2.3" / "1.2" → Version(1,2,3).</summary>
        public static Version? ParseVersion(string tag)
        {
            string s = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(s, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}
