using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class M137FeatureRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
        // Independently constructed protocol vector. The hardware report stays local.
        byte[] Frame(int level = 79, string identity = "M137", byte state = 0)
        {
            var bytes = new byte[33];
            new byte[] { 12, 1, 32, 0 }.CopyTo(bytes, 0);
            System.Text.Encoding.ASCII.GetBytes(identity).CopyTo(bytes, 8);
            bytes[18] = (byte)level; bytes[19] = state;
            return bytes;
        }
        var link = new Link(Frame(), Frame(78));
        var result = M137FeatureProtocol.Read(link);
        Check(result.Level == 78 && result.Model == "狼蛛 SC580" && result.Identity == "M137",
            "Two M137 GetInfo transactions must publish the latest percentage from byte18.");
        Check(!result.Charging && !result.ChargingKnown, "Do not invent charging semantics.");
        Check(link.Commands.SequenceEqual(new[] { "S0", "G1", "S2", "G3" }),
            "Both transactions must use SET/GetInfo GET with separate prepared headers and increasing sequences.");
        Check(result.Diagnostic.Contains("RX=") && result.Diagnostic.Contains("4D313337"),
            "Keep accepted raw responses in scan diagnostics.");
        foreach (int level in new[] { 0, 1, 50, 100 })
        foreach (byte state in new byte[] { 0, 1, 255 })
        {
            var reading = M137FeatureProtocol.Read(new Link(Frame(level, state: state), Frame(level, state: state)));
            Check(reading.Level == level && !reading.Charging && !reading.ChargingKnown,
                "Percent boundaries must be valid without interpreting the unverified charge byte.");
        }
        var malformed = new List<byte[]> { Frame(101), Frame(255), Frame()[..20], new byte[33], new byte[34] };
        foreach (int offset in new[] { 0, 1, 2, 3, 8, 11 })
        {
            var bytes = Frame(); bytes[offset] = 0xFF; malformed.Add(bytes);
        }
        foreach (var bytes in malformed)
            Check(M137FeatureProtocol.Read(new Link(bytes, bytes, bytes, bytes)).Level == -1,
                "Wrong headers, identity, length and out-of-range percent must not publish a level.");
        foreach (string model in new[] { "M139", "M620", "M184", "M230", "M999" })
        {
            var other = M137FeatureProtocol.Read(new Link(Frame(identity: model), Frame(identity: model)));
            Check(other.Level == -1 && other.Model == null, "Shared-ID devices must not inherit SC580 parsing or branding.");
            Check(M137FeatureProtocol.Read(new Link(Frame(), Frame(identity: model))).Level == -1,
                "Changed identity between transactions must invalidate the reading.");
        }
        Check(M137FeatureProtocol.Read(new Link(Frame())).Level == -1, "One response is not two confirmed transactions.");
        var failed = new Link(Frame(), Frame()) { SetOk = false };
        Check(M137FeatureProtocol.Read(failed).Level == -1 && failed.Commands.SequenceEqual(new[] { "S0" }),
            "A failed SET must not be followed by GET.");
        foreach (byte? id in new byte?[] { null, 0, 6, 13 })
        {
            var wrong = new Link(Frame(), Frame()) { FeatureReportId = id };
            Check(M137FeatureProtocol.Read(wrong).Level == -1 && wrong.Commands.Count == 0,
                "Do not send anything without descriptor report12.");
        }
        var silent = new Link();
        Check(M137FeatureProtocol.Read(silent).Level == -1 && silent.Commands.Count <= 3,
            "Silent receivers get at most two GET attempts; do not poll indefinitely.");
        Check(M137FeatureProtocol.Read(new Link(Frame(), Frame()) { ThrowOnGet = true }).Level == -1,
            "Native exceptions must downgrade to unknown.");

        InstanceScope.BeginScan();
        string a = InstanceScope.Register("receiver-A", new[] { (@"USB\VID_248A&PID_5B2F\A", "usbccgp") });
        string b = InstanceScope.Register("receiver-B", new[] { (@"USB\VID_248A&PID_5B2F\B", "usbccgp") });
        HidDiscovery.Bucket Bucket(string key) => new()
        {
            Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver", InstanceId = key,
            Kind = DeviceKind.Keyboard, Path = key
        };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        {
            Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver", Path = path,
            UsagePage = 0xFF00, Usage = 1, Feature = 33
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("receiver-A"), Channel("receiver-B") };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Query(string path)
        {
            calls++;
            Check(InstanceScope.Current == (path == "receiver-A" ? a : b), "Enter the exact selected physical scope.");
            return M137FeatureProtocol.Read(new Link(Frame(path == "receiver-A" ? 79 : 23), Frame(path == "receiver-A" ? 79 : 23)));
        }
        HidDiscovery.QueryM137FeatureCollections(buckets, channels, false, null, Query);
        Check(calls == 2 && buckets[a].ToDevice().Level == 79 && buckets[b].ToDevice().Level == 23,
            "Identical receivers must keep independent percentages.");
        Check(buckets[a].ToDevice().Kind == DeviceKind.Mouse && buckets[a].ToDevice().ConfirmedModel == "狼蛛 SC580",
            "Confirmed M137 must override an auxiliary keyboard interface.");
        Check(!buckets[a].ToDevice().PeripheralConfirmed && !buckets[a].ToDevice().Charging,
            "GetInfo has not established wireless online or charging state.");
        var displayed = new AutoDetectSource().UpdateFromDetection(new() { buckets[a].ToDevice() }).Single();
        Check(displayed.Kind == DeviceKind.Mouse && displayed.Name == "狼蛛 SC580" &&
            displayed.Level == 79 && displayed.Connected, "The production UI data source must receive the confirmed mouse percentage.");
        Check(HidDiscovery.TryDeviceKind(0x248A, 0x5B2F, 0xFF00, 1, out var featureKind, "Wireless-Receiver") &&
            featureKind == DeviceKind.Other, "The Feature collection can seed an unresolved receiver when mouse input cannot open.");
        var featureOnly = Bucket(a); featureOnly.Kind = DeviceKind.Other;
        HidDiscovery.QueryM137FeatureCollections(new() { [a] = featureOnly }, channels, false, null, Query);
        Check(featureOnly.ToDevice()?.Kind == DeviceKind.Mouse && featureOnly.ToDevice()?.Level == 79,
            "A validated Feature-only receiver must survive device conversion.");
        calls = 0;
        HidDiscovery.QueryM137FeatureCollections(buckets, channels, true, null, Query);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "Safe mode clears readings and cannot reuse a previous identity.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        HidDiscovery.QueryM137FeatureCollections(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 1 && buckets[a].ToDevice().Level == -1, "A disabled instance must not query or borrow another instance.");
        settings.DisabledRecognitionDeviceIds.Add("hid-248A-5B2F"); calls = 0;
        HidDiscovery.QueryM137FeatureCollections(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 0, "Respect legacy disabled IDs.");
        calls = 0;
        HidDiscovery.QueryM137FeatureCollections(new() { [a] = buckets[a] }, new() { Channel("receiver-B") }, false, null, Query);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1, "Do not borrow a channel from another instance.");
        InstanceScope.Register("receiver-A-extra", new[] { (@"USB\VID_248A&PID_5B2F\A", "usbccgp") });
        HidDiscovery.QueryM137FeatureCollections(new() { [a] = buckets[a] },
            new() { Channel("receiver-A"), Channel("receiver-A-extra") }, false, null, Query);
        Check(calls == 0, "Two different matching channels are ambiguous.");
        var badChannel = Channel("receiver-A"); badChannel.Input = 33;
        HidDiscovery.QueryM137FeatureCollections(new() { [a] = buckets[a] }, new() { badChannel }, false, null, Query);
        Check(calls == 0, "The separate input33 channel must never get a Feature query.");
        HidDiscovery.QueryM137FeatureCollections(new() { [a] = buckets[a] }, channels, false, null,
            _ => new(99, true, true, ReferenceBatteryProtocol.State.Online, "foreign", "Other", "M620"));
        Check(buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "" &&
            buckets[a].ToDevice().Kind == DeviceKind.Other, "Unconfirmed receiver must not be labelled a keyboard or SC580.");
        Check(RecognitionPolicy.CanQuery(0x248A, 0x5B2F, "Wireless-Receiver") &&
            !RecognitionPolicy.CanQuery(0x248A, 0x5B2F, "OTHER RECEIVER"), "Only the reported product may enter the scoped route.");
        Check(VendorProtocol.ProbeDevice(0x248A, 0x5B2F, "Wireless-Receiver", false).Exchanges == 0,
            "Never enable the generic cross-instance probe.");
        Check(VendorProtocol.ReadM137Feature("receiver-A").Level == -1, "Unscoped native reader cannot open a device.");
        using (InstanceScope.Enter(b))
            Check(VendorProtocol.ReadM137Feature("receiver-A").Level == -1, "Native reader cannot cross the current scope.");
        using var entered = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        using var done = new System.Threading.ManualResetEventSlim();
        const string gateKey = "test-m137-gate";
        int duplicate = 0;
        try
        {
            var timeout = VendorProtocol.RunReceiverQuery(gateKey, () =>
            {
                entered.Set();
                release.Wait();
                done.Set();
                return "late";
            }, _ => "timeout", 20);
            Check(entered.Wait(2000) && timeout == "timeout", "Native wait must be bounded without adopting late results.");
            var refused = VendorProtocol.RunReceiverQuery(gateKey, () => { duplicate++; return 1; }, _ => -1, 20);
            Check(refused == -1 && duplicate == 0, "Report and polling result types must share one instance gate.");
            Check(VendorProtocol.RunReceiverQuery("test-m137-other", () => 7, _ => -1) == 7,
                "A blocked receiver must not block another receiver.");
        }
        finally
        {
            release.Set();
            done.Wait(2000);
        }
        return count;
    }

    internal static void ReplayReport(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("probe-events.jsonl")!.Open());
        byte[]? response = null;
        while (reader.ReadLine() is { } line)
        {
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.GetProperty("Dir").GetString() != "RX") continue;
            string hex = json.RootElement.GetProperty("Hex").GetString()!;
            response = hex.Split('-').Select(s => Convert.ToByte(s, 16)).ToArray();
            if (ReceiverIdentityProtocol.TryIdentity(response, out var identity) && identity == "M137") break;
            response = null;
        }
        if (response == null) throw new Exception("Report contains no valid M137 response.");
        InstanceScope.BeginScan();
        const string channel = "offline-report-feature";
        string key = InstanceScope.Register(channel, new[] { (@"USB\VID_248A&PID_5B2F\OFFLINE", "usbccgp") });
        var bucket = new HidDiscovery.Bucket
        { InstanceId = key, Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver", Kind = DeviceKind.Keyboard };
        HidDiscovery.QueryM137FeatureCollections(new() { [key] = bucket }, new()
        {
            new() { Path = channel, Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver",
                UsagePage = 0xFF00, Usage = 1, Feature = 33 }
        }, false, null, _ => M137FeatureProtocol.Read(new Link(response, response)));
        var device = new AutoDetectSource().UpdateFromDetection(new() { bucket.ToDevice() }).Single();
        if (device.Level < 0 || device.Kind != DeviceKind.Mouse) throw new Exception("Report did not reach the UI data source.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            device.Name, Kind = device.Kind.ToString(), device.Level, device.Charging, device.PeripheralConfirmed,
            Mode = "Offline replay: one stored response repeated, not two physical samples"
        }));
    }

    private sealed class Link(params byte[][] frames) : ReceiverIdentityProtocol.ILink
    {
        private readonly Queue<byte[]> replies = new(frames);
        public byte? FeatureReportId { get; set; } = 12;
        public bool SetOk = true, ThrowOnGet;
        public List<string> Commands = new();
        private static void Validate(byte[] frame)
        {
            if (frame.Length != 33 || frame[0] != 12 || frame[1] != 1 || frame[2] != 32 ||
                frame[3] != 0 || frame.Skip(5).Any(b => b != 0))
                throw new Exception("Unexpected non-GetInfo command.");
        }
        public bool SetFeature(byte[] frame) { Validate(frame); Commands.Add("S" + frame[4]); return SetOk; }
        public byte[] GetFeature(byte[] frame)
        {
            Validate(frame); Commands.Add("G" + frame[4]);
            if (ThrowOnGet) throw new IOException("synthetic native failure");
            return replies.Count > 0 ? replies.Dequeue() : Array.Empty<byte>();
        }
        public void Delay(int milliseconds) { if (milliseconds != 16) throw new Exception("Expected official delay."); }
        public void Dispose() { }
    }
}
