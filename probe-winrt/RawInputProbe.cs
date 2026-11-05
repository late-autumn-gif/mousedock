using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// 原始输入（Raw Input）探针：注册 RIDEV_INPUTSINK 后统计每个物理设备实际产生了多少鼠标事件。
/// 谁在动，就是谁在用——不依赖任何配对/连接历史。
/// </summary>
internal static class RawInputProbe
{
    private const int WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000b;
    private const int RIM_TYPEMOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_MOUSE
    {
        public uint dwId;
        public uint dwNumberOfButtons;
        public uint dwSampleRate;
        public int fHasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_MOUSE mouse;
        public ulong pad1, pad2, pad3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr buf, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfoW(IntPtr h, uint cmd, StringBuilder buf, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfoW(IntPtr h, uint cmd, IntPtr buf, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

    private static readonly Dictionary<IntPtr, int> Counts = new Dictionary<IntPtr, int>();
    private static WndProc _proc;   // 保持引用，防止被 GC

    public static void Run(int seconds)
    {
        _proc = WndProcImpl;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = Marshal.StringToHGlobalUni("MouseDockRawInputProbe")
        };
        RegisterClassExW(ref wc);
        IntPtr hwnd = CreateWindowExW(0, "MouseDockRawInputProbe", "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = hwnd }
        };
        bool ok = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        Console.WriteLine("RegisterRawInputDevices = " + ok + " (窗口 " + hwnd + ")");
        if (!ok) return;

        Console.WriteLine($"请把鼠标在屏幕上移动 {seconds} 秒（左键点几下更好）…");
        ulong deadline = GetTickCount64() + (ulong)seconds * 1000;

        while (GetTickCount64() < deadline)
        {
            while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            System.Threading.Thread.Sleep(15);
        }

        Console.WriteLine();
        Console.WriteLine("=== 实际产生鼠标事件的设备（按事件数降序） ===");
        if (Counts.Count == 0)
        {
            Console.WriteLine("没有捕获到任何鼠标事件。");
            return;
        }

        var list = new List<KeyValuePair<IntPtr, int>>(Counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var kv in list)
        {
            Console.WriteLine();
            Console.WriteLine($"事件数: {kv.Value}");
            Console.WriteLine("  设备路径: " + DeviceName(kv.Key));
            var info = DeviceInfo(kv.Key);
            if (info.HasValue && info.Value.dwType == RIM_TYPEMOUSE)
                Console.WriteLine($"  鼠标 ID={info.Value.mouse.dwId} 按键数={info.Value.mouse.dwNumberOfButtons} 采样率={info.Value.mouse.dwSampleRate}Hz 横向滚轮={info.Value.mouse.fHasHorizontalWheel}");
        }
    }

    private static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            uint size = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
            if (size > 0)
            {
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buf, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == size)
                    {
                        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
                        if (header.dwType == RIM_TYPEMOUSE)
                        {
                            Counts.TryGetValue(header.hDevice, out int n);
                            Counts[header.hDevice] = n + 1;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static string DeviceName(IntPtr h)
    {
        uint size = 0;
        GetRawInputDeviceInfoW(h, RIDI_DEVICENAME, (StringBuilder)null, ref size);
        if (size == 0) return "(无法读取)";
        var sb = new StringBuilder((int)size + 2);
        GetRawInputDeviceInfoW(h, RIDI_DEVICENAME, sb, ref size);
        return sb.ToString();
    }

    private static RID_DEVICE_INFO? DeviceInfo(IntPtr h)
    {
        var info = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>() };
        uint size = info.cbSize;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.StructureToPtr(info, buf, false);
            uint got = GetRawInputDeviceInfoW(h, RIDI_DEVICEINFO, buf, ref size);
            if (got == unchecked((uint)-1) || got == 0) return null;
            return Marshal.PtrToStructure<RID_DEVICE_INFO>(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
