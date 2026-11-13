using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionBolutek
{
    internal static byte[] Request(int size = 64) => Packet(size, 0x55, 0x65, 1);
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] raw, bool v9Pro = false, bool zhou = false)
    {
        Require(raw.Length == 64 && raw[0] == 0x55 && raw[1] == 0x65 &&
            !raw.AsSpan().SequenceEqual(Request()), "Invalid Bolutek battery report/echo");
        Require(raw[2] <= 100, "Bolutek percentage out of range");
        if (zhou) return Value(raw[2]);
        if (v9Pro)
        {
            if (raw[3] is 0 or 3)
                return Value(-1, diagnostic: "External power; frozen receiver percentage omitted");
            Require(raw[3] == 2, "V9 Pro is not reporting a fresh on-battery percentage");
        }
        else Require(raw[3] is 2 or 3 or 4, "Bolutek headset unavailable");
        return Value(raw[2]);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeMhub(byte[] raw)
    {
        Require(raw.Length == 64 && raw[0] == 0x55 && raw[1] == 0xAA && raw[2] == 1,
            "Invalid MHUB headset header");
        Require(raw[3] <= 1 && raw[4] == 3 && raw[5] == 0 && raw[6] == 1 && raw[7] == 11,
            "MHUB headset command mismatch");
        byte checksum = 0;
        if (raw[3] == 1) for (int i = 3; i < 10; i++) checksum ^= raw[i];
        Require(raw[10] == checksum && raw[8] != 0 && (raw[9] & 127) <= 100,
            "MHUB headset checksum/online/percentage invalid");
        return Value(raw[9] & 127);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, string family)
    {
        if (family == "AtkN9V2")
            wire.CheckInput = b =>
            {
                if (b.Length < 2 || b[0] != 0x55 || b[1] != 0x0A) return;
                Require(b.Length == 64 && b[2] <= 1 && b[2] == 0, "ATK N9 V2 disconnected");
            };
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            byte[] query = family == "Mhub" ? Packet(wire.Output, 0x55, 0xAA, 1, 0, 1, 0, 1, 11, 0) :
                Request(wire.Output);
            var b = wire.Exchange(query, b => b.Length == 64 && b[0] == 0x55 &&
                b[1] == (family == "Mhub" ? 0xAA : 0x65), 2000);
            result = family == "Mhub" ? DecodeMhub(b) : Decode(b, family == "V9Pro", family == "Zhou");
        }
        wire.Drain();
        return result;
    }
}
