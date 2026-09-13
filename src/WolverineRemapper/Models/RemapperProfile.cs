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
        /// <summary>UI language code (en, fr, de, es, nl). Null = detect from the OS.</summary>
        public string? Language { get; set; }
        /// <summary>Start the engine as soon as the app launches (same as --autostart).</summary>
        public bool AutoStartEngine { get; set; } = false;
        /// <summary>Close button hides to the tray instead of quitting.</summary>
        public bool CloseToTray { get; set; } = true;
        /// <summary>When launched by the Windows Run key, stay hidden in the tray.</summary>
        public bool StartMinimized { get; set; } = true;
        /// <summary>Query GitHub releases once at startup.</summary>
        public bool CheckUpdatesAtStartup { get; set; } = true;
    }
}
