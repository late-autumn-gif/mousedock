using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 主动查询探针 v2：先读 HID 报告描述符里的 Output/Input 报告 ID，
/// 再用「合法报告 ID」发零填充查询，收集 IN 通道回应。
/// 只发短查询报文，总量上限 40 条。
/// </summary>
internal static class ActiveProbe2
{
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int HidP_Output = 1;
    private const int HidP_Input = 0;

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
    private struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage; public byte ReportID; public byte BitField;
        public int LinkCollection; public ushort LinkUsagePage, LinkUsage;
        public int IsRange, IsStringRange, IsDesignatorRange, IsAbsolute;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
        public int UnitExponent; public uint Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public int NullValue;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr hEvent; }

    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr d);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr d, ref HIDP_CAPS c);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr pre);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] b, int n);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, uint f);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr det, int size, out int req, ref SP_DEVINFO_DATA di);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sec, uint cr, uint fl, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr h, byte[] b, int n, IntPtr w, ref OVERLAPPED o);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr h, byte[] b, int n, IntPtr r, ref OVERLAPPED o);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr s, bool m, bool i, IntPtr n);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] private static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED o, out uint t, bool w);
    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private sealed class Target
    {
        public string Path, Product;
        public HIDP_CAPS Caps;
        public List<byte> OutputIds = new List<byte>();
        public List<byte> InputIds = new List<byte>();
    }

    public static void Run(ushort vid, ushort pid)
    {
        foreach (var t in FindTargets(vid, pid))
        {
            Console.WriteLine();
            Console.WriteLine($"=== {t.Product} | UPage 0x{t.Caps.UsagePage:X2} Usage 0x{t.Caps.Usage:X2} | IN={t.Caps.InputReportByteLength} OUT={t.Caps.OutputReportByteLength}");
            Console.WriteLine("    Output 报告 ID: " + (t.OutputIds.Count == 0 ? "(无)" : string.Join(",", t.OutputIds.ConvertAll(b => b.ToString("X2")))));
            Console.WriteLine("    Input  报告 ID: " + (t.InputIds.Count == 0 ? "(无)" : string.Join(",", t.InputIds.ConvertAll(b => b.ToString("X2")))));

            int sent = 0;
            var ids = t.OutputIds.Count > 0 ? t.OutputIds : new List<byte> { 0x00 };
            foreach (byte rid in ids)
            {
                foreach (byte cmd in new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x10, 0x11 })
                {
                    if (sent >= 40) break;
                    string r = Exchange(t, rid, cmd);
                    if (r == null) continue;
                    sent++;
                    bool echo = IsEcho(r, rid, cmd);
                    Console.WriteLine($"   out[{rid:X2}] cmd {cmd:X2} -> {Trim(r)}   {(echo ? "(回显)" : "★ 真实响应")}");
                    Thread.Sleep(150);
                }
                if (sent >= 40) break;
            }
            Console.WriteLine($"   共发送 {sent} 条。");
        }
    }

    private static string Trim(string hex)
    {
        var parts = hex.Split(' ');
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        foreach (var p in parts)
        {
            sb.Append(p).Append(' ');
            if (++shown >= 16) { sb.Append("…"); break; }
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsEcho(string response, byte rid, byte cmd)
    {
        // 回显特征：只有报告 ID / 命令字节非零，其余全 0
        var b = ToBytes(response);
        int nonZero = 0;
        for (int i = 1; i < b.Length; i++) if (b[i] != 0) nonZero++;
        return nonZero <= 2 && (b.Length > 1 && (b[1] == rid || b[1] == cmd || b[0] == rid));
    }

    private static byte[] ToBytes(string hex)
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
                    var outIds = new List<byte>();
                    var inIds = new List<byte>();
                    if (HidD_GetPreparsedData(h, out IntPtr pre))
                    {
                        HidP_GetCaps(pre, ref caps);
                        outIds = GetIds(pre, HidP_Output);
                        inIds = GetIds(pre, HidP_Input);
                        HidD_FreePreparsedData(pre);
                    }
                    if (caps.UsagePage < 0xFF00) continue;

                    var buf = new byte[256];
                    string product = HidD_GetProductString(h, buf, buf.Length)
                        ? System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim() : "(无产品名)";

                    list.Add(new Target { Path = path, Product = product, Caps = caps, OutputIds = outIds, InputIds = inIds });
                }
                finally { CloseHandle(h); }
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }

    private static List<byte> GetIds(IntPtr pre, int reportType)
    {
        var ids = new List<byte>();
        ushort len = 0;
        HidP_GetValueCaps(reportType, null, ref len, pre);
        if (len == 0) { ids.Add(0x00); return ids; }

        var caps = new HIDP_VALUE_CAPS[len];
        if (HidP_GetValueCaps(reportType, caps, ref len, pre) < 0) return ids;

        for (int i = 0; i < len; i++)
            if (!ids.Contains(caps[i].ReportID)) ids.Add(caps[i].ReportID);

        if (ids.Count == 0) ids.Add(0x00);
        ids.Sort();
        return ids;
    }

    private static string Exchange(Target t, byte rid, byte cmd)
    {
        IntPtr h = CreateFile(t.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;

        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            int outLen = Math.Max(2, (int)t.Caps.OutputReportByteLength);
            var wbuf = new byte[outLen];
            wbuf[0] = rid;
            if (outLen > 1) wbuf[1] = cmd;

            var ow = new OVERLAPPED { hEvent = ev };
            if (!WriteFile(h, wbuf, wbuf.Length, IntPtr.Zero, ref ow))
                if ((uint)Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, 400) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ow, out _, false);
            ResetEvent(ev);

            int inLen = Math.Max(2, (int)t.Caps.InputReportByteLength);
            var rbuf = new byte[inLen];
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
        catch { return null; }
        finally { CloseHandle(ev); CloseHandle(h); }
    }
}
