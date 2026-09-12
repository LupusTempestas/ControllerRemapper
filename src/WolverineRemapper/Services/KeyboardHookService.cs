using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WolverineRemapper.Services
{
    /// <summary>
    /// Low-level (WH_KEYBOARD_LL) keyboard hook.
    ///
    /// The hook callback must be FAST — Windows silently removes hooks that
    /// exceed the LowLevelHooksTimeout. All per-key work is delegated to a
    /// single synchronous <see cref="ProcessKey"/> callback that returns
    /// whether the event should be swallowed. The owner (RemapperEngine)
    /// guarantees O(1) dictionary lookups inside it.
    /// </summary>
    public class KeyboardHookService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        // Keep a strong reference to the delegate so the GC never collects it
        // while the native hook is installed.
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public KeyboardHookService()
        {
            _proc = HookCallback;
        }

        public bool IsActive => _hookId != IntPtr.Zero;

        /// <summary>
        /// (vkCode, isKeyDown) → true to suppress the event from Windows and all apps.
        /// Runs on the thread that installed the hook (the UI thread).
        /// </summary>
        public Func<uint, bool, bool>? ProcessKey { get; set; }

        /// <summary>Install the hook. Returns false with a Win32 error message on failure.</summary>
        public bool Start(out string? error)
        {
            error = null;
            if (IsActive) return true;

            // hMod may be IntPtr.Zero for low-level hooks — the callback runs
            // in-process, no DLL injection is involved.
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            if (_hookId == IntPtr.Zero)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            return true;
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var handler = ProcessKey;
                    if (handler != null && handler(hookStruct.vkCode, isDown))
                    {
                        return (IntPtr)1; // swallow: nothing reaches Windows or the game
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose() => Stop();
    }
}
