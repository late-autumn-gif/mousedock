using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 收尾探针：
/// A. 在真正的厂商通道（0xFF01 / ReportID 0x0A / 64B）上扫 0x00–0x20 的只读命令，挑出非回显的响应；
/// B. 在 0xFF02（ReportID 0 / 33B）上被动读 6 秒，看鼠标是否周期性上报状态（可能含电量）。
/// </summary>
internal static class SweepProbe
{
    private const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct OVERLAPPED { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr hEvent; }

    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr d);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr d, ref HIDP_CAPS c);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] b, int n);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr det, int size, out int req, ref SP_DEVINFO_DATA di);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sec, uint cr, uint fl, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr h, byte[] b, int n, IntPtr w, ref OVERLAPPED o);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr h, byte[] b, int n, IntPtr r, ref OVERLAPPED o);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr s, bool m, bool i, IntPtr n);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] private static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED o, out uint t, bool w);
    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private sealed class T { public string Path, Product; public HIDP_CAPS Caps; }

    /// <summary>发一条全 0 空帧（0xFF01 需要带报告 ID 0x0A），尝试让设备退出写入态。</summary>
    public static void Noop(ushort vid, ushort pid)
    {
        foreach (var t in FindTargets(vid, pid))
        {
            IntPtr h = CreateFile(t.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) { Console.WriteLine($"   {t.Caps.UsagePage:X2} 打开失败"); continue; }

            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                var buf = new byte[Math.Max(2, (int)t.Caps.OutputReportByteLength)];
                if (t.Caps.UsagePage == 0xFF01) buf[0] = 0x0A;   // 这个集合的报告 ID 是 0x0A
                // 其余全 0

                var ov = new OVERLAPPED { hEvent = ev };
                bool ok = WriteFile(h, buf, buf.Length, IntPtr.Zero, ref ov);
                if (!ok && (uint)Marshal.GetLastWin32Error() != 997) { Console.WriteLine($"   UPage 0x{t.Caps.UsagePage:X2} 写入失败 err={Marshal.GetLastWin32Error()}"); continue; }
                WaitForSingleObject(ev, 400); GetOverlappedResult(h, ref ov, out uint w, false); ResetEvent(ev);
                Console.WriteLine($"   UPage 0x{t.Caps.UsagePage:X2} 已发空帧 {buf.Length}B，写入 {w}B");

                var rb = new byte[Math.Max(2, (int)t.Caps.InputReportByteLength)];
                var or = new OVERLAPPED { hEvent = ev };
                ReadFile(h, rb, rb.Length, IntPtr.Zero, ref or);
                if (WaitForSingleObject(ev, 400) == 0)
                {
                    GetOverlappedResult(h, ref or, out uint got, false); ResetEvent(ev);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < got && i < 12; i++) sb.Append(rb[i].ToString("X2")).Append(' ');
                    Console.WriteLine($"      回应: {(got == 0 ? "(无)" : sb.ToString())}");
                }
                else { ResetEvent(ev); Console.WriteLine("      回应: (超时)"); }
            }
            finally { CloseHandle(ev); CloseHandle(h); }
        }
    }

    public static void Listen(ushort vid, ushort pid, int seconds)
    {
        foreach (var t in FindTargets(vid, pid))
        {
            Console.WriteLine($"--- 监听 {t.Product} UPage 0x{t.Caps.UsagePage:X2} IN={t.Caps.InputReportByteLength}");
            Passive(t, seconds);
        }
    }

    public static void Run(ushort vid, ushort pid)
    {
        var targets = FindTargets(vid, pid);
        var main = targets.Find(t => t.Caps.UsagePage == 0xFF01);
        var aux = targets.Find(t => t.Caps.UsagePage == 0xFF02);

        if (main != null)
        {
            Console.WriteLine($"=== A. 厂商通道扫描 {main.Product} UPage 0xFF01 | 报��ID 0x0A | {main.Caps.OutputReportByteLength}B ===");
            int sent = 0, hits = 0;
            // 已知 0A 00 00 ?? 会返回 0A 00 00 01 01 ...，说明命令是分层字节。
            // 扫 byte2（子命令）与 byte3（参数）。
            for (int b1 = 0; b1 <= 0x0C && sent < 56; b1++)
            {
                for (int b2 = 0; b2 <= 0x10 && sent < 56; b2++)
                {
                    string resp = Exchange2(main, 0x0A, (byte)b1, (byte)b2, out bool ok);
                    if (!ok) continue;
                    sent++;
                    if (resp == null) continue;

                    var by = Bytes(resp);
                    bool echo = by.Length > 2 && by[1] == b1 && by[2] == b2 && AllZeroFrom(by, 3);
                    if (echo) continue;

                    hits++;
                    var hitsPct = new List<string>();
                    for (int i = 3; i < by.Length; i++)
                        if (by[i] >= 1 && by[i] <= 100) hitsPct.Add($"[{i}]={by[i]}");
                    Console.WriteLine($"   {b1:X2} {b2:X2} -> {(by.Length > 20 ? string.Join(" ", Slice(by, 0, 20)) + " …" : resp)}" +
                                      (hitsPct.Count > 0 ? "   百分比候选: " + string.Join(" ", hitsPct) : ""));
                }
            }
            Console.WriteLine($"   发送 {sent} 条，非回显 {hits} 条。");
        }

        if (aux != null)
        {
            Console.WriteLine();
            Console.WriteLine($"=== B. 被动监听 {aux.Product} UPage 0xFF02 | 报告ID 0x00 | {aux.Caps.InputReportByteLength}B（请动鼠标）===");
            Passive(aux, 6);
        }
    }

    private static string[] Slice(byte[] b, int start, int count)
    {
        var list = new List<string>();
        for (int i = start; i < Math.Min(b.Length, start + count); i++) list.Add(b[i].ToString("X2"));
        return list.ToArray();
    }

    private static bool AllZeroFrom(byte[] b, int start)
    {
        for (int i = start; i < b.Length; i++) if (b[i] != 0) return false;
        return true;
    }

    private static byte[] Bytes(string hex)
    {
        var parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<byte>();
        foreach (var p in parts)
            if (byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out byte v)) list.Add(v);
        return list.ToArray();
    }

    private static void Passive(T t, int seconds)
    {
        IntPtr h = CreateFile(t.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            h = CreateFile(t.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }
        if (h == IntPtr.Zero || h == new IntPtr(-1)) { Console.WriteLine("   打开失败 err=" + Marshal.GetLastWin32Error()); return; }

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        var seen = new Dictionary<string, int>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        int total = 0;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var buf = new byte[Math.Max(2, (int)t.Caps.InputReportByteLength)];
                var ov = new OVERLAPPED { hEvent = ev };
                if (!ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov))
                    if ((uint)Marshal.GetLastWin32Error() != 997) break;

                if (WaitForSingleObject(ev, 700) != 0) { ResetEvent(ev); continue; }
                GetOverlappedResult(h, ref ov, out uint got, false);
                ResetEvent(ev);
                if (got < 2) continue;

                total++;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < got; i++) sb.Append(buf[i].ToString("X2")).Append(' ');
                string key = sb.ToString().TrimEnd();
                if (seen.ContainsKey(key)) { seen[key]++; continue; }
                seen[key] = 1;
                Console.WriteLine($"   [{DateTime.Now:HH:mm:ss.fff}] {(key.Length > 60 ? key.Substring(0, 60) + "…" : key)}");
            }
        }
        finally { CloseHandle(ev); CloseHandle(h); }
        Console.WriteLine($"   共 {total} 条，{seen.Count} 种。");
        if (total == 0) Console.WriteLine("   （没有被动数据：这个接口只在被查询时才有内容）");
    }

    private static string Exchange2(T t, byte rid, byte b1, byte b2, out bool ok)
    {
        ok = false;
        IntPtr h = CreateFile(t.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            var wbuf = new byte[Math.Max(4, (int)t.Caps.OutputReportByteLength)];
            wbuf[0] = rid; wbuf[1] = b1; wbuf[2] = b2;

            var ow = new OVERLAPPED { hEvent = ev };
            if (!WriteFile(h, wbuf, wbuf.Length, IntPtr.Zero, ref ow))
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, 350) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ow, out _, false);
            ResetEvent(ev);
            ok = true;

            var rbuf = new byte[Math.Max(2, (int)t.Caps.InputReportByteLength)];
            var or = new OVERLAPPED { hEvent = ev };
            if (!ReadFile(h, rbuf, rbuf.Length, IntPtr.Zero, ref or))
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, 400) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref or, out uint got, false);
            ResetEvent(ev);
            if (got < 2) return null;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < got; i++) sb.Append(rbuf[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }
        finally { CloseHandle(ev); CloseHandle(h); }
    }

    private static string Exchange(T t, byte rid, byte cmd, out bool ok)
    {
        ok = false;
        IntPtr h = CreateFile(t.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            var wbuf = new byte[Math.Max(2, (int)t.Caps.OutputReportByteLength)];
            wbuf[0] = rid; wbuf[1] = cmd;

            var ow = new OVERLAPPED { hEvent = ev };
            if (!WriteFile(h, wbuf, wbuf.Length, IntPtr.Zero, ref ow))
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, 400) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ow, out _, false);
            ResetEvent(ev);
            ok = true;

            var rbuf = new byte[Math.Max(2, (int)t.Caps.InputReportByteLength)];
            var or = new OVERLAPPED { hEvent = ev };
            if (!ReadFile(h, rbuf, rbuf.Length, IntPtr.Zero, ref or))
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, 450) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref or, out uint got, false);
            ResetEvent(ev);
            if (got < 2) return null;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < got; i++) sb.Append(rbuf[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }
        finally { CloseHandle(ev); CloseHandle(h); }
    }

    private static List<T> FindTargets(ushort vid, ushort pid)
    {
        var list = new List<T>();
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
                    if (caps.UsagePage < 0xFF00) continue;

                    var buf = new byte[256];
                    string product = HidD_GetProductString(h, buf, buf.Length)
                        ? System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim() : "(无)";
                    list.Add(new T { Path = path, Product = product, Caps = caps });
                }
                finally { CloseHandle(h); }
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }
}
