using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 厂商协议读取器：复刻 AULA/雷柏系 2.4G 接收器的 32 字节命令帧。
    /// 已用 USBPcap 抓到官方驱动的真实帧并逐字节验证：
    ///   30 00 01 … → 30 00 01 0B "M137" 01 16 11 AA 00 41 01   （Identify，0x41=65% 电量）
    ///   12 00 01 … → 12 00 01 01 01
    ///   13 00 01 … → 13 00 01 19 06 10/20/30/40/80/DC…          （DPI 档位）
    ///   D1 00 41 … ← 设备主动推送的状态（第 3 字节同样是电量）
    /// </summary>
    internal static class VendorProtocol
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr h, byte[] buf, int len, IntPtr written, ref NativeOverlapped ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr h, byte[] buf, int len, IntPtr read, ref NativeOverlapped ov);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr a, bool manual, bool init, IntPtr name);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] private static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint transferred, bool wait);
        [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int needed, IntPtr info);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attr);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr prep);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr prep);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr prep, ref HIDP_CAPS caps);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        /// <summary>读一次 2.4G 电量；失败返回 -1。接收器偶尔回 status=0x02，每次重试用新句柄。</summary>
        public static int TryReadBattery(ushort vid, ushort pid)
        {
            var paths = Paths(vid, pid);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (var path in paths)
                {
                    IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                    try
                    {
                        var caps = Caps(h);
                        if (caps.OutputReportByteLength != 33) continue;   // 只处理 32 字节厂商集合
                        int level = Session(h);
                        if (level >= 0) return level;
                    }
                    catch { }
                    finally { CloseHandle(h); }
                }

                if (attempt < 2) Thread.Sleep(120);
            }
            return -1;
        }

        private static HIDP_CAPS Caps(IntPtr h)
        {
            var caps = new HIDP_CAPS();
            if (HidD_GetPreparsedData(h, out IntPtr prep))
            {
                HidP_GetCaps(prep, ref caps);
                HidD_FreePreparsedData(prep);
            }
            return caps;
        }

        private static int Session(IntPtr h)
        {
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                // 1) Identify：回应里第 15 字节是电量（实测 0x41 = 65%，对应驱动图标 3/4 格）
                var r30 = Exchange(h, ev, 0x30, 0x00);
                if (r30 != null && r30.Length >= 16 && r30[0] == 0x00 && r30[1] == 0x30)
                {
                    int v = r30[14];   // 载荷 "M137" 之后第 9 字节 = 电量
                    if (v >= 1 && v <= 100) return v;
                }

                // 2) 顺带握手，让设备进入可推送状态
                Exchange(h, ev, 0x12, 0x00);
                Exchange(h, ev, 0x13, 0x00);

                // 3) 收 400ms 内的 D1 推送（第 3 字节同样是电量）
                var deadline = DateTime.UtcNow.AddMilliseconds(400);
                while (DateTime.UtcNow < deadline)
                {
                    var push = ReadReport(h, ev, 150);
                    if (push == null) continue;
                    int at = (push.Length > 32 && push[0] == 0x00) ? 1 : 0;   // 去掉报告 ID
                    if (push.Length > at + 3 && push[at] == 0xD1)
                    {
                        int v = push[at + 2];
                        if (v >= 1 && v <= 100) return v;
                    }
                }
            }
            catch { }
            finally { CloseHandle(ev); }
            return -1;
        }

        private static byte[] Exchange(IntPtr h, IntPtr ev, byte cmd, byte sub)
        {
            var frame = new byte[33];
            frame[0] = 0x00;   // 报告 ID
            frame[1] = cmd;
            frame[2] = sub;
            frame[3] = 0x01;

            var ov = new NativeOverlapped { EventHandle = ev };
            if (!WriteFile(h, frame, frame.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
            WaitForSingleObject(ev, 400);
            GetOverlappedResult(h, ref ov, out _, false);
            ResetEvent(ev);
            return ReadReport(h, ev, 400);
        }

        private static byte[] ReadReport(IntPtr h, IntPtr ev, int ms)
        {
            var buf = new byte[64];
            var ov = new NativeOverlapped { EventHandle = ev };
            if (!ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, (uint)ms) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ov, out uint got, false);
            ResetEvent(ev);
            if (got == 0) return null;
            var slice = new byte[got];
            Array.Copy(buf, slice, got);
            return slice;
        }

        private static List<string> Paths(ushort vid, ushort pid)
        {
            var list = new List<string>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            try
            {
                for (uint i = 0; ; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA { Size = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                    if (need <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, out _, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                            IntPtr hh = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (hh != IntPtr.Zero && hh != new IntPtr(-1))
                            {
                                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                                if (HidD_GetAttributes(hh, ref a) && a.VendorID == vid && a.ProductID == pid) list.Add(path);
                                CloseHandle(hh);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { CloseHandle(set); }
            return list;
        }
    }
}
