using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionLangtu
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length == 64 && b.AsSpan(0, 5).SequenceEqual(new byte[] { 0xAA,0x55,0xCC,0x33,0x0E }) &&
            b[5] <= 100 && b[6] is 1 or 3, "Invalid Langtu battery or connection state");
        return Value(b[5]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading value = null;
        for (int i = 0; i < 2; i++)
            value = Decode(wire.Exchange(Packet(65, 0, 0xAA, 0x55, 0xCC, 0x33, 0x0E),
                b => b.Length == 64 && b[0] == 0xAA && b[4] == 0x0E, 1200));
        return value;
    }
}
