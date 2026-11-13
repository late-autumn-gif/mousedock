using System;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionRazerFeature
{
    internal static ReferenceBatteryProtocol.Reading Decode2020(byte[] b)
    {
        Require(b.Length == 64 && b.AsSpan(0, 9).SequenceEqual(
            new byte[] { 255, 15, 5, 254, 18, 4, 31, 8, 5 }) && (b[12] << 8 | b[13]) != 0 && b[14] <= 100,
            "BlackShark 2020 has no fresh powered battery response");
        return Value(-1, b[11] == 9, b[11] == 9 ? "External power; frozen numeric omitted" :
            $"device-scale={b[14]}/100; exact percentage unavailable");
    }
    internal static ReferenceBatteryProtocol.Reading Read2020(ExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            var packet = Packet(64, 255, 10, 0, 253, 4, 18, 241, 2, 5);
            Require(wire.Link.SetFeature(packet), "BlackShark 2020 query failed");
            wire.Trace?.Add("expansion-set-feature", true, raw: packet, keep: true);
            long deadline = wire.Link.Now + 600;
            result = null;
            for (int poll = 0; poll < 5 && wire.Link.Now + 100 < deadline; poll++)
            {
                wire.Link.Delay(100); wire.CheckTime();
                var b = wire.Link.GetFeature(255, 64); wire.CheckTime();
                wire.Trace?.Add("expansion-get-feature", b != null, raw: b, keep: true);
                if (b?.Length != 64 || !b.AsSpan(0, 9).SequenceEqual(
                    new byte[] { 255, 15, 5, 254, 18, 4, 31, 8, 5 })) continue;
                result = Decode2020(b); break;
            }
            Require(result != null, "No fresh BlackShark 2020 reply");
        }
        return result;
    }
    internal static ReferenceBatteryProtocol.Reading Read2023(ExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            var packet = Packet(91, 0, 0, 0x3F, 0, 0, 0, 2, 7, 0x80);
            for (int i = 3; i < 89; i++) packet[89] ^= packet[i];
            Require(wire.Link.SetFeature(packet), "Razer control query failed");
            wire.Trace?.Add("expansion-set-feature", true, raw: packet, keep: true);
            result = null;
            for (int poll = 0; poll < 8; poll++)
            {
                wire.Link.Delay(80); wire.CheckTime();
                var b = wire.Link.GetFeature(0, 91); wire.CheckTime();
                if (b?.Length != 91 || !b.AsSpan(0, 9).SequenceEqual(
                    new byte[] { 0, 2, 0x3F, 0, 0, 0, 2, 7, 0x80 }) || b[90] != 0) continue;
                byte xor = 0; for (int i = 3; i < 89; i++) xor ^= b[i];
                if (xor != b[89]) continue;
                wire.Trace?.Add("expansion-get-feature", true, raw: b, keep: true);
                result = Value(-1, diagnostic: $"device-scale={b[10]}/255; exact percentage unavailable");
                break;
            }
            Require(result != null, "No valid Razer control battery response");
        }
        return result;
    }
}
