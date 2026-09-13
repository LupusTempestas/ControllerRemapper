using System;
using System.Collections.Generic;
using System.Linq;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Turns what the player does on the physical pad into macro steps.
    /// Feed it pad samples (the tester already polls at ~60 Hz); on Stop it
    /// emits an ordered step list using the editor's existing vocabulary:
    ///  • a button held shorter than <see cref="TapThresholdMs"/> with nothing
    ///    else happening in between becomes one Tap(duration);
    ///  • anything longer, or overlapped by other inputs, becomes Press … Release;
    ///  • silence between events becomes Wait;
    ///  • a stick pushed past <see cref="StickPushThreshold"/> becomes PushStick
    ///    at its peak deflection, and coming back to centre becomes CenterStick;
    ///  • inputs still down when recording stops stay down (macro hold semantics).
    /// Timings are quantized to <see cref="QuantumMs"/>.
    /// </summary>
    /// <summary>User-tunable recorder behaviour (Recorder tab).</summary>
    public sealed record RecorderOptions(int TapThresholdMs, double StickPushThreshold, bool KeepWaits, bool HoldAtEnd)
    {
        public static readonly RecorderOptions Default = new(150, 0.35, true, true);
    }

    public sealed class MacroRecorder
    {
        public const int QuantumMs = 10;
        private const byte TriggerThreshold = 30;

        private RecorderOptions _opt = RecorderOptions.Default;
        private int TapThresholdMs => _opt.TapThresholdMs;
        private double StickPushThreshold => _opt.StickPushThreshold;
        private double StickCenterThreshold => _opt.StickPushThreshold * 0.6;

        private enum Kind { Down, Up, StickPush, StickCenter }
        private sealed record Event(long T, Kind Kind, VirtualButtonId Button, StickSide Stick, double X, double Y);

        private readonly List<Event> _events = new();
        private readonly Dictionary<VirtualButtonId, bool> _down = new();
        private readonly Dictionary<StickSide, (bool Pushed, double PeakX, double PeakY, double PeakMag, int PushIndex)> _sticks = new();
        private long _startMs;

        public bool IsRecording { get; private set; }
        public int EventCount => _events.Count;
        public long ElapsedMs(long nowMs) => IsRecording ? nowMs - _startMs : 0;

        private static readonly (ushort Mask, VirtualButtonId Id)[] ButtonMap =
        {
            (XInputService.XINPUT_GAMEPAD_A, VirtualButtonId.A),
            (XInputService.XINPUT_GAMEPAD_B, VirtualButtonId.B),
            (XInputService.XINPUT_GAMEPAD_X, VirtualButtonId.X),
            (XInputService.XINPUT_GAMEPAD_Y, VirtualButtonId.Y),
            (XInputService.XINPUT_GAMEPAD_LEFT_SHOULDER, VirtualButtonId.LB),
            (XInputService.XINPUT_GAMEPAD_RIGHT_SHOULDER, VirtualButtonId.RB),
            (XInputService.XINPUT_GAMEPAD_DPAD_UP, VirtualButtonId.DPadUp),
            (XInputService.XINPUT_GAMEPAD_DPAD_DOWN, VirtualButtonId.DPadDown),
            (XInputService.XINPUT_GAMEPAD_DPAD_LEFT, VirtualButtonId.DPadLeft),
            (XInputService.XINPUT_GAMEPAD_DPAD_RIGHT, VirtualButtonId.DPadRight),
            (XInputService.XINPUT_GAMEPAD_LEFT_THUMB, VirtualButtonId.LS),
            (XInputService.XINPUT_GAMEPAD_RIGHT_THUMB, VirtualButtonId.RS),
            (XInputService.XINPUT_GAMEPAD_BACK, VirtualButtonId.View),
            (XInputService.XINPUT_GAMEPAD_START, VirtualButtonId.Menu),
        };

        public void Start(long nowMs, RecorderOptions? options = null)
        {
            _opt = options ?? RecorderOptions.Default;
            _events.Clear();
            _down.Clear();
            _sticks.Clear();
            _sticks[StickSide.Left] = (false, 0, 0, 0, -1);
            _sticks[StickSide.Right] = (false, 0, 0, 0, -1);
            _startMs = nowMs;
            IsRecording = true;
        }

        /// <summary>One pad sample. Call from the poll loop while recording.</summary>
        public void Sample(in XINPUT_GAMEPAD pad, long nowMs)
        {
            if (!IsRecording) return;
            long t = nowMs - _startMs;

            foreach (var (mask, id) in ButtonMap)
                Track(id, (pad.wButtons & mask) != 0, t);
            Track(VirtualButtonId.LT, pad.bLeftTrigger > TriggerThreshold, t);
            Track(VirtualButtonId.RT, pad.bRightTrigger > TriggerThreshold, t);

            TrackStick(StickSide.Left, pad.sThumbLX / 32767.0, pad.sThumbLY / 32767.0, t);
            TrackStick(StickSide.Right, pad.sThumbRX / 32767.0, pad.sThumbRY / 32767.0, t);
        }

        private void Track(VirtualButtonId id, bool isDown, long t)
        {
            bool was = _down.GetValueOrDefault(id, false);
            if (isDown == was) return;
            _down[id] = isDown;
            _events.Add(new Event(t, isDown ? Kind.Down : Kind.Up, id, StickSide.Left, 0, 0));
        }

        private void TrackStick(StickSide side, double x, double y, long t)
        {
            var s = _sticks[side];
            double mag = Math.Sqrt(x * x + y * y);
            if (!s.Pushed)
            {
                if (mag >= StickPushThreshold)
                {
                    _events.Add(new Event(t, Kind.StickPush, VirtualButtonId.A, side, x, y));
                    _sticks[side] = (true, x, y, mag, _events.Count - 1);
                }
            }
            else
            {
                if (mag > s.PeakMag)
                {
                    // Keep the strongest deflection of this push in the event itself.
                    _events[s.PushIndex] = _events[s.PushIndex] with { X = x, Y = y };
                    _sticks[side] = (true, x, y, mag, s.PushIndex);
                }
                if (mag <= StickCenterThreshold)
                {
                    _events.Add(new Event(t, Kind.StickCenter, VirtualButtonId.A, side, 0, 0));
                    _sticks[side] = (false, 0, 0, 0, -1);
                }
            }
        }

        /// <summary>Stop and convert. Returns an empty list if nothing was pressed.</summary>
        public List<MacroStep> Stop()
        {
            IsRecording = false;
            var steps = new List<MacroStep>();
            if (_events.Count == 0) return steps;

            var consumedUps = new HashSet<int>();
            long cursor = _events[0].T; // leading silence is trimmed

            for (int i = 0; i < _events.Count; i++)
            {
                if (consumedUps.Contains(i)) continue;
                var ev = _events[i];

                int gap = Quantize(ev.T - cursor);
                if (_opt.KeepWaits && gap >= QuantumMs)
                    steps.Add(new MacroStep { Type = MacroStepType.Wait, DurationMs = gap });
                cursor = ev.T;

                switch (ev.Kind)
                {
                    case Kind.Down:
                    {
                        // A clean tap: the matching Up is the very next event and it comes quickly.
                        int upIndex = i + 1 < _events.Count && _events[i + 1].Kind == Kind.Up && _events[i + 1].Button == ev.Button
                            ? i + 1 : -1;
                        if (upIndex >= 0 && _events[upIndex].T - ev.T < TapThresholdMs)
                        {
                            int held = Math.Max(QuantumMs, Quantize(_events[upIndex].T - ev.T));
                            steps.Add(new MacroStep { Type = MacroStepType.Tap, Button = ev.Button, DurationMs = held });
                            consumedUps.Add(upIndex);
                            cursor = _events[upIndex].T;
                        }
                        else
                        {
                            steps.Add(new MacroStep { Type = MacroStepType.Press, Button = ev.Button });
                        }
                        break;
                    }
                    case Kind.Up:
                        steps.Add(new MacroStep { Type = MacroStepType.Release, Button = ev.Button });
                        break;
                    case Kind.StickPush:
                        steps.Add(new MacroStep
                        {
                            Type = MacroStepType.PushStick, Stick = ev.Stick,
                            StickX = Math.Round(Math.Clamp(ev.X, -1, 1), 3), StickY = Math.Round(Math.Clamp(ev.Y, -1, 1), 3)
                        });
                        break;
                    case Kind.StickCenter:
                        steps.Add(new MacroStep { Type = MacroStepType.CenterStick, Stick = ev.Stick });
                        break;
                }
            }

            if (!_opt.HoldAtEnd)
            {
                // Tidy ending: release whatever was still down when STOP was pressed.
                foreach (var (button, down) in _down)
                    if (down) steps.Add(new MacroStep { Type = MacroStepType.Release, Button = button });
                foreach (var (side, s) in _sticks)
                    if (s.Pushed) steps.Add(new MacroStep { Type = MacroStepType.CenterStick, Stick = side });
            }
            return steps;
        }

        private static int Quantize(long ms) => (int)(Math.Round(ms / (double)QuantumMs) * QuantumMs);

        /// <summary>Compact one-line description for the console log.</summary>
        public static string Describe(IEnumerable<MacroStep> steps) => string.Join(" → ", steps.Select(s => s.Type switch
        {
            MacroStepType.Tap => $"Tap {s.Button} {s.DurationMs}ms",
            MacroStepType.Wait => $"Wait {s.DurationMs}ms",
            MacroStepType.Press => $"Press {s.Button}",
            MacroStepType.Release => $"Release {s.Button}",
            MacroStepType.PushStick => $"Push {s.Stick} ({s.StickX:F2},{s.StickY:F2})",
            MacroStepType.CenterStick => $"Center {s.Stick}",
            _ => s.Type.ToString()
        }));
    }
}
