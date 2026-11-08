using System.IO;
using System.Reflection;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class F75Regression
{
    private static object Call(string name, params object[] args)
    {
        var type = typeof(ReferenceBatteryProtocol).Assembly.GetType("MouseDock.Services.Detection.F75Protocol");
        if (type == null) throw new Exception("F75 scoped battery protocol is missing.");
        return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    }

    private static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link) =>
        (ReferenceBatteryProtocol.Reading)Call("Read", link);

    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
        // Official UUID 03 00 00 00 00 CD. Native report includes its report ID.
        byte[] identity = Convert.FromHexString("13050100060300000000CD0000000000000000EF");
        // Independently captured F75 90% / battery-powered frame from the original research.
        byte[] battery = Convert.FromHexString("134A0100025A01000000000000000000000000BB");
        byte[] Identity(byte final)
        {
            var frame = (byte[])identity.Clone();
            frame[10] = final; frame[19] = (byte)(0x22 + final);
            return frame;
        }
        byte[] Battery(int level)
        {
            var frame = (byte[])battery.Clone();
            frame[5] = (byte)level; frame[19] = (byte)(0x61 + level);
            return frame;
        }
        var link = new Link(new[] { identity }, new[] { battery });
        var result = Read(link);
        Check(result.Level == 90 && result.Model == "AULA F75" && result.Identity == "0300000000CD",
            "The scoped F75 route must verify identity before publishing native byte5.");
        Check(!result.Charging && !result.ChargingKnown,
            "Unverified charging values must remain unknown.");
        Check(link.Commands.Select(Convert.ToHexString).SequenceEqual(new[]
        {
            "1305010000000000000000000000000000000019",
            "134A00000000000000000000000000000000005D"
        }), "Only the official identity and battery requests may be sent.");
        foreach (int level in new[] { 0, 1, 50, 100 })
            Check(Read(new Link(new[] { identity }, new[] { Battery(level) })).Level == level,
                "Valid percent boundaries must be retained.");
        foreach (byte flag in new byte[] { 0, 0x10, 0xFF })
        {
            var frame = (byte[])battery.Clone(); frame[6] = flag; frame[19] = (byte)(0xBA + flag);
            var state = Read(new Link(new[] { identity }, new[] { frame }));
            Check(state.Level == 90 && !state.Charging && !state.ChargingKnown,
                "The unverified state byte cannot invent charging or transform the verified percentage.");
        }
        foreach (byte other in new byte[] { 0, 0xA4, 0xFF })
        {
            var foreign = new Link(new[] { Identity(other) }, new[] { battery });
            Check(Read(foreign).Level == -1 && foreign.Commands.All(p => p[1] == 5),
                "Shared receiver identities must not receive an F75 battery request.");
        }
        var malformed = new List<byte[]> { battery[..19], battery.Concat(new byte[] { 0 }).ToArray(),
            Battery(101), Battery(255), new byte[20] };
        foreach (int offset in new[] { 0, 1, 2, 3, 4, 19 })
        {
            var frame = (byte[])battery.Clone(); frame[offset] ^= 0x80;
            if (offset != 19) frame[19] = (byte)frame.Take(19).Sum(v => (int)v);
            malformed.Add(frame);
        }
        foreach (var frame in malformed)
            Check(Read(new Link(new[] { identity }, new[] { frame })).Level == -1,
                "Malformed, offline, wrong-command and unbounded battery replies must be rejected.");
        var echoed = new Link(new[] { identity }, new[]
        { Convert.FromHexString("134A00000000000000000000000000000000005D") });
        Check(Read(echoed).Level == -1, "A request echo is not a 0% response.");
        var badIdentity = (byte[])identity.Clone(); badIdentity[19]++;
        var invalidIdentity = new Link(new[] { badIdentity }, new[] { battery });
        Check(Read(invalidIdentity).Level == -1 && invalidIdentity.Commands.All(p => p[1] == 5),
            "Corrupt identity cannot authorize a battery query.");
        var stale = new Link(new[] { identity }, Array.Empty<byte[]>()) { Initial = new Queue<byte[]>(new[] { battery }) };
        Check(Read(stale).Level == -1, "A stale queued battery response must not be adopted.");
        var flooded = new Link { Flood = true };
        Check(Read(flooded).Level == -1 && flooded.Commands.Count == 0 && flooded.Reads <= 32,
            "A queue that cannot be drained must not trigger a query.");
        var silent = new Link();
        Check(Read(silent).Level == -1 && silent.Now <= 2000 && silent.Commands.Count <= 3,
            "A silent keyboard has bounded attempts and cannot turn into a fabricated percentage.");
        var failedWrite = new Link(new[] { identity }) { WriteOk = false };
        Check(Read(failedWrite).Level == -1 && failedWrite.Commands.Count == 1,
            "An incomplete write stops the exchange.");
        Check(Read(new Link { ThrowOnRead = true }).Level == -1,
            "An I/O exception must downgrade to unknown.");
        Check((bool)Call("IsCandidate", (ushort)0x3554, (ushort)0xFA09, "2.4G Wireless Receiver"),
            "The reported F75 product can enter identity validation.");
        Check(!(bool)Call("IsCandidate", (ushort)0x3554, (ushort)0xFA09, "MOBA Gaming Mouse") &&
            !(bool)Call("IsCandidate", (ushort)0x3554, (ushort)0xF5C0, "2.4G Wireless Receiver"),
            "A mouse or unrelated PID cannot enter the F75 route.");
        Check((bool)Call("MatchesChannel", (ushort)0x3554, (ushort)0xFA09, "2.4G Wireless Receiver",
            (ushort)0xFF02, (ushort)2, 20, 20, 0), "The verified report channel is eligible.");
        Check(!(bool)Call("MatchesChannel", (ushort)0x3554, (ushort)0xFA09, "2.4G Wireless Receiver",
            (ushort)0xFF04, (ushort)2, 0, 0, 8), "The static Feature8 collection cannot supply battery.");

        InstanceScope.BeginScan();
        string a = InstanceScope.Register("f75-A", new[] { (@"USB\VID_3554&PID_FA09\A", "usbccgp") });
        string b = InstanceScope.Register("f75-B", new[] { (@"USB\VID_3554&PID_FA09\B", "usbccgp") });
        HidDiscovery.Bucket Bucket(string key) => new()
        {
            Vid = 0x3554, Pid = 0xFA09, Product = "2.4G Wireless Receiver",
            InstanceId = key, Kind = DeviceKind.Mouse, Path = key
        };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        {
            Vid = 0x3554, Pid = 0xFA09, Product = "2.4G Wireless Receiver", Path = path,
            UsagePage = 0xFF02, Usage = 2, Input = 20, Output = 20, Feature = 0
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("f75-A"), Channel("f75-B") };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Query(string path)
        {
            calls++;
            Check(InstanceScope.Current == (path == "f75-A" ? a : b), "Enter the exact F75 physical instance.");
            return new(path == "f75-A" ? 90 : 23, false, false, ReferenceBatteryProtocol.State.Online,
                "offline verified fixture", "AULA F75", "0300000000CD");
        }
        void Discover(Dictionary<string, HidDiscovery.Bucket> selected,
            List<HidDiscovery.VendorCollectionInfo> selectedChannels, bool safe, DevicePreferences? preferences,
            Func<string, ReferenceBatteryProtocol.Reading>? query = null) =>
            typeof(HidDiscovery).GetMethod("QueryF75Collections", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object?[] { selected, selectedChannels, safe, preferences, query ?? Query });
        Discover(buckets, channels, false, null);
        Check(calls == 2 && buckets[a].ToDevice().Level == 90 && buckets[b].ToDevice().Level == 23,
            "Identical keyboard receivers must retain independent readings.");
        Check(buckets[a].ToDevice().Kind == DeviceKind.Keyboard &&
            buckets[a].ToDevice().ConfirmedModel == "AULA F75", "Verified F75 must override an auxiliary mouse collection.");
        var displayed = new AutoDetectSource().UpdateFromDetection(new() { buckets[a].ToDevice() }).Single();
        Check(displayed.Kind == DeviceKind.Keyboard && displayed.Name == "AULA F75" && displayed.Level == 90,
            "The normal UI data source must receive the verified keyboard result.");
        calls = 0;
        Discover(buckets, channels, true, null);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1 &&
            buckets[a].ToDevice().ConfirmedModel == "", "Safe mode must clear previous F75 readings and identity.");
        Check(buckets[a].ToDevice().Kind == DeviceKind.Keyboard,
            "An unresolved exact receiver must remain a keyboard without inventing its model.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        Discover(buckets, channels, false, new DevicePreferences(settings));
        Check(calls == 1 && buckets[a].ToDevice().Level == -1, "A disabled keyboard instance must not be queried.");
        settings.DisabledRecognitionDeviceIds.Add("hid-3554-FA09"); calls = 0;
        Discover(buckets, channels, false, new DevicePreferences(settings));
        Check(calls == 0, "Legacy disabled receiver IDs must remain honored.");
        Discover(new() { [a] = buckets[a] }, new() { Channel("f75-B") }, false, null);
        Check(calls == 0, "A matching channel from another keyboard cannot be borrowed.");
        InstanceScope.Register("f75-A-extra", new[] { (@"USB\VID_3554&PID_FA09\A", "usbccgp") });
        Discover(new() { [a] = buckets[a] }, new() { Channel("f75-A"), Channel("f75-A-extra") }, false, null);
        Check(calls == 0, "Ambiguous matching collections cannot trigger a query.");
        var feature = Channel("f75-A"); feature.UsagePage = 0xFF04; feature.Input = feature.Output = 0; feature.Feature = 8;
        Discover(new() { [a] = buckets[a] }, new() { feature }, false, null);
        Check(calls == 0, "Discovery cannot route the static Feature collection into the battery protocol.");
        Discover(new() { [a] = buckets[a] }, channels, false, null,
            _ => new(99, true, true, ReferenceBatteryProtocol.State.Online, "foreign", "AULA F99", "0300000000A4"));
        Check(buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "A foreign model result must not be applied to F75.");
        var native = typeof(VendorProtocol).GetMethod("ReadF75", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(((ReferenceBatteryProtocol.Reading)native.Invoke(null, new object[] { "f75-A" })!).Level == -1,
            "The native F75 reader cannot open outside an instance scope.");
        using (InstanceScope.Enter(b))
            Check(((ReferenceBatteryProtocol.Reading)native.Invoke(null, new object[] { "f75-A" })!).Level == -1,
                "The native F75 reader cannot cross instance boundaries.");
        return count;
    }

    private sealed class Link(params byte[][][] transactions) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[][]> pendingTransactions = new(transactions);
        private readonly Queue<byte[]> incoming = new();
        public Queue<byte[]> Initial = new();
        public List<byte[]> Commands = new();
        public bool WriteOk = true, Flood, ThrowOnRead;
        public int Reads;
        public long Now { get; private set; }
        public byte[] Read(int timeout)
        {
            if (ThrowOnRead) throw new IOException("synthetic F75 read failure");
            Reads++; Now += Math.Max(1, timeout);
            if (Flood) return new byte[20];
            if (Initial.Count > 0) return Initial.Dequeue();
            return incoming.Count > 0 ? incoming.Dequeue() : null!;
        }
        public bool Write(byte[] packet, int timeout)
        {
            Commands.Add((byte[])packet.Clone());
            if (!WriteOk) return false;
            if (pendingTransactions.Count > 0)
                foreach (var response in pendingTransactions.Dequeue()) incoming.Enqueue(response);
            return true;
        }
        public bool SetFeature(byte[] packet) => throw new Exception("F75 cannot write Feature reports.");
        public byte[] GetFeature(byte id, int length) => throw new Exception("F75 cannot read Feature reports.");
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
