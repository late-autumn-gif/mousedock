using MouseDock.Services.Detection;

// 公版 2.4G 接收器（248A/249A:5B2F/5C2F）的只读身份帧解析。
// 正向向量来自 249A:5C2F 用户报告的实机回帧；其余为同结构的构造帧，不是硬件捕获。
internal static class Receiver33Regression
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        static byte[] Hex(string text) => Convert.FromHexString(text);

        // 实机捕获：J315 / 86% / 未充电 / 在线
        var inphic = Hex("001000010B4A3331358501150100560100000000000000000000000000000000D6");
        Check(VendorProtocol.TryParseReceiver33Frame(inphic, out int jLevel, out bool jCharging, out string jModel, out bool jAula),
            "Captured Inphic frame must parse");
        Check(jLevel == 86 && !jCharging && jModel == "J315" && !jAula, "Captured frame decodes to J315/86/not charging");

        // 构造的狼蛛侧同结构帧：M137 / 82%
        var aula = new byte[33];
        aula[1] = 0x30; aula[3] = 0x01; aula[4] = 0x0B;
        "M137"u8.CopyTo(aula.AsSpan(5));
        aula[14] = 82; aula[15] = 0x01;
        Check(VendorProtocol.TryParseReceiver33Frame(aula, out int aLevel, out bool aCharging, out string aModel, out bool aAula),
            "AULA-shaped frame must parse");
        Check(aLevel == 82 && !aCharging && aModel == "M137" && aAula, "AULA frame keeps its own head marker");

        // 32 字节无报告编号也要能解
        Check(VendorProtocol.TryParseReceiver33Frame(aula.AsSpan(1), out _, out _, out string noIdModel, out _) && noIdModel == "M137",
            "Report-id-less frame parses too");

        // 结构不合法的帧一律拒
        var bad = (byte[])inphic.Clone(); bad[13] = 101;
        Check(!VendorProtocol.TryParseReceiver33Frame(bad, out _, out _, out _, out _), "Level 101 rejected");
        bad = (byte[])inphic.Clone(); bad[15] = 0;
        Check(!VendorProtocol.TryParseReceiver33Frame(bad, out _, out _, out _, out _), "Offline frame rejected");
        bad = (byte[])inphic.Clone(); bad[1] = 0x40;
        Check(!VendorProtocol.TryParseReceiver33Frame(bad, out _, out _, out _, out _), "Unknown command byte rejected");
        bad = (byte[])inphic.Clone(); bad[5] = 0x1F;
        Check(!VendorProtocol.TryParseReceiver33Frame(bad, out _, out _, out _, out _), "Non-printable model rejected");
        bad = (byte[])inphic.Clone(); bad[3] = 0x0A;
        Check(!VendorProtocol.TryParseReceiver33Frame(bad, out _, out _, out _, out _), "Wrong version byte rejected");

        // 只在这几个公共接收器号段上启用，且让开黑爵/多彩
        Check(VendorProtocol.IsGenericReceiver33(0x248A, 0x5B2F, "Wireless-Receiver"), "248A:5B2F generic receiver accepted");
        Check(VendorProtocol.IsGenericReceiver33(0x249A, 0x5C2F, "Wireless-Receiver"), "249A:5C2F generic receiver accepted");
        Check(VendorProtocol.IsGenericReceiver33(0x248A, 0x5B2F, ""), "Empty product string still accepted");
        Check(!VendorProtocol.IsGenericReceiver33(0x248A, 0x5B2F, "AJ159 Receiver"), "AJAZZ AJ159 collision excluded");
        Check(!VendorProtocol.IsGenericReceiver33(0x248A, 0x5C2F, "DELUX Receiver"), "DELUX collision excluded");
        Check(!VendorProtocol.IsGenericReceiver33(0x046D, 0xC539, "USB Receiver"), "Logitech receiver not claimed");
        Check(!VendorProtocol.IsGenericReceiver33(0x248A, 0x5C2E, "Wireless-Receiver"), "Wired IN9 PID not claimed");

        // 策略白名单必须和旁路一致：2026-11-08 之前 CanQuery 只放行 089D:062F，
        // 248A/249A 的 5B2F/5C2F 在 QueryGenericReceiver33 第一步就被 continue 掉，
        // 33 字节旁路从未执行过。
        Check(DeviceRoutes.IsKnown(0x248A, 0x5B2F, "Wireless-Receiver"), "Policy admits 248A:5B2F generic receiver");
        Check(DeviceRoutes.IsKnown(0x248A, 0x5C2F, "Wireless-Receiver"), "Policy admits 248A:5C2F generic receiver");
        Check(DeviceRoutes.IsKnown(0x249A, 0x5C2F, ""), "Policy admits 249A:5C2F without a product string");
        Check(!DeviceRoutes.IsKnown(0x248A, 0x5C2F, "AJ159 Receiver"), "Policy keeps the AJAZZ exclusion");
        Check(!DeviceRoutes.IsKnown(0x248A, 0x5B2F, "DELUX Receiver"), "Policy keeps the DELUX exclusion");

        // 读不到身份帧时的名字：不能回退成产品串 "Wireless-Receiver"，
        // 也不能把鼠标接收器标成键盘。品牌按 VID 定，型号不猜。
        Check(VendorProtocol.Receiver33FallbackName(0x089D, null) == "狼蛛 2.4G 接收器（型号未确认）",
            "AULA receiver without a model keeps its brand");
        Check(VendorProtocol.Receiver33FallbackName(0x248A, null) == "英菲克 2.4G 接收器（型号未确认）",
            "Inphic receiver without a model keeps its brand");
        Check(VendorProtocol.Receiver33FallbackName(0x249A, "") == "英菲克 2.4G 接收器（型号未确认）",
            "Empty model string takes the same fallback");
        Check(VendorProtocol.Receiver33FallbackName(0x089D, "M137") == "狼蛛 SC580",
            "Verified AULA model still resolves to the retail name");
        Check(VendorProtocol.Receiver33FallbackName(0x248A, "J315") == "英菲克 J315",
            "Inphic model renders as brand plus code");

        return assertions;
    }
}

