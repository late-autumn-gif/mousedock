using System;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionHyperX
{
    internal static ReferenceBatteryProtocol.Reading DecodeCloud3(byte[] b)
    {
        Require(b.Length is >= 5 and <= 64 && b[0] == 0x66 && b[1] is 0x89 or 0x0D &&
            (b[2] != 0 || b[3] != 0) && b[4] <= 100, "Invalid HyperX Cloud III battery response");
        return Value(b[4]);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeCloud2(byte[] b)
    {
        Require(b.Length == 20 && b.AsSpan(0, 4).SequenceEqual(new byte[] { 6, 255, 187, 2 }) &&
            b.AsSpan(4).ContainsAnyExcept((byte)0) && b[7] <= 100, "Invalid HyperX Cloud II battery response");
        return Value(b[7]);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeAlpha2(byte[] b)
    {
        Require(b.Length is >= 7 and <= 64 && b[0] == 0x51 && b[1] == 2 && b[2] <= 100,
            "Invalid HyperX Cloud Alpha2 battery response");
        return Value(b[2], (b[6] & 128) != 0);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, string family)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            byte[] packet = family switch
            {
                "Cloud3" => Packet(wire.Output is >= 62 and <= 64 ? wire.Output : wire.Feature, 0x66, 0x89),
                "Cloud2" => Packet(wire.Output, 6, 255, 187, 2, 0),
                _ => Packet(wire.Output, 0x50, 2)
            };
            wire.Drain();
            if (family == "Cloud3" && wire.Output is < 62 or > 64)
            {
                packet = Packet(wire.Feature, 0x66, 0x89);
                Require(wire.Link.SetFeature(packet), "HyperX Feature query failed");
                wire.Trace?.Add("expansion-set-feature", true, raw: packet, keep: true);
            }
            else wire.Send(packet);
            if (family is "Cloud3" or "Cloud2") wire.Link.Delay(100);
            var b = wire.Wait(b => b.Length >= 2 && (family == "Cloud3" ? b[0] == 0x66 && b[1] is 0x89 or 0x0D :
                family == "Cloud2" ? b[0] == 6 && b[1] == 255 : b[0] == 0x51 && b[1] == 2), 2000);
            result = family switch
            {
                "Cloud3" => DecodeCloud3(b), "Cloud2" => DecodeCloud2(b), _ => DecodeAlpha2(b)
            };
        }
        return result;
    }
}
