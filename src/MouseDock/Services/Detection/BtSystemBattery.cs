using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 读 Windows 自己已经拿到的蓝牙设备电量。
    /// Windows 11 系统设置里显示的那个百分比来自 AEP 设备属性（System.Devices.BatteryLife /
    /// System.Devices.BatteryPlusCharging），跟 GATT 和 HID 都不是一条路过。
    /// </summary>
    public static class BtSystemBattery
    {
        public sealed class Entry
        {
            public string Name = "";
            public string DeviceId = "";
            public int Level = -1;
            public bool Charging;
            public string Raw = "";
            public List<string> Extra = new();
        }

        private static readonly string[] BatteryKeys =
        {
            "System.Devices.BatteryLife",
            "System.Devices.BatteryPlusCharging",
            "System.Devices.BatteryPlusChargingText",
            "System.Devices.BatteryLevel"
        };

        public static async Task<List<Entry>> ReadAsync()
        {
            var result = new List<Entry>();

            try
            {
                // 直接枚举所有关联端点，把带 Battery 字样的属性全打出来
                var infos = await DeviceInformation.FindAllAsync(
                    string.Empty, null, DeviceInformationKind.AssociationEndpoint);

                foreach (var info in infos)
                {
                    var entry = new Entry
                    {
                        Name = string.IsNullOrWhiteSpace(info.Name) ? "(无名)" : info.Name,
                        DeviceId = info.Id
                    };

                    foreach (var kv in info.Properties)
                    {
                        if (kv.Key.IndexOf("Battery", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        entry.Extra.Add($"{kv.Key} = {kv.Value} ({kv.Value?.GetType().Name})");

                        if (kv.Key == "System.Devices.BatteryLife" || kv.Key == "System.Devices.BatteryLevel")
                        {
                            entry.Level = kv.Value switch
                            {
                                byte b => b,
                                int i => i,
                                uint u => (int)u,
                                short s => s,
                                _ => -1
                            };
                        }

                        if (kv.Key == "System.Devices.BatteryPlusCharging" && kv.Value is byte pc)
                        {
                            entry.Level = pc & 0x7F;
                            entry.Charging = (pc & 0x80) != 0;
                            entry.Raw = $"0x{pc:X2}";
                        }
                    }

                    if (entry.Extra.Count > 0) result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                result.Add(new Entry { Name = "查询失败", Extra = { ex.GetType().Name + ": " + ex.Message } });
            }

            return result;
        }

        public static string Describe(List<Entry> entries)
        {
            if (entries.Count == 0)
                return "没有任何蓝牙设备带电量属性。";

            var lines = new List<string> { $"带电量属性的设备：{entries.Count} 台", "" };
            foreach (var e in entries)
            {
                string level = e.Level >= 0 ? $"{e.Level}%" : "—";
                string charge = e.Charging ? " 充电中" : "";
                lines.Add($"{e.Name}   {level}{charge}");
                foreach (var x in e.Extra) lines.Add("    " + x);
                lines.Add("");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
