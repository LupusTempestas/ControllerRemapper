using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WolverineRemapper.Models
{
    /// <summary>How an M-button translates its trigger key into virtual pad output.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActionMode
    {
        /// <summary>All chord buttons pressed together on key-down, released on key-up.</summary>
        Chord,
        /// <summary>Hold-set pressed first, then after a delay the press-set — all released on key-up.</summary>
        HoldTap,
        /// <summary>User-defined ordered step sequence (full macro).</summary>
        Sequence
    }

    /// <summary>How many times an activated action runs.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RepeatMode
    {
        /// <summary>Run the action a single time.</summary>
        Once,
        /// <summary>Run the action a fixed number of times, then stop.</summary>
        Count,
        /// <summary>Repeat the action until the trigger is released / toggled off (turbo).</summary>
        WhileHeld
    }

    /// <summary>How the physical M-button press decides when the action is active.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TriggerType
    {
        /// <summary>Action is active while the key is physically held (release ends it).</summary>
        Hold,
        /// <summary>Press latches the action on; press again turns it off (hands-free).</summary>
        Toggle,
        /// <summary>Two quick presses toggle the action on/off; a single press does nothing.</summary>
        DoubleTap
    }

    /// <summary>Every output the virtual Xbox 360 pad can produce.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VirtualButtonId
    {
        A, B, X, Y,
        LB, RB, LT, RT,
        DPadUp, DPadDown, DPadLeft, DPadRight,
        LS, RS, View, Menu
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MacroStepType
    {
        /// <summary>Press and keep holding.</summary>
        Press,
        /// <summary>Release a previously pressed button.</summary>
        Release,
        /// <summary>Press, hold for DurationMs, release.</summary>
        Tap,
        /// <summary>Do nothing for DurationMs.</summary>
        Wait,
        /// <summary>Hold a thumbstick at a saved position (until CenterStick / key-up).</summary>
        PushStick,
        /// <summary>Recenter a thumbstick pushed earlier in the sequence.</summary>
        CenterStick
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StickSide
    {
        Left,
        Right
    }

    /// <summary>One editable step of a macro sequence.</summary>
    public class MacroStep : INotifyPropertyChanged
    {
        private MacroStepType _type = MacroStepType.Press;
        private VirtualButtonId _button = VirtualButtonId.A;
        private int _durationMs;
        private StickSide _stick = StickSide.Right;
        private double _stickX;
        private double _stickY;

        public MacroStepType Type
        {
            get => _type;
            set
            {
                _type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsesButton));
                OnPropertyChanged(nameof(UsesDuration));
                OnPropertyChanged(nameof(UsesStick));
                OnPropertyChanged(nameof(UsesStickPosition));
            }
        }

        public VirtualButtonId Button
        {
            get => _button;
            set { _button = value; OnPropertyChanged(); }
        }

        public int DurationMs
        {
            get => _durationMs;
            set { _durationMs = value; OnPropertyChanged(); }
        }

        /// <summary>Which thumbstick a PushStick/CenterStick step drives.</summary>
        public StickSide Stick
        {
            get => _stick;
            set { _stick = value; OnPropertyChanged(); }
        }

        /// <summary>Saved stick deflection, -1..1 (PushStick only).</summary>
        public double StickX
        {
            get => _stickX;
            set { _stickX = System.Math.Clamp(value, -1, 1); OnPropertyChanged(); OnPropertyChanged(nameof(StickSummary)); }
        }

        public double StickY
        {
            get => _stickY;
            set { _stickY = System.Math.Clamp(value, -1, 1); OnPropertyChanged(); OnPropertyChanged(nameof(StickSummary)); }
        }

        [JsonIgnore] public bool UsesButton => Type is MacroStepType.Press or MacroStepType.Release or MacroStepType.Tap;
        [JsonIgnore] public bool UsesDuration => Type is MacroStepType.Wait or MacroStepType.Tap;
        [JsonIgnore] public bool UsesStick => Type is MacroStepType.PushStick or MacroStepType.CenterStick;
        [JsonIgnore] public bool UsesStickPosition => Type == MacroStepType.PushStick;

        /// <summary>Human-readable direction, e.g. "↑ 90° · 98%".</summary>
        [JsonIgnore]
        public string StickSummary
        {
            get
            {
                double mag = System.Math.Sqrt(_stickX * _stickX + _stickY * _stickY);
                if (mag < 0.01) return "(centered — capture a position)";
                double angle = System.Math.Atan2(_stickY, _stickX) * 180.0 / System.Math.PI;
                if (angle < 0) angle += 360;
                return $"{angle:F0}° · {System.Math.Min(mag, 1) * 100:F0}%";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Immutable step resolved at key-press time for execution.</summary>
    public sealed record ResolvedStep(
        MacroStepType Type,
        VirtualButtonId Button,
        int DurationMs,
        StickSide Stick = StickSide.Right,
        double StickX = 0,
        double StickY = 0);

    /// <summary>UI wrapper: one selectable virtual button in a Hold+Tap picker.</summary>
    public class ButtonPick : INotifyPropertyChanged
    {
        private bool _isSelected;

        public VirtualButtonId Id { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
