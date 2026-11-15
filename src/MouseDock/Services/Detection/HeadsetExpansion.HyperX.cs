using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionHyperX
{
    private const byte Cloud3ReportId = 0x66;
    private const byte Cloud3BatteryRequest = 0x89;
    private const byte Cloud3BatteryEvent = 0x0d;

    private static bool IsCloud3BatteryReport(byte[] report) =>
        report.Length >= 2 && report[0] == Cloud3ReportId &&
        report[1] is Cloud3BatteryRequest or Cloud3BatteryEvent;

    internal static ReferenceBatteryProtocol.Reading DecodeCloud3(byte[] report)
    {
        Require(report.Length is >= 5 and <= 64 && IsCloud3BatteryReport(report),
            "Invalid HyperX Cloud III battery response");
        ReadOnlySpan<byte> data = report.AsSpan(2);
        Require((data[0] | data[1]) != 0 && data[2] <= 100,
            "Invalid HyperX Cloud III battery response");
        return Value(data[2]);
    }

    private static ReferenceBatteryProtocol.Reading ReadCloud3(ExpansionWire wire)
    {
        bool useOutput = wire.Output is >= 62 and <= 64;
        byte[] request = Packet(useOutput ? wire.Output : wire.Feature, Cloud3ReportId, Cloud3BatteryRequest);
        int samples = 2;
        ReferenceBatteryProtocol.Reading reading = null;
        while (samples-- > 0)
        {
            wire.Drain();
            if (useOutput)
                wire.Send(request);
            else
            {
                Require(wire.Link.SetFeature(request), "HyperX Feature query failed");
                wire.Trace?.Add("expansion-set-feature", true, raw: request, keep: true);
            }
            wire.Link.Delay(100);
            reading = DecodeCloud3(wire.Wait(IsCloud3BatteryReport, 2000));
        }
        return reading;
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
        if (family == "Cloud3") return ReadCloud3(wire);
        bool cloud2 = family == "Cloud2";
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            var request = cloud2 ? Packet(wire.Output, 6, 255, 187, 2, 0)
                : Packet(wire.Output, 0x50, 2);
            wire.Drain();
            wire.Send(request);
            if (cloud2) wire.Link.Delay(100);
            var reply = wire.Wait(b => b.Length >= 2 && (cloud2 ? b[0] == 6 && b[1] == 255 :
                b[0] == 0x51 && b[1] == 2), 2000);
            result = cloud2 ? DecodeCloud2(reply) : DecodeAlpha2(reply);
        }
        return result;
    }
}
