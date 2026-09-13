using System.Collections.Generic;

namespace WolverineRemapper.Models
{
    /// <summary>A named, per-game remapping profile (serialized to %AppData%).</summary>
    public class RemapperProfile
    {
        public string ProfileName { get; set; } = "Default";
        /// <summary>Which controller family this profile targets (drives trigger source per M-button).</summary>
        public ControllerModel ControllerModel { get; set; } = ControllerModel.WolverineV3Pro8K;
        public List<MButtonConfig> MButtons { get; set; } = new();
        public bool PassthroughEnabled { get; set; } = false;
        public double InnerDeadzone { get; set; } = 10.0;
        public double OuterDeadzone { get; set; } = 95.0;
    }

    /// <summary>App-level settings, independent of any profile.</summary>
    public class AppSettings
    {
        public string? LastProfileName { get; set; }
    }
}
