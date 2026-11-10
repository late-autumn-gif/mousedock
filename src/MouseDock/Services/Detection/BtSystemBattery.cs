using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MouseDock.Services.Detection
{
    /// <summary>读取 Windows 已缓存的蓝牙电量；显式请求端点及其设备容器的电池属性。</summary>
    public static class BtSystemBattery
    {
        public sealed class Entry
        {
            public string Name = "";
            public string DeviceId = "";
            public ulong Address;
            public bool Connected;
            public int Level = -1;
            public bool Charging;
            public bool ChargingKnown;
            public string Raw = "";
            public List<string> Extra = new();
        }

        internal const string BluetoothBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        private static readonly string[] BatteryKeys =
        {
            "System.Devices.BatteryLife",
            BluetoothBatteryKey,
            "System.Devices.ChargingState",
            "System.Devices.BatteryPlusCharging",
            "System.Devices.BatteryPlusChargingText"
        };
        private static readonly string[] RequestedProperties = BatteryKeys.Concat(new[]
        {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected"
        }).ToArray();
        private static readonly string[] DeviceProperties = BatteryKeys.Concat(new[]
        {
            "System.Devices.Aep.AepId",
            "System.Devices.ContainerId"
        }).ToArray();

        public static async Task<List<Entry>> ReadAsync(int timeoutMs = 1500)
        {
            var result = new List<Entry>();
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs));
            TimeSpan Remaining() => TimeSpan.FromMilliseconds(Math.Max(1,
                (deadline - DateTime.UtcNow).TotalMilliseconds));
            try
            {
                string selector = "(" + BluetoothDevice.GetDeviceSelector() + ") OR (" +
                    BluetoothLEDevice.GetDeviceSelector() + ")";
                var infos = await DeviceInformation.FindAllAsync(selector, RequestedProperties,
                    DeviceInformationKind.AssociationEndpoint).AsTask()
                    .WaitAsync(Remaining()).ConfigureAwait(false);
                var containers = new Dictionary<Guid, DeviceInformation>();
                foreach (var info in infos)
                {
                    var entry = new Entry
                    {
                        Name = string.IsNullOrWhiteSpace(info.Name) ? "(无名)" : info.Name,
                        DeviceId = info.Id,
                        Connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected)
                            && connected is bool isConnected && isConnected
                    };
                    if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address))
                        TryParseAddress(address as string, out entry.Address);
                    ApplyProperties(entry, info.Properties, "端点");
                    result.Add(entry);

                    if (!entry.Connected || DateTime.UtcNow >= deadline ||
                        (entry.Level >= 0 && entry.ChargingKnown)) continue;
                    try
                    {
                        // PnP Device 通过 Aep.AepId 关联端点，再由它的 ContainerId 找 PnP 容器。
                        // Aep.ContainerId 指向另一种 AssociationEndpointContainer，不能混用。
                        // https://learn.microsoft.com/uwp/api/windows.devices.enumeration.deviceinformationkind
                        if (info.Id.IndexOf('"') >= 0 || info.Id.Any(char.IsControl)) continue;
                        string deviceSelector = "System.Devices.Aep.AepId:=\"" + info.Id + "\"";
                        var nodes = await DeviceInformation.FindAllAsync(deviceSelector, DeviceProperties,
                            DeviceInformationKind.Device).AsTask().WaitAsync(Remaining()).ConfigureAwait(false);
                        foreach (var node in nodes)
                        {
                            if (!node.Properties.TryGetValue("System.Devices.Aep.AepId", out var aepId)
                                || !string.Equals(aepId as string, info.Id, StringComparison.OrdinalIgnoreCase)) continue;
                            ApplyProperties(entry, node.Properties, "PnP 节点");
                            if (DateTime.UtcNow >= deadline ||
                                !node.Properties.TryGetValue("System.Devices.ContainerId", out var containerValue) ||
                                !TryGetContainerId(containerValue, out var containerId)) continue;
                            if (!containers.TryGetValue(containerId, out var container))
                            {
                                container = await DeviceInformation.CreateFromIdAsync(containerId.ToString("B"),
                                    BatteryKeys, DeviceInformationKind.DeviceContainer).AsTask()
                                    .WaitAsync(Remaining()).ConfigureAwait(false);
                                containers[containerId] = container;
                            }
                            if (container != null) ApplyProperties(entry, container.Properties, "PnP 容器");
                            if (entry.Level >= 0 && entry.ChargingKnown) break;
                        }
                    }
                    catch (Exception ex)
                    {
                        entry.Extra.Add("容器电量查询失败: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Add(new Entry { Name = "查询失败", Extra = { ex.GetType().Name + ": " + ex.Message } });
            }
            return result;
        }

        private static bool TryGetContainerId(object value, out Guid containerId)
        {
            if (value is Guid id) { containerId = id; return id != Guid.Empty; }
            return Guid.TryParse(value as string, out containerId) && containerId != Guid.Empty;
        }

        internal static bool TryParseAddress(string value, out ulong address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            if (value.Length == 17)
            {
                char separator = value[2];
                if (separator != ':' && separator != '-') return false;
                for (int i = 2; i < value.Length; i += 3)
                    if (value[i] != separator) return false;
                value = value.Replace(separator.ToString(), "");
            }
            return value.Length == 12 && ulong.TryParse(value, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out address) && address != 0;
        }

        internal static void ApplyProperties(Entry entry, IReadOnlyDictionary<string, object> properties, string source)
        {
            foreach (var key in BatteryKeys)
            {
                if (properties.TryGetValue(key, out var value) && value != null)
                    entry.Extra.Add($"{source} {key} = {value} ({value.GetType().Name})");
            }
            // BatteryLife 是明确的百分比；101 表示未知，不能显示成电量。
            // https://learn.microsoft.com/windows/win32/properties/props-system-devices-batterylife
            if (entry.Level < 0 && properties.TryGetValue("System.Devices.BatteryLife", out var battery)
                && TryGetInteger(battery, out int level) && level >= 0 && level <= 100)
                entry.Level = level;
            if (entry.Level < 0 && properties.TryGetValue(BluetoothBatteryKey, out var bluetoothBattery)
                && TryGetInteger(bluetoothBattery, out int bluetoothLevel) && bluetoothLevel >= 0 && bluetoothLevel <= 100)
                entry.Level = bluetoothLevel;

            // ChargingState: 0=未充电，1=充电，2=未知。合并值和文本只保留原值，
            // BatteryPlusCharging 并非 bit7 标志，不能用 &0x7F / &0x80 解析。
            // https://learn.microsoft.com/windows/win32/properties/props-system-devices-chargingstate
            if (!entry.ChargingKnown && properties.TryGetValue("System.Devices.ChargingState", out var charging)
                && TryGetInteger(charging, out int state) && (state == 0 || state == 1))
            {
                entry.Charging = state == 1;
                entry.ChargingKnown = true;
            }
            if (properties.TryGetValue("System.Devices.BatteryPlusCharging", out var raw) && raw != null)
                entry.Raw = raw.ToString();
        }

        private static bool TryGetInteger(object value, out int number)
        {
            number = value switch
            {
                byte b => b,
                sbyte b => b,
                ushort s => s,
                short s => s,
                int i => i,
                uint u when u <= int.MaxValue => (int)u,
                long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
                ulong u when u <= int.MaxValue => (int)u,
                _ => -1
            };
            return number >= 0;
        }

        public static string Describe(List<Entry> entries)
        {
            if (entries.Count == 0) return "没有已配对的蓝牙设备端点。";
            var lines = new List<string> { $"蓝牙系统电量属性：{entries.Count} 条", "" };
            foreach (var e in entries)
            {
                string level = e.Level >= 0 ? $"{e.Level}%" : "—";
                string charge = e.ChargingKnown ? (e.Charging ? " 充电中" : " 未充电") : "";
                string address = e.Address != 0 ? e.Address.ToString("X12") : "地址未知";
                lines.Add($"{address}  {e.Name}   {level}{charge}");
                if (e.Extra.Count == 0) lines.Add("    Windows 未提供电量属性");
                foreach (var x in e.Extra) lines.Add("    " + x);
                lines.Add("");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
