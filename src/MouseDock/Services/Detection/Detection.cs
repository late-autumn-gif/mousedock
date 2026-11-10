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
        public string RawName { get; set; } = "";
        public string ConfirmedModel { get; set; } = "";
        public bool InterfaceDiscovered { get; set; }
        public bool ReceiverDiscovered { get; set; }
        public bool PeripheralConfirmed { get; set; }
        public bool BatteryRead => Level >= 0 && !IsStale;
        public DeviceKind Kind { get; set; }
        public string Transport { get; set; } = "";   // 蓝牙 / 蓝牙 LE / 2.4G / USB / 内置
        public int Level { get; set; } = -1;
        /// <summary>硬件 DPI；-1 表示这台设备没有读到或没有该能力。</summary>
        public int Dpi { get; set; } = -1;
        /// <summary>DPI 各档位，未使用的位置为 -1。</summary>
        public int[] DpiSlots { get; set; }
        public string DpiText { get; set; } = "";
        public bool Charging { get; set; }
        public bool IsStale { get; set; }
        public bool Connected { get; set; } = true;
        public string Detail { get; set; } = "";      // VID:PID 或地址，供排查
        internal HandledBy HandledBy { get; set; }   // 实际接管这台设备的读取器
        public string Note { get; set; } = "";        // 电量读不到的原因，直接给用户看

        /// <summary>同一只物理设备的合并键：蓝牙模式与 2.4G 模式会共用同一个键。</summary>
        public string PhysicalKey { get; set; }
    }

    /// <summary>VID/PID → 品牌；产品名 → 更干净的型号名；接收器 → 真正挂在它后面的设备。</summary>
    public static partial class VendorMap
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
            [0x3837] = "MCHOSE",
            [0x5253] = "MCHOSE",
            [0x05AC] = "Apple",
            [0x8087] = "Intel"
        };

        public static string Brand(ushort vid) => Brands.TryGetValue(vid, out var b) ? b : null;

        internal static bool TryUsbHeadset(ushort vid, ushort pid, out string name, string product = null)
        {
            name = null;

            // The WebHID filter is shared across models; also require the device's own product string.
            if (vid == 0x3654 && pid == 0x5055 && IsZhuAudioZg4(product))
            {
                name = product.Trim();
                return true;
            }

            // Confirmed locally: USB audio endpoints and Consumer Control HID share this composite device.
            if (vid == 0x4C4A && pid == 0x4155) { name = "USB 音频设备（型号未确认）"; return true; }

            // HyperX（惠普 03F0）。03F0:06BE 的反馈包里六个集合全部被判成「usage 跳过」，
            // 设备连面板都上不去——耳机在 USB 上根本没有 0x01 取值的集合，只有
            // 0x0C 消费者控制和几条 0xFF13 / 0xFFC0 / 0x01C0 私有页。
            if (vid == 0x03F0)
            {
                name = pid switch
                {
                    0x06BE or 0x02CC => "HyperX Cloud III S Wireless",
                    0x05B7 or 0x0C9D or 0x0FCC => "HyperX Cloud III Wireless",
                    0x069F => "HyperX Cloud II Core Wireless",
                    0x018B => "HyperX Cloud II Wireless",
                    0x08BE => "HyperX Cloud Alpha 2",
                    0x0AC1 => "HyperX Cloud Flight 2",
                    _ => null
                };
                return name != null;
            }

            return false;
        }

        /// <summary>
        /// 已知接收器 → 它背后真正的外设。接收器自身只报 "Wireless-Receiver"，
        /// 这里把实测确认过的对应关系固化下来（键 = "VID:PID"）。
        /// </summary>
        internal static readonly Dictionary<string, (string Name, DeviceKind Kind, string PhysicalKey)> KnownReceivers =
            new Dictionary<string, (string, DeviceKind, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["089D:062F"] = ("2.4G 鼠标接收器", DeviceKind.Mouse, null),
                ["3554:FA09"] = ("无线键盘 3554:FA09", DeviceKind.Keyboard, "kzzi-k75v2"),
                ["391D:2A05"] = ("VGN Neon75 Air", DeviceKind.Keyboard, "vgn-neon75"),

                // 前行者 X87 三模键盘。判据来自 3.0 网页驱动 hub.eweadn.cn：
                //   vendorId === 0x0C45 && productId === 0x8006 && productName === "X87"
                // 同 PID 下还有别的键盘，官方靠接口区分：
                //   usagePage 含 0xFF68 && usage 含 0x0061  → 这一支是 X87
                //   usagePage 含 0xFF60 && usage 含 0x0061  → 另一支
                ["0C45:8006"] = ("前行者 X87", DeviceKind.Keyboard, "eweadn-x87"),

                // 瓦尔基里（Valkyrie）。05AC:024F 是 Apple 公版号，VK87 / VK99 / Valkyrie 75
                // 都挂在这一个号下面，只能靠产品名分。config.xml 原文：
                //   VK87 / VK99 / VK99 TFT / Valkyrie 75 → MI_02 (USB) 或 MI_03 (2.4G)
                // 另有 0C45:FEFE 的 Sonix 2.4G Dongle。
                ["0C45:FEFE"] = ("瓦尔基里 2.4G 接收器", DeviceKind.Keyboard, "valkyrie-dongle"),

                // 罗技接收器。Nano 只带 1 台，Unifying/Bolt 最多 6 台，Lightspeed 1 台。
                // 具体型号靠 HID++ 自己报，这里只保证通道和类型不丢。
                ["046D:C531"] = ("罗技无线接收器", DeviceKind.Mouse, null),
                ["046D:C534"] = ("罗技 Nano 接收器", DeviceKind.Keyboard, null),
                ["046D:C537"] = ("罗技 Nano 接收器", DeviceKind.Keyboard, null),
                ["046D:C539"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C53A"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C53D"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C53F"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C541"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C545"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C547"] = ("罗技 Lightspeed 接收器", DeviceKind.Mouse, null),
                ["046D:C52B"] = ("罗技 Unifying 接收器", DeviceKind.Keyboard, null),
                ["046D:C532"] = ("罗技 Unifying 接收器", DeviceKind.Keyboard, null),
                ["046D:C548"] = ("罗技 Bolt 接收器", DeviceKind.Keyboard, null)
            };

        /// <summary>
        /// Identify 应答 [4:8] 的型号串 → 真实型号。VID:PID 只能说明「这是 llTECH 那套 OEM 接收器」，
        /// 同一对 VID:PID 下面既挂过狼蛛 SC580 也挂过前行者 X23，分家全靠这 4 个字符。
        /// </summary>
        internal static readonly Dictionary<string, (string Name, string PhysicalKey)> IdentityModels =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["M137"] = ("狼蛛 SC580", "aula-sc580"),
                ["M909"] = ("英菲克 IN9（3311）", "inphic-in9-3311"),
                ["M910"] = ("英菲克 IN9（6827）", "inphic-in9-6827"),
                ["IN90"] = ("英菲克 IN9（S205）", "inphic-in9-s205"),
                ["M900"] = ("英菲克 A9", "inphic-a9"),
                ["I149"] = ("英菲克 IN9 Pro", "inphic-in9pro"),
                ["M230"] = ("前行者 X23 标准版", "x23-standard")
            };

        public static bool TryIdentityModel(string identity, out string name, out string physicalKey)
        {
            if (!string.IsNullOrWhiteSpace(identity) && IdentityModels.TryGetValue(identity.Trim(), out var v))
            {
                name = v.Name;
                physicalKey = v.PhysicalKey;
                return true;
            }
            name = null;
            physicalKey = null;
            return false;
        }

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
        internal static bool IsZhuAudioZg4(string name) =>
            string.Equals(name?.Trim(), "ZhuAudio ZG4", StringComparison.OrdinalIgnoreCase);

        public static DeviceKind? KindFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (IsZhuAudioZg4(name)) return DeviceKind.Headset;
            if (VendorProtocol.RogScopeName(name) != null) return DeviceKind.Keyboard;
            string n = name.ToUpperInvariant();
            if (n.Contains("SC580") || n.Contains("MOUSE") || n.Contains("鼠标")) return DeviceKind.Mouse;
            if (n.Contains("NEON") || n.Contains("KEYBOARD") || n.Contains("键盘")) return DeviceKind.Keyboard;
            if (n.Contains("AIRPODS") || n.Contains("HEADSET") || n.Contains("HEADPHONE") || n.Contains("耳机") || n.Contains("BUDS")) return DeviceKind.Headset;
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

