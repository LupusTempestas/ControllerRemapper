using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Orchestrates the keyboard hook, the virtual pad and the M-button
    /// mapping table. All state lives on the UI thread (the low-level hook is
    /// installed from it, so its callback runs there too — no locking needed).
    ///
    /// Correctness rules enforced here:
    ///  • Auto-repeat guard: holding a key fires the chord exactly once.
    ///  • Press-time snapshots: a release always undoes exactly what the press
    ///    did, even if the user edits the chord while the key is held.
    ///  • O(1) vkCode → config lookup inside the hook path.
    /// </summary>
    public class RemapperEngine : IDisposable
    {
        private const uint VK_ESCAPE = 0x1B;

        private readonly KeyboardHookService _hook = new();
        private readonly VirtualControllerService _virtualPad = new();
        private readonly XInputService _xinput;
        private readonly DispatcherTimer _passthroughTimer;

        private Dictionary<uint, MButtonConfig> _mappings = new();

        // Pad-button triggers (Wolverine V2 family). Keyed by the sacrificed
        // physical button; the shared activation tables below use a synthetic
        // id (PadTriggerId) that can never collide with a virtual-key code.
        private Dictionary<VirtualButtonId, MButtonConfig> _padMappings = new();
        private readonly HashSet<VirtualButtonId> _padTriggersDown = new();
        private ushort _padStripMask;      // sacrificed digital buttons, removed from passthrough
        private bool _padStripLT, _padStripRT;

        private readonly Dictionary<uint, ChordSnapshot> _activeChords = new();
        private readonly Dictionary<uint, MacroRun> _macroRuns = new();
        private readonly HashSet<uint> _heldKeys = new();   // physical key currently down (auto-repeat guard)
        private readonly HashSet<uint> _activeKeys = new();  // action currently running/latched
        private readonly Dictionary<uint, long> _lastTapTime = new(); // for double-tap detection

        private MButtonConfig? _captureTarget;
        private uint _captureSwallowKeyUp;

        public bool IsRunning { get; private set; }
        public bool IsCapturing => _captureTarget != null;
        public int VirtualSlot { get; private set; } = -1;

        /// <summary>
        /// Slot of the physical pad to mirror in passthrough mode. Kept up to
        /// date by the ViewModel's activity-based slot detection.
        /// </summary>
        public int PhysicalSlot { get; set; } = -1;

        public event Action<string>? Log;
        public event Action<MButtonConfig>? KeyCaptured;
        public event Action? CaptureEnded;

        public RemapperEngine(XInputService xinput)
        {
            _xinput = xinput;
            _hook.ProcessKey = ProcessKey;
            _passthroughTimer = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(8) // ~125 Hz mirror rate
            };
            _passthroughTimer.Tick += OnPassthroughTick;
        }

        public bool PassthroughEnabled
        {
            get => _virtualPad.PassthroughEnabled;
            set
            {
                _virtualPad.PassthroughEnabled = value;
                UpdatePassthroughTimer();
                if (!value)
                {
                    _virtualPad.UpdatePhysicalState(null);
                    if (IsRunning) _virtualPad.Submit();
                }
            }
        }

        public double InnerDeadzonePercent
        {
            get => _virtualPad.InnerDeadzonePercent;
            set => _virtualPad.InnerDeadzonePercent = value;
        }

        public double OuterDeadzonePercent
        {
            get => _virtualPad.OuterDeadzonePercent;
            set => _virtualPad.OuterDeadzonePercent = value;
        }

        /// <summary>Rebuild the trigger lookups. Call whenever a trigger key or pad button changes.</summary>
        public void SetMappings(IEnumerable<MButtonConfig> configs)
        {
            var map = new Dictionary<uint, MButtonConfig>();
            var padMap = new Dictionary<VirtualButtonId, MButtonConfig>();
            ushort strip = 0;
            bool stripLT = false, stripRT = false;

            foreach (var cfg in configs)
            {
                if (cfg.Source == TriggerSource.PadButton)
                {
                    if (!padMap.TryAdd(cfg.PadTriggerButton, cfg))
                    {
                        Log?.Invoke($"[!] {cfg.MButtonName} shares pad button '{PadButtons.Label(cfg.PadTriggerButton)}' with {padMap[cfg.PadTriggerButton].MButtonName} — {padMap[cfg.PadTriggerButton].MButtonName} wins.");
                        continue;
                    }
                    if (cfg.PadTriggerButton == VirtualButtonId.LT) stripLT = true;
                    else if (cfg.PadTriggerButton == VirtualButtonId.RT) stripRT = true;
                    else strip |= PadButtonMask(cfg.PadTriggerButton);
                }
                else if (!map.TryAdd(cfg.VkCode, cfg))
                {
                    Log?.Invoke($"[!] {cfg.MButtonName} shares key '{cfg.KeyDisplayName}' with {map[cfg.VkCode].MButtonName} — {map[cfg.VkCode].MButtonName} wins.");
                }
            }

            _mappings = map;
            _padMappings = padMap;
            _padStripMask = strip;
            _padStripLT = stripLT;
            _padStripRT = stripRT;
            UpdatePassthroughTimer(); // pad triggers need the pad poll even without passthrough
        }

        /// <summary>Synthetic activation id for a pad-button trigger (VK codes are ≤ 0xFF).</summary>
        private static uint PadTriggerId(VirtualButtonId id) => 0x10000u | (uint)id;

        private static ushort PadButtonMask(VirtualButtonId id) => id switch
        {
            VirtualButtonId.A => XInputService.XINPUT_GAMEPAD_A,
            VirtualButtonId.B => XInputService.XINPUT_GAMEPAD_B,
            VirtualButtonId.X => XInputService.XINPUT_GAMEPAD_X,
            VirtualButtonId.Y => XInputService.XINPUT_GAMEPAD_Y,
            VirtualButtonId.LB => XInputService.XINPUT_GAMEPAD_LEFT_SHOULDER,
            VirtualButtonId.RB => XInputService.XINPUT_GAMEPAD_RIGHT_SHOULDER,
            VirtualButtonId.DPadUp => XInputService.XINPUT_GAMEPAD_DPAD_UP,
            VirtualButtonId.DPadDown => XInputService.XINPUT_GAMEPAD_DPAD_DOWN,
            VirtualButtonId.DPadLeft => XInputService.XINPUT_GAMEPAD_DPAD_LEFT,
            VirtualButtonId.DPadRight => XInputService.XINPUT_GAMEPAD_DPAD_RIGHT,
            VirtualButtonId.LS => XInputService.XINPUT_GAMEPAD_LEFT_THUMB,
            VirtualButtonId.RS => XInputService.XINPUT_GAMEPAD_RIGHT_THUMB,
            VirtualButtonId.View => XInputService.XINPUT_GAMEPAD_BACK,
            VirtualButtonId.Menu => XInputService.XINPUT_GAMEPAD_START,
            _ => 0
        };

        private const byte TriggerPressThreshold = 30; // XInput's own recommended threshold

        private static bool IsPadButtonDown(in XINPUT_GAMEPAD pad, VirtualButtonId id) => id switch
        {
            VirtualButtonId.LT => pad.bLeftTrigger > TriggerPressThreshold,
            VirtualButtonId.RT => pad.bRightTrigger > TriggerPressThreshold,
            _ => (pad.wButtons & PadButtonMask(id)) != 0
        };

        public bool Start(out string? error)
        {
            error = null;
            if (IsRunning) return true;

            if (!_virtualPad.IsConnected)
            {
                // Snapshot XInput slots before connecting so we can identify
                // the virtual pad's slot even on drivers without UserIndex.
                int slotsBefore = _xinput.GetConnectedSlotMask();

                if (!_virtualPad.Initialize(out var vigemError))
                {
                    error = $"ViGEmBus virtual controller failed: {vigemError}";
                    return false;
                }

                VirtualSlot = ResolveVirtualSlot(slotsBefore);

                // Push one neutral report right away: until the first Submit the
                // driver can expose stale axis values (seen as a ~10% stick offset).
                _virtualPad.Submit();
            }

            if (!_hook.IsActive && !_hook.Start(out var hookError))
            {
                error = $"Keyboard hook failed: {hookError}";
                return false;
            }

            IsRunning = true;
            UpdatePassthroughTimer();
            return true;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            // Release anything still held so no virtual button stays stuck.
            _heldKeys.Clear();
            _padTriggersDown.Clear();
            _activeKeys.Clear();
            _lastTapTime.Clear();
            _activeChords.Clear();
            foreach (var run in _macroRuns.Values) run.Abort();
            _macroRuns.Clear();
            _virtualPad.UpdatePhysicalState(null);
            _virtualPad.ResetChords();

            foreach (var cfg in _mappings.Values) cfg.IsPressed = false;

            UpdatePassthroughTimer();
            if (!IsCapturing) _hook.Stop(); // keep the hook if a capture is mid-flight
        }

        #region Key capture (rebinding)

        /// <summary>Begin listening for the next key press to bind to <paramref name="target"/>.</summary>
        public bool BeginCapture(MButtonConfig target, out string? error)
        {
            error = null;
            _captureTarget = target;

            // The hook must run for capture even when the engine is stopped.
            if (!_hook.IsActive && !_hook.Start(out error))
            {
                _captureTarget = null;
                return false;
            }
            return true;
        }

        public void CancelCapture() => EndCapture(assigned: null);

        private void EndCapture(MButtonConfig? assigned)
        {
            _captureTarget = null;
            if (!IsRunning) _hook.Stop();

            if (assigned != null)
            {
                var duplicate = _mappings.Values.FirstOrDefault(c => c != assigned && c.VkCode == assigned.VkCode);
                if (duplicate != null)
                {
                    Log?.Invoke($"[!] Warning: '{assigned.KeyDisplayName}' is also bound to {duplicate.MButtonName}.");
                }
                KeyCaptured?.Invoke(assigned);
            }
            CaptureEnded?.Invoke();
        }

        #endregion

        /// <summary>Hook callback body. Returns true to swallow the key event.</summary>
        private bool ProcessKey(uint vkCode, bool isDown)
        {
            // --- Capture mode -------------------------------------------------
            if (_captureTarget != null)
            {
                if (!isDown) return false; // ignore key-ups while waiting
                if (vkCode == VK_ESCAPE)
                {
                    EndCapture(assigned: null);
                    return true;
                }

                var target = _captureTarget;
                target.VkCode = vkCode;
                target.KeyDisplayName = GetKeyName(vkCode);
                _captureSwallowKeyUp = vkCode;
                EndCapture(target);
                return true; // the captured press never reaches the game
            }

            // Swallow the key-up matching a captured press.
            if (!isDown && vkCode == _captureSwallowKeyUp)
            {
                _captureSwallowKeyUp = 0;
                return true;
            }

            // --- Normal remapping --------------------------------------------
            if (!_mappings.TryGetValue(vkCode, out var config)) return false;
            if (!IsRunning) return false;

            if (isDown)
            {
                // Auto-repeat guard: Windows re-sends WM_KEYDOWN while a key is
                // held. Without this, chord ref-counts inflate and buttons
                // stick pressed forever (the original app's worst bug).
                if (!_heldKeys.Add(vkCode))
                    return config.SuppressKey;

                HandleTriggerDown(config, vkCode);
            }
            else
            {
                _heldKeys.Remove(vkCode);
                // Only the Hold trigger ends on key-up; Toggle/Double-tap latch.
                if (config.Trigger == TriggerType.Hold)
                    Deactivate(config, vkCode);
            }

            return config.SuppressKey;
        }

        /// <summary>Decide, per trigger type, whether a fresh key-press activates or deactivates.</summary>
        private void HandleTriggerDown(MButtonConfig config, uint vkCode)
        {
            switch (config.Trigger)
            {
                case TriggerType.Hold:
                    Activate(config, vkCode);
                    break;

                case TriggerType.Toggle:
                    if (_activeKeys.Contains(vkCode)) Deactivate(config, vkCode);
                    else Activate(config, vkCode);
                    break;

                case TriggerType.DoubleTap:
                    long now = Environment.TickCount64;
                    if (_lastTapTime.TryGetValue(vkCode, out long last) && now - last <= config.DoubleTapWindowMs)
                    {
                        _lastTapTime.Remove(vkCode); // consumed — a 3rd quick tap starts fresh
                        if (_activeKeys.Contains(vkCode)) Deactivate(config, vkCode);
                        else Activate(config, vkCode);
                    }
                    else
                    {
                        _lastTapTime[vkCode] = now; // first tap: wait for the second
                    }
                    break;
            }
        }

        /// <summary>Start an M-button's action (chord or macro). Idempotent.</summary>
        private void Activate(MButtonConfig config, uint vkCode)
        {
            if (!_activeKeys.Add(vkCode)) return; // already active

            // A plain held chord (Repeat=Once) is a static hold. Any other
            // action — a macro, a hold+tap, or a *repeated* chord — runs
            // through MacroRun so the repeat/turbo logic applies uniformly.
            if (config.Mode == ActionMode.Chord && config.Repeat == RepeatMode.Once)
            {
                var snapshot = config.GetChordSnapshot();
                _activeChords[vkCode] = snapshot;
                _virtualPad.SetChordState(snapshot, pressed: true);
            }
            else
            {
                var steps = config.Mode == ActionMode.Chord
                    ? BuildChordPulseSteps(config)
                    : BuildSteps(config);
                // Hold+Tap: the hold-set press and the step delay run once,
                // then only the tap pulse repeats (so the gap stays the gap).
                var preamble = config.Mode == ActionMode.HoldTap
                    ? BuildHoldTapPreamble(config)
                    : null;
                var run = new MacroRun(_virtualPad, steps, config.EffectiveRepeat, config.RepeatCount, config.RepeatGapMs, preamble);
                _macroRuns[vkCode] = run;
                run.Start();
            }

            config.IsPressed = true;
            string verb = config.Trigger == TriggerType.Hold ? "↓" : "ON";
            Log?.Invoke($"[{config.MButtonName}] {config.TriggerDisplayName} {verb} → {config.ChordSummary}");
        }

        /// <summary>Stop an M-button's action, releasing exactly what it holds. Idempotent.</summary>
        private void Deactivate(MButtonConfig config, uint vkCode)
        {
            if (!_activeKeys.Remove(vkCode)) return; // wasn't active

            if (_activeChords.Remove(vkCode, out var snapshot))
                _virtualPad.SetChordState(snapshot, pressed: false);
            if (_macroRuns.Remove(vkCode, out var run))
                run.NotifyKeyReleased();

            config.IsPressed = false;
            if (config.Trigger != TriggerType.Hold)
                Log?.Invoke($"[{config.MButtonName}] {config.TriggerDisplayName} OFF");
        }

        /// <summary>
        /// A repeated chord is one pulse: press all outputs, hold briefly,
        /// release all. MacroRun repeats it a set number of times / while held.
        /// </summary>
        private static List<ResolvedStep> BuildChordPulseSteps(MButtonConfig config)
        {
            var ids = config.GetChordButtonIds();
            int hold = Math.Max(20, config.RepeatGapMs);
            var steps = new List<ResolvedStep>();
            foreach (var id in ids) steps.Add(new ResolvedStep(MacroStepType.Press, id, 0));
            steps.Add(new ResolvedStep(MacroStepType.Wait, VirtualButtonId.A, hold));
            foreach (var id in ids) steps.Add(new ResolvedStep(MacroStepType.Release, id, 0));
            return steps;
        }

        /// <summary>Resolve an M-button's action into executable steps at press time.</summary>
        private static List<ResolvedStep> BuildSteps(MButtonConfig config)
        {
            if (config.Mode == ActionMode.HoldTap)
            {
                // The hold-set press + delay live in the preamble (see
                // BuildHoldTapPreamble); this is only the repeatable part.
                var steps = new List<ResolvedStep>();
                foreach (var b in config.TapButtons)
                    steps.Add(new ResolvedStep(MacroStepType.Press, b, 0));
                if (config.Repeat != RepeatMode.Once)
                {
                    // Repeated: the tap-set must release between iterations
                    // or the game only ever sees one press edge. Same pulse
                    // width convention as a repeated chord.
                    int pulse = Math.Max(20, config.RepeatGapMs);
                    steps.Add(new ResolvedStep(MacroStepType.Wait, VirtualButtonId.A, pulse));
                    foreach (var b in config.TapButtons)
                        steps.Add(new ResolvedStep(MacroStepType.Release, b, 0));
                }
                // Once: the tap-set stays held until the M-button is released.
                return steps;
            }

            var resolved = new List<ResolvedStep>(config.Steps.Count);
            foreach (var s in config.Steps)
                resolved.Add(new ResolvedStep(s.Type, s.Button, s.DurationMs, s.Stick, s.StickX, s.StickY));
            return resolved;
        }

        /// <summary>
        /// Hold+Tap one-shot prefix: press the hold-set, then wait the step
        /// delay. Runs a single time before the (possibly repeated) tap pulse.
        /// The hold-set stays down until the M-button is released.
        /// </summary>
        private static List<ResolvedStep> BuildHoldTapPreamble(MButtonConfig config)
        {
            var steps = new List<ResolvedStep>();
            foreach (var b in config.HoldButtons)
                steps.Add(new ResolvedStep(MacroStepType.Press, b, 0));
            steps.Add(new ResolvedStep(MacroStepType.Wait, VirtualButtonId.A, Math.Max(20, config.HoldTapDelayMs)));
            return steps;
        }

        #region Passthrough

        /// <summary>
        /// The 125 Hz pad poll serves two jobs: mirroring the physical pad
        /// (passthrough) and watching sacrificed buttons (pad-button triggers).
        /// It runs whenever either is needed.
        /// </summary>
        private void UpdatePassthroughTimer()
        {
            bool shouldRun = IsRunning && (PassthroughEnabled || _padMappings.Count > 0);
            if (shouldRun && !_passthroughTimer.IsEnabled) _passthroughTimer.Start();
            else if (!shouldRun && _passthroughTimer.IsEnabled) _passthroughTimer.Stop();
        }

        private void OnPassthroughTick(object? sender, EventArgs e)
        {
            int slot = PhysicalSlot;
            if (slot < 0 || slot == VirtualSlot || !_xinput.GetState(slot, out var state))
            {
                slot = _xinput.FindFirstConnectedSlot(excludeSlot: VirtualSlot);
                if (slot < 0 || !_xinput.GetState(slot, out state))
                {
                    // Pad gone: a held pad-button trigger counts as released.
                    ReleaseAllPadTriggers();
                    if (PassthroughEnabled)
                    {
                        _virtualPad.UpdatePhysicalState(null);
                        _virtualPad.Submit();
                    }
                    return;
                }
            }

            var pad = state.Gamepad;
            if (_padMappings.Count > 0) ScanPadTriggers(ref pad);

            if (PassthroughEnabled)
            {
                _virtualPad.UpdatePhysicalState(pad);
                _virtualPad.Submit();
            }
        }

        /// <summary>
        /// Edge-detect every sacrificed button, route it through the same
        /// trigger logic as a key, then remove it from the report so the
        /// mirrored pad never shows the raw press.
        /// </summary>
        private void ScanPadTriggers(ref XINPUT_GAMEPAD pad)
        {
            foreach (var (button, config) in _padMappings)
            {
                bool down = IsPadButtonDown(pad, button);
                uint id = PadTriggerId(button);

                if (down)
                {
                    if (_padTriggersDown.Add(button)) HandleTriggerDown(config, id);
                }
                else if (_padTriggersDown.Remove(button) && config.Trigger == TriggerType.Hold)
                {
                    Deactivate(config, id);
                }
            }

            pad.wButtons &= (ushort)~_padStripMask;
            if (_padStripLT) pad.bLeftTrigger = 0;
            if (_padStripRT) pad.bRightTrigger = 0;
        }

        private void ReleaseAllPadTriggers()
        {
            if (_padTriggersDown.Count == 0) return;
            foreach (var button in _padTriggersDown.ToList())
            {
                _padTriggersDown.Remove(button);
                if (_padMappings.TryGetValue(button, out var config) && config.Trigger == TriggerType.Hold)
                    Deactivate(config, PadTriggerId(button));
            }
        }

        #endregion

        private int ResolveVirtualSlot(int slotsBeforeConnect)
        {
            // Preferred: ask the driver directly.
            int reported = _virtualPad.TryGetVirtualSlot();
            if (reported is >= 0 and <= 3) return reported;

            // Fallback: the newly appeared slot is the virtual pad. The driver
            // may take a moment to enumerate, so poll briefly.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int diff = _xinput.GetConnectedSlotMask() & ~slotsBeforeConnect;
                if (diff != 0)
                {
                    for (int i = 0; i < 4; i++)
                        if ((diff & (1 << i)) != 0) return i;
                }
                System.Threading.Thread.Sleep(50);
            }
            return -1;
        }

        public static string GetKeyName(uint vkCode)
        {
            try
            {
                var key = KeyInterop.KeyFromVirtualKey((int)vkCode);
                if (key != Key.None) return key.ToString();
            }
            catch { /* fall through to hex */ }
            return $"VK_0x{vkCode:X2}";
        }

        public void Dispose()
        {
            _captureTarget = null;
            Stop();
            _passthroughTimer.Stop();
            _hook.Dispose();
            _virtualPad.Dispose();
        }
    }
}
