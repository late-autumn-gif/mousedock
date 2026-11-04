using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MouseDock.Models;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// SetupAPI + hid.dll 枚举「正在运行」的 HID 顶层集合，按 VID/PID 归并成一台设备，
    /// 尝试标准电池用法（UsagePage 0x06 / Usage 0x20）与罗技 HID++，读不到就留 -1。
    /// </summary>
    public static class HidDiscovery
    {
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private const uint DN_STARTED = 0x00000008;
        private const int DETAIL_SIZE = 1024;

        private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        /// <summary>罗技 HID++ 自报的型号和电量，键是 "VID:PID"。
        /// 厂商集合（FF00）接口会在 usage 检查时被跳过，所以在那里先捞，存这儿等 ToDevice 用。</summary>
        private static readonly Dictionary<string, (int Battery, string Name, bool Charging)> _logiHints =
            new Dictionary<string, (int, string, bool)>();

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid Format; public uint Id; }

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

        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);
        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool HidD_GetProductString(IntPtr h, IntPtr buffer, int length);
        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool HidD_GetManufacturerString(IntPtr h, IntPtr buffer, int length);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumPtr, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA devInfo);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA devInfo,
            StringBuilder buffer, int size, out int required);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, uint length, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property,
            out uint registryType, byte[] buffer, ref uint length, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key,
            out uint type, byte[] buffer, ref uint length, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        public static List<DetectedDevice> Enumerate(DevicePreferences preferences = null)
        {
            LastDiagnostics.Clear();
            var buckets = new Dictionary<string, Bucket>();
            var vendorCollections = new List<(string Path, ushort Vid, ushort Pid, ushort UsagePage, string Product)>();
            int interfaces = 0, noOpen = 0, notRunning = 0, kindRejected = 0, noise = 0, collected = 0;

            IntPtr set = IntPtr.Zero;
            try
            {
                var guid = GUID_DEVINTERFACE_HID;
                set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (set == IntPtr.Zero || set == new IntPtr(-1))
                {
                    LastDiagnostics.Add("SetupDiGetClassDevs 失败");
                    return new List<DetectedDevice>();
                }

                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did); i++)
                {
                    interfaces++;
                    var devInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    IntPtr detail = Marshal.AllocHGlobal(DETAIL_SIZE);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, DETAIL_SIZE, out _, ref devInfo)) { noOpen++; continue; }
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) { noOpen++; continue; }
                        if (!IsRunning(set, ref devInfo)) { notRunning++; continue; }

                        var ancestors = ReadAncestors(devInfo.DevInst, out bool integratedUsb);
                        var verdict = TryCollect(path, ancestors, integratedUsb, buckets, preferences,
                            vendorCollections, out bool wasNoise);
                        if (verdict) collected++;
                        else if (wasNoise) noise++;
                        else kindRejected++;
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            catch (Exception ex) { LastDiagnostics.Add("异常: " + ex.GetType().Name + " " + ex.Message); }
            finally
            {
                if (set != IntPtr.Zero && set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set);
            }

            // 专用电量接口不会参与输入类型归并。按归并后的设备读取一次。
            if (!(App.Settings?.SafeMode ?? false))
            {
                foreach (var bucket in buckets.Values)
                {
                    bool inphic = VendorProtocol.IsInphicSplitReceiver(bucket.Vid, bucket.Pid);
                    bool atk = VendorProtocol.IsAtkZeroBitMouse(bucket.Vid, bucket.Pid);
                    bool rainy = VendorProtocol.IsRainy75Wired(bucket.Vid, bucket.Pid);
                    bool f99 = VendorProtocol.IsAulaF99(bucket.Vid, bucket.Pid, bucket.Product);
                    bool weikav = VendorProtocol.IdentifyWeikav(bucket.Vid, bucket.Pid, bucket.Product) != null;
                    if (bucket.Battery >= 0 || !(inphic || atk || rainy || f99 || weikav)) continue;
                    string id = $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}";
                    if (!(preferences?.CanRead(id, VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true)) continue;

                    var trace = new List<string>();
                    bool charging = false;
                    bool atkZeroIdentified = false;
                    string source = atk ? "ATK HUB BITMOUSE 电量查询"
                        : rainy ? "Rainy 75 有线电量查询"
                        : f99 ? "AULA F99 Feature 09 电量查询"
                        : weikav ? "维咖 DriveAll 设备信息查询" : "英菲克 2.4G 接收器状态上报";
                    bucket.Battery = ReadBattery(source, () =>
                    {
                        if (atk) return VendorProtocol.TryReadAtkZero(bucket.Vid, bucket.Pid, out charging, out atkZeroIdentified);
                        if (rainy) return VendorProtocol.TryReadRainy75Wired(bucket.Vid, bucket.Pid, out charging);
                        if (f99) return VendorProtocol.TryReadAulaF99(bucket.Vid, bucket.Pid);
                        if (weikav) return VendorProtocol.TryReadWeikavBattery(bucket.Vid, bucket.Pid, bucket.Product);
                        return VendorProtocol.TryReadInphic(bucket.Vid, bucket.Pid, out charging);
                    }, trace);
                    if (atkZeroIdentified) bucket.Product = bucket.Pid == 0x1154 ? "ATK ZERO" : "ATK ZERO 2.4G Receiver";
                    if (bucket.Battery >= 0)
                    {
                        bucket.Charging = charging;
                        bucket.BatterySource = source;
                    }
                    else bucket.BatteryFailures.UnionWith(trace);
                    LastDiagnostics.Add($"{id} {string.Join("；", trace)}");
                }
            }

            LastDiagnostics.Add($"接口={interfaces} 打开失败={noOpen} 未运行={notRunning} 用法不符={kindRejected} 噪音={noise} 采集={collected} 归并={buckets.Count}");

            bool safeMode = false;
            try { safeMode = App.Settings?.SafeMode ?? false; } catch { }

            // 罗技 HID++ / VGN / AJAZZ 的读数挂在厂商集合上，统一在归并后读一次。
            QueryVendorCollections(buckets, vendorCollections, safeMode, path =>
            {
                var result = LogitechHidpp.Probe(path);
                return (result.Battery, result.Name, result.Charging);
            }, ReadVendorCollection);

            // MCHOSE K87 V3：2.4G 键盘的 QHW 电量在独立厂商集合上，按整机身份读一次。
            foreach (var bucket in buckets.Values)
            {
                if (!MchoseKeyboardProtocol.IsSupportedDevice(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                var result = MchoseKeyboardProtocol.TryRead(bucket.Vid, bucket.Pid, bucket.Product, safeMode);
                if (result.Level >= 0)
                {
                    bucket.Battery = result.Level;
                    bucket.Charging = result.Charging;
                    bucket.BatterySource = "MCHOSE K87 V3 QHW 电量查询";
                }
                LastDiagnostics.Add($"MCHOSE K87 V3 QHW: battery={result.Level}, charging={result.Charging}");
            }

            return buckets.Values.Select(b =>
            {
                // RK R98Pro V2：接收器 PID 与别家共用，先验身份再读电量；连接状态也要带出来。
                var rk = RkKeyboardProtocol.TryRead(b.Vid, b.Pid, safeMode);
                if (rk != null) LastDiagnostics.Add($"RK R98Pro V2: connected={rk.Connected}, battery={rk.Level}");
                return rk ?? b.ToDevice();
            }).Where(d => d != null).ToList();
        }

        public static List<string> LastDiagnostics { get; } = new List<string>();

        private static List<(string InstanceId, string Service)> ReadAncestors(uint devInst, out bool integratedUsb)
        {
            integratedUsb = false;
            bool checkedUsb = false;
            var result = new List<(string, string)>();
            var visited = new HashSet<uint>();
            try
            {
                for (int depth = 0; depth < 16 && visited.Add(devInst); depth++)
                {
                    var id = new StringBuilder(1024);
                    CM_Get_Device_IDW(devInst, id, (uint)id.Capacity, 0);
                    var bytes = new byte[1024];
                    uint length = (uint)bytes.Length;
                    string service = CM_Get_DevNode_Registry_PropertyW(devInst, 5, out _, bytes, ref length, 0) == 0
                        ? Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(length, (uint)bytes.Length)).TrimEnd('\0')
                        : "";
                    result.Add((id.ToString(), service));
                    // Check only the actual USB device, never its upstream hub/controller.
                    if (!checkedUsb && id.ToString().StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                    {
                        checkedUsb = true;
                        if (!id.ToString().StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
                        {
                            var key = new DEVPROPKEY { Format = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 4 };
                            var value = new byte[1];
                            uint size = 1;
                            integratedUsb = CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, value, ref size, 0) == 0
                                && type == 0x11 && size == 1 && value[0] != 0;
                        }
                    }
                    if (CM_Get_Parent(out uint parent, devInst, 0) != 0) break;
                    devInst = parent;
                }
            }
            catch { /* 元数据不可读时保留未知设备，不凭名称或 VID/PID 猜测。 */ }
            return result;
        }

        internal static string ExclusionReason(IEnumerable<(string InstanceId, string Service)> ancestors, bool integratedUsb = false)
        {
            if (integratedUsb) return "内置 USB HID（Windows 本机容器标记）";
            foreach (var node in ancestors)
            {
                string id = (node.InstanceId ?? "").ToUpperInvariant();
                string service = (node.Service ?? "").ToLowerInvariant();
                if (id.StartsWith("HID\\TENCENTMYAPPSHIDBUS", StringComparison.Ordinal) ||
                    id.StartsWith("ROOT\\MYAPPSVIRTUALBUS", StringComparison.Ordinal) ||
                    service == "myappshidbus" || service == "myappsusbcontroller")
                    return "腾讯 MyApps 虚拟输入设备（PnP 证据）";
                // 在 USB / 蓝牙传输层停止。其上游控制器最终也会挂到 ACPI，不能据此排除外设。
                if (id.StartsWith("USB\\", StringComparison.Ordinal) ||
                    id.StartsWith("BTHENUM\\", StringComparison.Ordinal) ||
                    id.StartsWith("BTHLE\\", StringComparison.Ordinal) ||
                    id.StartsWith("BTHLEDEVICE\\", StringComparison.Ordinal)) return null;
                if (service == "hidi2c" || service == "i8042prt" ||
                    id.StartsWith("ACPI\\", StringComparison.Ordinal) || id.StartsWith("I2C\\", StringComparison.Ordinal))
                    return "内置 I2C/ACPI/PS2 输入设备（PnP 证据）";
                if (service == "vhf" || service == "hidinject" || service == "vmulti" ||
                    id.StartsWith("ROOT\\RDP_KBD", StringComparison.Ordinal) ||
                    id.StartsWith("ROOT\\RDP_MOU", StringComparison.Ordinal) ||
                    id.StartsWith("ROOT\\VHF", StringComparison.Ordinal) ||
                    id.StartsWith("ROOT\\VMULTI", StringComparison.Ordinal))
                    return "虚拟输入设备（PnP 证据）";
            }
            return null;
        }

        internal static string InspectExclusion(string instanceId)
        {
            if (CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != 0) return null;
            var ancestors = ReadAncestors(devInst, out bool integratedUsb);
            return ExclusionReason(ancestors, integratedUsb);
        }

        internal static bool TryDeviceKind(ushort vid, ushort pid, ushort usagePage, ushort usage, out DeviceKind kind)
        {
            if (VendorMap.TryUsbHeadset(vid, pid, out _) && usagePage == 0x0C && usage == 1)
            {
                kind = DeviceKind.Headset;
                return true;
            }
            return UsageMap.TryKind(usagePage, usage, out kind);
        }

        private static bool IsRunning(IntPtr set, ref SP_DEVINFO_DATA devInfo)
        {
            try
            {
                var sb = new StringBuilder(600);
                if (SetupDiGetDeviceInstanceId(set, ref devInfo, sb, sb.Capacity, out _))
                {
                    if (CM_Locate_DevNodeW(out uint devInst, sb.ToString(), 0) == 0 &&
                        CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) == 0)
                    {
                        return (status & DN_STARTED) != 0 && problem == 0;
                    }
                }
            }
            catch { }
            // 判定不了就当它在运行：SetupAPI 已经用 DIGCF_PRESENT 过滤过一次
            return true;
        }

        /// <summary>返回 true 表示这台设备被采集（鼠标/键盘/手柄）。</summary>
        private static bool TryCollect(string path, List<(string InstanceId, string Service)> ancestors, bool integratedUsb,
            Dictionary<string, Bucket> buckets, DevicePreferences preferences,
            List<(string Path, ushort Vid, ushort Pid, ushort UsagePage, string Product)> vendorCollections,
            out bool wasNoise)
        {
            wasNoise = false;

            // 键盘/鼠标集合常被系统独占，先用「零访问权限」打开，只做查询
            IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe == IntPtr.Zero || probe == new IntPtr(-1))
            {
                LastDiagnostics.Add($"查询打开失败 Win32={Marshal.GetLastWin32Error()} 路径={path}");
                return false;
            }

            ushort vid, pid;
            HIDP_CAPS caps = default;
            string product, vendor;

            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(probe, ref attr))
                {
                    LastDiagnostics.Add($"读取 VID/PID 失败 路径={path}");
                    return false;
                }
                vid = attr.VendorID;
                pid = attr.ProductID;

                if (HidD_GetPreparsedData(probe, out IntPtr pre))
                {
                    try
                    {
                        int status = HidP_GetCaps(pre, ref caps);
                        if (status != 0x110000)
                        {
                            LastDiagnostics.Add($"{vid:X4}:{pid:X4} 能力读取失败 NTSTATUS=0x{status:X8} 路径={path}");
                            return false;
                        }
                    }
                    finally { HidD_FreePreparsedData(pre); }
                }

                // 蓝牙 HID（BTHLE/BTHENUM）在部分驱动上调用字符串接口会直接崩，
                // 这两条链路的名字由 BluetoothDiscovery 提供，这里跳过即可
                bool overBluetooth = path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0
                                  || path.IndexOf("BTHLE", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!overBluetooth)
                {
                    product = Unicode(HidD_GetProductString, probe);
                    vendor = Unicode(HidD_GetManufacturerString, probe);
                }
                else
                {
                    product = null;
                    vendor = null;
                }
            }
            finally { CloseHandle(probe); }

            string key = $"{vid:X4}:{pid:X4}";
            string ancestry = string.Join(" <- ", ancestors.Select(n => n.InstanceId + " [" + n.Service + "]"));
            string exclusion = ExclusionReason(ancestors, integratedUsb);
            LastDiagnostics.Add($"{key} 产品={product ?? "（无产品名）"} UPage=0x{caps.UsagePage:X4} Usage=0x{caps.Usage:X4} " +
                $"in={caps.InputReportByteLength} out={caps.OutputReportByteLength} feat={caps.FeatureReportByteLength} 路径={path} PnP={ancestry}");
            if (exclusion != null)
            {
                wasNoise = true;
                LastDiagnostics.Add($"{key} 已排除: {exclusion} 路径={path}");
                return false;
            }

            bool safeMode = false;
            try { safeMode = App.Settings?.SafeMode ?? false; } catch { }
            bool recognitionEnabled = preferences?.CanRead($"hid-{vid:X4}-{pid:X4}", VendorMap.PhysicalKeyFromName(product)) ?? true;

            // 厂商集合（FF00/FF80…）不是鼠标/键盘用法，下面会被跳过。
            // 先记下这些接口，等设备本体归并好，再按整机身份统一读一次电量。
            if (recognitionEnabled && caps.UsagePage >= 0xFF00)
                vendorCollections.Add((path, vid, pid, caps.UsagePage, product));

            if (!TryDeviceKind(vid, pid, caps.UsagePage, caps.Usage, out DeviceKind kind))
            {
                LastDiagnostics.Add($"{key} usage 跳过 UPage=0x{caps.UsagePage:X2} Usage=0x{caps.Usage:X2} feat={caps.FeatureReportByteLength} 路径={path}");
                return false;
            }

            if (kind == DeviceKind.Headset && VendorMap.TryUsbHeadset(vid, pid, out string headsetName))
            {
                if (!buckets.TryGetValue(key, out var headset))
                    buckets[key] = headset = new Bucket { Vid = vid, Pid = pid, Product = headsetName, Vendor = vendor, Path = path, Kind = kind };
                headset.Counts[kind] = headset.Counts.TryGetValue(kind, out int count) ? count + 1 : 1;
                headset.BatteryFailures.Add("已识别 USB 耳机；电量协议尚未适配，当前电量未知");
                LastDiagnostics.Add($"{key} 已识别 USB 耳机，未发送未知厂商查询命令 路径={path}");
                return true;
            }

            // 仅对已知蓝牙链路的零 ID 集合去重；未知 ID 的 USB 外设仍需保留。
            if (vid == 0 && pid == 0 && (path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("BTHLE", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                wasNoise = true;
                LastDiagnostics.Add($"{key} 零 ID 蓝牙集合交给蓝牙发现链路 路径={path}");
                return false;
            }

            // 需要读电量时才申请读写权限；拿不到就保持 -1，不影响设备本身被列出
            int battery = -1;
            bool batteryCharging = false;
            bool batteryStale = false;
            string batterySource = "";
            bool mchoseQueried = false;
            var batteryTrace = new List<string>();
            if (recognitionEnabled)
            {
            if (VendorProtocol.IsAtkZeroBitMouse(vid, pid) || VendorProtocol.IsRainy75Wired(vid, pid) ||
                VendorProtocol.IsAulaF99(vid, pid, product) || VendorProtocol.IdentifyWeikav(vid, pid, product) != null)
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过专用电量查询" : "专用电量由归并后的设备读取");
            }
            else if (VendorProtocol.IsNeon75Air(vid, pid))
            {
                battery = ReadBattery("VGN Neon75 Air F7 状态查询", () => VendorProtocol.TryReadNeon75(vid, pid, !safeMode,
                    out batteryCharging, out batteryStale), batteryTrace);
                batterySource = safeMode ? "VGN 接收器上次状态（安全模式未发送查询）" : "VGN Neon75 Air 官方 F7 状态查询";
            }
            else
            {
            if (caps.FeatureReportByteLength > 1 && caps.UsagePage == 0x06 && caps.Usage == 0x20)
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    batteryTrace.Add($"标准电量接口打开失败 Win32={Marshal.GetLastWin32Error()}");
                }
                else
                {
                    try
                    {
                        var buf = new byte[caps.FeatureReportByteLength];
                        if (!HidD_GetFeature(h, buf, buf.Length))
                            batteryTrace.Add($"标准电量 GET_FEATURE 失败 Win32={Marshal.GetLastWin32Error()}");
                        else if (buf[1] > 100)
                            batteryTrace.Add($"标准电量返回值无效: {buf[1]}");
                        else battery = buf[1];
                    }
                    catch (Exception ex) { batteryTrace.Add("标准电量异常: " + ex.GetType().Name); }
                    finally { CloseHandle(h); }
                }
            }
            else batteryTrace.Add("本 HID 集合未暴露标准电池 Feature 用法");
            if (safeMode) batteryTrace.Add("安全模式已跳过需要发送命令的厂商读取器");

            // 国产 2.4G 接收器（AULA/VXE 系）：走厂商 32 字节协议读电量
            if (battery < 0 && !safeMode && VendorMap.KnownReceivers.ContainsKey(key))
            {
                battery = ReadBattery("通用 32 字节协议", () => VendorProtocol.TryReadBattery(vid, pid, out batteryCharging), batteryTrace);
                batteryStale = battery >= 0 && VendorProtocol.IdentifyStale;
            }

            // 0xFFFF 厂商集合的只读状态（VGN Neon75 Air 这类键盘接收器只在这里报电量）
            if (battery < 0 && !VendorProtocol.IsNeon75Air(vid, pid))
            {
                battery = ReadBattery("只读状态 Feature", () => VendorProtocol.TryReadStatus(vid, pid, out batteryCharging), batteryTrace);
                batteryStale = battery >= 0 && VendorProtocol.StatusStale;
            }

            // M HUB 家族（迈从 / RealTek 2.4G 接收器，e.g. 5253:1021 MCHOSE A7）
            if (battery < 0 && !safeMode)
            {
                if (VendorProtocol.IsMchoseModern(vid, pid) || (vid == 0x5253 && pid == 0x1021))
                {
                    mchoseQueried = true;
                    if (buckets.TryGetValue(key, out var previous) && previous.MchoseQueried)
                    {
                        battery = previous.Battery;
                        batteryCharging = previous.Charging;
                        batterySource = previous.BatterySource;
                        batteryTrace.Add("MCHOSE：复用本轮同一接收器的查询结果");
                    }
                    else
                    {
                        try
                        {
                            battery = VendorProtocol.TryReadMchose(vid, pid, out batteryCharging, out batterySource);
                            batteryTrace.Add(batterySource);
                        }
                        catch (Exception ex) { batteryTrace.Add("MCHOSE：" + ex.GetType().Name + " " + ex.Message); }
                    }
                }
            }

            // 第一批厂商读取器：写一帧读一帧，按 VID 分派，互不抢通道
            if (battery < 0 && !safeMode)
            {
                try
                {
                    if (vid == 0x1532) battery = ReadBattery("Razer", () => VendorProtocol.TryReadRazer(vid, pid), batteryTrace);
                    else if (vid == 0x0B05) battery = ReadBattery("ROG", () => VendorProtocol.TryReadRog(vid, pid), batteryTrace);
                    else if (vid == 0x1038) battery = ReadBattery("SteelSeries", () => VendorProtocol.TryReadSteelSeries(vid, pid), batteryTrace);
                    else if (vid == 0x361D) battery = ReadBattery("Finalmouse", () => VendorProtocol.TryReadFinalmouse(vid, pid), batteryTrace);
                    else if (vid == 0x373B || vid == 0x3554 || vid == 0x33E4)
                        battery = ReadBattery("Compx", () => VendorProtocol.TryReadCompx(vid, pid), batteryTrace);
                }
                catch { }
            }

            // 第二批：帧形已确认的厂商读取器（AUSDOM / AM Infinity / ZYNOO / AULA SC525 / RIDLAG·VGN / 雷柏）
            if (battery < 0 && !safeMode)
            {
                battery = ReadBattery("厂商 Family", () => VendorProtocol.TryReadFamily(vid, pid), batteryTrace);
            }

            // 第三批：报告 ID / 命令字是变量的，靠试探 + 强校验确定
            if (battery < 0 && !safeMode)
            {
                battery = ReadBattery("厂商 Extra", () => VendorProtocol.TryReadExtra(vid, pid), batteryTrace);
            }

            // 按接口形状认的两家（WEBBRANDS F83 / 达尔优 A980），VID 不固定，回包头必须完全对上
            if (battery < 0 && !safeMode && caps.UsagePage >= 0xFF00)
            {
                battery = ReadBattery("接口形状协议", () => VendorProtocol.TryReadByShape(vid, pid), batteryTrace);
            }

            // 第四批：G-Wolves / ipi / RAWM / passive 型（AULA SC900、雷柏 VT2 MAX）
            if (battery < 0 && !safeMode)
            {
                battery = ReadBattery("厂商 Third", () => VendorProtocol.TryReadThird(vid, pid), batteryTrace);
            }

            // 形状型老协议：WLMouse Beast X（report4 + CRC16）、Angry Miao（report F7）
            if (battery < 0 && !safeMode && caps.UsagePage >= 0xFF00)
            {
                battery = ReadBattery("Legacy 接口形状协议", () => VendorProtocol.TryReadLegacyShape(vid, pid), batteryTrace);
            }

            // 前行者（X87 三模及全系）。两套协议：
            //   CH315  16 字节 report 8，cmd 0x04，校验 0x55 - sum
            //   北赢   63 字节，cmd 0x87，校验 255 - sum%256
            // 协议抄自 hub.eweadn.cn 和 new-hub.eweadn.cn 两套网页驱动。
            if (battery < 0 && !safeMode && caps.UsagePage >= 0xFF00 && VendorProtocol.IsEweadnVid(vid))
            {
                battery = ReadBattery("Eweadn", () => VendorProtocol.TryReadEweadn(vid, pid), batteryTrace);
            }

            // VGN / AJAZZ 改在厂商集合统一读取（QueryVendorCollections），
            // 因为它们的电量接口是 FF00 厂商页，走不到这条标准用法分支。

            // 瓦尔基里（VK87 / VK99 / Valkyrie 75）。feature report 上的 F7 命令：
            //   响应 [0]=可读 [1]=键盘电量 [2]=鼠标电量 [3]=键盘在线(0) [4]=鼠标在线(0)
            // 05AC:024F 是 Apple 公版号，多家共用，只能靠产品名认。
            if (battery < 0 && !safeMode && VendorProtocol.IsValkyrieDevice(vid, pid, product))
            {
                battery = ReadBattery("Valkyrie", () => VendorProtocol.TryReadValkyrie(vid, pid, kind == DeviceKind.Keyboard), batteryTrace);
            }

            // 英菲克（Inphic）。网页驱动 HUB WEB 的本地桥 Inphic.exe 只做转发：
            // hid_read 拿到的输入报告前 5 字节拼成 "8x01"+5×%02x 推给页面，
            // 页面按 [机型][0x40|0x41][状态][电量] 取值。0x40/0x41 是电量类子命令，
            // 状态 1=百分比 2=满电 3=充电中。桥不发查询帧，电量靠接收器主动上报。
            // 协议抄自 Inphic_SetUp_V1.01.exe 装出来的本地桥（FUN_0040e3d0 等五套通道）。
            if (battery < 0 && !safeMode && caps.UsagePage >= 0xFF00 &&
                VendorProtocol.IsInphicDevice(vid, pid, product))
            {
                battery = ReadBattery("Inphic", () => VendorProtocol.TryReadInphic(vid, pid, out batteryCharging), batteryTrace);
            }

            }
            }
            else batteryTrace.Add("用户已取消识别：仅保留设备管理条目，未发起电量查询");

            if (!buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket = new Bucket { Vid = vid, Pid = pid, Product = product, Vendor = vendor, Path = path };
                buckets[key] = bucket;
            }

            string identity = null;
            try { identity = VendorProtocol.IdentityFor(vid, pid); } catch { }
            if (!string.IsNullOrEmpty(identity)) bucket.Identity = identity;

            // 记录厂商集合和接口号：无线接收器通常横跨 mi_00/01/02，有线鼠标只有一个接口
            if (caps.UsagePage >= 0xFF00) bucket.HasVendor = true;
            string miTag = InterfaceTag(path);
            if (miTag != null) bucket.Interfaces.Add(miTag);

            bucket.Counts[kind] = bucket.Counts.TryGetValue(kind, out int c) ? c + 1 : 1;

            if (UsageMap.Priority(kind) > UsageMap.Priority(bucket.Kind))
            {
                bucket.Kind = kind;
                if (!string.IsNullOrWhiteSpace(product)) bucket.Product = product;
            }
            if (!string.IsNullOrWhiteSpace(product) && string.IsNullOrWhiteSpace(bucket.Product))
                bucket.Product = product;
            if (battery > bucket.Battery)
            {
                bucket.Battery = battery;
                bucket.Charging = batteryCharging;
                bucket.ReadStale = batteryStale;
                bucket.BatterySource = batterySource;
            }
            bucket.MchoseQueried |= mchoseQueried;
            if (battery < 0) bucket.BatteryFailures.UnionWith(batteryTrace);
            LastDiagnostics.Add($"{key} 电量={(battery >= 0 ? battery + "%" : "未读到")} 读取过程={string.Join("；", batteryTrace)} 路径={path}");

            return true;
        }

        /// <summary>厂商集合上的电量读取：设备本体归并完之后，每台最多读一次。</summary>
        private static void QueryVendorCollections(Dictionary<string, Bucket> buckets,
            List<(string Path, ushort Vid, ushort Pid, ushort UsagePage, string Product)> collections,
            bool safeMode, Func<string, (int Battery, string Name, bool Charging)> probeLogitech,
            Func<string, ushort, ushort, int> readVendor)
        {
            // 上一轮读到的电量只属于上一次扫描；型号和名称留着，电量作废。
            lock (_logiHints)
            {
                foreach (var key in _logiHints.Keys.ToArray())
                    _logiHints[key] = (-1, _logiHints[key].Name, false);
            }

            var probedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queriedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var collection in collections)
            {
                if (collection.UsagePage < 0xFF00 ||
                    IsExcludedInterface(collection.Vid, collection.Pid, collection.Product, collection.Path))
                    continue;

                string key = $"{collection.Vid:X4}:{collection.Pid:X4}";
                if (!buckets.TryGetValue(key, out var bucket) ||
                    IsExcludedInterface(bucket.Vid, bucket.Pid, bucket.Product, bucket.Path))
                    continue;

                if (collection.Vid == 0x046D && collection.UsagePage == 0xFF00)
                {
                    if (!probedPaths.Add(collection.Path)) continue;
                    (int Battery, string Name, bool Charging) current;
                    lock (_logiHints)
                    {
                        if (!_logiHints.TryGetValue(key, out current)) current = (-1, null, false);
                    }
                    if (current.Battery >= 0 && !string.IsNullOrWhiteSpace(current.Name)) continue;
                    try
                    {
                        var result = probeLogitech(collection.Path);
                        bool usable = result.Battery >= 0 && result.Battery <= 100;
                        lock (_logiHints)
                            _logiHints[key] = (
                                current.Battery >= 0 ? current.Battery : usable ? result.Battery : -1,
                                !string.IsNullOrWhiteSpace(result.Name) ? result.Name : current.Name,
                                current.Battery >= 0 ? current.Charging : usable && result.Charging);
                    }
                    catch { }
                    continue;
                }

                if (safeMode || bucket.Battery >= 0 || queriedDevices.Contains(key)) continue;
                string route = null;
                if (VendorProtocol.IsVgnVid(collection.Vid)) route = "vgn";
                else if (VendorProtocol.IsAjazzVid(collection.Vid) && VendorProtocol.IsAjazzPid(collection.Pid))
                    route = "ajazz";

                // 前行者解码与形状型协议识别还没用实机验证，这里不启用。
                if (route == null) continue;
                queriedDevices.Add(key);
                try
                {
                    int level = readVendor(route, collection.Vid, collection.Pid);
                    if (level >= 0 && level <= 100) bucket.Battery = level;
                }
                catch { }
            }
        }

        private static int ReadVendorCollection(string route, ushort vid, ushort pid) => route switch
        {
            "vgn" => VendorProtocol.TryReadVgn(vid, pid),
            "ajazz" => VendorProtocol.TryReadAjazz(vid, pid),
            _ => -1
        };

        private static bool IsExcludedInterface(ushort vid, ushort pid, string product, string path)
        {
            if (IsNoise(vid, product) || (vid == 0 && pid == 0)) return true;
            string lowerPath = (path ?? "").ToLowerInvariant();
            return lowerPath.Contains("acpi\\") || lowerPath.Contains("hidi2c") ||
                lowerPath.Contains("syna") || lowerPath.Contains("isensor") ||
                lowerPath.Contains("camera") || lowerPath.Contains("vid_04f2");
        }

        private static bool IsNoise(ushort vid, string product)
        {
            if (vid == 0x06CB || vid == 0xFFAA) return true;   // 触摸板 / 腾讯虚拟 HID
            if (vid == 0x048D) return true;                    // ITE 键盘 RGB 控制器，不是独立外设
            if (string.IsNullOrWhiteSpace(product)) return false;
            string p = product.ToLowerInvariant();
            return p.Contains("virtual") || p.Contains("虚拟") || p.Contains("touchpad")
                   || p.Contains("ite device");
        }

        private static string InterfaceTag(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int i = path.ToLowerInvariant().IndexOf("mi_", StringComparison.Ordinal);
            if (i < 0 || i + 5 > path.Length) return null;
            return path.Substring(i, 5);
        }

        internal static void InvalidateDeviceHints(string id)
        {
            string[] parts = id.Split('-');
            if (parts.Length != 3 || !parts[0].Equals("hid", StringComparison.OrdinalIgnoreCase) ||
                !ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort vid) ||
                !ushort.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out ushort pid)) return;
            lock (_logiHints) _logiHints.Remove($"{vid:X4}:{pid:X4}");
        }

        private static int ReadBattery(string reader, Func<int> read, List<string> trace)
        {
            try
            {
                int value = read();
                if (value >= 0 && value <= 100)
                {
                    trace.Add($"{reader}: {value}%");
                    return value;
                }
                trace.Add(reader + ": 未返回有效电量（未匹配接口或未取得有效响应）");
            }
            catch (Exception ex) { trace.Add(reader + ": " + ex.GetType().Name + " " + ex.Message); }
            return -1;
        }

        internal static string ReadHidProduct(IntPtr handle, string path)
        {
            if (path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("BTHLE", StringComparison.OrdinalIgnoreCase)) return null;
            return Unicode(HidD_GetProductString, handle);
        }

        private static string Unicode(Func<IntPtr, IntPtr, int, bool> getter, IntPtr h)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(512);
                for (int i = 0; i < 512; i++) Marshal.WriteByte(buf, i, 0);
                if (!getter(h, buf, 512)) return null;
                string s = Marshal.PtrToStringUni(buf)?.Trim();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { return null; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        internal sealed class Bucket
        {
            public ushort Vid, Pid;
            public string Product;
            public string Vendor;
            public string Path = "";
            public string Identity = "";
            public bool HasVendor;
            public readonly HashSet<string> Interfaces = new HashSet<string>();
            public DeviceKind Kind = DeviceKind.Other;
            public int Battery = -1;
            public bool Charging;
            public bool ReadStale;
            public bool MchoseQueried;
            public string BatterySource = "";
            public readonly HashSet<string> BatteryFailures = new HashSet<string>();
            public readonly Dictionary<DeviceKind, int> Counts = new Dictionary<DeviceKind, int>();

            public DetectedDevice ToDevice()
            {
                if (Kind == DeviceKind.Other) return null;

                string clean = VendorMap.CleanName(Product, out bool dongle);
                string brand = VendorMap.Brand(Vid);
                string name = clean ?? brand ?? Product ?? Vendor;
                if (string.IsNullOrWhiteSpace(name)) name = "未知设备";

                var kind = RefineKind(name);
                string physicalKey = null;

                string weikav = VendorProtocol.IdentifyWeikav(Vid, Pid, Product)
                    ?? VendorProtocol.IdentifyWeikavVia(Vid, Pid, Product);
                // 已知接收器：显示它背后真正的外设，而不是 "Wireless-Receiver"
                if (weikav != null)
                {
                    name = "WEIKAV " + weikav;
                    kind = DeviceKind.Keyboard;
                    physicalKey = "weikav-" + weikav.Replace(" ", "").ToLowerInvariant();
                }
                else if (VendorProtocol.IsAulaF99(Vid, Pid, Product))
                {
                    name = "AULA F99";
                    kind = DeviceKind.Keyboard;
                    physicalKey = "aula-f99";
                }
                else if (VendorProtocol.IsRainy75Wired(Vid, Pid))
                {
                    name = "WOBKEY Rainy 75";
                    kind = DeviceKind.Keyboard;
                    physicalKey = "wobkey-rainy75";
                }
                else if (VendorMap.TryKnownReceiver($"{Vid:X4}:{Pid:X4}", out string knownName, out DeviceKind knownKind, out string knownKey))
                {
                    name = knownName;
                    kind = knownKind;
                    physicalKey = knownKey;
                }
                else
                {
                    // 罗技鼠标本体：接收器之外的直连/有线场景按静态 PID 表兜底
                    if (VendorMap.TryKnownMouse($"{Vid:X4}:{Pid:X4}", out string logiName)) name = logiName;
                    physicalKey = VendorMap.PhysicalKeyFromName(name);
                }

                // 罗技 HID++ 自报的型号最可信，压过接收器的占位名（「罗技无线接收器」）
                bool charging = Charging;
                if (Vid == 0x046D)
                {
                    (int Battery, string Name, bool Charging) hint;
                    bool hasHint;
                    lock (_logiHints) hasHint = _logiHints.TryGetValue($"{Vid:X4}:{Pid:X4}", out hint);
                    if (hasHint)
                    {
                        if (!string.IsNullOrWhiteSpace(hint.Name))
                        {
                            name = hint.Name;
                            physicalKey = VendorMap.PhysicalKeyFromName(name);
                        }
                        if (Battery < 0 && hint.Battery >= 0) Battery = hint.Battery;
                        charging = hint.Charging;
                    }
                }

                // 型号串优先：同一对 VID:PID 下 M137 是狼蛛 SC580、M230 是前行者 X23。
                // 没收录的型号串也不猜，直接把串显示出来，方便以后补表。
                if (VendorMap.TryIdentityModel(Identity, out string idName, out string idKey))
                {
                    name = idName;
                    physicalKey = idKey;
                }
                else if (!string.IsNullOrWhiteSpace(Identity))
                {
                    name = $"未知型号（{Identity.Trim()}）";
                    physicalKey = "oem-" + Identity.Trim().ToLowerInvariant();
                }

                bool bt = Path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0
                          || Path.IndexOf("BTHLE", StringComparison.OrdinalIgnoreCase) >= 0;

                // 判通道：接收器 / 无线产品串 → 2.4G；其余直连 USB → 有线
                bool receiverProduct = !string.IsNullOrWhiteSpace(Product) &&
                    (Product.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     Product.IndexOf("接收", StringComparison.Ordinal) >= 0);

                // 鼠标键鼠一体接收器：同一个 VID:PID 下同时挂鼠标和键盘集合，
                // 有线鼠标不会这样，用它判断 2.4G 比猜产品串靠谱
                bool peerKeyboard = Kind == DeviceKind.Mouse && Counts.ContainsKey(DeviceKind.Keyboard);

                bool usbHeadset = VendorMap.TryUsbHeadset(Vid, Pid, out string headsetName);
                if (usbHeadset) { name = headsetName; kind = DeviceKind.Headset; }
                string transport = bt ? "蓝牙" : usbHeadset ? "USB"
                    : (dongle || receiverProduct || LooksWireless(Product) || peerKeyboard ||
                       (Vid == 0x3554 && VendorProtocol.IsAulaF99(Vid, Pid, Product)) ||
                       (HasVendor && Interfaces.Count >= 2) ? "2.4G" : "有线");

                if (VendorProtocol.IsRainy75Wired(Vid, Pid)) transport = "有线";
                else if (weikav != null) transport = Pid is 0xFEF9 or 0xFEFA ? "2.4G" : "有线";

                return new DetectedDevice
                {
                    Id = $"hid-{Vid:X4}-{Pid:X4}",
                    Name = name,
                    Kind = kind,
                    Transport = transport,
                    Level = Battery,
                    Charging = charging,
                    IsStale = ReadStale,
                    Connected = true,
                    Detail = $"{Vid:X4}:{Pid:X4}",
                    PhysicalKey = physicalKey,
                    Note = Battery >= 0
                        ? BatterySource
                        : (BatteryFailures.Count > 0 ? string.Join("；", BatteryFailures)
                            : "尚未取得有效电量，请查看设备报告中的读取诊断")
                };
            }

            /// <summary>
            /// 一个接收器可能同时暴露鼠标和键盘集合。优先使用产品名，
            /// 最后按集合数量判断。
            /// </summary>
            private DeviceKind RefineKind(string name)
            {
                if (VendorProtocol.IsInphicSplitReceiver(Vid, Pid) && Counts.ContainsKey(DeviceKind.Mouse))
                    return DeviceKind.Mouse;

                string n = (name ?? "").ToLowerInvariant();
                if (n.Contains("keyboard") || n.Contains("键盘") || n.Contains("keychron")) return DeviceKind.Keyboard;

                if (Counts.ContainsKey(DeviceKind.Mouse) &&
                    System.Text.RegularExpressions.Regex.IsMatch(n, @"\bmouse\b|鼠标"))
                    return DeviceKind.Mouse;

                // 键盘型号常见写法：Neon75 / F87 / K8 / MK870 之类
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(f|k|mk|neon|kzz|akko)[\s-]?\d{2,4}\b"))
                    return DeviceKind.Keyboard;
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b\d{2,3}\s*(air|pro)?\b") &&
                    Counts.TryGetValue(DeviceKind.Keyboard, out int kc) &&
                    Counts.TryGetValue(DeviceKind.Mouse, out int mc) && kc >= mc)
                    return DeviceKind.Keyboard;

                if (Counts.Count == 0) return Kind;

                return Counts.OrderByDescending(pair => pair.Value)
                             .ThenByDescending(pair => UsageMap.Priority(pair.Key))
                             .First().Key;
            }

            private static bool LooksWireless(string name)
            {
                if (string.IsNullOrEmpty(name)) return false;
                string n = name.ToLowerInvariant();
                return n.Contains("2.4g") || n.Contains("dongle") || n.Contains("receiver") || n.Contains("wireless");
            }
        }
    }
}
