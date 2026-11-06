using MouseDock.Services.Detection;

/// <summary>
/// 前行者 FREQ 方案（A8A4/A8A5:2255）的离线回归：
/// 证据来自官方 E HUB 目录 + FREQ SDK 的参考实现（wireless_x23_family），
/// 这里只验证请求组帧和严格应答解析，不需要真机，也不发任何 HID 帧。
/// </summary>
internal static class FreqRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception(message);
        }

        static byte[] RawA(byte marker, int markerAt, byte percent, byte charging)
        {
            // 65 字节带编号输入：报告编号 F0，body 从 raw[1] 开始。
            var raw = new byte[65];
            raw[0] = 0xF0;
            raw[1 + markerAt] = marker;
            raw[2 + markerAt] = 0x30;
            raw[1 + markerAt + 8] = percent;
            raw[1 + markerAt + 9] = charging;
            return raw;
        }

        static byte[] RawC(byte percent, byte charging)
        {
            var raw = new byte[64];
            raw[0] = 0xF0;
            raw[1] = 0x30;
            raw[8] = percent;
            raw[9] = charging;
            return raw;
        }

        // ── 设备门：VID/PID + 精确产品名，页面 FF01/0010 由运行时描述符校验 ──
        Check(VendorProtocol.IsFreqMouse(0xA8A5, 0x2255, "EWEADN X23SE"), "Reported 2.4G X23SE identity must be accepted");
        Check(VendorProtocol.IsFreqMouse(0xA8A4, 0x2255, "EWEADN X23SE"), "Wired X23SE shares the same FREQ profile");
        Check(VendorProtocol.IsFreqMouse(0xA8A5, 0x2255, "eweadn x23se"), "Product matching is case-insensitive");
        Check(!VendorProtocol.IsFreqMouse(0xA8A5, 0x2255, "EWEADN Z7"), "Only catalogued FREQ product names pass");
        Check(!VendorProtocol.IsFreqMouse(0xA8A5, 0x2255, "名龙星球 MX301"), "The same PID with another brand must not use FREQ");
        Check(!VendorProtocol.IsFreqMouse(0x3837, 0x100B, "MCHOSE A7 V2 Ultra+"), "Other vendor/PID pairs are untouched");
        Check(VendorProtocol.FreqSchemeFor("EWEADN X23SE") == "dual", "X23SE uses the dual generation probe profile");
        Check(VendorProtocol.FreqSchemeFor("EWEADN X23Pro") == "c", "X23Pro profile stays on scheme c");
        Check(VendorProtocol.FreqSchemeFor("unknown") == null, "Unknown products have no scheme");

        // ── 请求组帧：完全照参考实现 ──
        var a = VendorProtocol.BuildFreqSchemeAPacket(0xF0, 65);
        Check(a != null && a.Length == 65 && a[0] == 0xF0 &&
              a[1] == 0x55 && a[2] == 0x30 && a[3] == 0x01 && a[4] == 0x0B && a[5] == 0x2E &&
              a[6] == 1 && a[7] == 1 && a[8] == 1 && a[9] == 0 && a[10] == 0 && a[64] == 0,
            "Scheme a packet must be report F0 + 55 30 01 0B 2E 01 01 01, zero padded to 65");
        Check(VendorProtocol.BuildFreqSchemeAPacket(0, 65) == null, "Unnumbered scheme a layout is not guessed");
        Check(VendorProtocol.BuildFreqSchemeAPacket(0xF0, 9) == null, "Out-of-range report size is rejected");

        var c = VendorProtocol.BuildFreqSchemeCPacket();
        Check(c.Length == 64 && c[0] == 0xF0 && c[1] == 0x30 && c[2] == 0xA5 && c[3] == 0x0B && c[4] == 0x2E &&
              c[5] == 1 && c[6] == 1 && c[7] == 1 && c[8] == 0 && c[9] == 0 && c[63] == 0,
            "Scheme c packet must be F0 30 A5 0B 2E 01 01 01, zero padded to 64");

        var gen = VendorProtocol.BuildFreqGenerationPacket();
        Check(gen.Length == 64 && gen[0] == 0xF0 && gen[1] == 0x52 && gen[2] == 0,
            "Dual profile generation probe must be F0 52 + zeros");

        // ── 应答帧筛选与解析 ──
        var aa0 = RawA(0xAA, 0, 77, 0);
        Check(VendorProtocol.IsFreqBatteryFrame(aa0, "a", 0xF0), "AA 30 marker at body[0] is a battery frame");
        Check(VendorProtocol.TryParseFreqFrame(aa0, "a", 0xF0, 65, out int level, out bool charging) &&
              level == 77 && !charging, "Scheme a decodes battery at marker+8 and charge at marker+9");

        var f0At1 = RawA(0xF0, 1, 42, 1);
        Check(VendorProtocol.IsFreqBatteryFrame(f0At1, "a", 0xF0), "F0 30 marker at body[1] is also a battery frame");
        Check(VendorProtocol.TryParseFreqFrame(f0At1, "a", 0xF0, 65, out level, out charging) &&
              level == 42 && charging, "Scheme a honours the second marker offset and charge flag");

        Check(!VendorProtocol.TryParseFreqFrame(aa0, "a", 0xF0, 64, out _, out _),
            "A frame shorter/longer than the descriptor input size is rejected");
        var wrongId = RawA(0xAA, 0, 77, 0);
        wrongId[0] = 0x0A;
        Check(!VendorProtocol.TryParseFreqFrame(wrongId, "a", 0xF0, 65, out _, out _),
            "A battery frame with another report id is rejected");
        var badPercent = RawA(0xAA, 0, 101, 0);
        Check(!VendorProtocol.TryParseFreqFrame(badPercent, "a", 0xF0, 65, out _, out _),
            "Out-of-range battery percentage is rejected");
        var badCharge = RawA(0xAA, 0, 50, 2);
        Check(!VendorProtocol.TryParseFreqFrame(badCharge, "a", 0xF0, 65, out _, out _),
            "Charge flag outside 0/1 is rejected");
        var noMarker = new byte[65];
        noMarker[0] = 0xF0;
        Check(!VendorProtocol.IsFreqBatteryFrame(noMarker, "a", 0xF0) &&
              !VendorProtocol.TryParseFreqFrame(noMarker, "a", 0xF0, 65, out _, out _),
            "A frame without the FREQ marker is not a battery reading");

        var schemeC = RawC(55, 1);
        Check(VendorProtocol.IsFreqBatteryFrame(schemeC, "c", 0xF0), "Scheme c frame starts with 30 after the report id");
        Check(VendorProtocol.TryParseFreqFrame(schemeC, "c", 0xF0, 64, out level, out charging) &&
              level == 55 && charging, "Scheme c decodes body[7]/body[8]");
        Check(!VendorProtocol.TryParseFreqFrame(schemeC, "c", 0x0A, 64, out _, out _),
            "Scheme c requires the F0 report id");
        Check(!VendorProtocol.TryParseFreqFrame(RawC(55, 1), "a", 0xF0, 64, out _, out _),
            "A scheme c frame must not be accepted as scheme a");

        return checks;
    }
}
