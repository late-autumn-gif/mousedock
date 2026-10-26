using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 抓 2.4G 接收器「厂商集合」的原始报文（只读，不写设备）。
    /// 用重叠 I/O + 超时读中断管道，把每次收到的报文按十六进制打印，
    /// 并把与已知真值（比如 BLE 读到的电量）相同的字节位置标出来。
    /// </summary>
    public static class HidCapture
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
        private struct NativeOverlapped
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
        private static extern bool ReadFile(IntPtr h, byte[] buffer, int toRead, IntPtr read, ref NativeOverlapped ov);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(IntPtr h, ref NativeOverlapped ov);
        [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        public static string Run(int knownPercent, int seconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 2.4G 接收器报文抓取 ===");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  时长: {seconds}s  已知真值: {(knownPercent >= 0 ? knownPercent + "%" : "无")}");
            sb.AppendLine();

            var guid = GUID_DEVINTERFACE_HID;
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
            {
                sb.AppendLine("枚举 HID 失败");
                return sb.ToString();
            }

            int targets = 0;
            try
            {
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
                            var buf = new byte[256];
                            product = HidD_GetProductString(h, buf, buf.Length)
                                ? Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim()
                                : "";
                        }
                        finally { CloseHandle(h); }

                        if (caps.UsagePage < 0xFF00 || caps.OutputReportByteLength < 2 || caps.InputReportByteLength < 2) continue;
                        if (attr.VendorID == 0xFFAA) continue;   // 腾讯虚拟 HID

                        targets++;
                        sb.AppendLine($"[目标] VID {attr.VendorID:X4} PID {attr.ProductID:X4} | {product} | UPage 0x{caps.UsagePage:X2} Usage 0x{caps.Usage:X2} | IN={caps.InputReportByteLength} OUT={caps.OutputReportByteLength}");
                        CaptureStream(path, caps, knownPercent, seconds, sb);
                        sb.AppendLine();
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }

            if (targets == 0)
                sb.AppendLine("没有找到「厂商集合」接收器（可能是纯 BLE 连接，2.4G 接收器未插）。");

            return sb.ToString();
        }

        private static void CaptureStream(string path, HIDP_CAPS caps, int knownPercent, int seconds, StringBuilder sb)
        {
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
            {
                sb.AppendLine($"    打开失败 err={Marshal.GetLastWin32Error()}（可能被厂商驱动独占）");
                return;
            }

            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            var seen = new Dictionary<string, int>();
            int total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(seconds);

            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    var buffer = new byte[caps.InputReportByteLength];
                    var ov = new NativeOverlapped { hEvent = ev };

                    if (!ReadFile(h, buffer, buffer.Length, IntPtr.Zero, ref ov))
                    {
                        uint err = (uint)Marshal.GetLastWin32Error();
                        if (err != 997) { sb.AppendLine($"    读失败 err={err}"); break; }
                    }

                    if (WaitForSingleObject(ev, 900) != 0)
                    {
                        CancelIoEx(h, ref ov);
                        ResetEvent(ev);
                        continue;
                    }

                    GetOverlappedResult(h, ref ov, out uint got, false);
                    ResetEvent(ev);

                    if (got < 2) continue;
                    total++;

                    string hex = Mark(buffer, got, knownPercent, out string hit);
                    if (seen.TryGetValue(hex, out int n)) { seen[hex] = n + 1; continue; }
                    seen[hex] = 1;
                    sb.AppendLine($"    [{DateTime.Now:HH:mm:ss.fff}] {hex}{hit}");
                }
            }
            finally
            {
                CloseHandle(ev);
                CloseHandle(h);
            }

            sb.AppendLine($"    共收到 {total} 条，{seen.Count} 种不同报文");
            if (seen.Count == 0)
                sb.AppendLine("    （没有报文：鼠标此刻不在 2.4G 链路上，接收器不会上报。切到接收器模式再抓）");
        }

        private static string Mark(byte[] buffer, uint got, int knownPercent, out string hit)
        {
            hit = "";
            var hex = new StringBuilder();
            for (int k = 0; k < got && k < buffer.Length; k++)
            {
                hex.Append(buffer[k].ToString("X2")).Append(' ');
                if (knownPercent >= 0 && buffer[k] == knownPercent && hit.Length == 0)
                    hit = $"   <-- 第 {k} 字节 = 0x{knownPercent:X2}（等于已知电量 {knownPercent}%）";
            }
            return hex.ToString().TrimEnd();
        }
    }
}
