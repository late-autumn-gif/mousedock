using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionCherry
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, int size)
    {
        Require(size is >= 11 and <= 65 && b.Length == size && b[0] == 4 &&
            b[3] == 0x1A && b[4] == 3 && b[5] == 0 && b[6] == 0 && b[7] == 0x55 &&
            b[8] is >= 1 and <= 100, "Invalid CHERRY Evision response");
        return Value(b[8]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = Decode(wire.Exchange(Packet(wire.Output, 4, 0, 0, 0x1A, 3, 0, 0, 0x55),
                b => b.Length == wire.Input && b[0] == 4 && b[3] == 0x1A), wire.Input);
        return result;
    }
}
