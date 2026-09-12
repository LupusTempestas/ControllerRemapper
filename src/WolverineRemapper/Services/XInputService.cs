using System;
using System.Runtime.InteropServices;

namespace WolverineRemapper.Services
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    /// <summary>
    /// Thin wrapper over XInput with two improvements over the naive approach:
    /// 1. Disconnected slots are only re-probed every 2 s (probing empty XInput
    ///    slots every frame is expensive — this is Microsoft's own guidance).
    /// 2. Slot scanning helpers so the app can find the *physical* pad and skip
    ///    the ViGEm virtual pad's slot.
    /// </summary>
    public class XInputService
    {
        private const uint ERROR_SUCCESS = 0;
        private const long DISCONNECT_PROBE_COOLDOWN_MS = 2000;

        public const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState91(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        private static extern uint XInputGetBatteryInformation14(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

        private bool _useFallback = false;
        private bool _batteryUnavailable = false;
        private readonly long[] _lastFailedProbe = new long[4];

        public bool GetState(int userIndex, out XINPUT_STATE state)
        {
            state = default;
            if (userIndex < 0 || userIndex > 3) return false;

            // Skip slots that recently reported "not connected"
            long now = Environment.TickCount64;
            if (_lastFailedProbe[userIndex] != 0 && now - _lastFailedProbe[userIndex] < DISCONNECT_PROBE_COOLDOWN_MS)
                return false;

            bool connected = GetStateRaw((uint)userIndex, out state);
            _lastFailedProbe[userIndex] = connected ? 0 : now;
            return connected;
        }

        private bool GetStateRaw(uint userIndex, out XINPUT_STATE state)
        {
            state = default;
            if (!_useFallback)
            {
                try
                {
                    return XInputGetState14(userIndex, out state) == ERROR_SUCCESS;
                }
                catch (DllNotFoundException)
                {
                    _useFallback = true;
                }
            }

            try
            {
                return XInputGetState91(userIndex, out state) == ERROR_SUCCESS;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>First connected slot, excluding <paramref name="excludeSlot"/> (pass -1 to exclude none).</summary>
        public int FindFirstConnectedSlot(int excludeSlot = -1)
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == excludeSlot) continue;
                if (GetState(i, out _)) return i;
            }
            return -1;
        }

        /// <summary>Bitmask of currently connected slots (bit 0 = slot 0 …). Bypasses the probe cooldown.</summary>
        public int GetConnectedSlotMask()
        {
            int mask = 0;
            for (uint i = 0; i < 4; i++)
            {
                if (GetStateRaw(i, out _)) mask |= 1 << (int)i;
            }
            return mask;
        }

        /// <summary>Human-readable battery description, or null when unavailable.</summary>
        public string? GetBatteryDescription(int userIndex)
        {
            if (_batteryUnavailable || userIndex < 0 || userIndex > 3) return null;
            try
            {
                if (XInputGetBatteryInformation14((uint)userIndex, BATTERY_DEVTYPE_GAMEPAD, out var info) != ERROR_SUCCESS)
                    return null;

                return info.BatteryType switch
                {
                    0x01 => "Wired",
                    0x02 or 0x03 => info.BatteryLevel switch
                    {
                        0 => "Battery: Empty",
                        1 => "Battery: Low",
                        2 => "Battery: Medium",
                        _ => "Battery: Full"
                    },
                    _ => null
                };
            }
            catch (DllNotFoundException)
            {
                _batteryUnavailable = true;
                return null;
            }
            catch
            {
                return null;
            }
        }

        // Button bitmasks
        public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const ushort XINPUT_GAMEPAD_START = 0x0010;
        public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const ushort XINPUT_GAMEPAD_A = 0x1000;
        public const ushort XINPUT_GAMEPAD_B = 0x2000;
        public const ushort XINPUT_GAMEPAD_X = 0x4000;
        public const ushort XINPUT_GAMEPAD_Y = 0x8000;
    }
}
