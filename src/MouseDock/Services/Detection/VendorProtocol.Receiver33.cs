using System;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

// 公版 2.4G 接收器（248A/249A:5B2F/5C2F）的只读身份旁路。
//
// 依据：249A:5C2F 用户报告的实机回帧
//   001000010B4A3331358501150100560100000000000000000000000000000000D6
// 去掉 report id 0 后的布局：
//   [0] 0x10 命令（狼蛛那边是 0x30）  [1] 0  [2] 1  [3] 11 版本
//   [4..8) 四字节型号 ASCII（J315）    [12] 充电位  [13] 电量(0x56=86)  [14] 在线=1
//
// 这类接收器不一定暴露 OUTPUT 集合，发不出身份请求；报告里 248A:5B2F 就只有
// 33 字节输入集合加 FF00/0001 的 Feature 33。所以这里不写任何命令：
// 被动听输入集合，另加只读 GetFeature(0)，两次同型号有效帧才采用。
internal static partial class VendorProtocol
{
    private const int Receiver33Frame = 33;
    private const int Receiver33WindowMs = 900;

    internal static bool IsGenericReceiver33(ushort vid, ushort pid, string product)
    {
        // 248A:5B2F 归 M137 官方 GetInfo 独占：两套协议都读同一台接收器时，
        // 谁先跑都可能把另一台设备的名字带歪，所以这里直接让出去。
        if (vid == 0x248A && pid == 0x5B2F) return false;
        if (vid != 0x248A && vid != 0x249A && !(vid == 0x089D && pid == 0x062F)) return false;
        if (pid != 0x5B2F && pid != 0x5C2F && pid != 0x062F) return false;
        string p = product?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(p)) return true;
        // 黑爵/多彩的接收器会撞号，产品串认出来就让出去。
        if (p.Contains("AJ159") || p.Contains("DELUX") || p.Contains("多彩")) return false;
        return p.Contains("WIRELESS") || p.Contains("RECEIVER") || p.Contains("2.4G") || p.Contains("2.4 G");
    }

    internal static bool TryParseReceiver33Frame(ReadOnlySpan<byte> raw,
        out int level, out bool charging, out string model, out bool isAula)
    {
        level = -1;
        charging = false;
        model = null;
        isAula = false;

        var frame = raw;
        if (frame.Length == Receiver33Frame && frame[0] == 0) frame = frame[1..];
        if (frame.Length != Receiver33Frame - 1) return false;

        byte head = frame[0];
        if (head is not (0x10 or 0x30)) return false;
        if (frame[1] != 0 || frame[2] != 1 || frame[3] != 11) return false;
        if (frame[12] > 1 || frame[13] > 100 || frame[14] != 1) return false;

        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte b = frame[4 + i];
            if (b < 0x20 || b > 0x7E) return false;
            chars[i] = (char)b;
        }
        string id = new string(chars).Trim();
        if (id.Length < 2) return false;

        model = id;
        isAula = head == 0x30;
        level = frame[13];
        charging = frame[12] == 1;
        return true;
    }

    // 型号 -> 界面名字。公版接收器只报四字节代号，代号本身不证明是某款零售型号，
    // 所以除实机确认过的 M137（狼蛛 SC580）外，一律「品牌 + 代号」。
    private static string Receiver33Name(string model, bool isAula) =>
        isAula ? (model == "M137" ? "狼蛛 SC580" : "狼蛛 " + model)
               : "英菲克 " + model;

    /// <summary>
    /// 读不到身份帧时的兜底名字。这些 VID:PID 本身就是鼠标接收器号段，
    /// 不能因为鼠标集合被厂商驱动独占、只剩键盘集合，就把设备显示成
    /// "Wireless-Receiver / 键盘"。品牌按 VID 定，型号不猜。
    /// </summary>
    internal static string Receiver33FallbackName(ushort vid, string model)
    {
        bool aula = vid == 0x089D;
        if (!string.IsNullOrWhiteSpace(model)) return Receiver33Name(model.Trim(), aula);
        return aula ? "狼蛛 2.4G 接收器（型号未确认）" : "英菲克 2.4G 接收器（型号未确认）";
    }

    internal static ReferenceBatteryProtocol.Reading TryReadReceiver33(ushort vid, ushort pid)
    {
        if (InstanceScope.Current == null) return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的设备实例，未发送查询");
        string last = "无";
        ReferenceBatteryProtocol.Reading identifiedOnly = null;

        foreach (string path in Paths(vid, pid))
        {
            if (!InstanceScope.Allows(path)) continue;
            IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe == IntPtr.Zero || probe == new IntPtr(-1)) continue;
            HIDP_CAPS caps;
            try { caps = Caps(probe); }
            finally { CloseHandle(probe); }

            ReferenceBatteryProtocol.Reading reading = null;
            if (caps.InputReportByteLength == Receiver33Frame)
                reading = ListenReceiver33(path, ref last);
            else if (caps.FeatureReportByteLength == Receiver33Frame)
                reading = ReadReceiver33Feature(path, ref last);
            if (reading == null) continue;

            if (reading.Level >= 0) return reading;
            if (identifiedOnly == null && reading.Model != null) identifiedOnly = reading;
        }

        return identifiedOnly ?? ReferenceBatteryProtocol.Reading.Failed("公版接收器未上报可解析的 33 字节身份帧；最近RX=" + last);
    }

    private static ReferenceBatteryProtocol.Reading ListenReceiver33(string path, ref string last)
    {
        IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;
        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            if (ev == IntPtr.Zero) return null;
            long until = Environment.TickCount64 + Receiver33WindowMs;
            string pendingModel = null, pendingName = null;
            int pendingLevel = -1;
            bool pendingCharging = false;
            while (Environment.TickCount64 < until)
            {
                var buf = new byte[Receiver33Frame];
                int wait = (int)Math.Max(1, Math.Min(200, until - Environment.TickCount64));
                if (!Transfer(h, ev, buf, false, wait, out int got)) continue;
                if (got <= 0) continue;
                last = Convert.ToHexString(buf.AsSpan(0, Math.Min(got, Receiver33Frame)));
                if (!TryParseReceiver33Frame(buf.AsSpan(0, got), out int level, out bool charging, out string model, out bool aula)) continue;
                string name = Receiver33Name(model, aula);
                if (pendingModel != null && pendingModel == model)
                    return Done(level, charging, name, model, last);
                pendingModel = model;
                pendingName = name;
                pendingLevel = level;
                pendingCharging = charging;
            }
            return pendingModel == null ? null : Done(pendingLevel, pendingCharging, pendingName, pendingModel, last) with
            {
                Level = -1,
                Charging = false,
                ChargingKnown = false,
                Diagnostic = $"公版接收器只收到一次身份帧（{pendingModel}），未取得第二次电量；最近RX={last}"
            };
        }
        finally
        {
            if (ev != IntPtr.Zero) CloseHandle(ev);
            CloseHandle(h);
        }
    }

    private static ReferenceBatteryProtocol.Reading ReadReceiver33Feature(string path, ref string last)
    {
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;
        try
        {
            var caps = Caps(h);
            // 报告编号不能默认 0：248A:5B2F 的报告里 GET_FEATURE(0) 直接失败。
            // 先按 HID 能力表取真实编号，取不到再试 0..5，每次结果都记进诊断。
            var ids = FeatureReportIds(h, caps);
            string pendingModel = null, pendingName = null;
            int pendingLevel = -1;
            bool pendingCharging = false;
            foreach (byte id in ids)
            {
                var buf = new byte[Receiver33Frame];
                buf[0] = id;
                if (!HidD_GetFeature(h, buf, buf.Length))
                {
                    last = $"GET_FEATURE({id:X2}) Win32={Marshal.GetLastWin32Error()}";
                    continue;
                }
                last = Convert.ToHexString(buf);
                if (!TryParseReceiver33Frame(buf, out int level, out bool charging, out string model, out bool aula)) continue;
                string name = Receiver33Name(model, aula);
                if (pendingModel != null && pendingModel == model)
                    return Done(level, charging, name, model, last);
                pendingModel = model;
                pendingName = name;
                pendingLevel = level;
                pendingCharging = charging;
            }
            return pendingModel == null ? null : Done(pendingLevel, pendingCharging, pendingName, pendingModel, last) with
            {
                Level = -1,
                Charging = false,
                ChargingKnown = false,
                Diagnostic = $"公版接收器 Feature 只读回一次身份帧（{pendingModel}）；最近RX={last}"
            };
        }
        finally { CloseHandle(h); }
    }

    private static ReferenceBatteryProtocol.Reading Done(int level, bool charging, string name, string identity, string last) =>
        new(level, charging, true,
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            $"公版接收器只读身份帧；最近RX={last}", name, identity);
}
