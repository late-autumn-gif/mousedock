using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 主动查询探针：往 2.4G 接收器的厂商 OUT 通道发少量「只读查询」候选命令，
/// 收集 IN 通道回来的报文，找 0~100 的百分比字节（电量）。
/// 每个候选都发两次，只保留响应稳定的，减少误判。
/// </summary>
internal static class ActiveProbe
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
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr d);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr d, ref HIDP_CAPS c);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] b, int n);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int req, ref SP_DEVINFO_DATA di);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sec, uint cr, uint fl, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr h, byte[] b, int n, IntPtr w, ref OVERLAPPED o);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr h, byte[] b, int n, IntPtr r, ref OVERLAPPED o);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr s, bool m, bool i, IntPtr n);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] private static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED o, out uint t, bool w);
    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private sealed class Target
    {
        public string Path;
        public string Product;
        public HIDP_CAPS Caps;
    }

    public static void Run(ushort vid, ushort pid)
    {
        foreach (var t in FindTargets(vid, pid))
        {
            Console.WriteLine();
            Console.WriteLine($"=== 目标 {t.Product} | UPage 0x{t.Caps.UsagePage:X2} Usage 0x{t.Caps.Usage:X2} | IN={t.Caps.InputReportByteLength} OUT={t.Caps.OutputReportByteLength}");

            // 候选：(报告ID, 子命令)。只发零填充的「查询型」短报文
            var candidates = new List<(byte id, byte sub)>();
            foreach (byte id in new byte[] { 0x05, 0x02, 0x03, 0x06, 0x07, 0x08, 0x09, 0x0A })
                foreach (byte sub in new byte[] { 0x01, 0x02, 0x03, 0x08 })
                    candidates.Add((id, sub));

            int sent = 0;
            foreach (var (id, sub) in candidates)
            {
                if (sent >= 48) break;                  // 总量上限，避免乱轰
                string first = Exchange(t, id, sub);
                if (first == null) continue;
                sent++;

                Thread.Sleep(120);
                string second = Exchange(t, id, sub);   // 再发一次看是否稳定
                bool stable = second != null && second == first;

                var flags = new List<string>();
                foreach (var b in Bytes(first))
                    if (b >= 1 && b <= 100) flags.Add(b.ToString());

                Console.WriteLine($"  cmd {id:X2} {sub:X2} -> {first}   {(stable ? "稳定" : "不稳定")}" +
                                  (flags.Count > 0 ? "   疑似百分比字节: " + string.Join(",", flags) : ""));
                Thread.Sleep(160);
            }
            Console.WriteLine($"  共发送 {sent} 条查询。");
        }
    }

    private static byte[] Bytes(string hex)
    {
        var parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<byte>();
        foreach (var p in parts)
            if (byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out byte v)) list.Add(v);
        return list.ToArray();
    }

    private static List<Target> FindTargets(ushort vid, ushort pid)
    {
        var list = new List<Target>();
        var guid = GUID_DEVINTERFACE_HID;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero) return list;

        var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did); i++)
        {
            var di = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            IntPtr detail = Marshal.AllocHGlobal(1024);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, 1024, out _, ref di)) continue;
                string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                if (string.IsNullOrEmpty(path)) continue;

                IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                try
                {
                    var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    HidD_GetAttributes(h, ref a);
                    if (a.VendorID != vid || a.ProductID != pid) continue;

                    HIDP_CAPS caps = default;
                    if (HidD_GetPreparsedData(h, out IntPtr pre)) { HidP_GetCaps(pre, ref caps); HidD_FreePreparsedData(pre); }
                    if (caps.UsagePage < 0xFF00 || caps.OutputReportByteLength < 2 || caps.InputReportByteLength < 2) continue;

                    var buf = new byte[256];
                    string product = HidD_GetProductString(h, buf, buf.Length)
                        ? System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim() : "(无产品名)";

                    list.Add(new Target { Path = path, Product = product, Caps = caps });
                }
                finally { CloseHandle(h); }
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }

    /// <summary>发一条查询，返回 300ms 内收到的第一条非空响应。</summary>
    private static string Exchange(Target t, byte reportId, byte sub)
    {
        IntPtr h = CreateFile(t.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            var buf = new byte[Math.Max(8, (int)t.Caps.OutputReportByteLength)];
            buf[0] = reportId;
            if (buf.Length > 1) buf[1] = sub;

            var ow = new OVERLAPPED { hEvent = ev };
            if (!WriteFile(h, buf, buf.Length, IntPtr.Zero, ref ow))
            {
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            }
            if (WaitForSingleObject(ev, 400) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ow, out _, false);
            ResetEvent(ev);

            var rb = new byte[Math.Max(8, (int)t.Caps.InputReportByteLength)];
            var or = new OVERLAPPED { hEvent = ev };
            if (!ReadFile(h, rb, rb.Length, IntPtr.Zero, ref or))
            {
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            }
            if (WaitForSingleObject(ev, 500) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref or, out uint got, false);
            ResetEvent(ev);

            if (got < 2) return null;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < got; i++) sb.Append(rb[i].ToString("X2")).Append(' ');
            string s = sb.ToString().TrimEnd();
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
        finally { CloseHandle(ev); CloseHandle(h); }
    }
}
