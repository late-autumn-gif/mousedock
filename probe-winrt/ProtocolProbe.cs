using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// 复刻官方驱动的握手：向 0xFF02（32 字节报告）依次发 0x30/0x12/0x13，
/// 然后被动收 0xD1 通知，验证我们能不能自己跟设备对话。
/// 发出的字节与 USBPcap 抓到的厂商帧逐字节一致。
/// </summary>
internal static class ProtocolProbe
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

    public static void Run(ushort vid, ushort pid)
    {
        foreach (var path in Paths(vid, pid))
        {
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
            HidD_GetAttributes(h, ref attr);
            HidD_GetPreparsedData(h, out IntPtr prep);
            var caps = new HIDP_CAPS();
            HidP_GetCaps(prep, ref caps);
            HidD_FreePreparsedData(prep);

            // 只处理 32 字节报告那个集合（OutputReportByteLength = 33：1 字节报告 ID + 32）
            if (caps.OutputReportByteLength != 33) { CloseHandle(h); continue; }

            Console.WriteLine($"=== 命中 0x{caps.UsagePage:X2} OUT={caps.OutputReportByteLength} IN={caps.InputReportByteLength} ===");
            Console.WriteLine("--- 布局 A：报告ID 前缀（cmd 在 byte1） ---");
            RunSession(h, true);
            Console.WriteLine();
            Console.WriteLine("--- 布局 B：无前缀（cmd 在 byte0） ---");
            // 布局 B 会失败，跳过
            CloseHandle(h);
            return;
        }
        Console.WriteLine("没找到 33 字节输出集合");
    }

    private static void RunSession(IntPtr h, bool prefix)
    {
        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);

        // 官方驱动启动时的三条命令，逐字节一致
        var commands = new (byte cmd, byte sub, string what)[]
        {
            (0x30, 0x00, "Identify(M137)"),
            (0x12, 0x00, "12"),
            (0x13, 0x00, "13(DPI)")
        };

        foreach (var c in commands)
        {
            var frame = new byte[33];
            int at = prefix ? 1 : 0;
            frame[at] = c.cmd;
            frame[at + 1] = c.sub;
            frame[at + 2] = 0x01;

            var ov = new NativeOverlapped { EventHandle = ev };
            bool ok = WriteFile(h, frame, frame.Length, IntPtr.Zero, ref ov);
            if (!ok && Marshal.GetLastWin32Error() != 997) { Console.WriteLine($"  写 {c.cmd:X2} 失败 err={Marshal.GetLastWin32Error()}"); continue; }
            WaitForSingleObject(ev, 500); GetOverlappedResult(h, ref ov, out _, false); ResetEvent(ev);

            Console.WriteLine($"  > {c.cmd:X2} {c.sub:X2} 01  ({c.what})");
            var resp = TryRead(h, ev, 600);
            if (resp != null) Console.WriteLine("  < " + Hex(resp));
        }

        Console.WriteLine("--- 被动收 45 秒通知 ---");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var r = TryRead(h, ev, 1500);
            if (r != null) Console.WriteLine("  < " + Hex(r));
        }

        CloseHandle(ev);
    }

    private static byte[] TryRead(IntPtr h, IntPtr ev, int ms)
    {
        var buf = new byte[64];
        var ov = new NativeOverlapped { EventHandle = ev };
        if (!ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
        if (WaitForSingleObject(ev, (uint)ms) != 0) { ResetEvent(ev); return null; }
        GetOverlappedResult(h, ref ov, out uint got, false);
        ResetEvent(ev);
        if (got == 0) return null;
        var outBuf = new byte[got];
        Array.Copy(buf, outBuf, got);
        return outBuf;
    }

    private static string Hex(byte[] b)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < b.Length && i < 36; i++) sb.Append(b[i].ToString("X2")).Append(' ');
        return sb.ToString().Trim();
    }

    private static List<string> Paths(ushort vid, ushort pid)
    {
        var list = new List<string>();
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
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
        CloseHandle(set);
        return list;
    }
}
