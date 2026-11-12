using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal const string A7UltraRoute = "MchoseA7Ultra";
    internal const string MonsGeekRoute = "MonsGeekBatteryEvent";
    internal const string MonsGeekStatusRoute = "MonsGeekDongleStatus";
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
        route != null && (route == MonsGeekStatusRoute ? MonsGeekRoute : route) == ReportedReceiverRoute(vid, pid, product) &&
        (route switch
        {
            A7UltraRoute => label == "MI_02/COL_02" && page == 0xFF01 && usage == 1 && input == 65 && output == 65 && feature == 65,
            MonsGeekRoute => label == "MI_01/COL_05" && page == 0xFFFF && usage == 1 && input == 32 && output == 0 && feature == 0,
            MonsGeekStatusRoute => label == "MI_02" && page == 0xFFFF && usage == 2 && input == 0 && output == 0 && feature == 65,
            _ => false
        });

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
        HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace) =>
        ReadReportedReceiver(route, channel, trace, false, null);

    internal static ReferenceBatteryProtocol.Reading ReadReportedReceiver(string route,
        HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace, bool readDpi, MouseDpiChange change)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(channel.Path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例");
        HidInventory.ParsePath(channel.Path, out _, out _, out string label);
        if (!MatchesReportedReceiverChannel(route, channel.Vid, channel.Pid, channel.Product,
                label, channel.UsagePage, channel.Usage, channel.Input, channel.Output, channel.Feature))
            return ReferenceBatteryProtocol.Reading.Failed("HID 集合不符");

        bool passive = route == MonsGeekRoute;
        readDpi = readDpi && route == A7UltraRoute && trace == null &&
            (change == null || change.Expected.Protocol == MouseDpiProtocol.MchoseA7Ultra);
        return ReferenceQueryGate.Run(channel.Path, () =>
        {
            IntPtr handle = CreateFile(channel.Path, passive ? GENERIC_READ : GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
                passive ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
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
                bool dpiReport = false;
                try
                {
                    var a7Features = route == A7UltraRoute
                        ? ReadDescriptorReportIds(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps) : null;
                    dpiReport = a7Features?.Contains(MchoseA7UltraDpi.ReportId) == true;
                    bool valid = route switch
                    {
                        A7UltraRoute => a7Features.Contains(0x11),
                        MonsGeekStatusRoute => TryGetReportId(prep, 2, caps.NumberFeatureValueCaps,
                            caps.NumberFeatureButtonCaps, out byte featureId) && featureId == 0,
                        _ => TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps,
                            out byte inputId) && inputId == 0x05
                    };
                    if (!valid) return ReferenceBatteryProtocol.Reading.Failed("HID 报告编号不符");
                }
                finally { HidD_FreePreparsedData(prep); }

                trace?.Add("open", true);
                return route switch
                {
                    A7UltraRoute => ReadA7UltraWithDpi(handle, trace, readDpi && dpiReport, change),
                    MonsGeekStatusRoute => ReadMonsGeekStatus(handle, trace),
                    _ => ReadMonsGeekEvent(handle, trace)
                };
            }
            finally { CloseHandle(handle); }
        }, readDpi ? 3500 : 1800);
    }

    private static ReferenceBatteryProtocol.Reading ReadA7UltraWithDpi(IntPtr handle, BatteryReplay.Trace trace,
        bool readDpi, MouseDpiChange change)
    {
        var battery = ReadA7UltraFeature(handle, trace);
        if (!readDpi || battery.Model != A7UltraName) return battery;

        bool Write(byte[] frame) => HidD_SetFeature(handle, frame, frame.Length);
        byte[] Exchange(byte[] request)
        {
            if (!Write(request)) return null;
            Thread.Sleep(100);
            var response = new byte[MchoseA7UltraDpi.ReportLength];
            response[0] = MchoseA7UltraDpi.ReportId;
            return HidD_GetFeature(handle, response, response.Length) ? response : null;
        }

        bool Select(byte[] request)
        {
            if (!Write(request)) return false;
            Thread.Sleep(100);
            return true;
        }

        return battery with { Dpi = MchoseA7UltraDpi.ReadOrChange(change, Exchange, Select,
            () => ReadA7UltraFeature(handle, null).Model == A7UltraName) };
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

    internal static byte[] BuildMonsGeekStatusRequest()
    {
        // MonsGeek V4 sends the direct F7 status request with zero padding.
        var request = new byte[65];
        request[1] = 0xF7;
        return request;
    }

    internal static bool TryParseMonsGeekStatus(ReadOnlySpan<byte> frame, out int level, out string reason)
    {
        level = -1;
        reason = "unexpected-status-layout";
        // Windows includes the report ID. The vendor driver's payload starts at index 1 here.
        if (frame.Length is not (10 or 65) || frame[0] != 0 || frame[1] > 1 || frame[7] is not (1 or 3))
            return false;
        if (frame[4] != 0 || frame[6] != 1 || frame[9] != 0)
        {
            reason = "keyboard-offline-busy-or-boot";
            return false;
        }
        // The receiver initializes its cached percentage to 0. The reply carries no update age.
        if (frame[2] is 0 or > 100)
        {
            reason = "battery-unavailable";
            return false;
        }
        level = frame[2];
        reason = "receiver-cache";
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading ReadMonsGeekStatus(Func<byte[], bool> send,
        Func<byte[]> receive, Action<int> delay, BatteryReplay.Trace trace)
    {
        var request = BuildMonsGeekStatusRequest();
        int previousLevel = -1;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool sent = send(request);
            trace?.Add("status-set-feature", sent, raw: request, keep: true);
            if (!sent) return ReferenceBatteryProtocol.Reading.Failed("MonsGeek F7 查询发送失败");

            delay(35);
            var response = receive();
            if (!TryParseMonsGeekStatus(response, out int level, out string reason) || response.Length != 65)
            {
                trace?.Add("status-get-feature", false, raw: response,
                    result: response == null ? "no-response" : response.Length != 65 ? "unexpected-length" : reason);
                previousLevel = -1;
                continue;
            }

            var replay = response.AsSpan(0, 10).ToArray();
            replay[3] = replay[5] = replay[8] = 0; // Preserve keyboard status only.
            trace?.Add("status-get-feature", true, raw: replay, keep: true, result: "receiver-cache");
            if (level == previousLevel)
                return new(level, false, false, ReferenceBatteryProtocol.State.Cached,
                    "MonsGeek F7 接收器缓存，采样时间与充电状态未知");
            previousLevel = level;
        }
        return ReferenceBatteryProtocol.Reading.Failed("MonsGeek F7 未取得一致的在线键盘缓存读数");
    }

    private static ReferenceBatteryProtocol.Reading ReadMonsGeekStatus(IntPtr handle, BatteryReplay.Trace trace)
    {
        bool Send(byte[] request)
        {
            bool sent = HidD_SetFeature(handle, request, request.Length);
            if (!sent) trace?.Add("status-native-set", false, nativeError: Marshal.GetLastWin32Error());
            return sent;
        }
        byte[] Receive()
        {
            var response = new byte[65];
            if (HidD_GetFeature(handle, response, response.Length)) return response;
            trace?.Add("status-native-get", false, nativeError: Marshal.GetLastWin32Error());
            return null;
        }
        return ReadMonsGeekStatus(Send, Receive, Thread.Sleep, trace);
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
