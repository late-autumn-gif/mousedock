using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class Zg4Regression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string reason)
        {
            count++;
            if (!ok) throw new Exception(reason);
        }

        const string model = "ZhuAudio ZG4";
        InstanceScope.BeginScan();
        string Register(string path, string parent) =>
            InstanceScope.Register(path, new[] { (parent, "usbccgp") });
        var first = Channel("zg4-mi03", 65, 0xFF02, 2);
        var second = Channel("zg4-mi04-col02", 64, 0xFF02, 2);
        var third = Channel("zg4-mi04-col03", 64, 0xFF05, 1);
        var other = Channel("another-zg4", 64, 0xFF02, 2);
        var consumer = Channel("zg4-consumer", 3, 0x000C, 1);
        var wrongModel = Channel("zg3", 64, 0xFF02, 2);
        wrongModel.Product = "ZhuAudio ZG3";
        string instance = Register(first.Path, @"USB\VID_3654&PID_5055\ONE");
        foreach (var channel in new[] { second, third, consumer, wrongModel })
            Register(channel.Path, @"USB\VID_3654&PID_5055\ONE");
        Register(other.Path, @"USB\VID_3654&PID_5055\TWO");
        var channels = new List<HidDiscovery.VendorCollectionInfo>
            { first, consumer, other, wrongModel, second, third, third };
        HidDiscovery.Bucket Bucket() => new()
        {
            InstanceId = instance, Vid = 0x3654, Pid = 0x5055, Product = model,
            Kind = DeviceKind.Headset, Battery = 99, Charging = true
        };
        var bucket = Bucket();
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [instance] = bucket };
        var scan = new BatteryReplay.Scan();
        foreach (var channel in channels.Distinct())
            scan.AddCollection(InstanceScope.ForPath(channel.Path, ""), channel.Path, channel.Vid, channel.Pid,
                channel.Product, channel.UsagePage, channel.Usage, channel.Input, channel.Output, channel.Feature, false);
        BatteryReplay.Current = scan;
        var queried = new List<string>();
        try
        {
            HidDiscovery.QueryZg4Collections(buckets, channels, false, null, channel =>
            {
                Check(InstanceScope.Current == instance, "ZG4 opens channels in the physical instance scope");
                queried.Add(channel.Path);
                return channel.Path == third.Path
                    ? new(67, true, true, ReferenceBatteryProtocol.State.Charging, "verified", model)
                    : ReferenceBatteryProtocol.Reading.Failed("channel unavailable");
            });
        }
        finally { BatteryReplay.Current = null; }
        Check(queried.SequenceEqual(new[] { first.Path, second.Path, third.Path }),
            "ZG4 tries each discovered private channel once and never queries another receiver or model");
        Check(bucket.ToDevice() is { Level: 67, Charging: true, Transport: "2.4G", Name: model },
            "A later ZG4 channel can provide the displayed battery");
        var capture = scan.Finish(null);
        Check(capture.Queries.Length == 3 && capture.Queries.All(q => q.Interface != null),
            "Each ZG4 attempt is attached to its discovered interface in the report");

        foreach (bool safe in new[] { false, true })
        {
            bucket = Bucket();
            buckets[instance] = bucket;
            int calls = 0;
            var preferences = safe ? null : new DevicePreferences(new AppSettings
                { DisabledRecognitionDeviceIds = new() { instance } });
            HidDiscovery.QueryZg4Collections(buckets, channels, safe, preferences, _ =>
            {
                calls++;
                return new(67, false, true, ReferenceBatteryProtocol.State.Online, "", model);
            });
            Check(calls == 0 && bucket.Battery == -1 && !bucket.Charging,
                "Disabled ZG4 reads clear old battery state without querying");
        }
        bucket = Bucket();
        buckets[instance] = bucket;
        HidDiscovery.QueryZg4Collections(buckets, channels, false, null,
            _ => ReferenceBatteryProtocol.Reading.Failed("no response"));
        Check(bucket.Battery == -1 && !bucket.Charging, "Failed ZG4 reads never reuse an old battery");
        HidDiscovery.QueryZg4Collections(buckets, channels, false, null,
            _ => new(50, true, true, ReferenceBatteryProtocol.State.Online, "", "ZhuAudio ZG3"));
        Check(bucket.Battery == -1 && !bucket.Charging, "A different model cannot establish a ZG4 reading");

        var trace = new BatteryReplay.Trace();
        trace.Add("zg4-open", true, 0xCD);
        var link = new Zg4Link();
        var reading = VendorProtocol.ReadZg4Exchange(link, 0xCD, 0xCD, 64, trace);
        Check(reading is { Level: 67, Charging: true, Model: model },
            "ZG4 requires online and model confirmation before accepting the battery");
        Check(link.Commands.SequenceEqual(new byte[] { 2, 6, 7 }),
            "ZG4 uses online, identity, and battery property commands in order");
        var replayCollection = new BatteryReplay.Collection("r", 1, 0x3654, 0x5055, model,
            0xFF02, 2, 64, 64, 0, false, false);
        var query = new BatteryReplay.Query("r", 1, "Zg4", "Charging", model, 67, trace.Snapshot());
        Check(BatteryReplay.Replay(query, new[] { replayCollection }) is
            { Percent: 67, Charging: true, ValueKind: "captured" },
            "ZG4 reports replay only the confirmed online, identity, and battery transaction");
        Check(BatteryReplay.Replay(query with
            { Samples = trace.Snapshot().Where(s => s.Stage != "zg4-identity").ToArray() },
            new[] { replayCollection }).Percent == null,
            "ZG4 replay cannot infer a battery without the model response");
        Check(BatteryReplay.Replay(query with
            { Samples = trace.Snapshot().Select(s => s.Stage == "zg4-open" ? s with { Sequence = 0xCE } : s).ToArray() },
            new[] { replayCollection }).Percent == null,
            "ZG4 replay rejects responses from a different input report ID");
        Check(BatteryReplay.Replay(query with
            { Samples = trace.Snapshot().Where(s => s.Stage != "zg4-open").ToArray() },
            new[] { replayCollection }).Percent == null,
            "ZG4 replay requires a confirmed interface report ID");
        Check(BatteryReplay.Replay(query with
            { Samples = trace.Snapshot().Select(s => s.Stage == "zg4-open" ? s with { Ok = false } : s).ToArray() },
            new[] { replayCollection }).Percent == null,
            "ZG4 replay rejects transactions after an interface open failure");
        Check(BatteryReplay.Replay(query, new[] { replayCollection with { Product = "ZhuAudio ZG3" } }).Percent == null,
            "ZG4 replay rejects a mismatched channel");

        foreach (bool offline in new[] { false, true })
        {
            var rejected = new Zg4Link { Offline = offline, WrongModel = !offline };
            Check(VendorProtocol.ReadZg4Exchange(rejected, 0xCD, 0xCD, 64, null).Level == -1 &&
                !rejected.Commands.Contains((byte)7), "Offline or unconfirmed ZG4 never receives a battery query");
        }
        InstanceScope.BeginScan();
        return count;
    }

    private static HidDiscovery.VendorCollectionInfo Channel(string path, ushort length, ushort page, ushort usage) =>
        new() { Path = path, Vid = 0x3654, Pid = 0x5055, Product = "ZhuAudio ZG4", UsagePage = page,
            Usage = usage, Input = length, Output = length };

    private sealed class Zg4Link : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        public bool Offline, WrongModel;
        public List<byte> Commands { get; } = new();
        private byte[]? reply;
        public bool Write(byte[] request, int timeoutMs)
        {
            if (request.Length != 64 || request[0] != 0xCD) throw new Exception("ZG4 request channel mismatch");
            byte command = request[1] == 7 ? request[8] : request[10];
            Commands.Add(command);
            reply = command switch
            {
                2 when !Offline => new byte[] { 0xCD, 0xAA, 0xCC, 0, 2, 0xC0, 0xA0, 2, 1 },
                6 => WrongModel
                    ? new byte[] { 0xCD, 0xAA, 0xCC, 0, 5, 0x8F, 0x9C, 6, 0, 0, 9, 16 }
                    : new byte[] { 0xCD, 0xAA, 0xCC, 0, 5, 0x8A, 0x9C, 6, 0, 0, 5, 16 },
                7 => new byte[] { 0xCD, 0xEA, 0x80, 0, 9, 0, 0xFF, request[7], 5, 0xFE, 7, 0xC3, 0, 0, 0xAA },
                _ => null
            };
            Now++;
            return true;
        }
        public byte[] Read(int timeoutMs)
        {
            Now += reply == null ? timeoutMs : 1;
            var value = reply;
            reply = null;
            return value!;
        }
        public void Delay(int milliseconds) => Now += milliseconds;
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
    }
}
