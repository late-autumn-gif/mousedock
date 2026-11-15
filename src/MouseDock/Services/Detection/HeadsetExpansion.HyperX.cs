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

    private static ReadOnlySpan<byte> Cloud2BatteryHeader => [0x06, 0xff, 0xbb, 0x02];
    private static ReadOnlySpan<byte> Alpha2BatteryQuery => [0x50, 0x02];
    private static ReadOnlySpan<byte> Alpha2BatteryHeader => [0x51, 0x02];

    internal static ReferenceBatteryProtocol.Reading DecodeCloud2(byte[] report)
    {
        Require(report.Length == 20 && report.AsSpan().StartsWith(Cloud2BatteryHeader),
            "Invalid HyperX Cloud II battery response");
        var payload = report.AsSpan(Cloud2BatteryHeader.Length);
        int percentage = payload[3];
        Require(payload.ContainsAnyExcept((byte)0) && percentage <= 100,
            "Invalid HyperX Cloud II battery response");
        return Value(percentage);
    }

    internal static ReferenceBatteryProtocol.Reading DecodeAlpha2(byte[] report)
    {
        Require(report.Length is >= 7 and <= 64 && report.AsSpan().StartsWith(Alpha2BatteryHeader),
            "Invalid HyperX Cloud Alpha2 battery response");
        var payload = report.AsSpan(Alpha2BatteryHeader.Length);
        int percentage = payload[0];
        Require(percentage <= 100, "Invalid HyperX Cloud Alpha2 battery response");
        bool charging = (payload[4] & 0x80) != 0;
        return Value(percentage, charging);
    }

    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, string family)
    {
        if (family == "Cloud3") return ReadCloud3(wire);
        bool cloud2 = family == "Cloud2";
        byte[] request = cloud2 ? Packet(wire.Output, 0x06, 0xff, 0xbb, 0x02, 0)
            : Packet(wire.Output, Alpha2BatteryQuery[0], Alpha2BatteryQuery[1]);
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            wire.Drain();
            wire.Send(request);
            if (cloud2) wire.Link.Delay(100);
            var reply = wire.Wait(b => b.AsSpan().StartsWith(cloud2 ? Cloud2BatteryHeader[..2] : Alpha2BatteryHeader), 2000);
            result = cloud2 ? DecodeCloud2(reply) : DecodeAlpha2(reply);
        }
        return result;
    }
}
