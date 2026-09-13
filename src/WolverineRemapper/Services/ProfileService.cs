using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Named per-game profiles stored in %AppData%\WolverineRemapper\Profiles
    /// (the old code wrote a single hard-coded profile.json into bin\ — lost on
    /// every rebuild). Also persists app settings (last used profile).
    /// </summary>
    public class ProfileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _rootDir;
        private readonly string _profilesDir;
        private readonly string _settingsPath;

        public ProfileService()
        {
            _rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WolverineRemapper");
            _profilesDir = Path.Combine(_rootDir, "Profiles");
            _settingsPath = Path.Combine(_rootDir, "settings.json");
            Directory.CreateDirectory(_profilesDir);
        }

        public string ProfilesDirectory => _profilesDir;

        public List<string> ListProfiles()
        {
            try
            {
                return Directory.EnumerateFiles(_profilesDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public void Save(RemapperProfile profile)
        {
            string name = Sanitize(profile.ProfileName);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Profile name cannot be empty.");

            profile.ProfileName = name;
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(profile, JsonOptions));
        }

        public RemapperProfile? Load(string name)
        {
            string path = PathFor(Sanitize(name));
            if (!File.Exists(path)) return null;

            var profile = JsonSerializer.Deserialize<RemapperProfile>(File.ReadAllText(path));
            return profile is { MButtons.Count: > 0 } ? profile : null;
        }

        public bool Delete(string name)
        {
            string path = PathFor(Sanitize(name));
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>Deep copy via a JSON round-trip (same shape as a Load).</summary>
        public RemapperProfile Clone(RemapperProfile profile)
        {
            string json = JsonSerializer.Serialize(profile, JsonOptions);
            return JsonSerializer.Deserialize<RemapperProfile>(json) ?? new RemapperProfile();
        }

        /// <summary>
        /// Explorer-style copy name: "Name (2)", "Name (3)", … — the first
        /// one not already on disk. A source that already carries a " (n)"
        /// suffix continues the same series instead of nesting.
        /// </summary>
        public string NextAvailableName(string sourceName)
        {
            string baseName = Sanitize(sourceName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Profile";

            var m = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*\S)\s\((\d+)\)$");
            if (m.Success) baseName = m.Groups[1].Value;

            var existing = new HashSet<string>(ListProfiles(), StringComparer.OrdinalIgnoreCase);
            for (int n = 2; ; n++)
            {
                string candidate = $"{baseName} ({n})";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
            }
            catch { /* corrupted settings → defaults */ }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch { /* settings persistence is best-effort */ }
        }

        private string PathFor(string name) => Path.Combine(_profilesDir, name + ".json");

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray());
        }
    }
}
