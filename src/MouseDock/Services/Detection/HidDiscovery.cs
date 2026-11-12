using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MouseDock.Models;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// SetupAPI + hid.dll 枚举「正在运行」的 HID 顶层集合，按物理实例归并成一台设备，
    /// 尝试标准电池用法（UsagePage 0x06 / Usage 0x20）与罗技 HID++，读不到就留 -1。
    /// </summary>
    public static partial class HidDiscovery
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

        private static readonly object ScanGate = new();

        public static List<DetectedDevice> Enumerate(DevicePreferences preferences = null)
        {
            lock (ScanGate) return ScanDevices(preferences);
        }

        internal static List<DetectedDevice> Enumerate(DevicePreferences preferences, IEnumerable<string> selectedIds,
            out BatteryReplay.Capture capture, Dictionary<string, string> aliases = null, bool? safeMode = null)
        {
            lock (ScanGate)
            {
                var scan = new BatteryReplay.Scan(aliases, safeMode);
                var scope = selectedIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                BatteryReplay.Current = scan;
                try { return scan.Detected = ScanDevices(preferences, scope); }
                catch (Exception ex)
                {
                    scan.ScanError = ex.GetType().Name;
                    return scan.Detected;
                }
                finally
                {
                    BatteryReplay.Current = null;
                    capture = scan.Finish(selectedIds);
                }
            }
        }

        private static bool IsSafeMode => BatteryReplay.Current?.SafeMode ?? App.Settings?.SafeMode ?? false;

        private static List<DetectedDevice> ScanDevices(DevicePreferences preferences, HashSet<string> scope = null)
        {
            LastDiagnostics.Clear();
            InstanceScope.BeginScan();
            lock (_logiHints) _logiHints.Clear();
            int scanStarted = Environment.TickCount;
            var buckets = new Dictionary<string, Bucket>();
            var vendorCollections = new List<VendorCollectionInfo>();
            var pending = new List<(string Path, List<(string InstanceId, string Service)> Ancestors, bool Integrated)>();
            int interfaces = 0, noOpen = 0, notRunning = 0, kindRejected = 0, noise = 0, collected = 0;

            IntPtr set = IntPtr.Zero;
            try
            {
                var guid = GUID_DEVINTERFACE_HID;
                set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (set == IntPtr.Zero || set == new IntPtr(-1))
                {
                    if (BatteryReplay.Current != null) BatteryReplay.Current.ScanError = "SetupDiGetClassDevs";
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
                        var ancestors = ReadAncestors(devInfo.DevInst, out bool integratedUsb);
                        string instance = InstanceScope.Register(path, ancestors);
                        if (scope != null && !scope.Contains(instance)) continue;
                        if (!IsRunning(set, ref devInfo))
                        {
                            notRunning++;
                            if (BatteryReplay.Current is { } scan)
                            {
                                HidInventory.ParsePath(path, out ushort vid, out ushort pid, out _);
                                scan.AddCollection(instance, path, vid, pid, null, 0, 0, 0, 0, 0,
                                    true, status: "pnp-not-running");
                            }
                            continue;
                        }
                        pending.Add((path, ancestors, integratedUsb));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            catch (Exception ex) { LastDiagnostics.Add("异常: " + ex.GetType().Name + " " + ex.Message); }
            finally
            {
                if (set != IntPtr.Zero && set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set);
            }

            foreach (var item in pending)
            {
                var verdict = TryCollect(item.Path, item.Ancestors, item.Integrated, buckets, preferences,
                    vendorCollections, out bool wasNoise);
                if (verdict) collected++; else if (wasNoise) noise++; else kindRejected++;
            }

            foreach (var bucket in buckets.Values)
            {
                if (!VendorProtocol.IsNeon75Air(bucket.Vid, bucket.Pid)) continue;
                if (!(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true) ||
                    (IsSafeMode && BatteryReplay.Current != null))
                {
                    BatteryReplay.Current?.AddQuery(bucket.InstanceId, null, "Neon75Feature",
                        IsSafeMode ? "SafeMode" : "RecognitionDisabled", "VGN Neon75 Air");
                    continue;
                }
                using var scopeForRead = InstanceScope.Enter(bucket.InstanceId);
                bucket.Battery = VendorProtocol.TryReadNeon75(bucket.Vid, bucket.Pid, !IsSafeMode,
                    out bool charging, out bool stale);
                bucket.Charging = charging;
                bucket.ReadStale = stale;
                if (bucket.Battery >= 0) bucket.BatterySource = stale ? "VGN F7 上次状态" : "VGN F7 状态查询";
                else bucket.BatteryFailures.Add("VGN F7 未取得可靠电量，100%需独立满电证据");
            }

            // 专用电量接口不会参与输入类型归并。按归并后的设备读取一次。
            if (!IsSafeMode)
            {
                foreach (var bucket in buckets.Values)
                {
                    using var instanceScope = InstanceScope.Enter(bucket.InstanceId);
                    if (!DeviceRoutes.IsKnown(bucket.Vid, bucket.Pid, bucket.Product) ||
                        VendorProtocol.IsRogOmni(bucket.Vid, bucket.Pid)) continue;
                    if (ReferenceProfiles.Find(bucket.Vid, bucket.Pid) != null) continue;
                    if (VendorProtocol.IsReportedX23Se(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                    if (AulaBatteryProtocol.IsCandidate(bucket.Vid, bucket.Pid, bucket.Sc630Product ?? bucket.Product)) continue;
                    if (OriginalGpwProtocol.IsCandidate(bucket.Vid, bucket.Pid) || VendorProtocol.IsScopedInphic(bucket.Vid,bucket.Pid,bucket.Product) || (VendorProtocol.IsScopedModernMouse(bucket.Vid,bucket.Pid,bucket.Product) || VendorProtocol.IsScopedG7(bucket.Vid,bucket.Pid,bucket.Product)) || ZeroRainyProtocol.Candidate(bucket.Vid,bucket.Pid,bucket.Product)) continue;
                    bool inphic = VendorProtocol.IsInphicDevice(bucket.Vid, bucket.Pid, bucket.Product);
                    bool atk = VendorProtocol.IsAtkZeroBitMouse(bucket.Vid, bucket.Pid);
                    bool atkV75 = VendorProtocol.IsAtkV75X(bucket.Vid, bucket.Pid, bucket.Product);
                    bool mchoseV9 = VendorProtocol.IsMchoseV9Pro(bucket.Vid, bucket.Pid, bucket.Product);
                    bool mchoseG7 = VendorProtocol.IsMchoseG7(bucket.Vid, bucket.Pid, bucket.Product);
                    bool rapooVt7 = VendorProtocol.IsRapooVt7(bucket.Vid, bucket.Pid);
                    bool rainy = VendorProtocol.IsRainy75Candidate(bucket.Vid, bucket.Pid) &&
                        !atkV75 && !Bucket.NamesOtherSharedBrand(bucket.Product);
                    bool sc630 = VendorProtocol.IsAulaSc630(bucket.Product);
                    bool f99 = VendorProtocol.IsAulaF99(bucket.Vid, bucket.Pid, bucket.Product);
                    bool weikav = VendorProtocol.IdentifyWeikav(bucket.Vid, bucket.Pid, bucket.Product) != null;
                    bool freq = VendorProtocol.IsFreqMouse(bucket.Vid, bucket.Pid, bucket.Product);
                    if (bucket.Battery >= 0 || !(inphic || atk || atkV75 || mchoseV9 || mchoseG7 || rapooVt7 || rainy || sc630 || f99 || weikav || freq)) continue;
                    string id = bucket.InstanceId ?? $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}";
                    if (!(preferences?.CanRead(id, VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true)) continue;

                    var trace = new List<string>();
                    bool charging = false;
                    bool atkZeroIdentified = false;
                    string source = sc630 ? "狼蛛 SC630 Pro 官方电量查询" : atkV75 ? "ATK V75X 官方电量查询" : mchoseV9 ? "MCHOSE V9 Pro 官方电量查询"
                        : mchoseG7 ? "MCHOSE G7 官方电量查询" : rapooVt7 ? "雷柏 VT7 Report 7 状态推送"
                        : atk ? "ATK HUB BITMOUSE 电量查询"
                        : rainy ? (VendorProtocol.IsRainy75Wired(bucket.Vid, bucket.Pid)
                            ? "Rainy 75 有线电量查询" : "Rainy 75 2.4G 电量查询")
                        : f99 ? "AULA F99 Feature 09 电量查询"
                        : weikav ? "维咖 DriveAll 设备信息查询"
                        : freq ? "前行者 FREQ 0x30 电量查询"
                        : "英菲克 2.4G 接收器状态上报";
                    bucket.Battery = ReadBattery(source, () =>
                    {
                        if (sc630) return VendorProtocol.TryReadAulaSc630(bucket.Vid, bucket.Pid, bucket.Product, out charging);
                        if (atkV75) return VendorProtocol.TryReadAtkV75X(bucket.Vid, bucket.Pid, bucket.Product, out charging);
                        if (mchoseV9) return VendorProtocol.TryReadMchoseV9Pro(bucket.Vid, bucket.Pid, bucket.Product, out charging);
                        if (mchoseG7) return VendorProtocol.TryReadMchoseG7(bucket.Vid, bucket.Pid, bucket.Product, out charging);
                        if (rapooVt7) return VendorProtocol.TryReadRapooVt7(bucket.Vid, bucket.Pid, out charging);
                        if (atk) return VendorProtocol.TryReadAtkZero(bucket.Vid, bucket.Pid, out charging, out atkZeroIdentified);
                        if (rainy) return VendorProtocol.TryReadRainy75Wired(bucket.Vid, bucket.Pid, out charging);
                        if (f99) return VendorProtocol.TryReadAulaF99(bucket.Vid, bucket.Pid);
                        if (weikav) return VendorProtocol.TryReadWeikavBattery(bucket.Vid, bucket.Pid, bucket.Product);
                        if (freq)
                        {
                            int level = VendorProtocol.TryReadFreqMouse(bucket.Vid, bucket.Pid, bucket.Product,
                                out charging, out string freqDiagnostic);
                            if (!string.IsNullOrEmpty(freqDiagnostic)) trace.Add("前行者 FREQ：" + freqDiagnostic);
                            return level;
                        }
                        return VendorProtocol.TryReadInphic(bucket.Vid, bucket.Pid, out charging);
                    }, trace);
                    bucket.Identity = VendorProtocol.IdentityFor(bucket.Vid, bucket.Pid) ?? bucket.Identity;
                    if (atkZeroIdentified) bucket.Product = bucket.Pid == 0x1154 ? "ATK ZERO" : "ATK ZERO 2.4G Receiver";
                    if (bucket.Battery >= 0)
                    {
                        bucket.Charging = charging;
                        bucket.BatterySource = source;
                        // Only a compatible Rainy frame may rename a shared 0x5055/0x5088 PID.
                        if (rainy) bucket.RainyIdentified = true;
                        // Compatible battery responses do not establish a brand on shared receivers.
                    }
                    else bucket.BatteryFailures.UnionWith(trace);
                    LastDiagnostics.Add($"{id} {string.Join("；", trace)}");
                }
            }

            LastDiagnostics.Add($"接口={interfaces} 打开失败={noOpen} 未运行={notRunning} 用法不符={kindRejected} 噪音={noise} 采集={collected} 归并={buckets.Count}");

            bool safeMode = false;
            try { safeMode = IsSafeMode; } catch { }

            QueryReportedReceivers(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadReportedReceiver);
            QueryZg4Collections(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadZg4);
            QueryReferenceCollections(buckets, vendorCollections, safeMode, preferences,
                VendorProtocol.ReadReferenceBattery);
            QueryReportedX23SeCollections(buckets, vendorCollections, safeMode, preferences,
                VendorProtocol.ReadReportedX23Se);
            QueryM137Feature(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadM137Feature);
            QueryRealTekBattery(buckets, vendorCollections, safeMode, preferences);
            // 3554:F503 / 3554:FA09 在通用旁路之前跑，避免公版接收器协议抢走。
            QueryVgnF1(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadVgnF1);
            QueryF75(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadF75);
            QueryM9Pro(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadM9Pro);
            QueryRazerBlackShark(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadRazerBlackShark);
            QueryK87Collections(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadK87);
            QueryAulaCollections(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadAulaBattery);
            QueryScopedInphic(buckets,vendorCollections,safeMode,preferences);
            QueryGenericReceiver33(buckets, safeMode, preferences);
            QueryRogScope(buckets, vendorCollections, safeMode, preferences);
            QueryScopedModernMouse(buckets,vendorCollections,safeMode,preferences,VendorProtocol.ReadScopedModernMouse);
            QueryScopedG7(buckets,vendorCollections,safeMode,preferences,VendorProtocol.ReadScopedG7);
            if (BatteryReplay.Current == null) QueryMchoseDpi(buckets, vendorCollections, safeMode, preferences);
            QueryZeroRainy(buckets,vendorCollections,safeMode,preferences,VendorProtocol.ReadZeroRainy);
            QueryOriginalGpwCollections(buckets, vendorCollections, safeMode, preferences, VendorProtocol.ReadOriginalGpw);

            // 罗技 HID++ / VGN / AJAZZ 的读数挂在厂商集合上，统一在归并后读一次。
            QueryVendorCollections(buckets, vendorCollections, safeMode, path =>
            {
                var result = LogitechHidpp.Probe(path);
                if (result.Battery >= 0 && !string.IsNullOrEmpty(result.BatteryNote))
                    foreach (var bucket in buckets.Values)
                        if (bucket.InstanceId == InstanceScope.ForPath(path, ""))
                            bucket.BatterySource = result.BatteryNote;
                return (result.Battery, result.Name, result.Charging, result.Diagnostic);
            }, ReadVendorCollection);

            // MCHOSE K87 V3：2.4G 键盘的 QHW 电量在独立厂商集合上，按整机身份读一次。
            foreach (var bucket in buckets.Values)
            {
                using var instanceScope = InstanceScope.Enter(bucket.InstanceId);
                if (!DeviceRoutes.IsKnown(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                if (!(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true)) continue;
                if (ReferenceProfiles.Find(bucket.Vid, bucket.Pid) != null) continue;
                if (VendorProtocol.IsReportedX23Se(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                if (AulaBatteryProtocol.IsCandidate(bucket.Vid, bucket.Pid, bucket.Sc630Product ?? bucket.Product)) continue;
                if (OriginalGpwProtocol.IsCandidate(bucket.Vid, bucket.Pid) || VendorProtocol.IsScopedInphic(bucket.Vid,bucket.Pid,bucket.Product) || (VendorProtocol.IsScopedModernMouse(bucket.Vid,bucket.Pid,bucket.Product) || VendorProtocol.IsScopedG7(bucket.Vid,bucket.Pid,bucket.Product)) || ZeroRainyProtocol.Candidate(bucket.Vid,bucket.Pid,bucket.Product)) continue;
                if (buckets.Values.Count(x => x.Vid == bucket.Vid && x.Pid == bucket.Pid) > 1) continue;
                if (!MchoseKeyboardProtocol.IsSupportedDevice(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                // K87 的电量由 QueryK87Collections 走专用读取器（带物理实例限定）完成了，
                // 这里不再调用不区分实例的 legacy TryRead。
            }

            LastDiagnostics.Add($"扫描完成 用时={Environment.TickCount - scanStarted}ms 设备={buckets.Count}");

            return buckets.Values.Select(b =>
            {
                // RK R98Pro V2：接收器 PID 与别家共用，先验身份再读电量；连接状态也要带出来。
                using var instanceScope = InstanceScope.Enter(b.InstanceId);
                if (ReferenceProfiles.Find(b.Vid, b.Pid) != null) return b.ToDevice();
                if (VendorProtocol.IsReportedX23Se(b.Vid, b.Pid, b.Product)) return b.ToDevice();
                if (b.HandledBy is HandledBy.Aula or HandledBy.Receiver33 or HandledBy.Inphic) return b.ToDevice();
                if (OriginalGpwProtocol.IsCandidate(b.Vid, b.Pid) || VendorProtocol.IsScopedInphic(b.Vid,b.Pid,b.Product) || (VendorProtocol.IsScopedModernMouse(b.Vid,b.Pid,b.Product) || VendorProtocol.IsScopedG7(b.Vid,b.Pid,b.Product)) || ZeroRainyProtocol.Candidate(b.Vid,b.Pid,b.Product)) return b.ToDevice();
                var rk = ReadRkReceiver(b, buckets.Values.Count(x => x.Vid == b.Vid && x.Pid == b.Pid),
                    safeMode, preferences, RkKeyboardProtocol.TryRead);
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
                // 罗技 G HUB 会挂两条 LGHUBDEVICE 虚拟 HID（C231 鼠标 / C232 键盘），
                // 它们没有真实硬件，却带着 0x01/0x02、0x01/0x06 用法，以前会被当成两台外设占面板配额。
                if (id.StartsWith("LGHUBDEVICE\\", StringComparison.Ordinal) ||
                    service == "logi_joy_vir_hid" || service == "logi_joy_bus_enum")
                    return "罗技 G HUB 虚拟输入设备（PnP 证据）";
                // 同一套虚拟 HID 在别的主机/驱动版本上挂的是 LOGIDEVICE 容器和
                // LGVirHid / LGBusEnum 服务名（用户反馈 1.4.3 报告里的 C232 就是这种）。
                // 只认 LGHUBDEVICE 会漏掉它，让一条没有硬件的幽灵占掉面板配额。
                if (id.StartsWith("LOGIDEVICE\\", StringComparison.Ordinal) ||
                    service == "lgvirhid" || service == "lgbusenum")
                    return "罗技 G HUB 虚拟输入设备（PnP 证据）";
                // 软件创建的 HID 总线（远程/串流工具的 "Virtual Multitouch Device"）。
                if (service == "gvinput")
                    return "虚拟输入设备（软件创建的 HID 总线）";
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

        internal static bool TryDeviceKind(ushort vid, ushort pid, ushort usagePage, ushort usage, out DeviceKind kind, string product = null)
        {
            if (RazerBlackSharkProtocol.IsCandidate(vid, pid) && usagePage == 0xFF14 && usage == 1)
            { kind = DeviceKind.Headset; return true; }
            if(ZeroRainyProtocol.Candidate(vid,pid,product) && ((usagePage==0xFF05&&usage==1)||(usagePage==0xFF1C&&usage==0x92))){kind=vid==0x373B?DeviceKind.Mouse:DeviceKind.Keyboard;return true;}
            // 凌豹 M9 Pro：FF05/0001 33/33 是鼠标的专用集合。
            if (M9ProProtocol.IsCandidate(vid, pid, product) && usagePage == 0xFF05 && usage == 1)
            { kind = DeviceKind.Mouse; return true; }
            if (VendorProtocol.IsInphicSplitReceiver(vid,pid) && usagePage==0x000A && usage==0) { kind=DeviceKind.Mouse; return true; }
            if (OriginalGpwProtocol.IsCandidate(vid, pid) && usagePage == 0xFF00 &&
                (pid == 0xC539 ? usage == 2 : usage is 1 or 2))
            { kind = DeviceKind.Mouse; return true; }
            if ((vid == 0x089D && pid == 0x062F && usagePage == 0xFF02 && usage == 0) ||
                (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product) && usagePage == 0xFF01 && usage == 1))
            { kind = DeviceKind.Mouse; return true; }
            if (VendorProtocol.IsReportedX23Se(vid, pid, product) && usagePage == 0xFF01 && usage == 0x10)
            { kind = DeviceKind.Mouse; return true; }
            if (ReferenceProfiles.Match(vid, pid, usagePage, usage) != null) { kind = DeviceKind.Mouse; return true; }
            if (VendorProtocol.IsMchoseV9Pro(vid, pid, product) &&
                ((usagePage == 0xFF90 && usage == 1) || (usagePage == 0x0C && usage == 1)))
            { kind = DeviceKind.Headset; return true; }
            if (VendorMap.TryUsbHeadset(vid, pid, out _, product))
            {
                // 已确认是 USB 耳机的设备，除了消费者控制，还要把它的私有页一起认领。
                // 不认的话 0xFF13 那条电量通道会被当成噪音丢掉。
                if ((usagePage == 0x0C && usage == 1) ||
                    usagePage is 0xFF13 or 0xFFC0 or 0x01C0 or 0x170F or 0x000B ||
                    (vid == 0x3654 && usagePage >= 0xFF00))
                {
                    kind = DeviceKind.Headset;
                    return true;
                }
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
            List<VendorCollectionInfo> vendorCollections,
            out bool wasNoise)
        {
            wasNoise = false;
            string key = InstanceScope.Register(path, ancestors);

            void RecordFailure(string status, int? error = null)
            {
                if (BatteryReplay.Current is not { } scan) return;
                HidInventory.ParsePath(path, out ushort failedVid, out ushort failedPid, out _);
                scan.AddCollection(key, path, failedVid, failedPid, null, 0, 0, 0, 0, 0,
                    false, openError: error, status: status);
            }

            // 键盘/鼠标集合常被系统独占，先用「零访问权限」打开，只做查询
            IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe == IntPtr.Zero || probe == new IntPtr(-1))
            {
                int error = Marshal.GetLastWin32Error();
                RecordFailure("open-failed", error);
                LastDiagnostics.Add($"查询打开失败 Win32={error} 路径={path}");
                return false;
            }

            ushort vid, pid;
            HIDP_CAPS caps = default;
            string product, vendor;
            ushort revision;
            string reportIds = null;

            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(probe, ref attr))
                {
                    RecordFailure("attributes-unavailable");
                    LastDiagnostics.Add($"读取 VID/PID 失败 路径={path}");
                    return false;
                }
                vid = attr.VendorID;
                pid = attr.ProductID;
                revision = attr.VersionNumber;

                if (HidD_GetPreparsedData(probe, out IntPtr pre))
                {
                    try
                    {
                        int status = HidP_GetCaps(pre, ref caps);
                        if (status != 0x110000)
                        {
                            RecordFailure("capabilities-unavailable", status);
                            LastDiagnostics.Add($"{vid:X4}:{pid:X4} 能力读取失败 NTSTATUS=0x{status:X8} 路径={path}");
                            return false;
                        }
                        if (BatteryReplay.Current != null)
                        {
                            string Ids(int type, ushort values, ushort buttons) => string.Join(",",
                                VendorProtocol.ReadDescriptorReportIds(pre, type, values, buttons)
                                    .OrderBy(id => id).Select(id => id.ToString("X2")));
                            reportIds = $"IN[{Ids(0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps)}] " +
                                $"OUT[{Ids(1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps)}] " +
                                $"FEATURE[{Ids(2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps)}]";
                        }
                    }
                    finally { HidD_FreePreparsedData(pre); }
                }
                else { RecordFailure("preparsed-data-unavailable"); return false; }

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

            string legacyKey = $"{vid:X4}:{pid:X4}";
            using var instanceScope = InstanceScope.Enter(key);
            string ancestry = string.Join(" <- ", ancestors.Select(n => n.InstanceId + " [" + n.Service + "]"));
            string exclusion = ExclusionReason(ancestors, integratedUsb);
            if(IsKnownHubSwitch(vid,pid))exclusion="HM Lab扩展坞切换键HID，不是独立外设";
            if (BatteryReplay.Current is { } evidence)
            {
                bool known = DeviceRoutes.IsKnown(vid, pid, product);
                bool zg4 = VendorProtocol.IsZg4Channel(vid, pid, product, caps.UsagePage,
                    caps.InputReportByteLength, caps.OutputReportByteLength);
                bool enabled = preferences?.CanRead(key, $"hid-{vid:X4}-{pid:X4}",
                    VendorMap.PhysicalKeyFromName(product)) ?? true;
                HidInventory.ParsePath(path, out _, out _, out string label);
                bool monsGeekStatus = VendorProtocol.MatchesReportedReceiverChannel(VendorProtocol.MonsGeekStatusRoute,
                    vid, pid, product, label, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
                evidence.AddCollection(key, path, vid, pid, product, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength,
                    exclusion != null, revision, exclusion, reportIds: reportIds,
                    route: zg4 ? "Zg4" : monsGeekStatus ? VendorProtocol.MonsGeekStatusRoute : DeviceRoutes.Find(vid, pid, product)?.Reader,
                    queryPolicy: exclusion != null ? "excluded" : !enabled ? "recognition-disabled" :
                        IsSafeMode ? "safe-mode" : known || zg4 ? "known-route-only" : "unknown-no-vendor-probe");
            }
            LastDiagnostics.Add($"{key} 产品={product ?? "（无产品名）"} UPage=0x{caps.UsagePage:X4} Usage=0x{caps.Usage:X4} " +
                $"in={caps.InputReportByteLength} out={caps.OutputReportByteLength} feat={caps.FeatureReportByteLength} 路径={path} PnP={ancestry}");
            if (exclusion != null)
            {
                wasNoise = true;
                LastDiagnostics.Add($"{key} 已排除: {exclusion} 路径={path}");
                return false;
            }

            bool safeMode = false;
            try { safeMode = IsSafeMode; } catch { }
            bool recognitionEnabled = DeviceRoutes.IsKnown(vid, pid, product) && (preferences?.CanRead(key, null) ?? true) && (preferences?.CanRead($"hid-{vid:X4}-{pid:X4}", VendorMap.PhysicalKeyFromName(product)) ?? true);

            // 厂商集合（FF00/FF80…）不是鼠标/键盘用法，下面会被跳过。
            // 先记下这些接口，等设备本体归并好，再按整机身份统一读一次电量。
            if ((recognitionEnabled && (caps.UsagePage >= 0xFF00 ||
                    VendorProtocol.IsScopedInphicChannel(vid, pid, product, path, caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength))) ||
                (string.IsNullOrWhiteSpace(product) && AulaBatteryProtocol.IsSc630Collection(caps.UsagePage, caps.Usage, caps.FeatureReportByteLength)) ||
                VendorProtocol.IsInphicWakeCollection(vid, pid, product, caps.UsagePage, caps.Usage, caps.FeatureReportByteLength) ||
                VendorProtocol.IsZg4Channel(vid, pid, product, caps.UsagePage,
                    caps.InputReportByteLength, caps.OutputReportByteLength))
                vendorCollections.Add(new VendorCollectionInfo
                {
                    Path = path,
                    Vid = vid,
                    Pid = pid,
                    UsagePage = caps.UsagePage,
                    Usage = caps.Usage,
                    Input = caps.InputReportByteLength,
                    Output = caps.OutputReportByteLength,
                    Feature = caps.FeatureReportByteLength,
                    Product = product
                });

            if (!TryDeviceKind(vid, pid, caps.UsagePage, caps.Usage, out DeviceKind kind, product))
            {
                LastDiagnostics.Add($"{key} usage 跳过 UPage=0x{caps.UsagePage:X2} Usage=0x{caps.Usage:X2} feat={caps.FeatureReportByteLength} 路径={path}");
                return false;
            }

            if (kind == DeviceKind.Headset && VendorMap.TryUsbHeadset(vid, pid, out string headsetName, product))
            {
                if (!buckets.TryGetValue(key, out var headset))
                    buckets[key] = headset = new Bucket { InstanceId = key, Vid = vid, Pid = pid, Product = product, Vendor = vendor, Path = path, Kind = kind };
                headset.Counts[kind] = headset.Counts.TryGetValue(kind, out int count) ? count + 1 : 1;

                // HyperX Cloud III 走 0xFF13 私有通道，一次读完整机就行，别的集合不再重复。
                if (VendorProtocol.IsHyperXHeadset(vid, pid) && recognitionEnabled && !safeMode && !headset.HeadsetQueried && headset.Battery < 0)
                {
                    headset.HeadsetQueried = true;
                    var headsetTrace = new List<string>();
                    bool headsetCharging = false;
                    int level = ReadBattery("HyperX Cloud III 私有通道查询", () =>
                        VendorProtocol.TryReadCloud3S(vid, pid, out headsetCharging), headsetTrace);
                    if (level >= 0)
                    {
                        headset.Battery = level;
                        headset.Charging = headsetCharging;
                        headset.BatterySource = "HyperX Cloud III 私有通道查询";
                    }
                    else headset.BatteryFailures.UnionWith(headsetTrace);
                }
                else if (safeMode)
                {
                    headset.BatteryFailures.Add("安全模式已跳过耳机电量查询");
                }

                if (headset.Battery < 0 && !VendorProtocol.IsHyperXHeadset(vid, pid) &&
                    !RazerBlackSharkProtocol.IsCandidate(vid, pid) &&
                    !(vid == 0x3654 && pid == 0x5055 && VendorMap.IsZhuAudioZg4(product)))
                    headset.BatteryFailures.Add("已识别 USB 耳机；电量协议尚未适配，当前电量未知");

                LastDiagnostics.Add($"{key} 已识别 USB 耳机 电量={(headset.Battery >= 0 ? headset.Battery + "%" : "未读到")} 路径={path}");
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
            if (VendorProtocol.IsRogOmni(vid, pid))
                batteryTrace.Add("ROG Omni 由本实例的配对与键盘集合读取");
            else if (VendorProtocol.ReportedReceiverRoute(vid, pid, product) != null)
                batteryTrace.Add("由归并后的物理实例与精确 HID 集合读取电量");
            else if (RkKeyboardProtocol.IsCandidate(vid, pid))
                batteryTrace.Add(safeMode ? "安全模式已跳过 RK 查询" : "RK 由本实例的精确集合确认身份后读取，不尝试通用协议");
            else if(ZeroRainyProtocol.Candidate(vid,pid,product)){batteryTrace.Add("ZERO/Rainy由本实例专用集合读取");}
            else if((VendorProtocol.IsScopedModernMouse(vid,pid,product) || VendorProtocol.IsScopedG7(vid,pid,product))){batteryTrace.Add("迈从鼠标由本实例精确集合读取");}
            else if (VendorProtocol.IsScopedInphic(vid,pid,product)) { batteryTrace.Add("英菲克由独立实例通道读取，不走旧通用兜底"); }
            else if (OriginalGpwProtocol.IsCandidate(vid, pid))
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过罗技查询" : "罗技由本实例20字节长报文集合确认型号后查询，不走通用罗技探针");
            }
            else if (AulaBatteryProtocol.IsCandidate(vid, pid, product))
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过狼蛛专用查询" : "狼蛛专用电量由归并后的精确实例/集合读取，不走握手或形状兜底");
            }
            else if (VendorProtocol.IsReportedX23Se(vid, pid, product))
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过 X23SE 电量查询" : "X23SE 电量由本实例 MI_02 精确集合读取，不尝试通用协议");
            }
            else if (ReferenceProfiles.Find(vid, pid) != null)
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过参考协议查询" : "参考协议由归并后的实例按精确集合读取一次");
            }
            else if (VendorProtocol.IsAtkZeroBitMouse(vid, pid) || VendorProtocol.IsRainy75Candidate(vid, pid) ||
                VendorProtocol.IsAulaF99(vid, pid, product) || VendorProtocol.IdentifyWeikav(vid, pid, product) != null ||
                VendorProtocol.IsMchoseG7(vid, pid, product) || VendorProtocol.IsMchoseV9Pro(vid, pid, product) ||
                VendorProtocol.IsRapooVt7(vid, pid) || VendorProtocol.IsAulaSc630(product) ||
                VendorProtocol.IsInphicDevice(vid, pid, product))
            {
                batteryTrace.Add(safeMode ? "安全模式已跳过专用电量查询" : "专用电量由归并后的设备读取");
            }
            else if (VendorProtocol.IsNeon75Air(vid, pid))
                batteryTrace.Add("Neon75 状态由本实例的 Feature 集合读取");
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
            if (battery < 0 && !safeMode && VendorMap.KnownReceivers.ContainsKey(legacyKey))
            {
                battery = ReadBattery("通用 32 字节协议", () => VendorProtocol.TryReadBattery(vid, pid, out batteryCharging), batteryTrace);
                batteryStale = battery >= 0 && VendorProtocol.IdentifyStale;
            }

            // 0xFFFF 厂商集合的只读状态（VGN Neon75 Air 这类键盘接收器只在这里报电量）
            if (battery < 0 && !VendorProtocol.IsNeon75Air(vid, pid))
            {
                batteryTrace.Add("未确认协议：通用形状 Feature 读取已禁用");
                batteryStale = battery >= 0 && VendorProtocol.StatusStale;
            }

            // M HUB 家族（迈从 / RealTek 2.4G 接收器，e.g. 5253:1021 MCHOSE A7）
            if (battery < 0 && !safeMode)
            {
                if (VendorProtocol.IsMchoseModern(vid, pid) || VendorProtocol.IsMchoseA7V2(vid, pid) ||
                    (vid == 0x5253 && pid == 0x1021))
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

            // 通用厂商读取器：表驱动，顺序即优先级；命中就停。
            // 加品牌只改 VendorReaderTable，不动扫描主循环。
            if (battery < 0 && !safeMode)
            {
                var readerContext = new VendorReaderTable.Context
                {
                    Vid = vid, Pid = pid, Product = product, UsagePage = caps.UsagePage, Kind = kind
                };
                foreach (var reader in VendorReaderTable.Readers)
                {
                    if (battery >= 0) break;
                    try
                    {
                        if (!reader.Applies(readerContext)) continue;
                        var (level, readerCharging) = reader.Read(readerContext);
                        if (level < 0) continue;
                        battery = level;
                        batteryCharging = readerCharging;
                        batterySource = reader.Label;
                        batteryTrace.Add(reader.Label);
                    }
                    catch (Exception ex)
                    {
                        batteryTrace.Add(reader.Label + "：" + ex.GetType().Name + " " + ex.Message);
                    }
                }
            }

            }
            }
            else batteryTrace.Add("用户已取消识别：仅保留设备管理条目，未发起电量查询");

            if (!buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket = new Bucket { InstanceId = key, Vid = vid, Pid = pid, Product = product, Vendor = vendor, Path = path };
                buckets[key] = bucket;
            }

            string identity = null;
            try { if (!AulaBatteryProtocol.IsCandidate(vid, pid, product)) identity = VendorProtocol.IdentityFor(vid, pid); } catch { }
            if (!string.IsNullOrEmpty(identity)) bucket.Identity = identity;

            // 记录厂商集合和接口号：无线接收器通常横跨 mi_00/01/02，有线鼠标只有一个接口
            if (caps.UsagePage >= 0xFF00) bucket.HasVendor = true;
            string miTag = InterfaceTag(path);
            if (miTag != null) bucket.Interfaces.Add(miTag);

            if (vid == 0x0B05 && kind == DeviceKind.Keyboard)
                bucket.RogScopeModel ??= pid switch
                {
                    0x1AB5 => VendorProtocol.RogScopeRxWired,
                    0x1B78 => VendorProtocol.RogScopeRxWireless,
                    _ => VendorProtocol.RogScopeName(product)
                };
            bucket.ReferenceModel ??= ReferenceProfiles.Match(vid, pid, caps.UsagePage, caps.Usage);
            if (VendorProtocol.IsAulaSc630(product)) bucket.Sc630Product = product;
            bucket.AulaDescriptorRoute ??= AulaBatteryProtocol.Match(vid, pid, product, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
            bucket.ReportedX23SeIdentified |= VendorProtocol.IsReportedX23SeCollection(vid, pid, product, path,
                caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
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

        internal static void QueryOriginalGpwCollections(Dictionary<string, Bucket> buckets,
            List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
            Func<string, ushort, ushort, ReferenceBatteryProtocol.Reading> query)
        {
            foreach (var b in buckets.Values)
            {
                if (!OriginalGpwProtocol.IsCandidate(b.Vid,b.Pid) || AulaBatteryProtocol.IsCandidate(b.Vid,b.Pid,b.Product)) continue;
                b.HandledBy = HandledBy.OriginalGpw; b.GpwLive = false;
                b.GpwModel = b.Pid == 0xC088 ? OriginalGpwProtocol.Model : null;
                b.Battery = -1; b.Charging = false; b.ReadStale = false; b.BatterySource = ""; b.Identity = "";
                string legacy = $"hid-{b.Vid:X4}-{b.Pid:X4}";
                var channels = collections.Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                    InstanceScope.ForPath(c.Path, legacy) == b.InstanceId &&
                    OriginalGpwProtocol.IsChannel(c.Vid,c.Pid,c.UsagePage,c.Usage,c.Input,c.Output,c.Feature))
                    .GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
                string reason = safeMode ? "安全模式未查询罗技接收器" :
                    !(preferences?.CanRead(b.InstanceId,null) ?? true) ||
                    !(preferences?.CanRead(legacy,VendorMap.PhysicalKeyFromName(b.Product)) ?? true) ? "此实例识别已停用" :
                    string.IsNullOrWhiteSpace(b.InstanceId) ? "没有物理实例标识，未查询" :
                    channels.Count != 1 ? $"本实例精确HID++长报文集合={channels.Count}，必须唯一；不查询7字节短报文/32字节DFU集合" : null;
                if (reason != null) { b.BatteryFailures.Add(reason); LastDiagnostics.Add($"{b.InstanceId} GPW边界：{reason}"); continue; }
                ReferenceBatteryProtocol.Reading result;
                using (InstanceScope.Enter(b.InstanceId))
                {
                    try { result = query(channels[0].Path,b.Vid,b.Pid); }
                    catch(Exception ex) { result=ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name+": "+ex.Message); }
                }
                result ??= ReferenceBatteryProtocol.Reading.Failed("罗技读取器没有返回结果");
                bool g603 = b.Pid == 0xC539 && result.Model == OriginalGpwProtocol.G603Model &&
                    OriginalGpwProtocol.IsG603Name(result.Identity);
                if (g603 || (b.Pid==0xC547 ? result.Model=="Logitech G PRO X SUPERLIGHT" && OriginalGpwProtocol.IsSuperlightName(result.Identity) : result.Model == OriginalGpwProtocol.Model))
                {
                    b.GpwModel = result.Model;
                    b.GpwLive = g603 || (b.Pid==0xC547 ? OriginalGpwProtocol.IsSuperlightName(result.Identity) : OriginalGpwProtocol.IsIdentityEvidence(result.Identity));
                    if (result.Level is >=0 and <=100 && result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging or ReferenceBatteryProtocol.State.Full)
                    {
                        b.Battery=result.Level; b.Charging=result.ChargingKnown && result.Charging; b.GpwLive=true;
                        b.ChargeFull = result.Status == ReferenceBatteryProtocol.State.Full;
                        if (g603) b.Charging = false;
                        b.BatterySource=(g603 ? "G603：" : "GPW：")+result.Diagnostic;
                    }
                }
                string reader = g603 ? "G603" : "GPW";
                if(b.Battery<0)b.BatteryFailures.Add(reader+"："+result.Diagnostic);
                LastDiagnostics.Add($"{b.InstanceId} {reader}专用 状态={result.Status} 型号={result.Model} 电量={result.Level} {result.Diagnostic}");
            }
        }

        internal static void QueryAulaCollections(Dictionary<string, Bucket> buckets,
            List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
            Func<AulaBatteryProtocol.Route, string, ushort, ushort, string, ReferenceBatteryProtocol.Reading> query)
        {
            foreach (var b in buckets.Values)
            {
                string product = b.Sc630Product ?? b.Product;
                if (!AulaBatteryProtocol.IsCandidate(b.Vid, b.Pid, product)) continue;
                b.HandledBy = HandledBy.Aula;
                b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";
                b.AulaModel = null; b.AulaOnline = false;
                string legacy = $"hid-{b.Vid:X4}-{b.Pid:X4}";
                var instanceCollections = collections.Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                    InstanceScope.ForPath(c.Path, "") == b.InstanceId).ToList();
                var channels = instanceCollections.Where(c =>
                    AulaBatteryProtocol.Match(c.Vid, c.Pid, AulaBatteryProtocol.CollectionProduct(c.Product, product), c.UsagePage, c.Usage, c.Input, c.Output, c.Feature) != null)
                    .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                if (b.Vid == 0x089D && b.Pid == 0x062F && channels.Count != 1)
                {
                    string shapes = string.Join(", ", instanceCollections.Select(c =>
                        $"{c.UsagePage:X4}/{c.Usage:X4} I/O/F={c.Input}/{c.Output}/{c.Feature}").Distinct());
                    LastDiagnostics.Add($"{b.InstanceId} 089D:062F 同实例 HID 集合：{(shapes.Length == 0 ? "无" : shapes)}");
                }
                if (channels.Count == 1)
                {
                    var c = channels[0];
                    b.AulaDescriptorRoute = AulaBatteryProtocol.Match(c.Vid, c.Pid, AulaBatteryProtocol.CollectionProduct(c.Product, product), c.UsagePage, c.Usage, c.Input, c.Output, c.Feature);
                }
                if (VendorProtocol.IsAulaSc630(product)) b.AulaModel = "AULA SC630 Pro";
                string reason = safeMode ? "安全模式已跳过狼蛛电量查询" :
                    !(preferences?.CanRead(b.InstanceId, null) ?? true) ||
                    !(preferences?.CanRead(legacy, VendorMap.PhysicalKeyFromName(b.Product)) ?? true) ? "此实例识别已停用" :
                    string.IsNullOrWhiteSpace(b.InstanceId) ? "缺少物理实例，未发送查询" :
                    channels.Count != 1 ? $"本实例精确电量集合数量={channels.Count}，必须唯一，未发送查询" : null;
                if (reason != null)
                {
                    BatteryReplay.Current?.AddQuery(b.InstanceId, null,
                        VendorProtocol.IsAulaSc630(product) ? "Sc630Pro" : "Sc580Receiver",
                        safeMode ? "safe-mode" : channels.Count != 1 ? "collection-missing-or-ambiguous" : "recognition-disabled",
                        b.AulaModel);
                    b.BatteryFailures.Add(reason);
                    LastDiagnostics.Add($"{b.InstanceId} 狼蛛边界：{reason}");
                    continue;
                }
                var channel = channels[0];
                var scan = BatteryReplay.Current;
                var trace = scan == null ? null : new BatteryReplay.Trace();
                if (scan != null) scan.Active = trace;
                ReferenceBatteryProtocol.Reading result;
                using (InstanceScope.Enter(b.InstanceId))
                {
                    try { result = query(b.AulaDescriptorRoute.Value, channel.Path, b.Vid, b.Pid, product); }
                    catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name + ": " + ex.Message); }
                }
                result ??= ReferenceBatteryProtocol.Reading.Failed("专用读取器没有返回结果");
                if (b.AulaDescriptorRoute == AulaBatteryProtocol.Route.Sc580Receiver &&
                    result.Status == ReferenceBatteryProtocol.State.Online && result.Identity is "M137" or "M230")
                {
                    b.AulaModel = result.Model;
                    b.AulaOnline = true;
                }
                if (result.Level is >= 0 and <= 100 && result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging &&
                    (b.AulaDescriptorRoute != AulaBatteryProtocol.Route.Sc580Receiver || result.Identity is "M137" or "M230"))
                {
                    b.Battery = result.Level; b.Charging = result.ChargingKnown && result.Charging;
                    b.AulaModel = result.Model; b.AulaOnline = true;
                    b.BatterySource = "狼蛛专用：" + result.Diagnostic;
                }
                else b.BatteryFailures.Add("狼蛛专用：" + result.Diagnostic);
                scan?.AddQuery(b.InstanceId, channel.Path, b.AulaDescriptorRoute.ToString(),
                    result.Status.ToString(), b.AulaModel, b.Battery, trace);
                LastDiagnostics.Add($"{b.InstanceId} 狼蛛路线={b.AulaDescriptorRoute} 状态={result.Status} 电量={result.Level} 型号={result.Model} {result.Diagnostic}");
            }
        }

        internal static void QueryReportedX23SeCollections(Dictionary<string, Bucket> buckets,
            List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
            Func<string, ReferenceBatteryProtocol.Reading> query)
        {
            foreach (var bucket in buckets.Values)
            {
                if (!VendorProtocol.IsReportedX23Se(bucket.Vid, bucket.Pid, bucket.Product)) continue;
                string legacy = $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}";
                string instance = bucket.InstanceId ?? legacy;
                bucket.Battery = -1; bucket.Charging = false; bucket.ReadStale = false; bucket.BatterySource = "";
                var channel = collections.Where(c => InstanceScope.ForPath(c.Path, legacy) == instance &&
                    VendorProtocol.IsReportedX23SeCollection(c.Vid, c.Pid, c.Product, c.Path, c.UsagePage,
                        c.Usage, c.Input, c.Output, c.Feature)).OrderBy(c => c.Path, StringComparer.Ordinal).FirstOrDefault();
                bucket.ReportedX23SeIdentified = channel != null;
                string reason = safeMode ? "安全模式已跳过 X23SE 电量查询" :
                    !(preferences?.CanRead(instance, legacy) ?? true) ? "用户已停用此实例的识别" :
                    channel == null ? "缺少本实例的 EWEADN X23SE MI_02 FF01/0010 65/65/0 集合，未发送查询" : null;
                if (reason != null)
                {
                    bucket.BatteryFailures.Add(reason);
                    LastDiagnostics.Add($"{instance} X23SE 边界：{reason}");
                    continue;
                }
                using var scope = InstanceScope.Enter(bucket.InstanceId);
                ReferenceBatteryProtocol.Reading result;
                try { result = query(channel.Path); }
                catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name); }
                result ??= ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
                if (result.Level is >= 0 and <= 100 &&
                    result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
                {
                    bucket.Battery = result.Level;
                    bucket.Charging = result.ChargingKnown && result.Charging;
                    bucket.BatterySource = "X23SE：" + result.Diagnostic;
                }
                else bucket.BatteryFailures.Add("X23SE：" + result.Diagnostic);
                LastDiagnostics.Add($"{instance} X23SE 状态={result.Status} 电量={result.Level} {result.Diagnostic}");
            }
        }

        /// <summary>厂商集合上的电量读取：设备本体归并完之后，每台最多读一次。</summary>
        internal static void QueryReferenceCollections(Dictionary<string, Bucket> buckets,
            List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
            Func<ReferenceProfiles.Profile, string, ReferenceBatteryProtocol.Reading> query)
        {
            foreach (var bucket in buckets.Values)
            {
                var profile = ReferenceProfiles.Find(bucket.Vid, bucket.Pid);
                if (AulaBatteryProtocol.IsCandidate(bucket.Vid, bucket.Pid, bucket.Sc630Product ?? bucket.Product)) continue;
                if (OriginalGpwProtocol.IsCandidate(bucket.Vid,bucket.Pid)) continue;
                if (profile == null) continue;
                string legacyId = $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}";
                string instance = bucket.InstanceId ?? legacyId;
                if (safeMode || !(preferences?.CanRead(instance, null) ?? true) ||
                    !(preferences?.CanRead(legacyId, VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true))
                {
                    bucket.BatteryFailures.Add(safeMode ? "安全模式已跳过参考协议查询" : "此实例的识别已停用");
                    continue;
                }
                var channel = collections.Where(c => c.Vid == bucket.Vid && c.Pid == bucket.Pid &&
                    InstanceScope.ForPath(c.Path, legacyId) == instance &&
                    ReferenceProfiles.AcceptsChannel(profile, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature))
                    .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (channel == null)
                {
                    bucket.BatteryFailures.Add($"{profile.Model}：没有满足精确用法与报告长度的电量集合，未发送命令");
                    continue;
                }
                using var scope = InstanceScope.Enter(bucket.InstanceId);
                ReferenceBatteryProtocol.Reading result;
                try { result = query(profile, channel.Path); }
                catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name); }
                if (result == null) result = ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
                if (result.Level is >= 0 and <= 100 &&
                    result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging or ReferenceBatteryProtocol.State.Full)
                {
                    bucket.Battery = result.Level;
                    bucket.Charging = result.ChargingKnown && result.Charging;
                    bucket.ChargeFull = result.Status == ReferenceBatteryProtocol.State.Full;
                    bucket.ReadStale = false;
                    bucket.BatterySource = $"{profile.Model} 电量：{result.Diagnostic}";
                    bucket.ReferenceIdentity = result.Identity;
                    bucket.ReferenceIdentityModel = result.Model;
                }
                else bucket.BatteryFailures.Add($"{profile.Model}：{result.Diagnostic}");
                LastDiagnostics.Add($"{instance} 参考配置={profile.Model} 协议={profile.Battery} " +
                    $"状态={result.Status} 电量={result.Level} 充电已知={result.ChargingKnown} {result.Diagnostic}");
            }
        }

        private static void QueryVendorCollections(Dictionary<string, Bucket> buckets,
            List<VendorCollectionInfo> collections, bool safeMode,
            Func<string, (int Battery, string Name, bool Charging, string Diagnostic)> probeLogitech,
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

            // 罗技 HID++ 单独一段：一个接收器最多试两条接口，整体限时。
            // 1.4.3 就是在这里卡住的：C539 的三条 FF00 集合逐条同步写，其中一次写不返回，
            // 整轮扫描（含后面的设备列表）就永远出不来。
            var logiPaths = collections
                .Where(c => c.Vid == 0x046D && c.UsagePage == 0xFF00 &&
                            !AulaBatteryProtocol.IsCandidate(c.Vid, c.Pid, c.Product) &&
                            !OriginalGpwProtocol.IsCandidate(c.Vid,c.Pid) &&
                            !IsExcludedInterface(c.Vid, c.Pid, c.Product, c.Path))
                .GroupBy(c => InstanceScope.ForPath(c.Path, $"{c.Vid:X4}:{c.Pid:X4}"), StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                                  .Select(x => x.First())
                                  .OrderByDescending(LogiInterfaceRank)
                                  .ThenBy(c => c.Path, StringComparer.Ordinal)
                                  .Take(2))
                .ToList();

            if (logiPaths.Count > 0)
                LastDiagnostics.Add($"罗技 HID++ 探针 候选={logiPaths.Count} 安全模式={(safeMode ? "开" : "关")}");

            int logiStarted = Environment.TickCount;
            foreach (var collection in logiPaths)
            {
                if (safeMode) break;
                if (!DeviceRoutes.IsKnown(collection.Vid, collection.Pid, collection.Product)) continue;
                string key = InstanceScope.ForPath(collection.Path, $"{collection.Vid:X4}:{collection.Pid:X4}");
                using var instanceScope = InstanceScope.Enter(key);
                if (!buckets.TryGetValue(key, out var bucket) ||
                    IsExcludedInterface(bucket.Vid, bucket.Pid, bucket.Product, bucket.Path)) continue;

                (int Battery, string Name, bool Charging) current;
                lock (_logiHints)
                {
                    if (!_logiHints.TryGetValue(key, out current)) current = (-1, null, false);
                }
                if (current.Battery >= 0 && !string.IsNullOrWhiteSpace(current.Name)) continue;
                if (Environment.TickCount - logiStarted > 4500)
                {
                    LastDiagnostics.Add($"{key} 罗技 HID++ 探针总时长超限（{Environment.TickCount - logiStarted}ms），跳过剩余接口");
                    break;
                }
                if (!probedPaths.Add(collection.Path)) continue;

                int started = Environment.TickCount;
                try
                {
                    var result = probeLogitech(collection.Path);
                    bool usable = result.Battery >= 0 && result.Battery <= 100;
                    lock (_logiHints)
                        _logiHints[key] = (
                            current.Battery >= 0 ? current.Battery : usable ? result.Battery : -1,
                            !string.IsNullOrWhiteSpace(result.Name) ? result.Name : current.Name,
                            current.Battery >= 0 ? current.Charging : usable && result.Charging);
                    LastDiagnostics.Add($"{key} HID++ 探针 路径={collection.Path} 用时={Environment.TickCount - started}ms " +
                        $"名称={result.Name ?? "（无）"} 电量={(usable ? result.Battery + "%" : "未读到")} {result.Diagnostic}");
                    // 同一接收器只认第一个报出名字的接口；后面的接口通常是同一条通道的别名。
                    if (!string.IsNullOrWhiteSpace(result.Name)) continue;
                }
                catch (Exception ex)
                {
                    LastDiagnostics.Add($"{key} HID++ 探针异常 {ex.GetType().Name} 路径={collection.Path}");
                }
            }

            foreach (var collection in collections)
            {
                if (ReferenceProfiles.Find(collection.Vid, collection.Pid) != null ||
                    VendorProtocol.IsReportedX23Se(collection.Vid, collection.Pid, collection.Product) ||
                    AulaBatteryProtocol.IsCandidate(collection.Vid, collection.Pid, collection.Product) ||
                    OriginalGpwProtocol.IsCandidate(collection.Vid,collection.Pid) || VendorProtocol.IsScopedInphic(collection.Vid,collection.Pid,collection.Product) || (VendorProtocol.IsScopedModernMouse(collection.Vid,collection.Pid,collection.Product) || VendorProtocol.IsScopedG7(collection.Vid,collection.Pid,collection.Product)) || ZeroRainyProtocol.Candidate(collection.Vid,collection.Pid,collection.Product) ||
                    !DeviceRoutes.IsKnown(collection.Vid, collection.Pid, collection.Product) || collection.UsagePage < 0xFF00 ||
                    IsExcludedInterface(collection.Vid, collection.Pid, collection.Product, collection.Path))
                    continue;

                string key = InstanceScope.ForPath(collection.Path, $"{collection.Vid:X4}:{collection.Pid:X4}");
                using var instanceScope = InstanceScope.Enter(key);
                if (!buckets.TryGetValue(key, out var bucket) ||
                    IsExcludedInterface(bucket.Vid, bucket.Pid, bucket.Product, bucket.Path))
                    continue;

                // 罗技已经在上面单独处理过。
                if (collection.Vid == 0x046D && collection.UsagePage == 0xFF00) continue;

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

        /// <summary>HID++ 长报文（20 字节）接口优先，其次 7 字节短报文；32 字节那类不是 HID++ 通道。</summary>
        internal static int LogiInterfaceRank(VendorCollectionInfo collection)
        {
            if (collection.Input == 20 || collection.Output == 20 ||
                (collection.Feature >= 20 && collection.Feature <= 21)) return 3;
            if (collection.Input == 7 || collection.Output == 7) return 2;
            return 1;
        }

        private static int ReadVendorCollection(string route, ushort vid, ushort pid) => route switch
        {
            "vgn" => VendorProtocol.TryReadVgn(vid, pid),
            "ajazz" => VendorProtocol.TryReadAjazz(vid, pid),
            _ => -1
        };

        private static bool IsExcludedInterface(ushort vid, ushort pid, string product, string path)
        {
            if(IsKnownHubSwitch(vid,pid))return true; // User-reported HM Lab USB hub switch HID.
            if (IsNoise(vid, product) || (vid == 0 && pid == 0)) return true;
            string lowerPath = (path ?? "").ToLowerInvariant();
            return lowerPath.Contains("acpi\\") || lowerPath.Contains("hidi2c") ||
                lowerPath.Contains("syna") || lowerPath.Contains("isensor") ||
                lowerPath.Contains("camera") || lowerPath.Contains("vid_04f2");
        }

        internal static bool IsKnownHubSwitch(ushort vid,ushort pid)=>vid==0xC019&&pid==0x0401;
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
            if (id.StartsWith("instance-", StringComparison.OrdinalIgnoreCase))
            { lock (_logiHints) _logiHints.Remove(id); VendorProtocol.ForgetInstance(id); return; }
            string[] parts = id.Split('-');
            if (parts.Length != 3 || !parts[0].Equals("hid", StringComparison.OrdinalIgnoreCase) ||
                !ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort vid) ||
                !ushort.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out ushort pid)) return;
            lock (_logiHints) _logiHints.Remove($"{vid:X4}:{pid:X4}");
        }

        private static int ReadBattery(string reader, Func<int> read, List<string> trace)
        {
            void Record(string status, int level = -1)
            {
                if (InstanceScope.Current is { } instance)
                    BatteryReplay.Current?.AddQuery(instance, null, reader, status, null, level);
            }
            try
            {
                int value = read();
                if (value >= 0 && value <= 100)
                {
                    trace.Add($"{reader}: {value}%");
                    Record("ReaderReturnedValue", value);
                    return value;
                }
                Record("NoValidValue");
                trace.Add(reader + ": 未返回有效电量（未匹配接口或未取得有效响应）");
            }
            catch (Exception ex)
            {
                Record(ex.GetType().Name);
                trace.Add(reader + ": " + ex.GetType().Name + " " + ex.Message);
            }
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

        /// <summary>
        /// 一个厂商私有 HID 集合。除了路径和 VID:PID，还带上报文长度：
        /// 罗技的 HID++ 只挂在 20 字节（长报文）或 7 字节（短报文）通道上，
        /// 32 字节那类不是 HID++，先读它等于白等超时。
        /// </summary>
        internal sealed class VendorCollectionInfo
        {
            public string Path;
            public ushort Vid;
            public ushort Pid;
            public ushort UsagePage;
            public ushort Usage;
            public ushort Input;
            public ushort Output;
            public ushort Feature;
            public string Product;
        }

        internal sealed class Bucket
        {
            public ushort Vid, Pid;
            public string Product;
            public string Vendor;

            /// <summary>
            /// 320F:5088 是 Rainy 75、ATK V75X、VGN V98 Pro 共用的号。
            /// 产品名里已经写明别家型号时，不能因为应答结构对得上就改叫 WOB Rainy 75。
            /// </summary>
            internal static bool NamesOtherSharedBrand(string product)
            {
                if (string.IsNullOrWhiteSpace(product)) return false;
                return product.IndexOf("ATK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("VGN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("VXE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("V75", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("V98", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("V87", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("DELUX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       product.IndexOf("多彩", StringComparison.Ordinal) >= 0;
            }
            public string Path = "";
            public string ReferenceModel;
            public string ReferenceIdentity;
            public string ReferenceIdentityModel;
            public bool ReportedX23SeIdentified;
            public bool AulaOnline;
            public AulaBatteryProtocol.Route? AulaDescriptorRoute;
            public string AulaModel, Sc630Product;
            /// <summary>248A:5B2F 官方 GetInfo 返回的型号名与身份串。</summary>
            public string M137Model;
            /// <summary>MCHOSE 官方 0x12 0x67 读到的 DPI；-1 表示未知。</summary>
            public int Dpi = -1;
            public int[] DpiSlots;
            public string DpiText;
            public string RogScopeModel;
            public string ZeroRainyModel;
            /// <summary>VGN 蜻蜓 F1 MOBA 的身份确认结果（CID/MID 命中才置位）。</summary>
            public string VgnF1Model;
            /// <summary>狼蛛 AULA F75 的身份确认结果（0300000000CD）。</summary>
            public string F75Model;
            /// <summary>凌豹 M9 Pro 的被动 Input9 确认结果。</summary>
            public string M9ProModel;
            public string ModernModel;
            public string G7Model;
            public bool GpwLive;
            public string GpwModel;
            /// <summary>最终接管这台设备的读取器；None 表示没有专用读取器处理。</summary>
            public HandledBy HandledBy;
            public string InstanceId;
            public string Identity = "";
            public bool HasVendor;
            public readonly HashSet<string> Interfaces = new HashSet<string>();
            public DeviceKind Kind = DeviceKind.Other;
            public int Battery = -1;
            public bool Charging;
            public bool ChargeFull;
            public bool ReadStale;
            public bool MchoseQueried;
            public bool HeadsetQueried;
            public bool RainyIdentified;
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
                if (ReferenceModel != null) { name = ReferenceModel; kind = DeviceKind.Mouse; }
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
                else if (VendorProtocol.IsMchoseA7V2(Vid, Pid))
                {
                    string model = VendorProtocol.MchoseA7V2ModelName(Pid);
                    name = model.StartsWith("MCHOSE", StringComparison.OrdinalIgnoreCase)
                        ? "迈从" + model.Substring("MCHOSE".Length)
                        : model;
                    kind = DeviceKind.Mouse;
                    physicalKey = Pid == 0x100A ? "mchose-a7v2pro" : "mchose-a7v2ultra";
                }
                else if (VendorProtocol.IsGenericReceiver33(Vid, Pid, Product))
                {
                    // 公版 33 字节接收器（狼蛛/英菲克鼠标接收器）的鼠标集合常被厂商驱动
                    // 独占（CreateFile Win32=5），HID 侧只剩键盘/消费集合。类型不能按
                    // 剩余集合计数判，否则一只鼠标会被显示成键盘。
                    name = !string.IsNullOrWhiteSpace(AulaModel)
                        ? AulaModel
                        : VendorProtocol.Receiver33FallbackName(Vid, null);
                    kind = DeviceKind.Mouse;
                    physicalKey = Vid == 0x089D ? "aula-receiver33" : "inphic-receiver33";
                }
                else if (VendorProtocol.IsMchoseG7(Vid, Pid, Product))
                {
                    name = Product.Trim();
                    if (name.StartsWith("MCHOSE", StringComparison.OrdinalIgnoreCase))
                        name = "迈从" + name.Substring("MCHOSE".Length);
                    kind = DeviceKind.Mouse;
                    physicalKey = "mchose-g7";
                }
                else if (VendorProtocol.IsMchoseV9Pro(Vid, Pid, Product))
                { name = "迈从 V9 Pro"; kind = DeviceKind.Headset; physicalKey = "mchose-v9pro"; }
                else if (VendorProtocol.IsRapooVt7(Vid, Pid))
                { name = "雷柏 VT7"; kind = DeviceKind.Mouse; physicalKey = "rapoo-vt7"; }
                else if (VendorProtocol.IsAulaF99(Vid, Pid, Product))
                {
                    name = "AULA F99";
                    kind = DeviceKind.Keyboard;
                    physicalKey = "aula-f99";
                }
                else if (VendorProtocol.IsAtkV75X(Vid, Pid, Product))
                {
                    name = "ATK V75X";
                    kind = DeviceKind.Keyboard;
                    physicalKey = "atk-v75x";
                }
                else if (VendorProtocol.IsRainy75Candidate(Vid, Pid) &&
                         VendorProtocol.IsRainy75Name(Product))
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
                if (Vid == 0x046D && HandledBy != HandledBy.OriginalGpw)
                {
                    (int Battery, string Name, bool Charging) hint;
                    bool hasHint;
                    lock (_logiHints) hasHint = _logiHints.TryGetValue(InstanceId ?? $"{Vid:X4}:{Pid:X4}", out hint);
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
                    kind = DeviceKind.Mouse;
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

                bool usbHeadset = VendorMap.TryUsbHeadset(Vid, Pid, out string headsetName, Product);
                if (usbHeadset) { name = (Vid == 0x4C4A ? Product : headsetName) ?? "USB 音频设备（型号未确认）"; kind = DeviceKind.Headset; }
                bool f99Wired = VendorProtocol.IsAulaF99Wired(Vid, Pid, Product);
                string transport = bt ? "蓝牙" : usbHeadset ? "USB"
                    : (dongle || receiverProduct || LooksWireless(Product) || peerKeyboard ||
                       (Vid == 0x3554 && VendorProtocol.IsAulaF99(Vid, Pid, Product) && !f99Wired) ||
                       (HasVendor && Interfaces.Count >= 2) ? "2.4G" : "有线");

                // 路由表给出的通道优先于产品串推断；后面的品牌分支仍可覆盖。
                var route = DeviceRoutes.Find(Vid, Pid, Product);
                if (route?.Transport != null) transport = route.Transport;
                bool zg4Receiver = !bt && Vid == 0x3654 && Pid == 0x5055 && VendorMap.IsZhuAudioZg4(Identity);
                if (zg4Receiver) transport = "2.4G";

                if (VendorProtocol.IsMchoseV9Pro(Vid, Pid, Product)) transport = "2.4G";
                if (VendorProtocol.IsMchoseA7V2(Vid, Pid)) transport = "2.4G";
                if (VendorProtocol.IsMchoseG7(Vid, Pid, Product)) transport = Vid == 0xA8A4 ? "有线" : "2.4G";
                if (VendorProtocol.IsRapooVt7(Vid, Pid)) transport = Pid == 0x1413 ? "2.4G" : "有线";
                if (VendorProtocol.IsAtkV75X(Vid, Pid, Product)) transport = Pid == 0x5055 ? "有线" : "2.4G";
                else if (VendorProtocol.IsRainy75Wired(Vid, Pid)) transport = "有线";
                else if (VendorProtocol.IsRainy75Wireless(Vid, Pid)) transport = "2.4G";
                else if (f99Wired) transport = "有线";
                else if (weikav != null) transport = Pid is 0xFEF9 or 0xFEFA ? "2.4G" : "有线";

                if (ReferenceModel != null)
                {
                    name = ReferenceIdentity != null
                        ? ReferenceIdentityModel ?? $"ATK 鼠标（CID/MID={ReferenceIdentity}，型号未收录）"
                        : ReferenceModel;
                    kind = DeviceKind.Mouse;
                    transport = "2.4G";
                }
                if (ReportedX23SeIdentified) { name = Product.Trim(); kind = DeviceKind.Mouse; transport = "2.4G"; }
                if (HandledBy == HandledBy.M137)
                {
                    // 248A:5B2F 的身份由官方 GetInfo 决定，读不到型号就保持"接收器"。
                    name = M137Model ?? "248A:5B2F 接收器";
                    kind = DeviceKind.Mouse;
                    transport = "2.4G";
                    charging = false;
                }
                if (HandledBy == HandledBy.RealTek)
                {
                    // 凌豹 M9 Pro：电量由 RealTek 0x11 0x06 读取，名称用产品串。
                    name = string.IsNullOrWhiteSpace(Product) ? "凌豹 2.4G 鼠标" : Product.Trim();
                    kind = DeviceKind.Mouse;
                    transport = "2.4G";
                    charging = Battery >= 0 && Charging;
                }
                if ((HandledBy is HandledBy.Aula or HandledBy.Receiver33 or HandledBy.Inphic))
                {
                    // AulaModel 已经是显示名（"狼蛛 SC580" / "英菲克 J315"）；
                    // 只有没读到身份帧时才用号段兜底，不能把 "Wireless-Receiver" 当名字。
                    name = !string.IsNullOrWhiteSpace(AulaModel)
                        ? AulaModel
                        : VendorProtocol.IsGenericReceiver33(Vid, Pid, Product)
                            ? VendorProtocol.Receiver33FallbackName(Vid, null)
                            : (string.IsNullOrWhiteSpace(Product) ? "未确认型号的接收器" : Product.Trim());
                    kind = DeviceKind.Mouse;
                    transport = VendorProtocol.IsGenericReceiver33(Vid, Pid, Product) ||
                                (Vid == 0x089D && Pid == 0x062F) || (HandledBy == HandledBy.Inphic && (Vid==0x1D57 || Pid==0x5C2F)) ? "2.4G" : "USB";
                    charging = Battery >= 0 && AulaOnline && Charging;
                }
                if(HandledBy == HandledBy.ZeroRainy){name=ZeroRainyModel ?? Product ?? "未确认型号的设备";kind=VendorProtocol.IsAtkZeroBitMouse(Vid,Pid)?DeviceKind.Mouse:DeviceKind.Keyboard;transport=Pid is 0x1154 or 0x5055?"有线":"2.4G";charging=Battery>=0&&Charging;}
                if(HandledBy == HandledBy.MchoseG7){name=G7Model ?? Product ?? "未确认型号的接收器";kind=DeviceKind.Mouse;transport=Vid==0xA8A4?"有线":"2.4G";charging=Battery>=0 && Charging;}
                if (HandledBy == HandledBy.OriginalGpw)
                {
                    name=GpwModel ?? "罗技 Lightspeed 接收器（型号未确认）"; kind=DeviceKind.Mouse;
                    transport=Pid==0xC539 ? "2.4G" : "有线";
                    charging=Battery>=0 && GpwLive && Charging;
                }

                if(HandledBy == HandledBy.MchoseModern){name=ModernModel??Product;kind=DeviceKind.Mouse;transport=VendorProtocol.ModernMouseProfiles.TryGetValue((Product??"").Trim(),out var profile)&&Pid==profile.Wired?"有线":"2.4G";}
                // VGN 蜻蜓 F1 MOBA / AULA F75：只有身份帧命中才用确认型号，否则保留原始产品串。
                if(HandledBy == HandledBy.VgnF1){name=VgnF1Model ?? Product ?? "未确认型号的接收器";kind=DeviceKind.Mouse;transport="2.4G";charging=Battery>=0 && Charging;}
                if(HandledBy == HandledBy.AulaF75){name=F75Model ?? Product ?? "未确认型号的接收器";kind=DeviceKind.Keyboard;transport="2.4G";charging=Battery>=0 && Charging;}
                if(HandledBy == HandledBy.M9Pro){name=M9ProModel ?? Product ?? "未确认型号的设备";kind=DeviceKind.Mouse;transport="2.4G";charging=Battery>=0 && Charging;}
                if (HandledBy == HandledBy.RogOmniMouse)
                {
                    name = RogOmniMouseProtocol.Model;
                    kind = DeviceKind.Mouse;
                    transport = "2.4G";
                    charging = Battery >= 0 && Charging;
                }
                if (HandledBy == HandledBy.RazerBlackShark)
                {
                    name = RazerBlackSharkProtocol.Model;
                    kind = DeviceKind.Headset;
                    charging = Battery >= 0 && Charging;
                }
                bool unresolved=kind==DeviceKind.Other && IsUnresolved8KReceiver();
                if(unresolved)name=Product.Trim();
                if (HandledBy == HandledBy.RogScope || RogScopeModel != null)
                {
                    name = RogScopeModel ?? (HandledBy == HandledBy.RogScope ? VendorProtocol.RogScopeRxWireless : null) ?? name;
                    kind = DeviceKind.Keyboard;
                    transport = HandledBy == HandledBy.RogScope ? "2.4G" : "有线";
                }
                return new DetectedDevice
                {
                    Id = InstanceId ?? $"hid-{Vid:X4}-{Pid:X4}",
                    RawName = Product ?? "",
                    ConfirmedModel = HandledBy == HandledBy.MchoseA7Ultra ? (Battery >= 0 ? VendorProtocol.A7UltraName : "") : HandledBy == HandledBy.RazerBlackShark ? RazerBlackSharkProtocol.Model : HandledBy == HandledBy.RogOmniMouse ? RogOmniMouseProtocol.Model : HandledBy == HandledBy.M137 ? M137Model ?? "" : HandledBy == HandledBy.RealTek ? Product?.Trim() ?? "" : HandledBy == HandledBy.VgnF1 ? VgnF1Model ?? "" : HandledBy == HandledBy.AulaF75 ? F75Model ?? "" : HandledBy == HandledBy.M9Pro ? M9ProModel ?? "" : RogScopeModel ?? (HandledBy == HandledBy.RogScope ? VendorProtocol.RogScopeRxWireless : null) ?? (HandledBy == HandledBy.MchoseModern ? ModernModel??"" : unresolved ? "" : HandledBy == HandledBy.ZeroRainy ? ZeroRainyModel ?? "" : HandledBy == HandledBy.MchoseG7 ? G7Model ?? "" : HandledBy == HandledBy.OriginalGpw ? GpwModel ?? "" : (HandledBy is HandledBy.Aula or HandledBy.Receiver33 or HandledBy.Inphic) ? AulaModel ?? "" : ReportedX23SeIdentified ? Product.Trim() : ReferenceIdentity != null ? ReferenceIdentityModel ?? ""
                        : ReferenceModel ?? (!string.IsNullOrEmpty(Identity) && !ReadStale ? name : "")),
                    InterfaceDiscovered = true,
                    ReceiverDiscovered = HandledBy is HandledBy.MchoseA7Ultra or HandledBy.MonsGeekBatteryEvent or HandledBy.MonsGeekDongleStatus || zg4Receiver || (HandledBy == HandledBy.RazerBlackShark && Pid == RazerBlackSharkProtocol.WirelessPid) || HandledBy is HandledBy.RogScope or HandledBy.RogOmniMouse || HandledBy == HandledBy.M9Pro || (HandledBy == HandledBy.M137 ? true : HandledBy == HandledBy.MchoseModern ? VendorProtocol.ModernMouseProfiles.TryGetValue((Product??"").Trim(),out var mp)&&mp.Receivers.Contains(Pid) : unresolved ? true : HandledBy == HandledBy.ZeroRainy ? Pid is 0x1155 or 0x124F or 0x5088 : HandledBy == HandledBy.MchoseG7 ? Vid==0x3837 && G7Model!=null : HandledBy == HandledBy.OriginalGpw ? Pid==0xC539 : (HandledBy is HandledBy.Aula or HandledBy.Receiver33 or HandledBy.Inphic) ? (Vid == 0x089D && Pid == 0x062F) || (HandledBy == HandledBy.Inphic && (Vid==0x1D57 || Pid==0x5C2F)) : ReportedX23SeIdentified || ReferenceModel != null || LooksWireless(Product)),
                    PeripheralConfirmed = HandledBy == HandledBy.M9Pro ? Battery >= 0 && !ReadStale : HandledBy == HandledBy.M137 ? false : HandledBy == HandledBy.RealTek ? Battery >= 0 && !ReadStale : HandledBy == HandledBy.MchoseModern ? false : unresolved ? false : HandledBy == HandledBy.ZeroRainy ? false : HandledBy == HandledBy.MchoseG7 ? false : HandledBy == HandledBy.OriginalGpw ? GpwLive : (HandledBy is HandledBy.Aula or HandledBy.Receiver33 or HandledBy.Inphic) ? AulaOnline : !ReportedX23SeIdentified && Battery >= 0 && !ReadStale,
                    Name = name,
                    Kind = kind,
                    Transport = transport,
                    Level = Battery,
                    Dpi = Dpi,
                    DpiSlots = DpiSlots,
                    DpiText = DpiText ?? "",
                    Charging = charging,
                    ChargeFull = ChargeFull,
                    IsStale = ReadStale,
                    Connected = true,
                    Detail = $"{Vid:X4}:{Pid:X4}",
                    PhysicalKey = InstanceId,
                    HandledBy = HandledBy,
                    Note = HandledBy is HandledBy.RogScope or HandledBy.RogOmniMouse ? BatterySource :
                        RogScopeModel == VendorProtocol.RogScopeRxWired ? "有线键盘，无电池" : unresolved ? "已发现8K复合接收器，外设类型与型号尚未确认；不按辅助接口数量定型" : Battery < 0 &&
                        !(Vid == 0x3654 && Pid == 0x5055 && VendorMap.IsZhuAudioZg4(Product)) &&
                        !DeviceRoutes.IsKnown(Vid, Pid, Product) ? "候选设备：型号与电量协议尚未确认，未发送厂商命令" : Battery >= 0
                        ? BatterySource
                        : (BatteryFailures.Count > 0 ? string.Join("；", BatteryFailures)
                            : "尚未取得有效电量，请查看设备报告中的读取诊断")
                };
            }

            /// <summary>
            /// 明确键盘特征优先。只有鼠标和键盘集合的接收器保留主功能优先级，
            /// 辅助键盘集合数量不能证明它是一把键盘；其它复合设备沿用计数兜底。
            /// </summary>
            private DeviceKind RefineKind(string name)
            {
                // 路由表优先：已知读取器直接给出类型。集合计数只留给表里没有的
                // 未知设备兜底，厂商驱动独占鼠标集合不会再把鼠标判成键盘。
                var route = DeviceRoutes.Find(Vid, Pid, Product);
                if (route != null) return route.Kind;

                if (VendorProtocol.IsAtkZeroBitMouse(Vid, Pid) &&
                    (string.Equals(Product, "ATK ZERO", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Product, "ATK ZERO 2.4G Receiver", StringComparison.OrdinalIgnoreCase))) return DeviceKind.Mouse;
                if (Vid==0x3837 && Pid is 0x3033 or 0x2015 or 0x201A or 0x201B &&
                    System.Text.RegularExpressions.Regex.IsMatch(Product??"",@"(?i)^MCHOSE G87 V2(?: 2\.4G| \(1K\))?$")) return DeviceKind.Keyboard;
                if (VendorProtocol.IsMchoseG7(Vid, Pid, Product)) return DeviceKind.Mouse;
                if (VendorProtocol.IsAtkV75X(Vid, Pid, Product)) return DeviceKind.Keyboard;
                if (VendorProtocol.IsRainy75Candidate(Vid, Pid) &&
                    VendorProtocol.IsRainy75Name(Product)) return DeviceKind.Keyboard;
                string n = (name ?? "").ToLowerInvariant();
                if (n.Contains("keyboard") || n.Contains("键盘") || n.Contains("keychron")) return DeviceKind.Keyboard;

                if (Counts.ContainsKey(DeviceKind.Mouse) &&
                    System.Text.RegularExpressions.Regex.IsMatch(n, @"\bmouse\b|鼠标"))
                    return DeviceKind.Mouse;

                // Polling rate is marketing metadata, not a peripheral identity.
                // Mixed generic receivers may expose many auxiliary collections.
                if(IsUnresolved8KReceiver())return DeviceKind.Other;

                // 键盘型号常见写法：Neon75 / F87 / K8 / MK870 之类
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(f|mk|neon|kzz|akko)[\s-]?\d{2,4}\b") || (!Counts.ContainsKey(DeviceKind.Mouse) && System.Text.RegularExpressions.Regex.IsMatch(n, @"\bk[\s-]?\d{2,4}\b")))
                    return DeviceKind.Keyboard;
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b\d{2,3}\s*(air|pro)?\b") &&
                    Counts.TryGetValue(DeviceKind.Keyboard, out int kc) &&
                    Counts.TryGetValue(DeviceKind.Mouse, out int mc) && kc >= mc)
                    return DeviceKind.Keyboard;

                if (Counts.Count == 0) return Kind;
                if (Counts.Count == 2 && Counts.ContainsKey(DeviceKind.Mouse) &&
                    Counts.ContainsKey(DeviceKind.Keyboard)) return Kind;

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
            private bool IsUnresolved8KReceiver() => Counts.ContainsKey(DeviceKind.Mouse) && Counts.ContainsKey(DeviceKind.Keyboard) &&
                System.Text.RegularExpressions.Regex.IsMatch(Product??"", @"(?i)\b8[\s-]*k\b.*(receiver|dongle|接收器)") &&
                !System.Text.RegularExpressions.Regex.IsMatch(Product??"", @"(?i)keyboard|键盘|\bmouse\b|鼠标");
        }
    }
}




