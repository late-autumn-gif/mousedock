using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>数据源抽象：任何能提供「设备名 + 电量」的东西都可以接进来。</summary>
    public interface IBatterySource
    {
        string Key { get; }
        string Label { get; }
        IEnumerable<DeviceInfo> Read();
    }

    /// <summary>演示源：无需任何硬件，数值会缓慢漂移，用来复刻视频里的动态效果。</summary>
    public sealed class DemoBatterySource : IBatterySource
    {
        public string Key => "demo";
        public string Label => "演示数据";

        private readonly List<DeviceInfo> _devices = new List<DeviceInfo>
        {
            new DeviceInfo { Id = "demo-gpw2",  Name = "Logitech GPW2",      Kind = DeviceKind.Mouse,    Level = 86 },
            new DeviceInfo { Id = "demo-cloud", Name = "Cloud III Wireless", Kind = DeviceKind.Headset,  Level = 64 },
            new DeviceInfo { Id = "demo-vt3s",  Name = "Rapoo VT3S V2",      Kind = DeviceKind.Mouse,    Level = 86 },
            new DeviceInfo { Id = "demo-atk",   Name = "ATK A9 Air+",        Kind = DeviceKind.Mouse,    Level = 65 },
            new DeviceInfo { Id = "demo-rog",   Name = "ROG 夜魔",            Kind = DeviceKind.Keyboard, Level = 71 }
        };

        private readonly Random _rng = new Random(20261022);

        public IEnumerable<DeviceInfo> Read()
        {
            foreach (var d in _devices)
            {
                if (d.Charging)
                {
                    d.Level += _rng.Next(0, 2);
                    if (d.Level >= 100) d.Charging = false;
                }
                else
                {
                    if (_rng.NextDouble() < 0.35) d.Level -= 1;
                    if (d.Level <= 18) d.Charging = true;
                }
                d.Connected = true;
            }
            return _devices;
        }
    }

    /// <summary>JSON 源：外部脚本写文件，本体读取。方便接 HID++/BLE 读取器。</summary>
    public sealed class JsonBatterySource : IBatterySource
    {
        public string Key => "json";
        public string Label => "外部 JSON";

        private readonly string _path;
        private readonly List<DeviceInfo> _cache = new List<DeviceInfo>();

        public JsonBatterySource(string path) => _path = path;

        private sealed class Dto
        {
            public string id { get; set; }
            public string name { get; set; }
            public string kind { get; set; }
            public int level { get; set; }
            public bool charging { get; set; }
            public bool connected { get; set; } = true;
        }

        public IEnumerable<DeviceInfo> Read()
        {
            try
            {
                if (!File.Exists(_path)) return _cache;
                var dtos = JsonSerializer.Deserialize<List<Dto>>(File.ReadAllText(_path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dtos == null) return _cache;

                foreach (var dto in dtos)
                {
                    var id = string.IsNullOrWhiteSpace(dto.id) ? dto.name : dto.id;
                    var dev = _cache.FirstOrDefault(x => x.Id == id);
                    if (dev == null)
                    {
                        dev = new DeviceInfo { Id = id, Source = "json" };
                        _cache.Add(dev);
                    }
                    dev.Name = dto.name ?? dev.Name;
                    dev.Kind = ParseKind(dto.kind);
                    dev.Level = dto.level;
                    dev.Charging = dto.charging;
                    dev.Connected = dto.connected;
                }
            }
            catch { /* 文件半写入时忽略这一轮 */ }
            return _cache;
        }

        private static DeviceKind ParseKind(string k)
        {
            if (string.IsNullOrWhiteSpace(k)) return DeviceKind.Other;
            return k.Trim().ToLowerInvariant() switch
            {
                "mouse" or "鼠标" => DeviceKind.Mouse,
                "headset" or "耳机" => DeviceKind.Headset,
                "keyboard" or "键盘" => DeviceKind.Keyboard,
                "controller" or "手柄" => DeviceKind.Controller,
                _ => DeviceKind.Other
            };
        }
    }

    /// <summary>
    /// 实验性 HID 源：枚举 HID 设备，凡是暴露了 Battery Strength（Usage Page 0x06 / Usage 0x20）
    /// 的，直接读特性报告拿百分比。没有该能力的设备（多数 2.4G 接收器）会被跳过。
    /// </summary>
    public sealed class HidBatterySource : IBatterySource
    {
        public string Key => "hid";
        public string Label => "HID 电池（实验）";

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;

        private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
        }

        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, byte[] buffer, int length);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        private readonly List<DeviceInfo> _cache = new List<DeviceInfo>();

        public IEnumerable<DeviceInfo> Read()
        {
            var list = new List<DeviceInfo>();
            Enumerate(list);
            return list;
        }

        private void Enumerate(List<DeviceInfo> result)
        {
            IntPtr set = IntPtr.Zero;
            try
            {
                var guid = GUID_DEVINTERFACE_HID;
                set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (set == IntPtr.Zero || set == new IntPtr(-1)) return;

                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did); i++)
                {
                    const int detailSize = 1024;
                    IntPtr detail = Marshal.AllocHGlobal(detailSize);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, detailSize, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        TryReadDevice(path, result);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            catch { /* 任何 P/Invoke 异常都不应影响界面 */ }
            finally
            {
                if (set != IntPtr.Zero && set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set);
            }
        }

        private void TryReadDevice(string path, List<DeviceInfo> result)
        {
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return;
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(h, ref attr)) return;

                string name = ProductName(h) ?? $"0x{attr.VendorID:X4}:0x{attr.ProductID:X4}";

                if (!HidD_GetPreparsedData(h, out IntPtr pre)) return;
                HIDP_CAPS caps = default;
                try { HidP_GetCaps(pre, ref caps); } finally { HidD_FreePreparsedData(pre); }

                // Usage Page: 0x06 Generic Device Controls / Usage 0x20 Battery Strength
                bool hasBatteryUsage = caps.FeatureReportByteLength > 0 &&
                                       ((caps.UsagePage == 0x06 && caps.Usage == 0x20) || caps.UsagePage == 0x84);

                if (!hasBatteryUsage) return;

                var buffer = new byte[Math.Max(2, (int)caps.FeatureReportByteLength)];
                if (!HidD_GetFeature(h, buffer, buffer.Length)) return;
                int level = buffer.Length > 1 ? buffer[1] : buffer[0];
                if (level <= 0 || level > 100) return;

                string id = $"hid-{attr.VendorID:X4}-{attr.ProductID:X4}";
                var dev = _cache.FirstOrDefault(x => x.Id == id);
                if (dev == null)
                {
                    dev = new DeviceInfo { Id = id, Source = "hid", Kind = GuessKind(name) };
                    _cache.Add(dev);
                }
                dev.Name = name;
                dev.Level = level;
                dev.Connected = true;
                result.Add(dev);
            }
            finally { CloseHandle(h); }
        }

        private static string ProductName(IntPtr h)
        {
            var buf = new byte[256];
            if (!HidD_GetProductString(h, buf, buf.Length)) return null;
            string s = System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static DeviceKind GuessKind(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("mouse") || n.Contains("鼠标")) return DeviceKind.Mouse;
            if (n.Contains("keyboard") || n.Contains("键盘")) return DeviceKind.Keyboard;
            if (n.Contains("headset") || n.Contains("耳机") || n.Contains("audio")) return DeviceKind.Headset;
            if (n.Contains("gamepad") || n.Contains("controller") || n.Contains("手柄")) return DeviceKind.Controller;
            return DeviceKind.Other;
        }
    }

    /// <summary>定时轮询数据源，把结果同步进一个可绑定的集合。</summary>
    public sealed class BatteryService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private IBatterySource _source;
        private readonly List<DeviceInfo> _mirror = new List<DeviceInfo>();
        private readonly Dictionary<string, int> _misses = new Dictionary<string, int>();

        public ObservableCollection<DeviceInfo> Devices { get; } = new ObservableCollection<DeviceInfo>();

        public event EventHandler Refreshed;
        public event EventHandler SourceChanged;
        public IBatterySource Source => _source;

        public BatteryService(AppSettings settings)
        {
            _source = CreateSource(settings);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, settings.RefreshSeconds)) };
            _timer.Tick += (_, __) => Poll();
            _timer.Start();
            Poll();
        }

        public void Configure(AppSettings settings)
        {
            string key = settings.Provider ?? "auto";
            if (_source == null || _source.Key != key)
            {
                _source = CreateSource(settings);
                foreach (var d in Devices) d.Selected = false;
                _mirror.Clear();
                Devices.Clear();
                SourceChanged?.Invoke(this, EventArgs.Empty);
            }
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, settings.RefreshSeconds));
            Poll();
        }

        private static IBatterySource CreateSource(AppSettings settings)
        {
            switch ((settings.Provider ?? "auto").ToLowerInvariant())
            {
                case "demo":
                    return new DemoBatterySource();
                case "json":
                    return new JsonBatterySource(string.IsNullOrWhiteSpace(settings.JsonPath)
                        ? Path.Combine(SettingsStore.Dir, "devices.json")
                        : settings.JsonPath);
                case "hid":
                    return new HidBatterySource();
                default:
                    return new Detection.AutoDetectSource();
            }
        }

        public void Poll() => _ = PollAsync();

        private bool _polling;

        /// <summary>探测要访问 P/Invoke 与 WinRT，放到线程池跑，避免卡住 UI。</summary>
        public async System.Threading.Tasks.Task PollAsync()
        {
            if (_polling) return;
            _polling = true;

            var source = _source;
            List<DeviceInfo> snapshot = null;
            try
            {
                snapshot = await System.Threading.Tasks.Task.Run(() => source.Read()?.ToList());
            }
            catch { }
            finally { _polling = false; }

            if (snapshot == null) return;

            foreach (var incoming in snapshot)
            {
                var existing = _mirror.FirstOrDefault(x => x.Id == incoming.Id);

                // 设备还在，只是这一次没读到（厂商通道忙/设备刚唤醒）：
                // 保留上一次的有效读数，避免百分比闪成「—」；连续 6 次读不到再清零
                if (existing != null && incoming.Level < 0 && existing.Level >= 0)
                {
                    int misses;
                    _misses.TryGetValue(incoming.Id, out misses);
                    misses++;
                    _misses[incoming.Id] = misses;
                    if (misses <= 6) incoming.Level = existing.Level;
                }
                else if (incoming.Level >= 0)
                {
                    _misses[incoming.Id] = 0;
                }

                if (existing == null)
                {
                    _mirror.Add(incoming);
                    Devices.Add(incoming);
                    continue;
                }
                if (!ReferenceEquals(existing, incoming))
                {
                    existing.Name = incoming.Name;
                    existing.Kind = incoming.Kind;
                    existing.Level = incoming.Level;
                    existing.Charging = incoming.Charging;
                    existing.Connected = incoming.Connected;
                    existing.Transport = incoming.Transport;
                    existing.Detail = incoming.Detail;
                    existing.Note = incoming.Note;
                }
            }

            // 只保留本轮快照里还存在的设备：拔掉/断开的立即从界面移除
            var alive = new HashSet<string>(snapshot.Select(d => d.Id));
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (alive.Contains(Devices[i].Id)) continue;
                _mirror.Remove(Devices[i]);
                Devices.RemoveAt(i);
            }

            Refreshed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _timer?.Stop();
        }
    }
}
