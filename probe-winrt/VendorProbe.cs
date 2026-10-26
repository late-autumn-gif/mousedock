using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static class VendorProbe
{
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

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
        public ushort UsagePage;
        public byte ReportID;
        public byte BitField;
        public int LinkCollection;
        public ushort LinkUsagePage;
        public ushort LinkUsage;
        public int IsRange;
        public int IsStringRange;
        public int IsDesignatorRange;
        public int IsAbsolute;
        public ushort UsageMin, UsageMax;
        public ushort StringMin, StringMax;
        public ushort DesignatorMin, DesignatorMax;
        public ushort DataIndexMin, DataIndexMax;
        public int UnitExponent;
        public uint Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public int NullValue;
    }

    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort length, IntPtr preparsed);
    [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);
    [DllImport("hid.dll")] private static extern bool HidD_GetInputReport(IntPtr h, byte[] buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetManufacturerString(IntPtr h, byte[] buffer, int length);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetSerialNumberString(IntPtr h, byte[] buffer, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumPtr, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    public static void Run(int knownBatteryPercent)
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

                try { Inspect(h, path, knownBatteryPercent); }
                finally { CloseHandle(h); }
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        SetupDiDestroyDeviceInfoList(set);
    }

    private static void Inspect(IntPtr h, string path, int known)
    {
        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        HidD_GetAttributes(h, ref attr);

        HIDP_CAPS caps = default;
        HidD_GetPreparsedData(h, out IntPtr pre);
        if (pre == IntPtr.Zero) return;
        try { HidP_GetCaps(pre, ref caps); }
        finally { HidD_FreePreparsedData(pre); }

        // 只关心 2.4G 接收器/鼠标的厂商集合
        if (caps.UsagePage < 0xFF00 && !(caps.UsagePage == 0x01 && caps.Usage == 0x02)) return;

        string product = Str(HidD_GetProductString, h);
        string vendor = Str(HidD_GetManufacturerString, h);
        string serial = Str(HidD_GetSerialNumberString, h);

        Console.WriteLine($"\n=== VID {attr.VendorID:X4} PID {attr.ProductID:X4} | {vendor} / {product} | SN={serial}");
        Console.WriteLine($"    UPage 0x{caps.UsagePage:X2} Usage 0x{caps.Usage:X2} | IN={caps.InputReportByteLength} OUT={caps.OutputReportByteLength} FEAT={caps.FeatureReportByteLength}");
        Console.WriteLine($"    path={System.IO.Path.GetFileName(path)}");

        if (caps.FeatureReportByteLength > 1)
        {
            Console.WriteLine("    -- 特性报告扫描（已知真值 " + known + "%）--");
            for (int id = 0; id <= 0x20; id++)
            {
                var buf = new byte[Math.Max(8, (int)caps.FeatureReportByteLength)];
                buf[0] = (byte)id;
                if (!HidD_GetFeature(h, buf, buf.Length)) continue;

                bool any = false;
                for (int k = 1; k < buf.Length; k++) if (buf[k] != 0) { any = true; break; }
                if (!any) continue;

                string hex = BitConverter.ToString(buf).Replace("-", " ");
                string mark = "";
                for (int k = 1; k < buf.Length; k++)
                    if (buf[k] == known) mark = $"  <-- 命中 {known}% 位置 [{k}]";
                Console.WriteLine($"      feature[{id:X2}] {hex}{mark}");
            }
        }

        if (caps.InputReportByteLength > 1)
        {
            Console.WriteLine("    -- 输入报告命中扫描 --");
            for (int id = 0; id <= 0x0F; id++)
            {
                var buf = new byte[Math.Max(8, (int)caps.InputReportByteLength)];
                buf[0] = (byte)id;
                if (!HidD_GetInputReport(h, buf, buf.Length)) continue;
                bool any = false;
                for (int k = 1; k < buf.Length; k++) if (buf[k] != 0) { any = true; break; }
                if (!any) continue;
                string hex = BitConverter.ToString(buf).Replace("-", " ");
                Console.WriteLine($"      input[{id:X2}] {hex}");
            }
        }
    }

    private delegate bool StringGetter(IntPtr h, byte[] buf, int len);

    private static string Str(StringGetter getter, IntPtr h)
    {
        var buf = new byte[512];
        if (!getter(h, buf, buf.Length)) return "";
        return Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
    }
}
