using System;
using System.Collections.Generic;
using MouseDock.Models;

namespace MouseDock.Services.Detection
{
    /// <summary>一个被探测到的真实设备。Level = -1 表示「读不到」，界面显示 —，不编数字。</summary>
    public sealed class DetectedDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DeviceKind Kind { get; set; }
        public string Transport { get; set; } = "";   // 蓝牙 / 蓝牙 LE / 2.4G / USB / 内置
        public int Level { get; set; } = -1;
        public bool Charging { get; set; }
        public bool Connected { get; set; } = true;
        public string Detail { get; set; } = "";      // VID:PID 或地址，供排查
        public string Note { get; set; } = "";        // 电量读不到的原因，直接给用户看

        /// <summary>同一只物理设备的合并键：蓝牙模式与 2.4G 模式会共用同一个键。</summary>
        public string PhysicalKey { get; set; }
    }

    /// <summary>VID/PID → 品牌；产品名 → 更干净的型号名；接收器 → 真正挂在它后面的设备。</summary>
    public static class VendorMap
    {
        private static readonly Dictionary<ushort, string> Brands = new Dictionary<ushort, string>
        {
            [0x046D] = "Logitech",
            [0x1532] = "Razer",
            [0x1038] = "SteelSeries",
            [0x089D] = "AULA",          // 狼蛛常用 OEM 2.4G 接收器 VID（用户实机确认 SC580）
            [0x3554] = "AULA",
            [0x391D] = "VGN",
            [0x1B1C] = "Corsair",
            [0x0B05] = "ROG",
            [0x04D9] = "RAPOO",
            [0x24AE] = "RAPOO",
            [0x1E7D] = "ROCCAT",
            [0x258A] = "SINOWEALTH",
            [0x3142] = "Mad Catz",
            [0x36B0] = "ATK",
            [0x3673] = "MCHOSE",
            [0x05AC] = "Apple",
            [0x8087] = "Intel"
        };

        public static string Brand(ushort vid) => Brands.TryGetValue(vid, out var b) ? b : null;

        /// <summary>
        /// 已知接收器 → 它背后真正的外设。接收器自身只报 "Wireless-Receiver"，
        /// 这里把实测确认过的对应关系固化下来（键 = "VID:PID"）。
        /// </summary>
        internal static readonly Dictionary<string, (string Name, DeviceKind Kind, string PhysicalKey)> KnownReceivers =
            new Dictionary<string, (string, DeviceKind, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["089D:062F"] = ("狼蛛 SC580", DeviceKind.Mouse, "aula-sc580"),
                ["3554:FA09"] = ("狼蛛 SC580", DeviceKind.Mouse, "aula-sc580"),
                ["391D:2A05"] = ("VGN Neon75 Air", DeviceKind.Keyboard, "vgn-neon75")
            };

        public static bool TryKnownReceiver(string key, out string name, out DeviceKind kind, out string physicalKey)
        {
            if (KnownReceivers.TryGetValue(key ?? "", out var v))
            {
                name = v.Name; kind = v.Kind; physicalKey = v.PhysicalKey;
                return true;
            }
            name = null; kind = DeviceKind.Other; physicalKey = null;
            return false;
        }

        /// <summary>从蓝牙名字推同一只设备的合并键（"AULA-SC580" 与 2.4G 接收器是同一只鼠标）。</summary>
        /// <summary>按设备名猜类型（Appearance 为空时的兜底）。</summary>
        public static DeviceKind? KindFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.ToUpperInvariant();
            if (n.Contains("SC580") || n.Contains("MOUSE") || n.Contains("鼠标")) return DeviceKind.Mouse;
            if (n.Contains("NEON") || n.Contains("KEYBOARD") || n.Contains("键盘")) return DeviceKind.Keyboard;
            if (n.Contains("AIR") || n.Contains("HEADSET") || n.Contains("耳机") || n.Contains("BUDS")) return DeviceKind.Headset;
            return null;
        }

        public static string PhysicalKeyFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (n.Contains("SC580")) return "aula-sc580";
            if (n.Contains("NEON75")) return "vgn-neon75";
            return null;
        }


        /// <summary>把厂商串（"VGN Neon75 Air 2.4G Dongle"）压成型号名（"VGN Neon75 Air"）。</summary>
        public static string CleanName(string product, out bool looksLikeDongle)
        {
            looksLikeDongle = false;
            if (string.IsNullOrWhiteSpace(product)) return null;

            string name = product.Trim();
            foreach (var token in new[] { " 2.4G Dongle", " 2.4G Receiver", " Dongle", " Receiver", " 2.4G" })
            {
                if (name.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - token.Length).Trim();
                    looksLikeDongle = true;
                    break;
                }
            }

            if (name.Contains("2.4G", StringComparison.OrdinalIgnoreCase)) looksLikeDongle = true;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }

    /// <summary>HID 顶层集合的 usage → 设备类型。</summary>
    public static class UsageMap
    {
        public static bool TryKind(ushort usagePage, ushort usage, out DeviceKind kind)
        {
            kind = DeviceKind.Other;
            if (usagePage != 0x01) return false;
            switch (usage)
            {
                case 0x02: kind = DeviceKind.Mouse; return true;
                case 0x04:
                case 0x05: kind = DeviceKind.Controller; return true;
                case 0x06:
                case 0x07: kind = DeviceKind.Keyboard; return true;
                default: return false;
            }
        }

        /// <summary>同一台设备可能同时暴露鼠标 + 键盘集合，取「更像主功能」的那个。</summary>
        public static int Priority(DeviceKind kind) => kind switch
        {
            DeviceKind.Mouse => 4,
            DeviceKind.Keyboard => 3,
            DeviceKind.Controller => 2,
            DeviceKind.Headset => 1,
            _ => 0
        };

        /// <summary>蓝牙 Class of Device → 设备类型（Peripheral 的 minor 高两位表示键盘/指向/组合）。</summary>
        public static DeviceKind FromClassOfDevice(uint cod)
        {
            uint major = (cod >> 8) & 0x1F;          // Major Device Class
            uint minor = (cod >> 2) & 0x3F;          // Minor Device Class
            switch (major)
            {
                case 0x04: return DeviceKind.Headset;                       // Audio/Video
                case 0x05:                                                  // Peripheral
                    uint peer = (minor >> 4) & 0x03;                        // 1=keyboard 2=pointing 3=combo
                    if (peer == 1) return DeviceKind.Keyboard;
                    if (peer == 2) return DeviceKind.Mouse;
                    if (peer == 3) return DeviceKind.Keyboard;
                    return DeviceKind.Other;
                default: return DeviceKind.Other;
            }
        }
    }
}
