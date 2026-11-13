using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 自动检测：HID（鼠标/键盘/手柄）+ 蓝牙（经典/LE）三路合并，读不到电量就保持 -1。
    /// 消失的设备标记为未连接而不是直接删掉，避免界面闪。
    /// </summary>
    public sealed class AutoDetectSource : IBatterySource
    {
        public string Key => "auto";
        public string Label => "自动检测";

        private readonly Dictionary<string, DeviceInfo> _known = new Dictionary<string, DeviceInfo>();
        private readonly object _gate = new object();
        private readonly DevicePreferences _preferences;
        public AutoDetectSource(DevicePreferences preferences = null) => _preferences = preferences;

        public IEnumerable<DeviceInfo> Read()
        {
            List<DetectedDevice> detected;
            try { detected = Detect(_preferences); }
            catch { detected = new List<DetectedDevice>(); }
            return UpdateFromDetection(detected);
        }

        internal List<DeviceInfo> UpdateFromDetection(List<DetectedDevice> detected)
        {
            lock (_gate)
            {
                var aliveIds = new HashSet<string>();

                // 同一只物理设备（例如狼蛛 SC580 的 2.4G 接收器与它的蓝牙身份）按 PhysicalKey 合并成一条
                foreach (var group in detected.GroupBy(d => Normalize(d.Id)))
                {
                    var merged = MergeGroup(group.ToList());
                    aliveIds.Add(group.Key);

                    if (!_known.TryGetValue(group.Key, out DeviceInfo device))
                    {
                        device = new DeviceInfo { Id = group.Key, Source = "auto" };
                        _known[group.Key] = device;
                    }

                    device.RawName = merged.RawName;
                    device.ConfirmedModel = merged.ConfirmedModel;
                    device.InterfaceDiscovered = merged.InterfaceDiscovered;
                    device.ReceiverDiscovered = merged.ReceiverDiscovered;
                    device.PeripheralConfirmed = merged.PeripheralConfirmed;
                    device.HandledByLabel = merged.HandledBy.ToString();
                    device.Name = merged.Name;
                    device.Kind = merged.Kind;
                    device.Transport = merged.Transport;
                    device.Detail = merged.Detail;
                    device.Note = merged.Note;
                    device.Dpi = merged.Dpi;
                    device.DpiSettings = merged.DpiSettings;
                    device.Connected = merged.Connected;
                    device.Level = merged.Level >= 0 ? merged.Level : -1;
                    // 换电池的型号（G304/G305、G602/G603、办公 Nano 鼠）没有充电状态，
                    // 别让任何读取路径把「电量低」画成「充电中」。
                    device.Charging = merged.Charging && !VendorMap.IsReplaceableBattery(merged.Name);
                    device.IsStale = merged.IsStale;
                    device.ChargeFull = merged.ChargeFull;
                    device.DetectionIds = group.SelectMany(d => new[] { d.Id, d.PhysicalKey })
                        .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }

                // 这一轮没出现的设备直接移除，列表里只留「现在连接中的」
                foreach (var key in _known.Keys.ToList())
                {
                    if (!aliveIds.Contains(key)) _known.Remove(key);
                }

                return _known.Values
                    .OrderByDescending(d => UsageMap.Priority(d.Kind))
                    .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                    // Background detection must not mutate the objects already bound to the UI.
                    // BatteryService needs the previous snapshot intact when a later query times out.
                    .Select(d => new DeviceInfo
                    {
                        RawName = d.RawName, ConfirmedModel = d.ConfirmedModel, InterfaceDiscovered = d.InterfaceDiscovered, ReceiverDiscovered = d.ReceiverDiscovered, PeripheralConfirmed = d.PeripheralConfirmed, HandledByLabel = d.HandledByLabel, Dpi = d.Dpi,
                        Id = d.Id, Name = d.Name, Kind = d.Kind, Source = d.Source,
                        Transport = d.Transport, Detail = d.Detail, Note = d.Note, DpiSettings = d.DpiSettings,
                        Connected = d.Connected, Level = d.Level, Charging = d.Charging, ChargeFull = d.ChargeFull, IsStale = d.IsStale,
                        DetectionIds = d.DetectionIds.ToArray()
                    })
                    .ToList();
            }
        }

        private static List<DetectedDevice> Detect(DevicePreferences preferences)
        {
            var list = new List<DetectedDevice>();

            try { list.AddRange(HidDiscovery.Enumerate(preferences)); } catch { }
            try { ExtraDeviceRoutes.AddSystemDevices(list, preferences, HidDiscovery.IsSafeMode); } catch { }

            // 蓝牙设备常常不给 Appearance，用 HID 里已经识别出的同名设备当提示
            var hints = new Dictionary<string, DeviceKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in list)
                if (!string.IsNullOrWhiteSpace(d.Name)) hints[d.Name] = d.Kind;

            try
            {
                var bt = BluetoothDiscovery.EnumerateAsync(1500, name =>
                    name != null && hints.TryGetValue(name, out var k) ? k : (DeviceKind?)null, preferences, includeHeadsets: true)
                    .GetAwaiter().GetResult();
                if (bt != null) list.AddRange(bt);
            }
            catch { }

            return list;
        }

        /// <summary>同一台设备可能同时出现在 HID 与蓝牙里：优先蓝牙的连接状态，电量谁有取谁。</summary>
        internal static DetectedDevice MergeGroup(List<DetectedDevice> group)
        {
            var connected = group.Where(d => d.Connected).ToList();
            if (connected.Count == 0)
            {
                var offline = group[0];
                return new DetectedDevice
                {
                    Id = offline.Id, Name = offline.Name, Kind = offline.Kind,
                    Transport = offline.Transport, Detail = offline.Detail, Note = offline.Note,
                    PhysicalKey = offline.PhysicalKey, Connected = false, Level = -1
                };
            }
            group = connected;
            var best = group.OrderByDescending(d => d.Level >= 0 ? 1 : 0)
                            .ThenBy(d => d.IsStale)
                            .ThenByDescending(d => d.Transport == "蓝牙" || d.Transport == "蓝牙 LE")
                            .First();

            var merged = new DetectedDevice
            {
                RawName = best.RawName, ConfirmedModel = best.ConfirmedModel, InterfaceDiscovered = best.InterfaceDiscovered, ReceiverDiscovered = best.ReceiverDiscovered, PeripheralConfirmed = best.PeripheralConfirmed, HandledBy = best.HandledBy,
                Id = best.Id,
                Name = best.Name,
                Kind = best.Kind,
                Transport = best.Transport,
                Level = best.Level,
                Dpi = !best.IsStale && best.Dpi > 0 ? best.Dpi : -1,
                DpiSettings = best.DpiSettings,
                Charging = best.Charging,
                ChargeFull = best.ChargeFull,
                IsStale = best.IsStale,
                Connected = best.Connected,
                Detail = best.Detail,
                Note = best.Note
            };

            foreach (var item in group)
            {
                if (merged.Level < 0 && item.Level >= 0)
                {
                    merged.Level = item.Level;
                    merged.Charging = item.Charging;
                    merged.ChargeFull = item.ChargeFull;
                    merged.IsStale = item.IsStale;
                    merged.Note = item.Note;                  // 有电量时用能读到的那条通道的说明
                }
                if (merged.Kind == DeviceKind.Other && item.Kind != DeviceKind.Other) merged.Kind = item.Kind;
                if (item.Connected) merged.Connected = true;
                if (string.IsNullOrWhiteSpace(merged.Name) && !string.IsNullOrWhiteSpace(item.Name)) merged.Name = item.Name;
                if (item.Transport == "蓝牙" || item.Transport == "蓝牙 LE") merged.Transport = item.Transport;
            }

            merged.PhysicalKey = best.PhysicalKey;
            return merged;
        }

        private static string Normalize(string id) => (id ?? "").Trim().ToLowerInvariant();
    }
}


