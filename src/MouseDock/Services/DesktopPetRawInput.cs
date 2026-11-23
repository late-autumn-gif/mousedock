using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MouseDock.Services;

internal enum DesktopPetInputActivity { None, Keyboard, Mouse }

internal sealed class DesktopPetRawInput
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RawMouse = 0;
    private const uint RawKeyboard = 1;
    private const ushort KeyBreak = 0x0001;
    private const ushort MouseButtonsDown = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RawInputHeader>();
    private static readonly uint InputSize = (uint)Marshal.SizeOf<RawInput>();
    private static readonly uint DeviceSize = (uint)Marshal.SizeOf<RawInputDevice>();
    private readonly HwndSource _source;
    private readonly Action<DesktopPetInputActivity, long> _react;
    private readonly HwndSourceHook _receive;
    private bool _registered;
    private readonly HashSet<ushort> _heldKeys = new();

    internal bool KeyboardDown
    {
        get
        {
            // A disconnected device may never send its release.
            _heldKeys.RemoveWhere(static key => !IsDown(key));
            return _heldKeys.Count != 0;
        }
    }

    internal static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    internal DesktopPetRawInput(HwndSource source, Action<DesktopPetInputActivity, long> react)
    {
        _source = source;
        _react = react;
        _receive = Receive; // HwndSource holds hooks by weak reference.
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled == _registered) return;
        _heldKeys.Clear();
        if (_source.IsDisposed)
        {
            _registered = false;
            return;
        }

        uint flags = enabled ? RidevInputSink : RidevRemove;
        IntPtr target = enabled ? _source.Handle : IntPtr.Zero;
        RawInputDevice[] devices = [new(0x01, 0x02, flags, target), new(0x01, 0x06, flags, target)];
        if (!enabled)
        {
            _source.RemoveHook(_receive);
            _registered = false;
        }
        bool success = RegisterRawInputDevices(devices, (uint)devices.Length, DeviceSize);
        if (enabled && success)
        {
            _source.AddHook(_receive);
            _registered = true;
        }
    }

    private IntPtr Receive(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmInput || !_registered) return IntPtr.Zero;

        uint size = InputSize;
        uint copied = GetRawInputData(lParam, RidInput, out RawInput input, ref size, HeaderSize);
        if (copied >= HeaderSize + 16 && copied <= InputSize)
        {
            if (input.Header.Type == RawKeyboard && input.Body.VirtualKey is > 0 and < 255)
            {
                if ((input.Body.KeyFlags & KeyBreak) == 0) _heldKeys.Add(input.Body.VirtualKey);
                else _heldKeys.Remove(input.Body.VirtualKey);
            }
            var activity = Classify(input.Header.Type, input.Header.Type == RawKeyboard ? input.Body.KeyFlags : input.Body.MouseFlags);
            if (activity != DesktopPetInputActivity.None)
                _react(activity, Environment.TickCount64);
        }

        // Leave normal input delivery and WM_INPUT cleanup with the window procedure.
        return IntPtr.Zero;
    }

    internal static DesktopPetInputActivity Classify(uint kind, ushort flags) => kind switch
    {
        RawKeyboard when (flags & KeyBreak) == 0 => DesktopPetInputActivity.Keyboard,
        RawMouse when (flags & MouseButtonsDown) != 0 => DesktopPetInputActivity.Mouse,
        _ => DesktopPetInputActivity.None
    };

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RawInputDevice(ushort UsagePage, ushort Usage, uint Flags, IntPtr Target);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal IntPtr Device;
        internal IntPtr Param;
    }

    // RAWMOUSE/RAWKEYBOARD share this 24-byte union.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct RawInputBody
    {
        [FieldOffset(2)] internal ushort KeyFlags;
        [FieldOffset(4)] internal ushort MouseFlags;
        [FieldOffset(6)] internal ushort VirtualKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        internal RawInputHeader Header;
        internal RawInputBody Body;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr input, uint command, out RawInput data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
