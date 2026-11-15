using System;
using System.Threading;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionLogitech
{
    private const byte RootFeatureIndex = 0;
    private const ushort DeviceIdentityFeature = 0x0003;
    private const ushort DeviceTypeFeature = 0x0005;
    private static uint requestSequence;

    private static byte[] CreateRequest(byte slot, byte featureIndex, byte function, ReadOnlySpan<byte> parameters)
    {
        uint sequence = Interlocked.Increment(ref requestSequence);
        byte functionAndClient = (byte)((function << 4) | (int)(sequence % 15 + 1));
        var report = Packet(7, 0x10, slot, featureIndex, functionAndClient);
        parameters[..Math.Min(parameters.Length, 3)].CopyTo(report.AsSpan(4));
        return report;
    }

    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire shortWire,KeyboardExpansionWire longWire)
    {
        long until = shortWire.Link.Now + 12000;
        KeyboardExpansionWire[] channels = [shortWire, longWire];
        byte[] Call(byte slot,byte feature,byte function,params byte[] parameters)
        {
            Require(shortWire.Link.Now < until,"HID++ keyboard budget expired");
            var packet = CreateRequest(slot, feature, function, parameters);
            byte functionAndClient = packet[3];
            shortWire.Drain(); longWire.Drain();
            Require(shortWire.Link.Write(packet,250),"Incomplete HID++ keyboard query");
            shortWire.Trace?.Add("keyboard-output",true,raw:packet,keep:true);
            long deadline = Math.Min(until,shortWire.Link.Now + 350);
            for (int reads = 0; reads < 64 && shortWire.Link.Now < deadline; reads++)
            {
                foreach (var wire in channels)
                {
                    var b = wire.Link.Read(10);
                    if (b == null || !(b.Length == 7 && b[0] == 0x10 || b.Length == 20 && b[0] == 0x11) ||
                        b[1] != slot) continue;
                    wire.Trace?.Add("keyboard-input",true,raw:b,keep:true);
                    if (b[2] is 0xFF or 0x8F && b[3] == feature && b[4] == functionAndClient) return null;
                    if (b[2] == feature && b[3] == functionAndClient) return b[4..];
                }
            }
            return null;
        }
        byte Feature(byte slot,ushort id) => Call(slot,RootFeatureIndex,0,(byte)(id >> 8),(byte)id)?[0] ?? 0;
        ReferenceBatteryProtocol.Reading Battery(byte slot)
        {
            foreach (var pair in new[] { (Id:(ushort)0x1004,Function:(byte)1), (Id:(ushort)0x1000,Function:(byte)0) })
            {
                byte index = Feature(slot,pair.Id);
                if (index == 0) continue;
                var b = Call(slot,index,pair.Function);
                if (b == null || b.Length < (pair.Id == 0x1004 ? 4 : 3) ||
                    b[0] is < 1 or > 100 || b[2] > 4) continue;
                return Value(b[0],b[2] is 1 or 2 or 4);
            }
            return ReferenceBatteryProtocol.Reading.Failed("No supported HID++ keyboard battery feature");
        }
        ReferenceBatteryProtocol.Reading found = null;
        int keyboards = 0;
        for (byte slot = 1; slot <= 6; slot++)
        {
            byte marker = (byte)Random.Shared.Next(1,256);
            var ping = Call(slot,RootFeatureIndex,1,0,0,marker);
            if (ping == null || ping.Length < 3 || ping[0] < 2 || ping[2] != marker) continue;
            byte kindFeature = Feature(slot,DeviceTypeFeature);
            var kind = kindFeature == 0 ? null : Call(slot,kindFeature,2);
            if (kind == null || kind.Length == 0 || kind[0] != 0) continue;
            byte identityFeature = Feature(slot,DeviceIdentityFeature);
            var identity = identityFeature == 0 ? null : Call(slot,identityFeature,0);
            if (identity == null || identity.Length < 5) continue;
            var first = Battery(slot);
            var second = Battery(slot);
            var after = Call(slot,identityFeature,0);
            if (after == null || !identity.AsSpan().SequenceEqual(after)) continue;
            keyboards++;
            if (first.Level < 0 || second.Level < 0) continue;
            found = second with { Model = "Logitech wireless keyboard",Identity =
                slot + ":" + Convert.ToHexString(identity) };
        }
        Require(keyboards == 1 && found != null,"HID++ requires one confirmed keyboard child with fresh battery");
        return found;
    }
}
