using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionHecate
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, bool tws)
    {
        Require(b.Length is >= 8 and <= 64 && b[0] == 0xD0 && b[1] == 0x35 && b[2] == 0xBB &&
            b[4] == 0 && b[3] + 4 <= b.Length, "Invalid HECATE response framing");
        if (!tws)
        {
            Require(b[3] >= 4 && b[5] == 1 && b[6] <= 100 && b[7] <= 2,
                "HECATE offline or invalid battery status");
            return Value(b[6], diagnostic: $"HECATE charge enum={b[7]}; charging unknown");
        }
        Require(b[3] >= 7 && (b[5] & 3) != 0, "HECATE earbuds offline or incomplete");
        var values = new List<int>();
        for (int i = 0; i < 2; i++)
        {
            if ((b[5] & (1 << i)) == 0) continue;
            int level = b[6 + i] & 127;
            Require(level <= 100, "HECATE earbud percentage out of range");
            values.Add(level);
        }
        return Value(values.Min(), diagnostic: "Minimum online earbud percentage; case and charging unknown");
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, bool tws)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = Decode(wire.Exchange(Packet(wire.Output, 0xD0, 0x35, 0xBB, 1, 0),
                b => b.Length >= 5 && b[0] == 0xD0 && b[1] == 0x35 && b[2] == 0xBB, 750), tws);
        return result;
    }
}
