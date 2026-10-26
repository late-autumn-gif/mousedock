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

        public IEnumerable<DeviceInfo> Read()
        {
            List<DetectedDevice> detected;
            try { detected = Detect(); }
            catch { detected = new List<DetectedDevice>(); }

            lock (_gate)
            {
                var aliveIds = new HashSet<string>();

                // 同一只物理设备（例如狼蛛 SC580 的 2.4G 接收器与它的蓝牙身份）按 PhysicalKey 合并成一条
                foreach (var group in detected.Where(d => d.Connected)
                                              .GroupBy(d => Normalize(d.PhysicalKey ?? d.Id)))
                {
                    var merged = MergeGroup(group.ToList());
                    aliveIds.Add(group.Key);

                    if (!_known.TryGetValue(group.Key, out DeviceInfo device))
                    {
                        device = new DeviceInfo { Id = group.Key, Source = "auto" };
                        _known[group.Key] = device;
                    }

                    device.Name = merged.Name;
                    device.Kind = merged.Kind;
                    device.Transport = merged.Transport;
                    device.Detail = merged.Detail;
                    device.Note = merged.Note;
                    device.Connected = true;
                    device.Level = merged.Level >= 0 ? merged.Level : -1;
                    device.Charging = merged.Charging;
                }

                // 这一轮没出现的设备直接移除，列表里只留「现在连接中的」
                foreach (var key in _known.Keys.ToList())
                {
                    if (!aliveIds.Contains(key)) _known.Remove(key);
                }

                return _known.Values
                    .OrderByDescending(d => UsageMap.Priority(d.Kind))
                    .ThenBy(d => d.Name, StringComparer.CurrentCulture)
                    .ToList();
            }
        }

        private static List<DetectedDevice> Detect()
        {
            var list = new List<DetectedDevice>();

            try { list.AddRange(HidDiscovery.Enumerate()); } catch { }

            // 蓝牙设备常常不给 Appearance，用 HID 里已经识别出的同名设备当提示
            var hints = new Dictionary<string, DeviceKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in list)
                if (!string.IsNullOrWhiteSpace(d.Name)) hints[d.Name] = d.Kind;

            try
            {
                var bt = BluetoothDiscovery.EnumerateAsync(1500, name =>
                    name != null && hints.TryGetValue(name, out var k) ? k : (DeviceKind?)null)
                    .GetAwaiter().GetResult();
                if (bt != null) list.AddRange(bt);
            }
            catch { }

            return list;
        }

        /// <summary>同一台设备可能同时出现在 HID 与蓝牙里：优先蓝牙的连接状态，电量谁有取谁。</summary>
        private static DetectedDevice MergeGroup(List<DetectedDevice> group)
        {
            var best = group.OrderByDescending(d => d.Level >= 0 ? 1 : 0)
                            .ThenByDescending(d => d.Transport == "蓝牙" || d.Transport == "蓝牙 LE")
                            .First();

            var merged = new DetectedDevice
            {
                Id = best.Id,
                Name = best.Name,
                Kind = best.Kind,
                Transport = best.Transport,
                Level = best.Level,
                Connected = best.Connected,
                Detail = best.Detail,
                Note = best.Note
            };

            foreach (var item in group)
            {
                if (merged.Level < 0 && item.Level >= 0)
                {
                    merged.Level = item.Level;
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
