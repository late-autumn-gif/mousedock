using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionRazer
{
    internal static byte[] Request()
    {
        var b = Packet(91,0,0,0x9F,0,0,0,2,7,0x80);
        for (int i = 3; i < 89; i++) b[89] ^= b[i];
        return b;
    }
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length == 91 && b[0] == 0 && b[1] == 2 && b[2] == 0x9F &&
            b[3] == 0 && b[4] == 0 && b[5] == 0 && b[6] == 2 &&
            b[7] == 7 && b[8] == 0x80 && b[90] == 0,"Invalid Razer keyboard response");
        byte xor = 0;
        for (int i = 3; i < 89; i++) xor ^= b[i];
        Require(xor == b[89],"Razer keyboard XOR mismatch");
        return Value(-1) with
        { Diagnostic = $"device-scale={b[10]}/255; exact percentage unavailable" };
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading value = null;
        for (int i = 0; i < 2; i++) value = Decode(wire.FeatureRead(Request(),100));
        return value;
    }
}
