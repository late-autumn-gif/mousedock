using System;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

// Official S-Center cfg.json + index-DUfu8aky.js: Ra, Mt, Cr, Es and BatteryLevel.
// V6/V8 share this receiver; the product string does not establish an exact model.
internal static class ScyroxLegacyProtocol
{
    internal const string Reader = "ExtraMouse:ScyroxLegacy";
    internal static bool IsCandidate(ushort vid, ushort pid, string product) => vid == 0x3554 && pid == 0xF5F7 &&
        Name(product) == "SCYROX 8K DONGLE";
    internal static bool Matches(HidDiscovery.VendorCollectionInfo c) => c != null &&
        IsCandidate(c.Vid,c.Pid,c.Product) && Mi(c,1) && Col(c,5) && c.UsagePage == 0xFF02 && c.Usage == 2 &&
        c.Input == 17 && c.Output == 17 && c.Feature == 0;
    internal static void Validate(HidDiscovery.VendorCollectionInfo c, ExpansionHidMetadata m) =>
        Require(Matches(c) && m.Revision == 0x0216 && m.HasInput(8,17) && m.HasOutput(8,17) && m.FeatureIds.Count == 0,
            "Scyrox reported receiver layout/revision not proven");
    internal static byte[] Request(byte command)
    {
        Require(command is 3 or 4, "Only Scyrox online and battery queries are allowed");
        var b = Packet(17,8,command); b[16] = unchecked((byte)(0x55 - 8 - command)); return b;
    }
    internal static bool Valid(byte[] b, byte command) => b is { Length:17 } && b[0] == 8 && b[1] == command &&
        b[2] == 0 && b[3] == 0 && (b.Sum(v => (int)v) & 255) == 0x55 && !b.AsSpan().SequenceEqual(Request(command));
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(Valid(b,4) && b[6] <= 100 && b[7] <= 1, "Invalid Scyrox battery frame");
        return Value(b[6],b[7] == 1,"Scyrox device-reported percentage; live status checked");
    }
    internal static byte[] Redact(byte[] b)
    {
        // Never export the online response's peripheral address or unknown payload.
        var masked = PassiveBatteryNotifications.Mask(b,0,1,2,3,4,5,6,7);
        if (b.Length == 17)
        {
            if (b[1] == 3) masked[7] = 0;
            masked[16] = unchecked((byte)(0x55 - masked.Take(16).Sum(v => (int)v)));
        }
        return masked;
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        wire.TraceInput = Redact;
        wire.Trace?.Add("scyrox-redacted",true,result:"Original checksum validated before masking; exported checksum regenerated; peripheral address omitted");
        byte[] peer = null;
        bool Online()
        {
            var b = wire.Exchange(Request(3), b => Valid(b,3), 700);
            Require(b[6] <= 1,"Invalid Scyrox online status");
            if (b[6] == 0) return false;
            var address = b.AsSpan(7,3).ToArray();
            if (peer != null) Require(peer.AsSpan().SequenceEqual(address),"Scyrox paired peripheral changed during read");
            peer = address; return true;
        }
        ReferenceBatteryProtocol.Reading result = null;
        for (int i=0;i<2;i++)
        {
            if (!Online()) return new(-1,false,false,ReferenceBatteryProtocol.State.Sleep,"Scyrox peripheral offline");
            result = Decode(wire.Exchange(Request(4),b => Valid(b,4),700));
        }
        return Online() ? result : new(-1,false,false,ReferenceBatteryProtocol.State.Sleep,"Scyrox peripheral went offline");
    }
}
