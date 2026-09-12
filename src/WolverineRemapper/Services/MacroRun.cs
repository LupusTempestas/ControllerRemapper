using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WolverineRemapper.Models;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// One in-flight execution of a Hold+Tap or Sequence action, started on
    /// trigger-key-down. Runs entirely on the UI thread (awaits resume via the
    /// WPF SynchronizationContext), so no locking is required.
    ///
    /// Semantics:
    ///  • A started sequence always runs to completion — a quick tap of the
    ///    M-button still executes the full combo.
    ///  • Buttons still held when the sequence ends stay held until the
    ///    trigger key is released (hold semantics), then release together.
    ///  • Turbo (loop) repeats the sequence only while the key is held and
    ///    stops at the end of the current iteration.
    ///  • Abort (engine stop / dispose) releases everything immediately.
    /// </summary>
    public class MacroRun
    {
        private readonly VirtualControllerService _pad;
        private readonly IReadOnlyList<ResolvedStep> _steps;
        private readonly RepeatMode _repeat;
        private readonly int _count;
        private readonly int _gapMs;
        private readonly HashSet<VirtualButtonId> _held = new();
        private readonly HashSet<StickSide> _heldSticks = new();

        private bool _keyReleased;
        private bool _finished;
        private bool _aborted;

        public MacroRun(VirtualControllerService pad, IReadOnlyList<ResolvedStep> steps,
                        RepeatMode repeat, int count, int gapMs)
        {
            _pad = pad;
            _steps = steps;
            _repeat = repeat;
            _count = Math.Max(1, count);
            _gapMs = Math.Max(10, gapMs);
        }

        public async void Start()
        {
            try
            {
                int iteration = 0;
                while (true)
                {
                    foreach (var step in _steps)
                    {
                        if (_aborted) return;

                        switch (step.Type)
                        {
                            case MacroStepType.Press:
                                if (_held.Add(step.Button))
                                    _pad.SetVirtualButton(step.Button, true);
                                break;

                            case MacroStepType.Release:
                                if (_held.Remove(step.Button))
                                    _pad.SetVirtualButton(step.Button, false);
                                break;

                            case MacroStepType.Tap:
                                if (_held.Add(step.Button))
                                    _pad.SetVirtualButton(step.Button, true);
                                await Task.Delay(Math.Max(10, step.DurationMs));
                                if (_aborted) return; // Abort() already released
                                if (_held.Remove(step.Button))
                                    _pad.SetVirtualButton(step.Button, false);
                                break;

                            case MacroStepType.Wait:
                                await Task.Delay(Math.Max(1, step.DurationMs));
                                break;

                            case MacroStepType.PushStick:
                                _heldSticks.Add(step.Stick);
                                _pad.SetMacroStick(step.Stick, step.StickX, step.StickY);
                                break;

                            case MacroStepType.CenterStick:
                                if (_heldSticks.Remove(step.Stick))
                                    _pad.ClearMacroStick(step.Stick);
                                break;
                        }
                    }

                    iteration++;
                    bool more = _repeat switch
                    {
                        RepeatMode.Count => iteration < _count,
                        RepeatMode.WhileHeld => !_keyReleased,
                        _ => false // Once
                    };
                    if (!more || _aborted) break;

                    // Breather between iterations (also the between-repeat gap).
                    await Task.Delay(_gapMs);
                    if (_aborted) return;
                }
            }
            catch
            {
                // A failed pad write mid-macro must never take down the app.
            }
            finally
            {
                _finished = true;
                if (_keyReleased || _aborted) ReleaseHeld();
            }
        }

        /// <summary>Trigger key released: finish semantics as documented above.</summary>
        public void NotifyKeyReleased()
        {
            _keyReleased = true;
            if (_finished) ReleaseHeld();
        }

        /// <summary>Hard stop (engine stopping): release everything now.</summary>
        public void Abort()
        {
            _aborted = true;
            ReleaseHeld();
        }

        private void ReleaseHeld()
        {
            // Buttons release before sticks recenter: for radial menus that
            // select on trigger-release, the stick must still point at the
            // slot in the report that carries the release.
            foreach (var button in _held)
                _pad.SetVirtualButton(button, false);
            _held.Clear();

            foreach (var stick in _heldSticks)
                _pad.ClearMacroStick(stick);
            _heldSticks.Clear();
        }
    }
}
