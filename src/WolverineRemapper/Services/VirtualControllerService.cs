using System;
using System.Collections.Generic;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Owns the ViGEm virtual Xbox 360 pad and composes its report from two
    /// sources: reference-counted chord state (from M-button presses) and,
    /// when passthrough is enabled, the mirrored physical pad state.
    ///
    /// Note: Xbox360Button is a CLASS in ViGEm.NET, not an enum — never use
    /// Enum.GetValues on it (the old code did, and crashed on engine stop).
    /// An explicit button map is used instead; it also gives us the
    /// XInput-bitmask ↔ ViGEm-button pairing needed for passthrough.
    /// </summary>
    public class VirtualControllerService : IDisposable
    {
        /// <summary>Every supported button, paired with its XInput bitmask.</summary>
        private static readonly (ushort Mask, Xbox360Button Button)[] ButtonMap =
        {
            (XInputService.XINPUT_GAMEPAD_A, Xbox360Button.A),
            (XInputService.XINPUT_GAMEPAD_B, Xbox360Button.B),
            (XInputService.XINPUT_GAMEPAD_X, Xbox360Button.X),
            (XInputService.XINPUT_GAMEPAD_Y, Xbox360Button.Y),
            (XInputService.XINPUT_GAMEPAD_LEFT_SHOULDER, Xbox360Button.LeftShoulder),
            (XInputService.XINPUT_GAMEPAD_RIGHT_SHOULDER, Xbox360Button.RightShoulder),
            (XInputService.XINPUT_GAMEPAD_LEFT_THUMB, Xbox360Button.LeftThumb),
            (XInputService.XINPUT_GAMEPAD_RIGHT_THUMB, Xbox360Button.RightThumb),
            (XInputService.XINPUT_GAMEPAD_BACK, Xbox360Button.Back),
            (XInputService.XINPUT_GAMEPAD_START, Xbox360Button.Start),
            (XInputService.XINPUT_GAMEPAD_DPAD_UP, Xbox360Button.Up),
            (XInputService.XINPUT_GAMEPAD_DPAD_DOWN, Xbox360Button.Down),
            (XInputService.XINPUT_GAMEPAD_DPAD_LEFT, Xbox360Button.Left),
            (XInputService.XINPUT_GAMEPAD_DPAD_RIGHT, Xbox360Button.Right),
        };

        private ViGEmClient? _client;
        private IXbox360Controller? _controller;

        private readonly Dictionary<Xbox360Button, int> _chordRefCounts = new();
        private int _ltRefCount;
        private int _rtRefCount;

        private XINPUT_GAMEPAD? _physicalState;

        // Macro-driven stick positions. While set, they override both the
        // centered default and the passthrough mirror for that stick.
        private (short X, short Y)? _macroLeftStick;
        private (short X, short Y)? _macroRightStick;

        public bool IsConnected { get; private set; }
        public bool PassthroughEnabled { get; set; }

        /// <summary>Radial deadzone (percent, 0–50) applied to passthrough sticks.</summary>
        public double InnerDeadzonePercent { get; set; } = 10.0;

        /// <summary>Outer saturation (percent, 50–100) applied to passthrough sticks.</summary>
        public double OuterDeadzonePercent { get; set; } = 95.0;

        public bool Initialize(out string? error)
        {
            error = null;
            if (IsConnected) return true;

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                // We batch state changes and submit exactly one report per event.
                _controller.AutoSubmitReport = false;
                _controller.Connect();
                IsConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _controller = null;
                _client?.Dispose();
                _client = null;
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// XInput slot the virtual pad was assigned to, or -1 if the driver
        /// doesn't support reporting it. Used to avoid polling our own output.
        /// </summary>
        public int TryGetVirtualSlot()
        {
            try
            {
                return _controller?.UserIndex ?? -1;
            }
            catch
            {
                return -1; // older ViGEmBus versions don't support UserIndex
            }
        }

        /// <summary>VirtualButtonId → ViGEm button (LT/RT are sliders, handled separately).</summary>
        private static readonly Dictionary<VirtualButtonId, Xbox360Button> VirtualIdMap = new()
        {
            [VirtualButtonId.A] = Xbox360Button.A,
            [VirtualButtonId.B] = Xbox360Button.B,
            [VirtualButtonId.X] = Xbox360Button.X,
            [VirtualButtonId.Y] = Xbox360Button.Y,
            [VirtualButtonId.LB] = Xbox360Button.LeftShoulder,
            [VirtualButtonId.RB] = Xbox360Button.RightShoulder,
            [VirtualButtonId.DPadUp] = Xbox360Button.Up,
            [VirtualButtonId.DPadDown] = Xbox360Button.Down,
            [VirtualButtonId.DPadLeft] = Xbox360Button.Left,
            [VirtualButtonId.DPadRight] = Xbox360Button.Right,
            [VirtualButtonId.LS] = Xbox360Button.LeftThumb,
            [VirtualButtonId.RS] = Xbox360Button.RightThumb,
            [VirtualButtonId.View] = Xbox360Button.Back,
            [VirtualButtonId.Menu] = Xbox360Button.Start,
        };

        /// <summary>Press or release a single virtual button (reference-counted). Used by macros.</summary>
        public void SetVirtualButton(VirtualButtonId id, bool pressed)
        {
            if (!IsConnected) return;

            if (id == VirtualButtonId.LT)
            {
                _ltRefCount = Math.Max(0, _ltRefCount + (pressed ? 1 : -1));
            }
            else if (id == VirtualButtonId.RT)
            {
                _rtRefCount = Math.Max(0, _rtRefCount + (pressed ? 1 : -1));
            }
            else
            {
                var button = VirtualIdMap[id];
                int count = _chordRefCounts.GetValueOrDefault(button, 0) + (pressed ? 1 : -1);
                _chordRefCounts[button] = Math.Max(0, count);
            }

            Submit();
        }

        /// <summary>Hold a thumbstick at a fixed position (-1..1 per axis). Used by macros.</summary>
        public void SetMacroStick(StickSide side, double x, double y)
        {
            var value = ((short)Math.Clamp(x * 32767.0, short.MinValue, short.MaxValue),
                         (short)Math.Clamp(y * 32767.0, short.MinValue, short.MaxValue));
            if (side == StickSide.Left) _macroLeftStick = value;
            else _macroRightStick = value;
            Submit();
        }

        /// <summary>Release a macro-held thumbstick back to normal control.</summary>
        public void ClearMacroStick(StickSide side)
        {
            if (side == StickSide.Left) _macroLeftStick = null;
            else _macroRightStick = null;
            Submit();
        }

        /// <summary>Press or release an exact chord snapshot (reference-counted).</summary>
        public void SetChordState(ChordSnapshot chord, bool pressed)
        {
            if (!IsConnected) return;

            foreach (var btn in chord.Buttons)
            {
                int count = _chordRefCounts.GetValueOrDefault(btn, 0) + (pressed ? 1 : -1);
                _chordRefCounts[btn] = Math.Max(0, count);
            }

            if (chord.LeftTrigger)
                _ltRefCount = Math.Max(0, _ltRefCount + (pressed ? 1 : -1));
            if (chord.RightTrigger)
                _rtRefCount = Math.Max(0, _rtRefCount + (pressed ? 1 : -1));

            Submit();
        }

        /// <summary>Latest physical pad state to mirror (null = pad disconnected).</summary>
        public void UpdatePhysicalState(XINPUT_GAMEPAD? state)
        {
            _physicalState = state;
        }

        /// <summary>Compose chord + (optional) passthrough state into one report.</summary>
        public void Submit()
        {
            if (!IsConnected || _controller == null) return;

            bool mirror = PassthroughEnabled && _physicalState.HasValue;
            ushort physButtons = mirror ? _physicalState!.Value.wButtons : (ushort)0;

            foreach (var (mask, button) in ButtonMap)
            {
                bool fromChord = _chordRefCounts.GetValueOrDefault(button, 0) > 0;
                bool fromPhysical = (physButtons & mask) != 0;
                _controller.SetButtonState(button, fromChord || fromPhysical);
            }

            byte lt = _ltRefCount > 0 ? (byte)255 : (byte)0;
            byte rt = _rtRefCount > 0 ? (byte)255 : (byte)0;
            if (mirror)
            {
                lt = Math.Max(lt, _physicalState!.Value.bLeftTrigger);
                rt = Math.Max(rt, _physicalState!.Value.bRightTrigger);
            }
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, lt);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, rt);

            // Stick priority per side: macro override > passthrough mirror > centered.
            var left = _macroLeftStick
                ?? (mirror ? ApplyRadialDeadzone(_physicalState!.Value.sThumbLX, _physicalState.Value.sThumbLY) : ((short)0, (short)0));
            var right = _macroRightStick
                ?? (mirror ? ApplyRadialDeadzone(_physicalState!.Value.sThumbRX, _physicalState.Value.sThumbRY) : ((short)0, (short)0));
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, left.Item1);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, left.Item2);
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, right.Item1);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, right.Item2);

            _controller.SubmitReport();
        }

        /// <summary>
        /// Scaled radial deadzone: dead below inner, saturated above outer,
        /// linearly rescaled in between — so small drift is silenced without
        /// losing fine-aim precision near the deadzone edge.
        /// </summary>
        private (short X, short Y) ApplyRadialDeadzone(short rawX, short rawY)
        {
            double x = rawX / 32767.0;
            double y = rawY / 32767.0;
            double magnitude = Math.Sqrt(x * x + y * y);

            double inner = Math.Clamp(InnerDeadzonePercent, 0, 50) / 100.0;
            double outer = Math.Clamp(OuterDeadzonePercent, 50, 100) / 100.0;

            if (magnitude <= inner || magnitude == 0) return (0, 0);

            double scaled = Math.Min(1.0, (magnitude - inner) / (outer - inner));
            double factor = scaled / magnitude;

            return ((short)Math.Clamp(x * factor * 32767.0, short.MinValue, short.MaxValue),
                    (short)Math.Clamp(y * factor * 32767.0, short.MinValue, short.MaxValue));
        }

        /// <summary>Release all chord/macro-held state (physical mirroring is unaffected).</summary>
        public void ResetChords()
        {
            _chordRefCounts.Clear();
            _ltRefCount = 0;
            _rtRefCount = 0;
            _macroLeftStick = null;
            _macroRightStick = null;
            Submit();
        }

        public void Dispose()
        {
            try
            {
                if (IsConnected && _controller != null)
                {
                    ResetChords();
                    _controller.Disconnect();
                }
            }
            catch { /* driver teardown races are non-fatal on exit */ }

            _client?.Dispose();
            _client = null;
            _controller = null;
            IsConnected = false;
        }
    }
}
