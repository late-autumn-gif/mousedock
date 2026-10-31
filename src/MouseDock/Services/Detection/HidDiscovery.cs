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
        private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage; public ushort UsagePage;
            public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        }

        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr templ);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        public static List<DetectedDevice> Enumerate()
        {
            LastDiagnostics.Clear();
            var buckets = new Dictionary<string, Bucket>();
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

                        int before = buckets.Count;
                        var verdict = TryCollect(path, buckets, out bool wasNoise);
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

            LastDiagnostics.Add($"接口={interfaces} 打开失败={noOpen} 未运行={notRunning} 用法不符={kindRejected} 噪音={noise} 采集={collected} 归并={buckets.Count}");

            return buckets.Values.Select(b => b.ToDevice()).Where(d => d != null).ToList();
        }

        public static List<string> LastDiagnostics { get; } = new List<string>();

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
        private static bool TryCollect(string path, Dictionary<string, Bucket> buckets, out bool wasNoise)
        {
            wasNoise = false;

            // 键盘/鼠标集合常被系统独占，先用「零访问权限」打开，只做查询
            IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe == IntPtr.Zero || probe == new IntPtr(-1)) return false;

            ushort vid, pid;
            HIDP_CAPS caps = default;
            string product, vendor;

            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(probe, ref attr)) return false;
                vid = attr.VendorID;
                pid = attr.ProductID;

                if (HidD_GetPreparsedData(probe, out IntPtr pre))
                {
                    HidP_GetCaps(pre, ref caps);
                    HidD_FreePreparsedData(pre);
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

            // 罗技 HID++ 挂在厂商集合（FF00）接口上，这些不是鼠标/键盘用法，下面会被跳过。
            // 先在这里把型号和电量捞出来，存进提示表，等 ToDevice 按 VID:PID 取。
            if (vid == 0x046D && caps.UsagePage == 0xFF00)
            {
                string hintKey = $"{vid:X4}:{pid:X4}";
                lock (_logiHints)
                {
                    _logiHints.TryGetValue(hintKey, out var prev);
                    if (prev.Battery == 0 && string.IsNullOrEmpty(prev.Name)) prev = (-1, null, false);
                    if (prev.Battery < 0 || string.IsNullOrWhiteSpace(prev.Name))
                    {
                        var lr = LogitechHidpp.Probe(path);
                        _logiHints[hintKey] = (
                            prev.Battery >= 0 ? prev.Battery : lr.Battery,
                            !string.IsNullOrWhiteSpace(prev.Name) ? prev.Name : lr.Name,
                            prev.Charging || lr.Charging);
                    }
                }
            }

            if (!UsageMap.TryKind(caps.UsagePage, caps.Usage, out DeviceKind kind))
            {
                if (LastDiagnostics.Count < 60)
                    LastDiagnostics.Add($"usage 跳过 UPage=0x{caps.UsagePage:X2} Usage=0x{caps.Usage:X2} feat={caps.FeatureReportByteLength}");
                return false;
            }

            if (IsNoise(vid, product)) { wasNoise = true; return false; }

            // 只显示外接外设：内置键盘/触摸板/摄像头一律排除
            string lowerPath = path.ToLowerInvariant();
            if (lowerPath.Contains("acpi\\") || lowerPath.Contains("hidi2c") ||
                lowerPath.Contains("syna") || lowerPath.Contains("isensor") ||
                lowerPath.Contains("camera") || lowerPath.Contains("vid_04f2"))
            {
                wasNoise = true;
                return false;
            }

            // BLE HID 集合常常读不到 VID/PID（全 0），这类交给蓝牙链路那条去报
            if (vid == 0 && pid == 0) { wasNoise = true; return false; }

            string key = $"{vid:X4}:{pid:X4}";

            // 需要读电量时才申请读写权限；拿不到就保持 -1，不影响设备本身被列出
            int battery = -1;
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != IntPtr.Zero && h != new IntPtr(-1))
            {
                try
                {
                    if (caps.FeatureReportByteLength > 1 && caps.UsagePage == 0x06 && caps.Usage == 0x20)
                    {
                        var buf = new byte[caps.FeatureReportByteLength];
                        if (HidD_GetFeature(h, buf, buf.Length) && buf[1] <= 100) battery = buf[1];
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }

            // 国产 2.4G 接收器（AULA/VXE 系）：走厂商 32 字节协议读电量
            bool safeMode = false;
            try { safeMode = App.Settings?.SafeMode ?? false; } catch { }

            if (battery < 0 && !safeMode && VendorMap.KnownReceivers.ContainsKey(key))
            {
                try { battery = VendorProtocol.TryReadBattery(vid, pid); } catch { }
            }

            // 0xFFFF 厂商集合的只读状态（VGN Neon75 Air 这类键盘接收器只在这里报电量）
            if (battery < 0)
            {
                try { battery = VendorProtocol.TryReadStatus(vid, pid); } catch { }
            }

            // M HUB 家族（迈从 / RealTek 2.4G 接收器，e.g. 5253:1021 MCHOSE A7）
            if (battery < 0)
            {
                try { battery = VendorProtocol.TryReadMchose(vid, pid); } catch { }
            }

            // 第一批厂商读取器：写一帧读一帧，按 VID 分派，互不抢通道
            if (battery < 0)
            {
                try
                {
                    if (vid == 0x1532) battery = VendorProtocol.TryReadRazer(vid, pid);
                    else if (vid == 0x0B05) battery = VendorProtocol.TryReadRog(vid, pid);
                    else if (vid == 0x1038) battery = VendorProtocol.TryReadSteelSeries(vid, pid);
                    else if (vid == 0x361D) battery = VendorProtocol.TryReadFinalmouse(vid, pid);
                    else if (vid == 0x373B || vid == 0x3554 || vid == 0x33E4)
                        battery = VendorProtocol.TryReadCompx(vid, pid);
                }
                catch { }
            }

            // 第二批：帧形已确认的厂商读取器（AUSDOM / AM Infinity / ZYNOO / AULA SC525 / RIDLAG·VGN / 雷柏）
            if (battery < 0)
            {
                try { battery = VendorProtocol.TryReadFamily(vid, pid); } catch { }
            }

            // 第三批：报告 ID / 命令字是变量的，靠试探 + 强校验确定
            if (battery < 0)
            {
                try { battery = VendorProtocol.TryReadExtra(vid, pid); } catch { }
            }

            // 按接口形状认的两家（WEBBRANDS F83 / 达尔优 A980），VID 不固定，回包头必须完全对上
            if (battery < 0 && caps.UsagePage >= 0xFF00)
            {
                try { battery = VendorProtocol.TryReadByShape(vid, pid); } catch { }
            }

            // 第四批：G-Wolves / ipi / RAWM / passive 型（AULA SC900、雷柏 VT2 MAX）
            if (battery < 0)
            {
                try { battery = VendorProtocol.TryReadThird(vid, pid); } catch { }
            }

            // 形状型老协议：WLMouse Beast X（report4 + CRC16）、Angry Miao（report F7）
            if (battery < 0 && caps.UsagePage >= 0xFF00)
            {
                try { battery = VendorProtocol.TryReadLegacyShape(vid, pid); } catch { }
            }

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
            if (battery > bucket.Battery) bucket.Battery = battery;

            return true;
        }

        private static string InterfaceTag(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int i = path.ToLowerInvariant().IndexOf("mi_", StringComparison.Ordinal);
            if (i < 0 || i + 5 > path.Length) return null;
            return path.Substring(i, 5);
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

                // 已知接收器：显示它背后真正的外设，而不是 "Wireless-Receiver"
                if (VendorMap.TryKnownReceiver($"{Vid:X4}:{Pid:X4}", out string knownName, out DeviceKind knownKind, out string knownKey))
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
                bool charging = false;
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

                string transport = bt ? "蓝牙"
                    : (dongle || receiverProduct || LooksWireless(Product) || peerKeyboard ||
                       (HasVendor && Interfaces.Count >= 2) ? "2.4G" : "有线");

                return new DetectedDevice
                {
                    Id = $"hid-{Vid:X4}-{Pid:X4}",
                    Name = name,
                    Kind = kind,
                    Transport = transport,
                    Level = Battery,
                    Charging = charging,
                    Connected = true,
                    Detail = $"{Vid:X4}:{Pid:X4}",
                    PhysicalKey = physicalKey,
                    Note = Battery >= 0
                        ? ""
                        : (transport == "2.4G"
                            ? "2.4G 走厂商 32 字节协议读取（Identify 应答里的电量字节），与官方驱动同源"
                            : "该 HID 设备没有暴露标准电池用法")
                };
            }

            /// <summary>
            /// 一个接收器可能同时暴露鼠标和键盘集合。先看名字里有没有键盘特征，
            /// 否则用「集合数量多的那种」压过默认优先级。
            /// </summary>
            private DeviceKind RefineKind(string name)
            {
                string n = (name ?? "").ToLowerInvariant();
                if (n.Contains("keyboard") || n.Contains("键盘") || n.Contains("keychron")) return DeviceKind.Keyboard;

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
