using MouseDock.Services.Detection;

/// <summary>
/// RealTek 系只读电量（官方 0x11 0x06 / mouseDeviceRead）。
/// 全部离线断言，不打开接口。
/// </summary>
internal static class RealTekRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string why)
        {
            count++;
            if (!ok) throw new Exception("RealTek: " + why);
        }

        var battery = RealTekHidProtocol.BatteryRequest();
        Check(battery.Length == 21 && battery[0] == 0x11 && battery[1] == 0x06,
            "official battery request header");
        Check(battery.Skip(2).All(x => x == 0), "battery request carries no payload");
        Check(RealTekHidProtocol.IsBatteryRequest(battery), "request shape accepted");

        var allInfo = RealTekHidProtocol.AllInfoRequest();
        Check(allInfo.Length == 65 && allInfo[0] == 0x12 && allInfo[1] == 0x67,
            "official allInfo request header");

        // 构造官方回包：前两字节命令头，其后逐字节取反才是字段。
        byte[] Frame(ushort vid, ushort pid, int level, int charge, int status = 0x88)
        {
            var body = new byte[32];
            body[0] = (byte)(vid & 0xFF); body[1] = (byte)(vid >> 8);
            body[2] = (byte)(pid & 0xFF); body[3] = (byte)(pid >> 8);
            body[8] = (byte)status;
            body[9] = (byte)level;
            body[10] = (byte)charge;
            var response = new byte[34];
            response[0] = 0x11;
            response[1] = 0x06 ^ 0xFF;
            for (int i = 0; i < body.Length; i++) response[i + 2] = (byte)(body[i] ^ 0xFF);
            return response;
        }

        Check(RealTekHidProtocol.TryParseBattery(Frame(0x093A, 0x522C, 75, 0), 0x093A,
                out int level, out bool charging, out var text),
            "official reply parses");
        Check(level == 75 && !charging, "battery and charge decode from the inverted body");
        Check(text.Contains("75") && text.Contains("093A"), "diagnostic names value and VID");

        Check(RealTekHidProtocol.TryParseBattery(Frame(0x093A, 0x522C, 100, 1), 0x093A,
                out int full, out bool fullCharging, out _) && full == 100 && fullCharging,
            "100% with charge flag parses");
        Check(RealTekHidProtocol.TryParseBattery(Frame(0x093A, 0x522C, 0, 0), 0x093A,
                out int empty, out _, out _) && empty == 0,
            "0% is a real reading");

        Check(!RealTekHidProtocol.TryParseBattery(Frame(0x093A, 0x522C, 101, 0), 0x093A,
                out _, out _, out var over), "level above 100 rejected");
        Check(over.Contains("101"), "rejection names the value");
        Check(!RealTekHidProtocol.TryParseBattery(Frame(0x046D, 0x522C, 50, 0), 0x093A,
                out _, out _, out var wrongVid), "VID mismatch rejected");
        Check(wrongVid.Contains("046D"), "rejection names the observed VID");
        Check(!RealTekHidProtocol.TryParseBattery(new byte[34], 0x093A, out _, out _, out _),
            "empty reply rejected");
        Check(!RealTekHidProtocol.TryParseBattery(new byte[10], 0x093A, out _, out _, out _),
            "short reply rejected");
        Check(!RealTekHidProtocol.TryParseBattery(null!, 0x093A, out _, out _, out _),
            "null reply rejected");

        // 只放行用户报告过的凌豹 M9 Pro。
        Check(VendorProtocol.IsRealTekShape(0x093A, 0x522C), "LB M9 Pro admitted");
        Check(!VendorProtocol.IsRealTekShape(0x093A, 0x522D), "other 093A products stay out");
        Check(!VendorProtocol.IsRealTekShape(0x046D, 0x522C), "other vendors stay out");

        return count;
    }
}
