using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionRog
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length is >= 9 and <= 64 && b[0] == 0xCC && b[1] == 0x12 && b[2] == 7 &&
            !(b[5] == 255 && b[6] == 170) && !b.AsSpan().SequenceEqual(Packet(b.Length, 0xCC, 0x12, 7)) &&
            b[6] <= 100, "Invalid ROG headset battery reply/echo");
        return Value(b[6]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        wire.CheckInput = b => Require(b.Length < 3 || b[0] != 0xCC || b[1] != 255 || b[2] != 170,
            "ROG headset negative acknowledgement");
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = Decode(wire.Exchange(Packet(wire.Output, 0xCC, 0x12, 7),
                b => b.Length >= 3 && b[0] == 0xCC && b[1] == 0x12 && b[2] == 7, 2000));
        try
        {
            var charge = wire.Exchange(Packet(wire.Output, 0xCC, 0x12, 8),
                b => b.Length >= 9 && b[0] == 0xCC && b[1] == 0x12 && b[2] == 8 &&
                    b[5] <= 1 && !(b[5] == 255 && b[6] == 170), 2000);
            return Value(result.Level, charge[5] == 1);
        }
        catch (System.IO.IOException) { return result; }
    }
}
