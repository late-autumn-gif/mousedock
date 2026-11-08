using MouseDock.Models;
using MouseDock.Services.Detection;

/// <summary>
/// 迈从 DPI 读取协议（官方网页驱动 0x12 0x67 / allInfo）。
/// 全部离线断言，不打开接口。
/// </summary>
internal static class MchoseDpiRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string why)
        {
            count++;
            if (!ok) throw new Exception("MCHOSE DPI: " + why);
        }

        var request = MchoseDpiProtocol.Request();
        Check(request.Length == 64 && request[0] == 0x12 && request[1] == 0x67,
            "official allInfo header");
        Check(request.Skip(2).All(x => x == 0), "allInfo request carries no payload");
        Check(MchoseDpiProtocol.IsRequest(request), "request shape accepted");
        var tampered = (byte[])request.Clone();
        tampered[10] = 1;
        Check(MchoseDpiProtocol.IsRequest(tampered), "length and header are what identify the request");

        byte[] Frame(int slots, int gIndex, int usbIndex, params int[] values)
        {
            var frame = new byte[64];
            frame[0] = 0x12; frame[1] = 0x67;
            frame[2] = 0;                       // profileIndex
            frame[3] = (byte)((3 << 4) | (gIndex & 0x0F));   // gRateIndex=3, gDpiIndex
            frame[4] = (byte)((2 << 4) | (usbIndex & 0x0F)); // usbRateIndex=2, usbDpiIndex
            for (int i = 0; i < 6; i++)
            {
                int v = i < values.Length ? values[i] : 0;
                frame[6 + i * 2] = (byte)(v & 0xFF);
                frame[7 + i * 2] = (byte)(v >> 8);
            }
            frame[18] = (byte)slots;
            frame[19] = 7;                      // sensor
            return frame;
        }

        var info = Frame(4, 1, 2, 400, 800, 1600, 3200);
        Check(MchoseDpiProtocol.TryParse(info, wired: false, out int dpi, out int index, out int slots, out var text),
            "allInfo parses for the wireless profile");
        Check(dpi == 800 && index == 1, "wireless index selects dpi1");
        Check(slots == 4, "slot count comes from dpiSum");
        Check(text.Contains("800") && text.Contains("4"), "diagnostic names the value and slot count");

        Check(MchoseDpiProtocol.TryParse(info, wired: true, out int wiredDpi, out int wiredIndex, out _, out _),
            "allInfo parses for the wired profile");
        Check(wiredDpi == 1600 && wiredIndex == 2, "wired index selects dpi2");

        Check(!MchoseDpiProtocol.TryParse(Frame(0, 0, 0, 400), false, out _, out _, out _, out _),
            "zero slots rejected");
        Check(!MchoseDpiProtocol.TryParse(Frame(7, 0, 0, 400), false, out _, out _, out _, out _),
            "slot count above six rejected");
        Check(!MchoseDpiProtocol.TryParse(Frame(4, 5, 0, 400), false, out _, out _, out _, out _),
            "index past the slot count rejected");
        Check(!MchoseDpiProtocol.TryParse(Frame(4, 0, 0, 10), false, out _, out _, out _, out _),
            "implausibly low DPI rejected");
        Check(!MchoseDpiProtocol.TryParse(Frame(4, 0, 0, 40000), false, out _, out _, out _, out _),
            "implausibly high DPI rejected");
        Check(!MchoseDpiProtocol.TryParse(new byte[64], false, out _, out _, out _, out _),
            "empty frame rejected");
        Check(!MchoseDpiProtocol.TryParse(null!, false, out _, out _, out _, out _),
            "null frame rejected");

        var readSlots = MchoseDpiProtocol.ReadSlots(info);
        Check(readSlots.Length == 6 && readSlots[0] == 400 && readSlots[3] == 3200 && readSlots[4] == -1 && readSlots[5] == -1,
            "slot list keeps order and marks unused entries");

        var device = new DetectedDevice { Kind = DeviceKind.Mouse, Level = 83, Dpi = 1600 };
        Check(device.Dpi == 1600 && device.BatteryRead, "detected device carries DPI alongside the battery");

        return count;
    }
}
