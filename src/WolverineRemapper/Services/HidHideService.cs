using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WolverineRemapper.Services
{
    /// <summary>Snapshot of what HidHide currently does for us.</summary>
    public sealed record HidHideStatus(
        bool Installed,
        bool CliFound,
        bool CloakOn,
        bool AppAllowed,
        IReadOnlyList<string> HiddenPadPaths,
        IReadOnlyList<string> PadPaths,
        string? Error)
    {
        /// <summary>Everything a V2 setup needs: cloak on, this app whitelisted, every Razer HID path hidden.</summary>
        public bool Configured => Installed && CloakOn && AppAllowed && PadPaths.Count > 0 && PadPaths.All(p => HiddenPadPaths.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Drives HidHide through its command-line tool so a V2 user never has to
    /// open the HidHide UI: whitelist this exe, hide the Razer pad's HID
    /// devices, switch the cloak on. Changes run elevated (one UAC prompt);
    /// reads run as the current user.
    /// </summary>
    public static class HidHideService
    {
        private static readonly string[] CliCandidates =
        {
            @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
            @"C:\Program Files\Nefarius Software Solutions\HidHide\HidHideCLI.exe",
            @"C:\Program Files (x86)\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
        };

        public const string RazerVendorId = "VID_1532";

        public static string? FindCli() => CliCandidates.FirstOrDefault(File.Exists);

        public static HidHideStatus Query()
        {
            bool installed = DriverCheck.HidHideInstalled;
            string? cli = FindCli();
            var pads = RazerHidInstancePaths();
            if (!installed || cli == null)
                return new HidHideStatus(installed, cli != null, false, false, Array.Empty<string>(), pads, null);

            try
            {
                string cloak = Run(cli, "--cloak-state");
                string apps = Run(cli, "--app-list");
                string devs = Run(cli, "--dev-list");
                bool cloakOn = cloak.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0 && cloak.IndexOf("off", StringComparison.OrdinalIgnoreCase) < 0;
                string exe = Environment.ProcessPath ?? "";
                bool appAllowed = apps.Split('\n').Any(l => l.Trim().Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase));
                var hidden = devs.Split('\n').Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0).ToList();
                var hiddenPads = hidden.Where(h => pads.Any(p => p.Equals(h, StringComparison.OrdinalIgnoreCase))).ToList();
                return new HidHideStatus(true, true, cloakOn, appAllowed, hiddenPads, pads, null);
            }
            catch (Exception ex)
            {
                return new HidHideStatus(true, true, false, false, Array.Empty<string>(), pads, ex.Message);
            }
        }

        /// <summary>Whitelist this exe, hide every Razer HID device, cloak on. One elevated call.</summary>
        public static Task<string?> HidePadAsync() => RunElevatedAsync(cli =>
        {
            var sb = new StringBuilder();
            sb.Append("--app-reg \"").Append(Environment.ProcessPath).Append('"');
            foreach (var p in RazerHidInstancePaths()) sb.Append(" --dev-hide \"").Append(p).Append('"');
            sb.Append(" --cloak-on");
            return sb.ToString();
        });

        /// <summary>Unhide the Razer HID devices (cloak state and whitelist are left alone).</summary>
        public static Task<string?> UnhidePadAsync() => RunElevatedAsync(cli =>
        {
            var sb = new StringBuilder();
            foreach (var p in RazerHidInstancePaths()) sb.Append(" --dev-unhide \"").Append(p).Append('"');
            return sb.ToString().Trim();
        });

        private static async Task<string?> RunElevatedAsync(Func<string, string> buildArgs)
        {
            string? cli = FindCli();
            if (cli == null) return "HidHideCLI.exe not found";
            string args = buildArgs(cli);
            if (string.IsNullOrWhiteSpace(args)) return "no Razer controller detected";
            try
            {
                var psi = new ProcessStartInfo(cli, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using var p = Process.Start(psi);
                if (p == null) return "could not start HidHideCLI";
                await p.WaitForExitAsync();
                return p.ExitCode == 0 ? null : $"HidHideCLI exit code {p.ExitCode}";
            }
            catch (Exception ex)
            {
                return ex.Message; // includes the UAC "cancelled by user" case
            }
        }

        private static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }

        #region HID enumeration (SetupAPI) — the device instance paths HidHide expects

        private static readonly Guid HidClassGuid = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
        private const int DIGCF_PRESENT = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        /// <summary>Instance paths of every connected HID device from Razer (HID\VID_1532&amp;PID_xxxx…).</summary>
        public static List<string> RazerHidInstancePaths()
        {
            var result = new List<string>();
            var guid = HidClassGuid;
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                var sb = new StringBuilder(512);
                for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    sb.Clear();
                    if (SetupDiGetDeviceInstanceId(set, ref data, sb, sb.Capacity, out _))
                    {
                        string id = sb.ToString();
                        if (id.IndexOf(RazerVendorId, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(id);
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        #endregion
    }
}
