using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MouseDock.Services.Detection;

/// <summary>
/// 用户报告里「这台设备有几个接口、哪个打不开、打不开时是什么错误」以前答不全：
/// HidDiscovery 只在接口打开成功后才记录能力，MI_00 被厂商驱动独占时整份报告里
/// 连这一路接口都不存在，读报告的人只能从「缺少某条前置条件」反推。
/// 这里枚举每个 HID 接口，成功和失败都保留：失败项带 Win32 错误码、MI/COL 编号和路径。
/// </summary>
internal sealed class HidInterfaceRow
{
    public string Path = "";
    public ushort Vid;
    public ushort Pid;
    public string Interface = "";      // mi_00&col01 这类接口/集合编号
    public string Product = "";
    public string Manufacturer = "";
    public ushort UsagePage;
    public ushort Usage;
    public int Input;
    public int Output;
    public int Feature;
    public bool Opened;
    public int OpenError;
    public bool CapsKnown;

    public bool IsKeyboardCollection => CapsKnown && UsagePage == 0x0001 && Usage == 0x0006;
    public bool IsMouseCollection => CapsKnown && UsagePage == 0x0001 && Usage == 0x0002;
    public bool IsVendorCollection => CapsKnown && UsagePage >= 0xFF00;
    public bool IsStandardBattery => CapsKnown && UsagePage == 0x0006 && Usage == 0x0020;

    public string OpenText => Opened
        ? "成功"
        : "失败 Win32=" + OpenError + "（" + Win32Text(OpenError) + "）";

    public static string Win32Text(int code) => code switch
    {
        0 => "无错误",
        5 => "拒绝访问，通常被厂商驱动独占",
        2 => "文件不存在，设备已拔出",
        87 => "参数错误，报告长度或报告编号不匹配",
        1167 => "设备未连接",
        _ => "未知"
    };
}

internal static class HidInventory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, StringBuilder buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder buf, int len);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumPtr, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);

    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint FILE_SHARE_RW = 0x03;
    private const uint OPEN_EXISTING = 3;

    /// <summary>枚举全部 HID 接口；给 vid/pid 时只保留这台物理设备的所有集合（含打不开的）。</summary>
    public static List<HidInterfaceRow> Enumerate(ushort? vid = null, ushort? pid = null)
    {
        var rows = new List<HidInterfaceRow>();
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return rows;

        try
        {
            for (uint i = 0; ; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;

                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, out _, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? "";
                    if (string.IsNullOrEmpty(path)) continue;

                    ParsePath(path, out ushort pathVid, out ushort pathPid, out string iface);
                    var row = new HidInterfaceRow { Path = path, Vid = pathVid, Pid = pathPid, Interface = iface };

                    IntPtr h = CreateFile(path, 0, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1))
                    {
                        row.Opened = false;
                        row.OpenError = Marshal.GetLastWin32Error();
                    }
                    else
                    {
                        try
                        {
                            row.Opened = true;
                            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                            if (HidD_GetAttributes(h, ref attr))
                            {
                                row.Vid = attr.VendorID;
                                row.Pid = attr.ProductID;
                            }
                            var product = new StringBuilder(256);
                            if (HidD_GetProductString(h, product, product.Capacity * 2)) row.Product = product.ToString();
                            var manuf = new StringBuilder(256);
                            if (HidD_GetManufacturerString(h, manuf, manuf.Capacity * 2)) row.Manufacturer = manuf.ToString();

                            if (HidD_GetPreparsedData(h, out IntPtr prep))
                            {
                                try
                                {
                                    var caps = default(HIDP_CAPS);
                                    if (HidP_GetCaps(prep, ref caps) == 0x110000)
                                    {
                                        row.CapsKnown = true;
                                        row.UsagePage = caps.UsagePage;
                                        row.Usage = caps.Usage;
                                        row.Input = caps.InputReportByteLength;
                                        row.Output = caps.OutputReportByteLength;
                                        row.Feature = caps.FeatureReportByteLength;
                                    }
                                }
                                finally { HidD_FreePreparsedData(prep); }
                            }
                        }
                        finally { CloseHandle(h); }
                    }

                    if (vid.HasValue && row.Vid != vid.Value) continue;
                    if (pid.HasValue && row.Pid != pid.Value) continue;
                    rows.Add(row);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        rows.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return rows;
    }

    private static readonly Regex VidPid = new(@"vid_([0-9a-fA-F]{4})&pid_([0-9a-fA-F]{4})", RegexOptions.Compiled);
    private static readonly Regex MiCol = new(@"&mi_(\d+)&col(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ParsePath(string path, out ushort vid, out ushort pid, out string iface)
    {
        vid = 0;
        pid = 0;
        iface = "";
        var vp = VidPid.Match(path);
        if (vp.Success)
        {
            ushort.TryParse(vp.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid);
            ushort.TryParse(vp.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
        }
        var mc = MiCol.Match(path);
        if (mc.Success) iface = "MI_" + mc.Groups[1].Value + "/COL_" + mc.Groups[2].Value;
    }

    /// <summary>接口按 MI 分组的中文摘要，一眼能看出哪一路打不开。</summary>
    public static string Summarize(IReadOnlyList<HidInterfaceRow> rows)
    {
        if (rows.Count == 0) return "（没有枚举到接口）";
        var sb = new StringBuilder();
        var byMi = new Dictionary<string, List<HidInterfaceRow>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var r in rows)
        {
            string mi = r.Interface.Length > 0 && r.Interface.StartsWith("MI_", StringComparison.OrdinalIgnoreCase)
                ? r.Interface: "无 MI 编号";
            if (!byMi.TryGetValue(mi, out var list)) { list = new List<HidInterfaceRow>(); byMi[mi] = list; order.Add(mi); }
            list.Add(r);
        }
        foreach (string mi in order)
        {
            var list = byMi[mi];
            int fail = 0, keyboard = 0, mouse = 0, vendor = 0;
            foreach (var r in list)
            {
                if (!r.Opened) fail++;
                if (r.IsKeyboardCollection) keyboard++;
                if (r.IsMouseCollection) mouse++;
                if (r.IsVendorCollection) vendor++;
            }
            sb.Append("  ").Append(mi).Append(": ").Append(list.Count).Append(" 个接口");
            if (fail > 0) sb.Append("，").Append(fail).Append(" 个打不开");
            if (keyboard > 0) sb.Append("，键盘集合 ").Append(keyboard);
            if (mouse > 0) sb.Append("，鼠标集合 ").Append(mouse);
            if (vendor > 0) sb.Append("，厂商集合 ").Append(vendor);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>完整接口清单：每个集合的用法页、能力、打开结果与路径。</summary>
    public static string Describe(IReadOnlyList<HidInterfaceRow> rows, bool singleDevice)
    {
        var sb = new StringBuilder();
        if (rows.Count == 0)
        {
            sb.AppendLine("（没有匹配 VID:PID 的 HID 接口；蓝牙设备走 BLE/GATT）");
            return sb.ToString();
        }

        sb.AppendLine("接口摘要：");
        sb.Append(Summarize(rows));
        sb.AppendLine();
        sb.AppendLine("逐接口清单（打开失败也保留，Win32 码就是失败原因）：");
        int idx = 0;
        foreach (var r in rows)
        {
            idx++;
            string role = r.IsStandardBattery ? "标准电池"
                : r.IsMouseCollection ? "鼠标集合"
                : r.IsKeyboardCollection ? "键盘集合"
                : r.IsVendorCollection ? "厂商私有"
                : r.CapsKnown ? "其他用法" : "能力未知";
            sb.AppendLine($"  {idx:D2}. {r.Vid:X4}:{r.Pid:X4}  {r.Interface}  {role}");
            if (!string.IsNullOrWhiteSpace(r.Product)) sb.AppendLine($"      产品: {r.Product}");
            if (!string.IsNullOrWhiteSpace(r.Manufacturer)) sb.AppendLine($"      厂商: {r.Manufacturer}");
            if (r.CapsKnown)
                sb.AppendLine($"      用法页: 0x{r.UsagePage:X4}/0x{r.Usage:X4}  能力: in={r.Input} out={r.Output} feature={r.Feature}");
            else if (!r.Opened)
                sb.AppendLine("      用法页: 未知（接口打不开，读不到能力；不是设备没有这个集合）");
            sb.AppendLine($"      打开: {r.OpenText}");
            sb.AppendLine($"      路径: {r.Path}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
