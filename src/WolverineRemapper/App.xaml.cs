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
        // original user so the per-user Run key lands in the right hive).
        bool enable = e.Args.Any(a => a.Equals("--enable-startup", StringComparison.OrdinalIgnoreCase));
        bool disable = e.Args.Any(a => a.Equals("--disable-startup", StringComparison.OrdinalIgnoreCase));
        if (enable || disable)
        {
            StartupService.SetEnabled(enable, startMinimized: true);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }
}
