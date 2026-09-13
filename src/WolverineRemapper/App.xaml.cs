using System;
using System.Linq;
using System.Windows;
using WolverineRemapper.Services;

namespace WolverineRemapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless switches used by the installer/uninstaller (run as the
        // original user so per-user state lands in the right place).
        bool enable = e.Args.Any(a => a.Equals("--enable-startup", StringComparison.OrdinalIgnoreCase));
        bool disable = e.Args.Any(a => a.Equals("--disable-startup", StringComparison.OrdinalIgnoreCase));
        string? lang = e.Args
            .Where(a => a.StartsWith("--set-language=", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Substring("--set-language=".Length))
            .FirstOrDefault();

        if (enable || disable || lang != null)
        {
            if (enable || disable) StartupService.SetEnabled(enable, startMinimized: true);
            if (lang != null) ApplyInstallerLanguage(lang);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// The installer passes Inno's {language} name (english, french, …) or a
    /// two-letter code; store it so the first launch opens in that language.
    /// </summary>
    private static void ApplyInstallerLanguage(string value)
    {
        string code = value.Trim().ToLowerInvariant() switch
        {
            "english" => "en",
            "french" => "fr",
            "german" => "de",
            "spanish" => "es",
            "dutch" => "nl",
            var other => other
        };
        if (!L10n.Available.Any(l => l.Code == code)) return;

        var profiles = new ProfileService();
        var settings = profiles.LoadSettings();
        settings.Language = code;
        profiles.SaveSettings(settings);
    }
}
