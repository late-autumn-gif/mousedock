using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class RazerBlackSharkRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string reason)
        {
            count++;
            if (!condition) throw new Exception("BlackShark V2 HyperSpeed: " + reason);
        }
        Check(DeviceRoutes.Find(0x1532, 0x0565, "Razer BlackShark V2 HS 2.4") is
            { Kind: DeviceKind.Headset, Transport: "2.4G" },
            "The exact wireless device must be routed as a headset.");
        Check(DeviceRoutes.Find(0x1532, 0x056E, "Razer BlackShark V2 HS USB") is
            { Kind: DeviceKind.Headset, Transport: "USB" }, "USB cable mode must use the same model and USB transport.");
        Check(DeviceRoutes.IsKnown(0x1532, 0x0565, null!) &&
            !DeviceRoutes.IsKnown(0x1532, 0x1014, "Razer BlackShark V2 Pro PS") &&
            !RazerBlackSharkProtocol.IsCandidate(0x1532, 0x0566) &&
            !RazerBlackSharkProtocol.IsCandidate(0x1532, 0x0577), "Other BlackShark models cannot enter this protocol.");
        Check(HidDiscovery.TryDeviceKind(0x1532, 0x0565, 0xFF14, 1, out var kind) && kind == DeviceKind.Headset &&
            VendorMap.TryUsbHeadset(0x1532, 0x056E, out var model) && model == RazerBlackSharkProtocol.Model,
            "The private control collection and USB product map must both identify a headset.");
        Check(RazerBlackSharkProtocol.MatchesChannel(0x1532, 0x0565, 0xFF14, 1, 64, 64, 9) &&
            !RazerBlackSharkProtocol.MatchesChannel(0x1532, 0x0565, 0xFF13, 1, 64, 64, 9) &&
            !RazerBlackSharkProtocol.MatchesChannel(0x1532, 0x0565, 0xFF14, 1, 64, 64, 0) &&
            !RazerBlackSharkProtocol.MatchesChannel(0x1532, 0x0565, 0xFF14, 1, 63, 64, 9),
            "The original descriptor's 64/64/9 capabilities must be respected.");

        var request = RazerBlackSharkProtocol.Request(1, 0x80, 0x21);
        Check(request.Length == 64 && request[0] == 2 && request[2] == 0x61 &&
            request[6] == 4 && request[9] == 0x80 && request[10] == 0x21 &&
            request[12] == 0 && request[62] == 0xC6, "Battery GET must use the independent WebHID query vector.");
        foreach (ushort pid in new ushort[] { 0x0565, 0x056E })
        foreach (byte level in new byte[] { 0, 1, 42, 99, 100 })
        foreach (byte charge in new byte[] { 0, 1 })
        {
            var link = new Link { Level = level, Charge = charge };
            var reading = RazerBlackSharkProtocol.Read(link, pid);
            Check(reading.Level == level && reading.Charging == (charge == 1) &&
                reading.ChargingKnown && reading.Model == RazerBlackSharkProtocol.Model,
                "Direct percent boundaries and separate charging must work in both transports.");
            Check(link.Writes.Count == (pid == 0x0565 ? 4 : 2) &&
                link.Writes.All(w => w[12] == 0 && w[10] is 0x20 or 0x21 or 0x2A &&
                    w[9] == (pid == 0x056E || w[10] == 0x20 ? 0 : 0x80)),
                "Only read-only link/battery/charging commands in their proper domains may be sent.");
        }
        var cableAlias = new Link { WiredDomainAlias = true };
        Check(RazerBlackSharkProtocol.Read(cableAlias, 0x056E).Level == 42,
            "Known wired firmware may reply with the headset domain 80 to local queries.");
        foreach (var mutation in new Action<byte[]>[]
        {
            b => b[0] = 3, b => b[1] = 1, b => b[1] = 4, b => b[1] = 5,
            b => b[2]++, b => b[6] = 4, b => b[8] = 0, b => b[9] = 0,
            b => b[10] = 0x20, b => b[11] = 0, b => b[12] = 2, b => b[13] = 101
        })
        {
            var invalid = new Link { MutateBattery = mutation };
            Check(RazerBlackSharkProtocol.Read(invalid, 0x0565).Level == -1,
                "A malformed, rejected, mismatched or out-of-range battery response must remain unknown.");
        }
        Check(RazerBlackSharkProtocol.Read(new Link { BadChecksum = true }, 0x0565).Level == -1 &&
            RazerBlackSharkProtocol.Read(new Link { ShortReply = true }, 0x0565).Level == -1,
            "Truncated frames and checksum errors cannot be interpreted as battery.");
        var offline = new Link { Online = false };
        Check(RazerBlackSharkProtocol.Read(offline, 0x0565).Level == -1 && offline.Writes.Count == 1,
            "An offline receiver must not query cached battery bytes.");
        Check(RazerBlackSharkProtocol.Read(new Link { DisconnectAfter = true }, 0x0565).Level == -1,
            "A headset disconnected during the transaction must lose the observed battery.");
        var noCharge = RazerBlackSharkProtocol.Read(new Link { NoCharging = true }, 0x0565);
        Check(noCharge.Level == 42 && !noCharge.ChargingKnown && !noCharge.Charging,
            "Missing charging reply must preserve battery while charging stays unknown.");
        var stale = new Link { StaleBeforeValid = true };
        Check(RazerBlackSharkProtocol.Read(stale, 0x0565).Level == 42,
            "A previous sequence must be ignored while awaiting the current response.");
        var timeout = new Link { Silent = true };
        Check(RazerBlackSharkProtocol.Read(timeout, 0x0565).Level == -1 && timeout.Now <= 3200,
            "Missing responses must remain unknown within the total budget.");
        var flood = new Link { Flood = true };
        Check(RazerBlackSharkProtocol.Read(flood, 0x0565).Level == -1 && flood.Reads <= 73,
            "Invalid immediate frames must terminate even without clock advancement.");

        var trace = new BatteryReplay.Trace();
        var captured = RazerBlackSharkProtocol.Read(new Link { Level = 0, Charge = 1 }, 0x0565, trace);
        var collection = new BatteryReplay.Collection("receiver-1", 1, 0x1532, 0x0565,
            "Razer BlackShark V2 HS 2.4", 0xFF14, 1, 64, 64, 9, false, false,
            ReportIds: "IN[02] OUT[02] FEATURE[02]");
        var query = new BatteryReplay.Query("receiver-1", 1, RazerBlackSharkProtocol.Protocol,
            captured.Status.ToString(), captured.Model, captured.Level, trace.Snapshot());
        Check(BatteryReplay.Replay(query, [collection]) is { Percent: 0, Charging: true, ValueKind: "captured" },
            "A genuine zero must survive production transaction recording and replay.");
        foreach (var changed in new[]
        {
            collection with { Receiver = "receiver-2" }, collection with { Feature = 0 },
            collection with { ReportIds = "IN[03] OUT[02] FEATURE[02]" }, collection with { Excluded = true },
            collection with { Pid = 0x0577 }
        })
            Check(BatteryReplay.Replay(query, [changed]).Percent == null,
                "Replay must require matching receiver, descriptor, report IDs and supported model.");
        Check(BatteryReplay.Replay(query, [collection, collection with { Interface = 2 }]).Percent == null &&
            BatteryReplay.Replay(query with { Samples = trace.Snapshot()[..^1] }, [collection]).Percent == null &&
            BatteryReplay.Replay(query with { Status = "Failed", ObservedPercent = 100 }, [collection]).Percent == null,
            "Ambiguous collections, missing final online check and observed values cannot manufacture a percentage.");
        var padded = Reply(request, 42);
        padded[20] = 0x55;
        Check(!RazerBlackSharkProtocol.CanExport(padded), "Unexpected padding must not leak into diagnostic raw frames.");

        InstanceScope.BeginScan();
        string a = InstanceScope.Register("blackshark-A", [( @"USB\VID_1532&PID_0565\A", "usbccgp")]);
        string b = InstanceScope.Register("blackshark-B", [( @"USB\VID_1532&PID_0565\B", "usbccgp")]);
        HidDiscovery.Bucket Bucket(string id) => new()
        {
            InstanceId = id, Vid = 0x1532, Pid = 0x0565, Kind = DeviceKind.Headset,
            Product = "Razer BlackShark V2 HS 2.4", Path = id
        };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        { Path = path, Vid = 0x1532, Pid = 0x0565, UsagePage = 0xFF14, Usage = 1, Input = 64, Output = 64, Feature = 9 };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("blackshark-A"), Channel("blackshark-B") };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Query(string path, ushort pid, BatteryReplay.Trace log)
        {
            calls++;
            Check(InstanceScope.Current == (path.EndsWith("A") ? a : b), "Reads must enter their own physical instance.");
            return RazerBlackSharkProtocol.Read(new Link { Level = path.EndsWith("A") ? (byte)0 : (byte)81 }, pid, log);
        }
        HidDiscovery.QueryRazerBlackShark(buckets, channels, false, null!, Query);
        Check(calls == 2 && buckets[a].ToDevice() is { Kind: DeviceKind.Headset, Level: 0,
            Transport: "2.4G", PeripheralConfirmed: true, ReceiverDiscovered: true } && buckets[b].ToDevice().Level == 81,
            "Two equal receivers must retain their own readings and headset identity.");
        Check(new AutoDetectSource().UpdateFromDetection([buckets[a].ToDevice()]).Single() is
            { Level: 0, Kind: DeviceKind.Headset }, "The fresh reading must reach the production device model.");
        calls = 0;
        HidDiscovery.QueryRazerBlackShark(buckets, channels, true, null!, Query);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1, "Safe mode must clear the old reading without querying.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        HidDiscovery.QueryRazerBlackShark(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 1 && buckets[a].ToDevice().Level == -1, "Disabling one instance must leave other instances usable.");
        settings.DisabledRecognitionDeviceIds.Add("hid-1532-0565");
        calls = 0;
        HidDiscovery.QueryRazerBlackShark(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 0, "The legacy whole-model disable must suppress queries too.");
        channels.Add(Channel("blackshark-A-extra"));
        InstanceScope.Register("blackshark-A-extra", [( @"USB\VID_1532&PID_0565\A", "usbccgp")]);
        calls = 0;
        HidDiscovery.QueryRazerBlackShark(buckets, channels, false, null!, Query);
        Check(calls == 1 && buckets[a].Battery == -1, "Duplicate control collections must not be arbitrarily selected.");
        return count;
    }

    private static byte[] Reply(byte[] request, byte value)
    {
        var reply = new byte[64];
        reply[0] = 2; reply[1] = 2; reply[2] = request[2]; reply[6] = 5;
        reply[8] = 0x80; reply[9] = request[9]; reply[10] = request[10]; reply[11] = 1;
        reply[12] = 1; reply[13] = value;
        Checksum(reply);
        return reply;
    }

    private static void Checksum(byte[] reply)
    {
        reply[62] = 0;
        for (int i = 1; i < 62; i++) reply[62] ^= reply[i];
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        internal byte Level = 42, Charge;
        internal bool Online = true, DisconnectAfter, NoCharging, StaleBeforeValid, Silent, Flood,
            ShortReply, BadChecksum, WiredDomainAlias;
        internal Action<byte[]>? MutateBattery;
        internal List<byte[]> Writes = new();
        private readonly Queue<byte[]> replies = new();
        internal int Reads;
        private int onlineQueries;
        public byte[] Read(int timeoutMs)
        {
            Reads++;
            if (Flood) return new byte[64];
            Now += replies.Count > 0 ? 1 : timeoutMs;
            return replies.Count > 0 ? replies.Dequeue() : null!;
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            Writes.Add(request);
            if (Silent || (NoCharging && request[10] == 0x2A)) return true;
            byte value = request[10] switch
            {
                0x20 => Online && !(DisconnectAfter && onlineQueries++ > 0) ? (byte)1 : (byte)0,
                0x21 => Level,
                _ => Charge
            };
            var reply = Reply(request, value);
            if (WiredDomainAlias) reply[9] = 0x80;
            if (request[10] == 0x21) MutateBattery?.Invoke(reply);
            Checksum(reply);
            if (StaleBeforeValid)
            {
                var old = (byte[])reply.Clone();
                old[2]--;
                Checksum(old);
                replies.Enqueue(old);
            }
            if (BadChecksum) reply[62]++;
            replies.Enqueue(ShortReply ? reply[..63] : reply);
            return true;
        }
        public bool SetFeature(byte[] request) => throw new Exception("No feature writes allowed.");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("No feature reads allowed.");
        public void Delay(int milliseconds) => throw new Exception("No device setting changes allowed.");
    }
}
