using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>
/// 2.4G 接收器「厂商集合」报文抓取：只读不写，用重叠 I/O 带超时读中断管道，
/// 把收到的 64 字节报文按去重打印出来。真值（BLE 读到 43%）用于比对定位电量字节。
/// </summary>
internal static class Capture
{
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal, InternalHigh;
        public uint Offset, OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetManufacturerString(IntPtr h, byte[] buffer, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumPtr, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr h, byte[] buffer, int toRead, IntPtr read, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr h, byte[] buffer, int toWrite, IntPtr written, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(IntPtr h, ref OVERLAPPED ov);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    public static void Run(int knownPercent, int seconds)
    {
        var guid = GUID_DEVINTERFACE_HID;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero) return;

        var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did); i++)
        {
            var devInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            IntPtr detail = Marshal.AllocHGlobal(1024);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, 1024, out _, ref devInfo)) continue;
                string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                if (string.IsNullOrEmpty(path)) continue;

                IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                HIDP_CAPS caps = default;
                HIDD_ATTRIBUTES attr = default;
                string product;
                try
                {
                    attr.Size = Marshal.SizeOf<HIDD_ATTRIBUTES>();
                    HidD_GetAttributes(h, ref attr);
                    if (HidD_GetPreparsedData(h, out IntPtr pre))
                    {
                        HidP_GetCaps(pre, ref caps);
                        HidD_FreePreparsedData(pre);
                    }
                    product = Str(HidD_GetProductString, h);
                }
                finally { CloseHandle(h); }

                // 只看「厂商集合 + 有输出和输入管道」的接收器
                if (caps.UsagePage < 0xFF00 || caps.OutputReportByteLength < 2 || caps.InputReportByteLength < 2) continue;
                if (attr.VendorID == 0xFFAA) continue;

                Console.WriteLine($"\n=== 抓取目标 VID {attr.VendorID:X4} PID {attr.ProductID:X4} | {product} | UPage 0x{caps.UsagePage:X2} Usage 0x{caps.Usage:X2}");
                Console.WriteLine($"    IN={caps.InputReportByteLength} OUT={caps.OutputReportByteLength}，只读抓 {seconds}s（真值 {knownPercent}%）");
                CaptureStream(path, caps, knownPercent, seconds);
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(set);
    }

    private static void CaptureStream(string path, HIDP_CAPS caps, int knownPercent, int seconds)
    {
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            Console.WriteLine($"    打开失败 err={Marshal.GetLastWin32Error()}");
            return;
        }

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        var seen = new Dictionary<string, int>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var buf = new byte[caps.InputReportByteLength];
                var ov = new OVERLAPPED { hEvent = ev };
                if (!ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov))
                {
                    uint err = (uint)Marshal.GetLastWin32Error();
                    if (err != 997) { Console.WriteLine($"    读失败 err={err}"); break; }   // 997 = IO_PENDING
                }

                uint wait = WaitForSingleObject(ev, 900);
                if (wait != 0)
                {
                    CancelIoEx(h, ref ov);
                    ResetEvent(ev);
                    continue;   // 超时：这段时间没有报文
                }

                GetOverlappedResult(h, ref ov, out uint got, false);
                ResetEvent(ev);

                if (got < 2) continue;
                string hex = BitConverter.ToString(buf, 0, (int)Math.Min(got, (uint)buf.Length)).Replace("-", " ");
                string trim = hex.TrimEnd(' ', '0').Trim();
                if (seen.TryGetValue(trim, out int n)) { seen[trim] = n + 1; continue; }

                seen[trim] = 1;
                string mark = "";
                for (int k = 1; k < got; k++)
                    if (buf[k] == knownPercent) { mark = $"   <-- 第 {k} 字节 = 0x{knownPercent:X2}（真值 {knownPercent}%）"; break; }
                Console.WriteLine($"    [{DateTime.Now:HH:mm:ss.fff}] {trim}{mark}");
            }
        }
        finally
        {
            CloseHandle(ev);
            CloseHandle(h);
        }

        Console.WriteLine($"    共 {seen.Count} 种不同报文");
        if (seen.Count == 0)
            Console.WriteLine("    （没有任何报文：鼠标当前走蓝牙/2.4G 链路空闲，接收器不会主动上报）");
    }

    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);

    private delegate bool StringGetter(IntPtr h, byte[] buf, int len);

    private static string Str(StringGetter getter, IntPtr h)
    {
        var buf = new byte[512];
        if (!getter(h, buf, buf.Length)) return "";
        return Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
    }
}
