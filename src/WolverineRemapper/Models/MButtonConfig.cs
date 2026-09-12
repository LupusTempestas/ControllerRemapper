using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace WolverineRemapper.Models
{
    /// <summary>
    /// Immutable snapshot of a chord taken at key-press time. Releasing always
    /// releases exactly what was pressed, even if the user edits the chord
    /// while the key is held.
    /// </summary>
    public sealed record ChordSnapshot(IReadOnlyList<Xbox360Button> Buttons, bool LeftTrigger, bool RightTrigger)
    {
        public static readonly ChordSnapshot Empty = new(new List<Xbox360Button>(), false, false);
    }

    public class MButtonConfig : INotifyPropertyChanged
    {
        private string _mButtonName = "M1";
        private uint _vkCode = 0x7C; // F13 default
        private string _keyDisplayName = "F13";
        private bool _suppressKey = true;

        // Controller button toggles
        private bool _a, _b, _x, _y;
        private bool _lb, _rb, _lt, _rt;
        private bool _dPadUp, _dPadDown, _dPadLeft, _dPadRight;
        private bool _lsClick, _rsClick;
        private bool _view, _menu;

        // Live/UI-only state — never serialized
        private bool _isPressed;
        private bool _isSelected;

        private ActionMode _mode = ActionMode.Chord;
        private TriggerType _trigger = TriggerType.Hold;
        private RepeatMode _repeat = RepeatMode.Once;
        private int _repeatCount = 3;
        private int _repeatGapMs = 40;
        private int _doubleTapWindowMs = 300;
        private int _holdTapDelayMs = 60;
        private ObservableCollection<MacroStep> _steps = new();

        public MButtonConfig()
        {
            HoldPicks = CreatePicks();
            TapPicks = CreatePicks();
            AttachSteps(_steps);
        }

        public string MButtonName
        {
            get => _mButtonName;
            set { _mButtonName = value; OnPropertyChanged(); }
        }

        public uint VkCode
        {
            get => _vkCode;
            set { _vkCode = value; OnPropertyChanged(); }
        }

        public string KeyDisplayName
        {
            get => _keyDisplayName;
            set { _keyDisplayName = value; OnPropertyChanged(); }
        }

        public bool SuppressKey
        {
            get => _suppressKey;
            set { _suppressKey = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsPressed
        {
            get => _isPressed;
            set { if (_isPressed != value) { _isPressed = value; OnPropertyChanged(); } }
        }

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        #region Trigger type (Hold / Toggle / Double-tap)

        public TriggerType Trigger
        {
            get => _trigger;
            set
            {
                _trigger = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHoldTrigger));
                OnPropertyChanged(nameof(IsToggleTrigger));
                OnPropertyChanged(nameof(IsDoubleTapTrigger));
                OnPropertyChanged(nameof(TriggerSummary));
            }
        }

        [JsonIgnore] public bool IsHoldTrigger { get => Trigger == TriggerType.Hold; set { if (value) Trigger = TriggerType.Hold; } }
        [JsonIgnore] public bool IsToggleTrigger { get => Trigger == TriggerType.Toggle; set { if (value) Trigger = TriggerType.Toggle; } }
        [JsonIgnore] public bool IsDoubleTapTrigger { get => Trigger == TriggerType.DoubleTap; set { if (value) Trigger = TriggerType.DoubleTap; } }

        /// <summary>Max gap (ms) between the two taps of a Double-tap trigger.</summary>
        public int DoubleTapWindowMs
        {
            get => _doubleTapWindowMs;
            set { _doubleTapWindowMs = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string TriggerSummary => Trigger switch
        {
            TriggerType.Toggle => "Toggle",
            TriggerType.DoubleTap => "Double-tap",
            _ => "Hold"
        };

        #endregion

        #region Action mode (Chord / Hold+Tap / Sequence)

        public ActionMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChordMode));
                OnPropertyChanged(nameof(IsHoldTapMode));
                OnPropertyChanged(nameof(IsSequenceMode));
                OnPropertyChanged(nameof(ChordSummary));
            }
        }

        [JsonIgnore] public bool IsChordMode { get => Mode == ActionMode.Chord; set { if (value) Mode = ActionMode.Chord; } }
        [JsonIgnore] public bool IsHoldTapMode { get => Mode == ActionMode.HoldTap; set { if (value) Mode = ActionMode.HoldTap; } }
        [JsonIgnore] public bool IsSequenceMode { get => Mode == ActionMode.Sequence; set { if (value) Mode = ActionMode.Sequence; } }

        /// <summary>Delay (ms) between the hold-set and the press-set in Hold+Tap mode.</summary>
        public int HoldTapDelayMs
        {
            get => _holdTapDelayMs;
            set { _holdTapDelayMs = value; OnPropertyChanged(); }
        }

        #region Repeat (Once / N times / While held)

        public RepeatMode Repeat
        {
            get => _repeat;
            set
            {
                _repeat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRepeatOnce));
                OnPropertyChanged(nameof(IsRepeatCount));
                OnPropertyChanged(nameof(IsRepeatWhileHeld));
                OnPropertyChanged(nameof(ChordSummary));
            }
        }

        [JsonIgnore] public bool IsRepeatOnce { get => Repeat == RepeatMode.Once; set { if (value) Repeat = RepeatMode.Once; } }
        [JsonIgnore] public bool IsRepeatCount { get => Repeat == RepeatMode.Count; set { if (value) Repeat = RepeatMode.Count; } }
        [JsonIgnore] public bool IsRepeatWhileHeld { get => Repeat == RepeatMode.WhileHeld; set { if (value) Repeat = RepeatMode.WhileHeld; } }

        public int RepeatCount
        {
            get => _repeatCount;
            set { _repeatCount = Math.Max(1, value); OnPropertyChanged(); OnPropertyChanged(nameof(ChordSummary)); }
        }

        /// <summary>Delay (ms) between repeats, and press-hold time for a repeated chord.</summary>
        public int RepeatGapMs
        {
            get => _repeatGapMs;
            set { _repeatGapMs = value; OnPropertyChanged(); }
        }

        /// <summary>Back-compat: old profiles stored a LoopWhileHeld bool; true migrates to WhileHeld.</summary>
        public bool LoopWhileHeld
        {
            get => Repeat == RepeatMode.WhileHeld;
            set { if (value && Repeat == RepeatMode.Once) Repeat = RepeatMode.WhileHeld; }
        }

        [JsonIgnore]
        public string RepeatSuffix => Repeat switch
        {
            RepeatMode.Count => $"  ×{RepeatCount}",
            RepeatMode.WhileHeld => "  · turbo",
            _ => ""
        };

        #endregion

        /// <summary>Macro steps (Sequence mode).</summary>
        public ObservableCollection<MacroStep> Steps
        {
            get => _steps;
            set
            {
                _steps = value ?? new ObservableCollection<MacroStep>();
                AttachSteps(_steps);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChordSummary));
            }
        }

        /// <summary>UI pickers for Hold+Tap mode (serialized via Hold/TapButtons).</summary>
        [JsonIgnore] public ObservableCollection<ButtonPick> HoldPicks { get; }
        [JsonIgnore] public ObservableCollection<ButtonPick> TapPicks { get; }

        public List<VirtualButtonId> HoldButtons
        {
            get => HoldPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            set { foreach (var p in HoldPicks) p.IsSelected = value?.Contains(p.Id) ?? false; }
        }

        public List<VirtualButtonId> TapButtons
        {
            get => TapPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            set { foreach (var p in TapPicks) p.IsSelected = value?.Contains(p.Id) ?? false; }
        }

        private ObservableCollection<ButtonPick> CreatePicks()
        {
            var col = new ObservableCollection<ButtonPick>();
            foreach (VirtualButtonId id in Enum.GetValues<VirtualButtonId>())
            {
                var pick = new ButtonPick { Id = id };
                pick.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ChordSummary));
                col.Add(pick);
            }
            return col;
        }

        private void AttachSteps(ObservableCollection<MacroStep> steps)
        {
            steps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ChordSummary));
        }

        #endregion

        #region Xbox chord toggles

        public bool A { get => _a; set { _a = value; OnChordChanged(); } }
        public bool B { get => _b; set { _b = value; OnChordChanged(); } }
        public bool X { get => _x; set { _x = value; OnChordChanged(); } }
        public bool Y { get => _y; set { _y = value; OnChordChanged(); } }
        public bool LB { get => _lb; set { _lb = value; OnChordChanged(); } }
        public bool RB { get => _rb; set { _rb = value; OnChordChanged(); } }
        public bool LT { get => _lt; set { _lt = value; OnChordChanged(); } }
        public bool RT { get => _rt; set { _rt = value; OnChordChanged(); } }
        public bool DPadUp { get => _dPadUp; set { _dPadUp = value; OnChordChanged(); } }
        public bool DPadDown { get => _dPadDown; set { _dPadDown = value; OnChordChanged(); } }
        public bool DPadLeft { get => _dPadLeft; set { _dPadLeft = value; OnChordChanged(); } }
        public bool DPadRight { get => _dPadRight; set { _dPadRight = value; OnChordChanged(); } }
        public bool LSClick { get => _lsClick; set { _lsClick = value; OnChordChanged(); } }
        public bool RSClick { get => _rsClick; set { _rsClick = value; OnChordChanged(); } }
        public bool View { get => _view; set { _view = value; OnChordChanged(); } }
        public bool Menu { get => _menu; set { _menu = value; OnChordChanged(); } }

        #endregion

        [JsonIgnore]
        public string ChordSummary
        {
            get
            {
                if (Mode == ActionMode.HoldTap)
                {
                    string holds = string.Join(" + ", HoldButtons);
                    string taps = string.Join(" + ", TapButtons);
                    return $"Hold {(holds.Length > 0 ? holds : "—")} → press {(taps.Length > 0 ? taps : "—")}{RepeatSuffix}";
                }
                if (Mode == ActionMode.Sequence)
                {
                    return Steps.Count == 0
                        ? "(empty macro)"
                        : $"{Steps.Count}-step macro{RepeatSuffix}";
                }

                var list = new List<string>();
                if (A) list.Add("A");
                if (B) list.Add("B");
                if (X) list.Add("X");
                if (Y) list.Add("Y");
                if (LB) list.Add("LB");
                if (RB) list.Add("RB");
                if (LT) list.Add("LT");
                if (RT) list.Add("RT");
                if (DPadUp) list.Add("D-Up");
                if (DPadDown) list.Add("D-Down");
                if (DPadLeft) list.Add("D-Left");
                if (DPadRight) list.Add("D-Right");
                if (LSClick) list.Add("LS");
                if (RSClick) list.Add("RS");
                if (View) list.Add("View");
                if (Menu) list.Add("Menu");

                return list.Count > 0 ? string.Join(" + ", list) : "(None)";
            }
        }

        /// <summary>The chord's selected outputs as VirtualButtonIds (for repeated-chord pulsing).</summary>
        public List<VirtualButtonId> GetChordButtonIds()
        {
            var ids = new List<VirtualButtonId>();
            if (A) ids.Add(VirtualButtonId.A);
            if (B) ids.Add(VirtualButtonId.B);
            if (X) ids.Add(VirtualButtonId.X);
            if (Y) ids.Add(VirtualButtonId.Y);
            if (LB) ids.Add(VirtualButtonId.LB);
            if (RB) ids.Add(VirtualButtonId.RB);
            if (LT) ids.Add(VirtualButtonId.LT);
            if (RT) ids.Add(VirtualButtonId.RT);
            if (DPadUp) ids.Add(VirtualButtonId.DPadUp);
            if (DPadDown) ids.Add(VirtualButtonId.DPadDown);
            if (DPadLeft) ids.Add(VirtualButtonId.DPadLeft);
            if (DPadRight) ids.Add(VirtualButtonId.DPadRight);
            if (LSClick) ids.Add(VirtualButtonId.LS);
            if (RSClick) ids.Add(VirtualButtonId.RS);
            if (View) ids.Add(VirtualButtonId.View);
            if (Menu) ids.Add(VirtualButtonId.Menu);
            return ids;
        }

        /// <summary>Capture the current chord as an immutable snapshot (taken at press time).</summary>
        public ChordSnapshot GetChordSnapshot()
        {
            var buttons = new List<Xbox360Button>();
            if (A) buttons.Add(Xbox360Button.A);
            if (B) buttons.Add(Xbox360Button.B);
            if (X) buttons.Add(Xbox360Button.X);
            if (Y) buttons.Add(Xbox360Button.Y);
            if (LB) buttons.Add(Xbox360Button.LeftShoulder);
            if (RB) buttons.Add(Xbox360Button.RightShoulder);
            if (LSClick) buttons.Add(Xbox360Button.LeftThumb);
            if (RSClick) buttons.Add(Xbox360Button.RightThumb);
            if (View) buttons.Add(Xbox360Button.Back);
            if (Menu) buttons.Add(Xbox360Button.Start);
            if (DPadUp) buttons.Add(Xbox360Button.Up);
            if (DPadDown) buttons.Add(Xbox360Button.Down);
            if (DPadLeft) buttons.Add(Xbox360Button.Left);
            if (DPadRight) buttons.Add(Xbox360Button.Right);
            return new ChordSnapshot(buttons, LT, RT);
        }

        private void OnChordChanged([CallerMemberName] string? name = null)
        {
            OnPropertyChanged(name);
            OnPropertyChanged(nameof(ChordSummary));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
