using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class K87DiscoveryRegression
{
    internal static int Run()
    {
        int checks = 0;
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) failures.Add(message);
        }

        const string product = "MCHOSE K87 V3 2.4G";
        Check(RecognitionPolicy.CanQuery(0x3837, 0x1015, product),
            "The verified K87 must reach its scoped battery reader.");
        Check(!RecognitionPolicy.CanQuery(0x3837, 0x1015, "MCHOSE G98 V3 2.4G"),
            "A shared PID must not admit a different keyboard.");
        Check(RecognitionPolicy.CanQuery(0x3837, 0x1018, "MCHOSE K5 Ultra"),
            "K5 Ultra now has its separate exact MHUB profile; K87 does not grant it.");

        foreach (int level in new[] { -1, 0, 85 })
        {
            var bucket = new HidDiscovery.Bucket
            {
                Vid = 0x3837, Pid = 0x1015, Product = product,
                Kind = DeviceKind.Mouse, Battery = level,
                InstanceId = "instance-k87"
            };
            bucket.Counts[DeviceKind.Mouse] = 1;
            bucket.Counts[DeviceKind.Keyboard] = 2;
            var device = bucket.ToDevice();
            Check(device.Kind == DeviceKind.Keyboard,
                $"K87 must retain its keyboard glyph even with a mouse collection and battery={level}.");
            Check(device.Level == level, "Classification must not invent or change a battery reading.");
            Check(device.Id == "instance-k87", "Classification must retain the physical instance.");
        }

        var mouse = new HidDiscovery.Bucket
        {
            Vid = 0x3837, Pid = 0x1018, Product = "MCHOSE K5 Ultra", Kind = DeviceKind.Mouse
        };
        mouse.Counts[DeviceKind.Mouse] = 1;
        mouse.Counts[DeviceKind.Keyboard] = 3;
        Check(mouse.ToDevice().Kind == DeviceKind.Mouse, "A K-number mouse must remain a mouse.");
        var live = new Link();
        Check(MchoseKeyboardProtocol.Read(live).Level == 85 && live.Writes == 1,
            "A fresh QHW response must survive a queued response with an older sequence.");
        var stale = new Link { StaleOnly = true };
        Check(MchoseKeyboardProtocol.Read(stale).Level == -1 && stale.Now <= 1000,
            "An old sequence must not become a fresh K87 battery reading.");
        Check(MchoseKeyboardProtocol.Read(new Link { ShortReply = true }).Level == -1,
            "A truncated native report cannot supply K87 battery.");
        Check(MchoseKeyboardProtocol.Read(new Link { Corrupt = true }).Level == -1,
            "The exchange must reject a corrupt QHW response.");
        var failedWrite = new Link { WriteOk = false };
        Check(MchoseKeyboardProtocol.Read(failedWrite).Level == -1 && failedWrite.Writes == 1,
            "An incomplete battery request cannot publish a reading.");
        var flood = new Link { Flood = true };
        Check(MchoseKeyboardProtocol.Read(flood).Level == -1 && flood.Writes == 0 && flood.Reads == 32,
            "A queue that cannot drain must not receive a new battery command.");
        var route = typeof(HidDiscovery).GetMethod("QueryK87Collections",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Check(route != null, "K87 needs a dedicated instance-scoped discovery route.");
        if (route != null)
        {
            InstanceScope.BeginScan();
            string a = InstanceScope.Register("k87-a", new[] { (@"USB\VID_3837&PID_1015\A", "usbccgp") });
            string b = InstanceScope.Register("k87-b", new[] { (@"USB\VID_3837&PID_1015\B", "usbccgp") });
            HidDiscovery.Bucket Bucket(string key) => new()
            { Vid = 0x3837, Pid = 0x1015, Product = product, InstanceId = key, Kind = DeviceKind.Keyboard };
            HidDiscovery.VendorCollectionInfo Channel(string path) => new()
            { Vid = 0x3837, Pid = 0x1015, Product = product, Path = path, UsagePage = 0xFF80, Usage = 1, Input = 64, Output = 64 };
            var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
            var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("k87-a"), Channel("k87-b") };
            int calls = 0;
            ReferenceBatteryProtocol.Reading Query(string path)
            {
                calls++;
                Check(InstanceScope.Current == (path == "k87-a" ? a : b), "K87 query must enter its own instance.");
                return new(path == "k87-a" ? 85 : 0, path == "k87-a", true,
                    ReferenceBatteryProtocol.State.Online, "fixture");
            }
            void Discover(bool safe = false, DevicePreferences? prefs = null,
                List<HidDiscovery.VendorCollectionInfo>? selected = null) =>
                route.Invoke(null, new object?[] { buckets, selected ?? channels, safe, prefs,
                    (Func<string, ReferenceBatteryProtocol.Reading>)Query });
            Discover();
            Check(calls == 2 && buckets[a].ToDevice().Level == 85 && buckets[a].ToDevice().Charging &&
                buckets[b].ToDevice().Level == 0, "Identical K87 receivers must keep distinct levels, including zero.");
            calls = 0; Discover(true);
            Check(calls == 0 && buckets[a].Battery == -1, "Safe mode cannot query or retain a live K87 reading.");
            var settings = new AppSettings();
            settings.DisabledRecognitionDeviceIds.Add(a);
            Discover(prefs: new DevicePreferences(settings));
            Check(calls == 1 && buckets[a].Battery == -1 && buckets[b].Battery == 0,
                "Disabling one K87 must not disable or borrow from the other.");
            calls = 0; buckets.Remove(b);
            Discover(selected: new() { Channel("k87-b") });
            Check(calls == 0, "A foreign K87 interface cannot supply this instance.");
            InstanceScope.Register("k87-extra", new[] { (@"USB\VID_3837&PID_1015\A", "usbccgp") });
            Discover(selected: new() { Channel("k87-a"), Channel("k87-extra") });
            Check(calls == 0, "Ambiguous K87 interfaces cannot be queried.");
            var wrong = Channel("k87-a"); wrong.UsagePage = 0xFF81;
            Discover(selected: new() { wrong });
            Check(calls == 0, "An unrelated vendor channel cannot authorize K87 queries.");
            Check(VendorProtocol.ReadK87("k87-a").Level == -1,
                "The native K87 reader cannot open without a physical instance.");
            using (InstanceScope.Enter(b))
                Check(VendorProtocol.ReadK87("k87-a").Level == -1,
                    "The native K87 reader cannot open a different receiver's channel.");
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        return checks;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> incoming = new();
        public bool WriteOk = true, StaleOnly, ShortReply, Corrupt, Flood;
        public int Writes, Reads;
        public long Now { get; private set; }
        public void Delay(int milliseconds) => Now += milliseconds;
        public bool SetFeature(byte[] report) => throw new InvalidOperationException("K87 must not send Feature commands.");
        public byte[] GetFeature(byte reportId, int length) => throw new InvalidOperationException("K87 must not probe Feature reports.");
        public byte[] Read(int timeout)
        {
            Reads++; Now += timeout;
            return Flood ? new byte[64] : incoming.Count > 0 ? incoming.Dequeue() : Array.Empty<byte>();
        }
        public bool Write(byte[] report, int timeout)
        {
            Writes++;
            if (!WriteOk) return false;
            if (report.Length != 64 || !report.Take(7).SequenceEqual(new byte[] { 0x4D, 1, 3, 0, 4, 9, 0 }))
                throw new Exception("The K87 reader sent a command other than the verified battery query.");
            byte[] Reply(byte sequence)
            {
                var response = new byte[ShortReply ? 11 : 64];
                new byte[] { 0x4D, 1, 3, 2, 4, 9, 0, sequence, 0, 85, (byte)(0x59 ^ sequence) }
                    .CopyTo(response, 0);
                if (Corrupt) response[10] ^= 0x80;
                return response;
            }
            incoming.Enqueue(Reply(unchecked((byte)(report[7] - 1))));
            if (!StaleOnly) incoming.Enqueue(Reply(report[7]));
            return true;
        }
    }
}
