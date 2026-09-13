using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>What Razer controllers are plugged in right now, by family.</summary>
    public sealed record RazerDeviceScan(bool HasV3, bool HasV2, IReadOnlyList<string> Pids)
    {
        public bool Any => HasV3 || HasV2;
        /// <summary>Only one family present: a suggestion to switch mode makes sense.</summary>
        public ControllerModel? SingleFamily => HasV3 && !HasV2 ? ControllerModel.WolverineV3Pro8K
                                              : HasV2 && !HasV3 ? ControllerModel.WolverineV2
                                              : null;
    }

    /// <summary>
    /// Identifies connected Razer Wolverine pads from their USB product ids so
    /// the app can suggest the matching controller mode. Unknown Razer devices
    /// (mice, keyboards, other pads) are ignored rather than guessed.
    /// </summary>
    public static class RazerDevices
    {
        // Wolverine V3 Pro 8K: 0A57 wired, 0A59 through the 8K dongle (seen on the dev machine).
        private static readonly HashSet<string> V3Pids = new(StringComparer.OrdinalIgnoreCase) { "0A57", "0A59" };
        // Wolverine V2 (0A29), V2 Chroma (0A2E), V2 Pro wired (0A3F) / wireless (0A40).
        private static readonly HashSet<string> V2Pids = new(StringComparer.OrdinalIgnoreCase) { "0A29", "0A2E", "0A3F", "0A40" };

        private static readonly Regex PidRx = new(@"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static RazerDeviceScan Scan()
        {
            var pids = HidHideService.RazerHidInstancePaths()
                .Select(p => PidRx.Match(p))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value.ToUpperInvariant())
                .Distinct()
                .ToList();
            return new RazerDeviceScan(pids.Any(V3Pids.Contains), pids.Any(V2Pids.Contains), pids);
        }
    }
}
