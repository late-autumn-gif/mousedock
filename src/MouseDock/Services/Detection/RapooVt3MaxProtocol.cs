using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

// Official AHUB VT3 MAX manifest -> VT_nrf54L -> Kx.handleReport -> Sx/hm.
// This receiver uses report7 notifications. Never send the legacy A5 AA query.
internal static class RapooVt3MaxProtocol
{
    internal const string Model = "雷柏 VT3 MAX";
    internal static bool IsCandidate(ushort vid, ushort pid) => vid == 0x24AE && pid == 0x1417;
    internal static bool Matches(HidDiscovery.VendorCollectionInfo c) => c != null &&
        IsCandidate(c.Vid, c.Pid) && Mi(c, 1) && c.UsagePage == 0xFF00 && c.Usage == 2 &&
        c.Input == 19 && c.Output == 19 && c.Feature == 0;
    internal static void Validate(HidDiscovery.VendorCollectionInfo c, ExpansionHidMetadata m) =>
        Require(Matches(c) && m.Revision == 0x0105 && m.HasInput(7, 19) && m.HasOutput(7, 19),
            "VT3 MAX receiver report7/revision not proven");
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b is { Length: 19 } && b[0] == 7 && (b[1] & 15) is 0 or 2 &&
            b[7] is 1 or 2 or 3 && b[8] <= 100, "Invalid VT3 MAX battery notification");
        // Payload[17] is rfConnected/RSSI. A receiver must not lend an offline cached value.
        Require((b[1] & 15) != 0 || (b[18] & 0x80) != 0, "VT3 MAX RF peripheral offline");
        return Value(b[8], b[7] is 2 or 3, "Fresh VT3 MAX report7 notification") with { Model = Model };
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        wire.TraceInput = b => PassiveBatteryNotifications.Mask(b, 0, 1, 7, 8, 18);
        return PassiveBatteryNotifications.Read(wire, b => b.Length > 0 && b[0] == 7, Decode);
    }
}

internal static class PassiveBatteryNotifications
{
    internal static byte[] Mask(byte[] raw, params int[] offsets)
    {
        var result = new byte[raw.Length];
        foreach (int at in offsets) if (at < raw.Length) result[at] = raw[at];
        return result;
    }
    // Drain first, then require two new matching reports. The link is opened read-only.
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire,
        Func<byte[], bool> matches, Func<byte[], ReferenceBatteryProtocol.Reading> decode)
    {
        wire.Drain();
        wire.Trace?.Add("battery-passive-drained", true);
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            result = decode(wire.Wait(matches, 1500));
            if (result.Level < 0) return result; // Frozen/external power is never a confirming sample.
        }
        wire.Trace?.Add("battery-passive-complete", true);
        return result;
    }
}
