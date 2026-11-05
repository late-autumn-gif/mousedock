using MouseDock.Models;
using MouseDock.Services.Detection;

/// <summary>
/// 1.4.3 用户报告（2026-11-05 16:26，046D:C539 + 一代 GPW）的离线回归：
/// 报告里「已识别设备: 0 台」，HID 扫描诊断停在「接口=25 … 归并=4」，
/// 蓝牙扫描一次都没开始 —— 说明扫描卡在归并后的罗技 HID++ 探针里。
/// 这里用合成帧断言修好之后的行为，不需要真机，也不发任何 HID 帧。
/// </summary>
internal static class LogitechReceiverRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception(message);
        }

        static byte[] Frame(byte b4, byte b5, byte b6)
        {
            var r = new byte[20];
            r[0] = 0x11;
            r[1] = 0x01;
            r[2] = 0x07;
            r[4] = b4;
            r[5] = b5;
            r[6] = b6;
            return r;
        }

        // 0x1001 按 HID++ 文档是大端 mV。名称只在确认初代 GPW 时起作用；
        // 名称读不到时也必须按电压换算，否则 1.4.3 上就是「GPW 永远没有电量」。
        Check(LogitechHidpp.TryParseBattery(0x1001, Frame(0x10, 0x00, 0x00), out int level, out bool charging, false) &&
              level == 93 && !charging,
            "0x1001 4096mV must estimate ~93% even when the device name was not read");
        Check(LogitechHidpp.TryParseBattery(0x1001, Frame(0x0E, 0xD8, 0x00), out level, out _, false) && level == 47,
            "0x1001 3800mV must estimate 47% without the GPW name");
        Check(LogitechHidpp.TryParseBattery(0x1001, Frame(0x10, 0x16, 0x80), out level, out charging, false) &&
              level == 50 && charging,
            "0x1001 4118mV charging anchor stays 50% and reports charging");
        Check(!LogitechHidpp.TryParseBattery(0x1001, Frame(0x03, 0xE8, 0x00), out _, out _, false),
            "0x1001 outside the voltage range must be rejected, not turned into a fake level");

        // 0x81 = 满电。没有名称确认时不能再无条件 100%，必须电压也到满电段。
        Check(!LogitechHidpp.TryParseBattery(0x1001, Frame(0x0E, 0xD8, 0x81), out _, out _, false),
            "0x81 at 3800mV must not claim 100% without GPW confirmation");
        Check(LogitechHidpp.TryParseBattery(0x1001, Frame(0x10, 0x5A, 0x81), out level, out _, false) && level == 100,
            "0x81 at 4186mV is a genuine full charge");
        Check(LogitechHidpp.TryParseBattery(0x1001, Frame(0x0E, 0xD8, 0x81), out level, out _, true) && level == 100,
            "Confirmed original GPW keeps the 0x81 full-charge shortcut");

        // 百分比特征不能被电压逻辑抢走。
        Check(LogitechHidpp.TryParseBattery(0x1000, Frame(0x4D, 0x00, 0x00), out level, out _, false) && level == 77,
            "0x1000 still reports the device percentage");
        Check(LogitechHidpp.TryParseBattery(0x1004, Frame(0x4D, 0x00, 0x01), out level, out charging, false) &&
              level == 77 && charging,
            "0x1004 unified battery keeps its percentage and charging flag");

        // C539 接收器：名称来自 HID++，电量读不到时也要列出「罗技 Lightspeed 接收器」。
        var receiver = new HidDiscovery.Bucket { Vid = 0x046D, Pid = 0xC539, Product = "USB Receiver" };
        receiver.Kind = DeviceKind.Mouse;
        receiver.Counts[DeviceKind.Mouse] = 1;
        receiver.Counts[DeviceKind.Keyboard] = 1;
        var device = receiver.ToDevice();
        Check(device != null && device.Name == "罗技 Lightspeed 接收器" &&
              device.Kind == DeviceKind.Mouse && device.Transport == "2.4G",
            "C539 receiver must stay listed as a 2.4G mouse receiver even when HID++ cannot be read");

        // 扫描卡死的那一段：罗技接口只挑最像 HID++ 的两条，20 字节长报文优先。
        var longChannel = new HidDiscovery.VendorCollectionInfo { Input = 20, Output = 20 };
        var shortChannel = new HidDiscovery.VendorCollectionInfo { Input = 7, Output = 7 };
        var dfuChannel = new HidDiscovery.VendorCollectionInfo { Input = 32, Output = 32 };
        Check(HidDiscovery.LogiInterfaceRank(longChannel) > HidDiscovery.LogiInterfaceRank(shortChannel),
            "HID++ long report channel must be probed before the 7-byte short channel");
        Check(HidDiscovery.LogiInterfaceRank(shortChannel) > HidDiscovery.LogiInterfaceRank(dfuChannel),
            "32-byte non-HID++ collection must be last, so a sleeping receiver cannot burn the budget first");
        Check(LogitechHidpp.ProbeBudgetMs > 0 && LogitechHidpp.ProbeBudgetMs <= 4000,
            "Per-receiver HID++ budget must stay small enough to keep one scan responsive");

        // G HUB 幽灵：1.4.3 报告里 C232 的 PnP 是 LOGIDEVICE + LGVirHid/LGBusEnum，
        // 旧规则只认 LGHUBDEVICE/logi_joy_*，所以它被当成一台真键盘收进了设备列表。
        Check(HidDiscovery.ExclusionReason(new[]
        {
            ("LOGIDEVICE\\VID_046D&PID_C232\\1&79F5D87&0&01", "LGVirHid"),
            ("ROOT\\SYSTEM\\0001", "LGBusEnum")
        }) != null, "G HUB LOGIDEVICE/LGVirHid ghost must be excluded");
        Check(HidDiscovery.ExclusionReason(new[]
        {
            ("ROOT\\HIDCLASS\\0000", "gvinput")
        }) != null, "software-created virtual HID bus must be excluded");
        Check(HidDiscovery.ExclusionReason(new[]
        {
            ("USB\\VID_046D&PID_C539\\A&1E45AA90&0&1", "usbccgp"),
            ("USB\\ROOT_HUB30\\7&DACBA&0&0", "USBHUB3")
        }) == null, "a real USB receiver must never be excluded by the ghost rules");

        return checks;
    }
}
