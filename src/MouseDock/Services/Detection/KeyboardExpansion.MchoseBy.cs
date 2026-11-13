using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionMchoseBy
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] raw)
    {
        Require(raw.Length is >= 20 and <= 65 && raw.AsSpan(20).IndexOfAnyExcept((byte)0) < 0,
            "Invalid MCHOSE report19 padding");
        var b = raw.AsSpan(0, 20);
        Require(b[0] == 19 && b[1] == 74 && b[2] == 1 && b[3] == 0 && b[5] <= 100 &&
            (b[6] & 15) <= 1 && (b[6] >> 4) <= 1 &&
            !b.SequenceEqual(Packet(20, 19, 74, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 94)),
            "Invalid MCHOSE report19 battery");
        return Value(b[5]);
    }
    internal static byte[] Request(int output)
    {
        var b = Packet(output, 19, 74, 1);
        b[19] = 94;
        return b;
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = Decode(wire.Exchange(Request(wire.Output),
                b => b.Length >= 20 && b[0] == 19 && b[1] == 74, 1500));
        return result;
    }
}
