using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal const string A7UltraRoute = "MchoseA7Ultra";
    internal const string MonsGeekRoute = "MonsGeekBatteryEvent";
    internal const string A7UltraName = "MCHOSE A7 Ultra";

    internal static string ReportedReceiverRoute(ushort vid, ushort pid, string product) =>
        (vid, pid) switch
        {
            (0x5253, 0x1020) when string.Equals(product?.Trim(), A7UltraName, StringComparison.OrdinalIgnoreCase) => A7UltraRoute,
            (0x3151, 0x5038) when string.Equals(product?.Trim(), "MonsGeek 2.4G Wireless Keyboard", StringComparison.OrdinalIgnoreCase) => MonsGeekRoute,
            _ => null
        };

    internal static bool MatchesReportedReceiverChannel(string route, ushort vid, ushort pid, string product,
        string label, ushort page, ushort usage, int input, int output, int feature) =>
        route != null && route == ReportedReceiverRoute(vid, pid, product) &&
        (route == A7UltraRoute
            ? label == "MI_02/COL_02" && page == 0xFF01 && usage == 1 && input == 65 && output == 65 && feature == 65
            : label == "MI_01/COL_05" && page == 0xFFFF && usage == 1 && input == 32 && output == 0 && feature == 0);

    internal static bool TryParseA7UltraBattery(ReadOnlySpan<byte> frame, out int level, out bool charging)
    {
        level = -1;
        charging = false;
        if (frame.Length != 21 || frame[0] != 0x11 || frame[1] != 0xF9) return false;
        int vid = (frame[2] ^ 255) | ((frame[3] ^ 255) << 8);
        int model = (frame[4] ^ 255) | ((frame[5] ^ 255) << 8);
        int state = (frame[10] ^ 255) & 0x0F;
        int percent = frame[11] ^ 255;
        int charge = frame[12] ^ 255;
        if (vid is not (0x5253 or 0x3837) || model != 0x0011 || state != 9 || percent > 100 || charge > 1)
            return false;
        level = percent;
        charging = charge == 1;
        return true;
    }

    internal static bool TryParseMonsGeekBatteryEvent(ReadOnlySpan<byte> frame, out int level, out bool charging)
    {
        level = -1;
        charging = false;
        if (frame.Length is not (6 or 32) || frame[0] != 0x05 || frame[1] != 0x88 ||
            frame[2] != 0 || frame[3] != 0 || frame[4] > 100 || frame[5] is not (1 or 3))
            return false;
        level = frame[4];
        charging = frame[5] == 3;
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading ReadReportedReceiver(string route,
        HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(channel.Path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例");
        HidInventory.ParsePath(channel.Path, out _, out _, out string label);
        if (!MatchesReportedReceiverChannel(route, channel.Vid, channel.Pid, channel.Product,
                label, channel.UsagePage, channel.Usage, channel.Input, channel.Output, channel.Feature))
            return ReferenceBatteryProtocol.Reading.Failed("HID 集合不符");

        bool mouse = route == A7UltraRoute;
        return ReferenceQueryGate.Run(channel.Path, () =>
        {
            IntPtr handle = CreateFile(channel.Path, mouse ? GENERIC_READ | GENERIC_WRITE : GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
                mouse ? 0 : FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                trace?.Add("open", false, nativeError: Marshal.GetLastWin32Error());
                return ReferenceBatteryProtocol.Reading.Failed("电量接口打开失败");
            }
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    !MatchesReportedReceiverChannel(route, attributes.VendorID, attributes.ProductID,
                        HidDiscovery.ReadHidProduct(handle, channel.Path), label, caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    return ReferenceBatteryProtocol.Reading.Failed("打开后的设备身份或集合能力不符");
                if (!HidD_GetPreparsedData(handle, out IntPtr prep))
                    return ReferenceBatteryProtocol.Reading.Failed("无法读取 HID 报告编号");
                try
                {
                    bool valid = mouse
                        ? ReadDescriptorReportIds(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps).Contains(0x11)
                        : TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out byte id) && id == 0x05;
                    if (!valid) return ReferenceBatteryProtocol.Reading.Failed("HID 报告编号不符");
                }
                finally { HidD_FreePreparsedData(prep); }

                trace?.Add("open", true);
                return mouse ? ReadA7UltraFeature(handle, trace) : ReadMonsGeekEvent(handle, trace);
            }
            finally { CloseHandle(handle); }
        }, 1800);
    }

    private static ReferenceBatteryProtocol.Reading ReadA7UltraFeature(IntPtr handle, BatteryReplay.Trace trace)
    {
        var request = BuildMchoseA7V2Request();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool sent = HidD_SetFeature(handle, request, request.Length);
            trace?.Add("set-feature", sent, raw: request, keep: true, nativeError: sent ? null : Marshal.GetLastWin32Error());
            if (!sent) break;

            Thread.Sleep(100);
            var response = new byte[65];
            response[0] = 0x11;
            if (!HidD_GetFeature(handle, response, response.Length))
            {
                trace?.Add("get-feature", false, nativeError: Marshal.GetLastWin32Error());
                continue;
            }

            var frame = response.AsSpan(0, 21);
            bool valid = TryParseA7UltraBattery(frame, out int level, out bool charging);
            // Firmware version and bytes beyond charge state are unnecessary for replay.
            byte[] replay = frame.ToArray();
            Array.Fill(replay, (byte)0xFF, 6, 4);
            Array.Fill(replay, (byte)0xFF, 13, 8);
            trace?.Add("get-feature", valid, raw: valid ? replay : response, keep: valid);
            if (valid)
                return new(level, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                    ReferenceBatteryProtocol.State.Online, "MCHOSE A7 Ultra 2.4G Feature 11/F9", A7UltraName);
        }
        return ReferenceBatteryProtocol.Reading.Failed("A7 Ultra 未取得匹配型号的在线电量应答");
    }

    private static ReferenceBatteryProtocol.Reading ReadMonsGeekEvent(IntPtr handle, BatteryReplay.Trace trace)
    {
        IntPtr signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        if (signal == IntPtr.Zero) return ReferenceBatteryProtocol.Reading.Failed("无法创建异步读取事件");
        try
        {
            if (!HidD_FlushQueue(handle)) return ReferenceBatteryProtocol.Reading.Failed("无法清空旧输入");
            return ReadMonsGeekEvent(new G7Link(handle, signal, 32), trace);
        }
        finally { CloseHandle(signal); }
    }

    internal static ReferenceBatteryProtocol.Reading ReadMonsGeekEvent(ReferenceBatteryProtocol.ILink link, BatteryReplay.Trace trace)
    {
        long deadline = link.Now + 800;
        for (int count = 0; count < 64 && link.Now < deadline; count++)
        {
            var frame = link.Read((int)Math.Max(1, deadline - link.Now));
            if (frame == null) break;
            if (!TryParseMonsGeekBatteryEvent(frame, out int level, out bool charging)) continue;
            trace?.Add("battery-event", true, raw: frame.AsSpan(0, 6).ToArray(), keep: true);
            return new(level, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                ReferenceBatteryProtocol.State.Online, "MonsGeek 2.4G 电量事件 05/88");
        }
        trace?.Add("battery-event", false, result: "no-valid-event");
        return ReferenceBatteryProtocol.Reading.Failed("MonsGeek 本轮未收到新的在线电量事件");
    }
}
