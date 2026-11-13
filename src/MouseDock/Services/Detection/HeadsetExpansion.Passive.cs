using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionPassive
{
    internal static ReferenceBatteryProtocol.Reading DecodeJbl(byte[] b)
    {
        Require(b.Length is >= 2 and <= 64 && b[0] == 8 && b[1] <= 100, "Invalid JBL battery notification");
        return Value(b[1]);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeAula(byte[] raw)
    {
        ReadOnlySpan<byte> b = raw;
        if (b.Length > 0 && b[0] == 0xAB) b = b[1..];
        Require(b.Length >= 7 && b[..3].SequenceEqual(new byte[] { 0x47, 0x4D, 0x86 }), "Invalid AULA headset notification");
        int size = b[3];
        Require(size is 3 or 4 && b.Length >= size + 4, "AULA notification size mismatch");
        int online, level;
        if (size == 3)
        { Require(b[4] == 1, "AULA battery notification type mismatch"); online = b[5]; level = b[6]; }
        else { online = b[4]; level = b[5]; }
        Require(online == 1 && level <= 100, "AULA headset offline or percentage invalid");
        return Value(level);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeSiren(byte[] b)
    {
        Require(b.Length is >= 13 and <= 64 && b[0] == 2 && b[1] == 11 && b[2] == 1 &&
            b[4] <= 9 && !b.AsSpan().SequenceEqual(Packet(b.Length, 2, 11, 1)), "Invalid VGN Siren status/step/echo");
        return Value(-1, diagnostic: $"device-band={b[4] + 1}/10");
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, string family)
    {
        byte[] raw = wire.Wait(b => HeadsetExpansionProtocol.Decode(family, b).Status != ReferenceBatteryProtocol.State.Failed,
            family == "AulaG9" ? 8000 : 3000);
        return HeadsetExpansionProtocol.Decode(family, raw);
    }
    internal static ReferenceBatteryProtocol.Reading ReadSiren(ExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = DecodeSiren(wire.Exchange(Packet(wire.Output, 2, 11, 1),
                b => b.Length >= 5 && b[0] == 2 && b[1] == 11, 2000));
        return result;
    }
}
