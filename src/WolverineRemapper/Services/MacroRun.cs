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
    ///  • An optional preamble runs exactly once before the first iteration
    ///    (Hold+Tap uses it for the hold-set press + delay).
    ///  • Abort (engine stop / dispose) releases everything immediately.
    /// </summary>
    public class MacroRun
    {
        private readonly VirtualControllerService _pad;
        private readonly IReadOnlyList<ResolvedStep> _steps;
        private readonly IReadOnlyList<ResolvedStep>? _preamble;
        private readonly RepeatMode _repeat;
        private readonly int _count;
        private readonly int _gapMs;
        private readonly HashSet<VirtualButtonId> _held = new();
        private readonly HashSet<StickSide> _heldSticks = new();

        // Buttons pressed by the preamble (the Hold+Tap hold-set). On
        // trigger-key release these go up LAST, one game frame after the
        // rest: games that fire "modifier + key" combos on key RELEASE
        // (Throne & Liberty quick slots, for example) need the modifier
        // still down when the key comes up. Unreal processes trigger
        // releases before D-pad/face-button releases within a frame, so a
        // simultaneous release loses the combo every time.
        private readonly HashSet<VirtualButtonId> _preambleHeld = new();
        private bool _inPreamble;
        private const int ModifierReleaseDelayMs = 40;

        private bool _keyReleased;
        private bool _finished;
        private bool _aborted;

        /// <param name="preamble">Optional steps executed exactly once before
        /// the first iteration. Anything they press stays held under the
        /// normal hold rules.</param>
        public MacroRun(VirtualControllerService pad, IReadOnlyList<ResolvedStep> steps,
                        RepeatMode repeat, int count, int gapMs,
                        IReadOnlyList<ResolvedStep>? preamble = null)
        {
            _pad = pad;
            _steps = steps;
            _preamble = preamble;
            _repeat = repeat;
            _count = Math.Max(1, count);
            _gapMs = Math.Max(10, gapMs);
        }

        public async void Start()
        {
            try
            {
                if (_preamble != null)
                {
                    _inPreamble = true;
                    bool ok = await RunStepsAsync(_preamble);
                    _inPreamble = false;
                    if (!ok) return;
                }

                int iteration = 0;
                while (true)
                {
                    if (!await RunStepsAsync(_steps)) return;

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
                if (_aborted) ReleaseHeld();
                else if (_keyReleased) _ = ReleaseHeldStagedAsync();
            }
        }

        /// <summary>Execute one pass over a step list. Returns false if aborted.</summary>
        private async Task<bool> RunStepsAsync(IReadOnlyList<ResolvedStep> steps)
        {
            foreach (var step in steps)
            {
                if (_aborted) return false;

                switch (step.Type)
                {
                    case MacroStepType.Press:
                        if (_held.Add(step.Button))
                        {
                            if (_inPreamble) _preambleHeld.Add(step.Button);
                            _pad.SetVirtualButton(step.Button, true);
                        }
                        break;

                    case MacroStepType.Release:
                        if (_held.Remove(step.Button))
                        {
                            _preambleHeld.Remove(step.Button);
                            _pad.SetVirtualButton(step.Button, false);
                        }
                        break;

                    case MacroStepType.Tap:
                        if (_held.Add(step.Button))
                            _pad.SetVirtualButton(step.Button, true);
                        await Task.Delay(Math.Max(10, step.DurationMs));
                        if (_aborted) return false; // Abort() already released
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
            return true;
        }

        /// <summary>Trigger key released: finish semantics as documented above.</summary>
        public void NotifyKeyReleased()
        {
            _keyReleased = true;
            if (_finished) _ = ReleaseHeldStagedAsync();
        }

        /// <summary>Hard stop (engine stopping): release everything now.</summary>
        public void Abort()
        {
            _aborted = true;
            ReleaseHeld();
        }

        /// <summary>
        /// Normal key-up unwind: everything the body pressed goes up first,
        /// then (after one game frame) the preamble's hold-set, then sticks.
        /// </summary>
        private async Task ReleaseHeldStagedAsync()
        {
            try
            {
                ReleaseButtons(keepPreamble: true);
                if (_preambleHeld.Count > 0)
                {
                    await Task.Delay(ModifierReleaseDelayMs);
                    if (_aborted) return; // Abort() already released everything
                }
                ReleaseButtons(keepPreamble: false);
                ReleaseSticks();
            }
            catch
            {
                // A failed pad write must never take down the app.
            }
        }

        /// <summary>Immediate unwind (abort path): everything at once.</summary>
        private void ReleaseHeld()
        {
            ReleaseButtons(keepPreamble: false);
            ReleaseSticks();
        }

        private void ReleaseButtons(bool keepPreamble)
        {
            var toRelease = new List<VirtualButtonId>();
            foreach (var button in _held)
                if (!keepPreamble || !_preambleHeld.Contains(button))
                    toRelease.Add(button);

            foreach (var button in toRelease)
            {
                _held.Remove(button);
                _preambleHeld.Remove(button);
                _pad.SetVirtualButton(button, false);
            }
        }

        private void ReleaseSticks()
        {
            // Buttons release before sticks recenter: for radial menus that
            // select on trigger-release, the stick must still point at the
            // slot in the report that carries the release.
            foreach (var stick in _heldSticks)
                _pad.ClearMacroStick(stick);
            _heldSticks.Clear();
        }
    }
}
