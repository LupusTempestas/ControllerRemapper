using System;
using Microsoft.Win32;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// "Start with Windows" via the per-user Run key. The installer writes the
    /// same value, so the settings tab, the tray toggle and the installer all
    /// agree on one switch.
    /// </summary>
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WolverineRemapper";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled, bool startMinimized)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;
                if (enabled)
                {
                    string exe = Environment.ProcessPath ?? "";
                    string args = startMinimized ? " --autostart --minimized" : " --autostart";
                    key.SetValue(ValueName, $"\"{exe}\"{args}");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // Registry refusal (policy) must not crash the app; the checkbox re-reads IsEnabled().
            }
        }
    }

    /// <summary>Presence checks for the two kernel drivers the app depends on.</summary>
    public static class DriverCheck
    {
        public static bool ViGEmBusInstalled => ServiceExists("ViGEmBus");
        public static bool HidHideInstalled => ServiceExists("HidHide");

        public const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";
        public const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/latest";

        private static bool ServiceExists(string name)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, writable: false);
                return key != null;
            }
            catch { return false; }
        }
    }
}
