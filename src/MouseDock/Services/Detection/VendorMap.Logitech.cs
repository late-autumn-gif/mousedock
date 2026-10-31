using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 罗技鼠标的静态表，单独一个 partial 文件，方便从上游库重新生成。
    /// 数据来源：libratbag data/devices/*.device（DeviceType=mouse），2026-10-30 拉取。
    /// c51a / c531 是接收器 ID，不放在这里。
    /// </summary>
    public static partial class VendorMap
    {
        /// <summary>罗技鼠标本体 PID → 型号。键是 4 位十六进制 PID（VID 固定 046D）。</summary>
        internal static readonly Dictionary<string, string> KnownMice =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0a89"] = "G635",
                ["101b"] = "M705",
                ["1028"] = "M570",
                ["400a"] = "M325",
                ["402c"] = "G602",
                ["4041"] = "MX Master",
                ["404a"] = "MX Anywhere 2",
                ["4052"] = "M545",
                ["4053"] = "G900",
                ["405d"] = "G403 Wireless",
                ["405e"] = "M720",
                ["4060"] = "MX Master",
                ["4063"] = "MX Anywhere 2",
                ["4067"] = "G903",
                ["4069"] = "MX Master 2S",
                ["406a"] = "MX Anywhere 2S",
                ["406b"] = "M585/M590",
                ["406c"] = "G603",
                ["406d"] = "Marathon M705",
                ["406f"] = "MX Ergo",
                ["4070"] = "G703",
                ["4071"] = "MX Master",
                ["4072"] = "MX Anywhere 2",
                ["4074"] = "G304 / G305",
                ["4079"] = "G Pro Wireless",
                ["407b"] = "MX Vertical",
                ["407f"] = "G502 Hero Wireless",
                ["4082"] = "MX Master 3",
                ["4085"] = "G604",
                ["4086"] = "G703 Hero",
                ["4087"] = "G903 Hero",
                ["4090"] = "MX Anywhere 3",
                ["4093"] = "G Pro X Wireless Superlight",
                ["4099"] = "G502 X PLUS",
                ["409d"] = "G705",
                ["409f"] = "G502 X Wireless",
                ["4101"] = "T650",
                ["c041"] = "G5",
                ["c048"] = "G9",
                ["c049"] = "G5",
                ["c066"] = "G9x [Original]",
                ["c068"] = "G500",
                ["c06b"] = "G700",
                ["c077"] = "MX Vertical",
                ["c07c"] = "G700s",
                ["c07d"] = "G502 Proteus Core",
                ["c07e"] = "G402 Gaming Mouse",
                ["c07f"] = "G302",
                ["c080"] = "G303",
                ["c081"] = "G900",
                ["c082"] = "G403 Wireless",
                ["c083"] = "G403",
                ["c084"] = "G102/G103/G203",
                ["c085"] = "G Pro",
                ["c086"] = "G903",
                ["c087"] = "G703",
                ["c088"] = "G Pro Wireless",
                ["c08a"] = "MX Vertical",
                ["c08b"] = "G502 Hero",
                ["c08c"] = "G Pro",
                ["c08d"] = "G502 Hero Wireless",
                ["c08e"] = "MX518",
                ["c08f"] = "G403 Hero",
                ["c090"] = "G703 Hero",
                ["c091"] = "G903 Hero",
                ["c092"] = "G102/G103/G203",
                ["c093"] = "M500s",
                ["c094"] = "G Pro X Wireless Superlight",
                ["c095"] = "G502 X PLUS",
                ["c096"] = "G705",
                ["c097"] = "G303 Shroud Edition",
                ["c098"] = "G502 X Wireless",
                ["c099"] = "G502 X",
                ["c09d"] = "G102/G103/G203",
                ["c246"] = "G300",
                ["c249"] = "G9x [Call of Duty MW3 Edition]",
                ["c24a"] = "G600",
                ["c24e"] = "G500s",
                ["c332"] = "G502 Proteus Spectrum",
            };

        /// <summary>换电池的型号：只掉电，不存在充电状态，UI 不要显示充电。</summary>
        internal static readonly HashSet<string> ReplaceableBatteryMice =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "G300",
                "G304 / G305",
                "G400",
                "G5",
                "G500",
                "G500s",
                "G602",
                "G603",
                "G7",
                "G700",
                "G700s",
                "G9",
                "G9x",
                "G9x [Call of Duty MW3 Edition]",
                "G9x [Original]",
                "M325",
                "M545",
                "M585/M590",
                "M705",
                "M720",
                "MX518",
                "Marathon M705",
            };

        /// <summary>按 "VID:PID" 查罗技鼠标型号；不是罗技或没收录返回 false。</summary>
        public static bool TryKnownMouse(string key, out string name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            int at = key.IndexOf(':');
            if (at != 4) return false;
            if (!key.StartsWith("046D", StringComparison.OrdinalIgnoreCase)) return false;
            if (!KnownMice.TryGetValue(key.Substring(5), out var v)) return false;
            name = v;
            return true;
        }

        /// <summary>这个型号是不是换电池的（没有充电状态）。</summary>
        public static bool IsReplaceableBattery(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var k in ReplaceableBatteryMice)
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
