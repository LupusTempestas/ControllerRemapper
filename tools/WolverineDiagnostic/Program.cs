using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace WolverineDiagnostic
{
    class Program
    {
        #region Win32 Keyboard Hook P/Invoke (Synapse F13-F18 Suppression)

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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        #endregion

        private static LowLevelKeyboardProc? _keyboardProc;
        private static IntPtr _keyboardHook = IntPtr.Zero;

        private static ViGEmClient? _vigemClient;
        private static IXbox360Controller? _virtualController;

        // Reference counted state aggregator to handle overlapping inputs perfectly
        private static readonly Dictionary<Xbox360Button, int> _buttonRefCounts = new Dictionary<Xbox360Button, int>();
        private static bool _virtualLtPressed = false;

        // F13-F18 to M1-M6 Chord Mappings
        // F13 -> M1 (vkCode 0x7C)
        // F14 -> M2 (vkCode 0x7D)
        // F15 -> M3 (vkCode 0x7E)
        // F16 -> M4 (vkCode 0x7F)
        // F17 -> M5 (vkCode 0x80)
        // F18 -> M6 (vkCode 0x81)
        private static readonly Dictionary<uint, (string Name, Xbox360Button[] Buttons, bool TriggerLT)> _mButtonChords = new()
        {
            { 0x7C, ("M1", new[] { Xbox360Button.LeftShoulder, Xbox360Button.X }, false) },                // M1 -> LB + X
            { 0x7D, ("M2", new[] { Xbox360Button.RightShoulder, Xbox360Button.Y }, false) },               // M2 -> RB + Y
            { 0x7E, ("M3", new[] { Xbox360Button.A, Xbox360Button.LeftShoulder }, false) },                 // M3 -> A + LB
            { 0x7F, ("M4", new[] { Xbox360Button.X, Xbox360Button.RightShoulder }, false) },                // M4 -> X + RB
            { 0x80, ("M5", new[] { Xbox360Button.X }, true) },                                             // M5 -> LT + X
            { 0x81, ("M6", new[] { Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder, Xbox360Button.Y }, false) } // M6 -> LB + RB + Y
        };

        private static readonly HashSet<uint> _activeMKeys = new HashSet<uint>();

        static void Main(string[] args)
        {
            Console.Title = "Razer Wolverine V3 Pro 8K - Interception & Chord Remapper Engine";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================");
            Console.WriteLine("    WOLVERINE V3 PRO 8K - ZERO-LAG KEYBOARD SUPPRESSION & REMAPPER        ");
            Console.WriteLine("==========================================================================");
            Console.ResetColor();
            Console.WriteLine();

            // 1. Initialize ViGEmBus Virtual Controller
            try
            {
                _vigemClient = new ViGEmClient();
                _virtualController = _vigemClient.CreateXbox360Controller();
                _virtualController.Connect();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] SUCCESS: Virtual Xbox 360 Controller connected to Windows!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] ViGEmBus Error: {ex.Message}");
                Console.WriteLine("    Please ensure ViGEmBus driver is installed.");
                Console.ResetColor();
                return;
            }

            // 2. Install Low-Level Keyboard Interception Hook
            _keyboardProc = KeyboardHookCallback;
            using (var currentProcess = Process.GetCurrentProcess())
            using (var currentModule = currentProcess.MainModule)
            {
                if (currentModule != null)
                {
                    _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(currentModule.ModuleName), 0);
                    if (_keyboardHook != IntPtr.Zero)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[+] SUCCESS: Kernel-level Keyboard Suppression Hook active.");
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("==========================================================================");
            Console.WriteLine(" HOW THIS WORKS FOR THRONE AND LIBERTY:");
            Console.WriteLine(" 1. In Razer Synapse, set your buttons to:");
            Console.WriteLine("    M1 -> F13 | M2 -> F14 | M3 -> F15 | M4 -> F16 | M5 -> F17 | M6 -> F18");
            Console.WriteLine(" 2. When you press M1-M6, our hook INTERCEPTS and SWALLOWS F13-F18 instantly.");
            Console.WriteLine(" 3. Zero keyboard events reach Windows or Throne & Liberty (No UI lag/stutter!).");
            Console.WriteLine(" 4. Our app sends the mapped Xbox button combinations to the Virtual Controller.");
            Console.WriteLine("==========================================================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" ACTIVE M-BUTTON COMBINATIONS (CHORDS):");
            Console.WriteLine("   [M1] (F13) -> LB + X");
            Console.WriteLine("   [M2] (F14) -> RB + Y");
            Console.WriteLine("   [M3] (F15) -> A + LB");
            Console.WriteLine("   [M4] (F16) -> X + RB");
            Console.WriteLine("   [M5] (F17) -> LT + X");
            Console.WriteLine("   [M6] (F18) -> LB + RB + Y");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" Listening... Press M1-M6 on your Wolverine controller! (Press CTRL+C to stop)");
            Console.ResetColor();
            Console.WriteLine();

            // Win32 Message Loop
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) != 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
            }
            _virtualController?.Disconnect();
            _vigemClient?.Dispose();
        }

        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                uint vkCode = hookStruct.vkCode;

                if (_mButtonChords.TryGetValue(vkCode, out var chord))
                {
                    bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                    bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                    if (isKeyDown && !_activeMKeys.Contains(vkCode))
                    {
                        _activeMKeys.Add(vkCode);
                        ApplyChordState(chord, isPressed: true);

                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] ");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[CONSUMED & REMAPPED] {chord.Name} (F{(vkCode - 0x7C + 13)}) -> Virtual Xbox Chord: {string.Join(" + ", chord.Buttons.Select(b => b.ToString()))}{(chord.TriggerLT ? " + LT" : "")}");
                            Console.ResetColor();
                        }
                    }
                    else if (isKeyUp && _activeMKeys.Contains(vkCode))
                    {
                        _activeMKeys.Remove(vkCode);
                        ApplyChordState(chord, isPressed: false);

                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [RELEASED] {chord.Name} (F{(vkCode - 0x7C + 13)})");
                            Console.ResetColor();
                        }
                    }

                    // Return 1 to SUPPRESS / SWALLOW the key event completely from Windows & Throne and Liberty!
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private static void ApplyChordState((string Name, Xbox360Button[] Buttons, bool TriggerLT) chord, bool isPressed)
        {
            if (_virtualController == null) return;

            foreach (var btn in chord.Buttons)
            {
                int count = _buttonRefCounts.GetValueOrDefault(btn, 0);
                if (isPressed) count++;
                else count = Math.Max(0, count - 1);

                _buttonRefCounts[btn] = count;
                _virtualController.SetButtonState(btn, count > 0);
            }

            if (chord.TriggerLT)
            {
                _virtualLtPressed = isPressed;
                _virtualController.SetSliderValue(Xbox360Slider.LeftTrigger, isPressed ? (byte)255 : (byte)0);
            }

            _virtualController.SubmitReport();
        }
    }
}
