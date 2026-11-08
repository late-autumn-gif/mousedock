using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class VgnF1Regression
{
    private static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link) =>
        VgnF1Protocol.Read(link);

    private static void Query(Dictionary<string, HidDiscovery.Bucket> buckets,
        List<HidDiscovery.VendorCollectionInfo> channels, bool safeMode, DevicePreferences? preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query) =>
        HidDiscovery.QueryVgnF1Collections(buckets, channels, safeMode, preferences!, query);

    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
        var link = new Link();
        var reading = Read(link);
        Check(reading.Level == 61 && reading.Charging && reading.ChargingKnown &&
            reading.Model == "VGN F1 MOBA", "Official F1 commands must deliver verified model and charging percentage.");
        Check(link.Commands.SequenceEqual(new byte[] { 1, 3, 4, 3 }),
            "Identity and battery must be bounded by current online evidence.");
        foreach (int level in new[] { 0, 1, 99, 100 })
        foreach (byte charge in new byte[] { 0, 1 })
        {
            reading = Read(new Link { Battery = Battery(level, charge) });
            Check(reading.Level == level && reading.Charging == (charge == 1),
                "Zero is a valid percentage and only the confirmed flag means charging.");
        }
        foreach (byte mid in new byte[] { 5, 6, 33 })
            Check(Read(new Link { Identity = Identity(2, mid) }).Model == "VGN F1 MOBA",
                "Official F1 MOBA MID variants must resolve only with CID2.");
        foreach (var identity in new[] { Identity(2, 1), Identity(2, 3), Identity(118, 5), Identity(2, 99) })
        {
            reading = Read(new Link { Identity = identity });
            Check(reading.Level == 61 && reading.Model == null,
                "Other or unknown identities may keep valid battery but must not inherit MOBA branding.");
        }
        reading = Read(new Link { Identity = null });
        Check(reading.Level == 61 && reading.Model == null, "Missing optional identity preserves valid generic battery.");
        var offline = new Link { OnlineBefore = Online(false) };
        Check(Read(offline).Level == -1 && !offline.Commands.Contains((byte)4),
            "Offline receivers must not request or expose battery.");
        Check(Read(new Link { OnlineAfter = Online(false) }).Level == -1,
            "A mouse going offline during the transaction invalidates battery.");
        Check(Read(new Link { OnlineAfter = Online(true, 0x44) }).Level == -1,
            "A pairing-address change during the transaction invalidates battery.");
        Check(Read(new Link { OnlineAfter = null }).Level == -1,
            "Missing final online confirmation must not publish an earlier battery.");

        var malformed = new List<byte[]> { Battery(101), Battery(255), new byte[17], Battery()[..16],
            new byte[18], Convert.FromHexString("0804000000000000000000000000000049") };
        foreach (int offset in new[] { 0, 1, 2, 16 })
        {
            byte[] bytes = Battery(); bytes[offset] ^= 0x80; malformed.Add(bytes);
        }
        foreach (int offset in new[] { 0, 1 })
        {
            byte[] bytes = Battery(); bytes[offset] ^= 0x80; FixChecksum(bytes); malformed.Add(bytes);
        }
        var error = Battery(); error[2] = 1; FixChecksum(error); malformed.Add(error);
        foreach (var frame in malformed)
            Check(Read(new Link { Battery = frame }).Level == -1,
                "Echoes, errors, malformed reports and invalid percentages cannot become a reading.");
        var neverDrained = new Link { EndlessInput = true };
        Check(Read(neverDrained).Level == -1 && neverDrained.Commands.Count == 0 && neverDrained.Reads <= 32,
            "An uncleared input queue must cancel the transaction before any write.");
        var failedWrite = new Link { WriteOk = false };
        Check(Read(failedWrite).Level == -1, "Failed writes must never publish buffered data.");
        Check(Read(new Link { ThrowOnRead = true }).Level == -1, "Native exceptions downgrade to unknown.");

        InstanceScope.BeginScan();
        string a = InstanceScope.Register("vgn-A", new[] { (@"USB\VID_3554&PID_F503\A", "usbccgp") });
        string b = InstanceScope.Register("vgn-B", new[] { (@"USB\VID_3554&PID_F503\B", "usbccgp") });
        HidDiscovery.Bucket Bucket(string id) => new()
        {
            Vid = 0x3554, Pid = 0xF503, Product = "VGN Mouse 2.4G Receiver",
            InstanceId = id, Kind = DeviceKind.Mouse, Path = id
        };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        {
            Path = path, Vid = 0x3554, Pid = 0xF503, Product = "VGN Mouse 2.4G Receiver",
            UsagePage = 0xFF02, Usage = 2, Input = 17, Output = 17, Feature = 0
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("vgn-A"), Channel("vgn-B") };
        int calls = 0;
        ReferenceBatteryProtocol.Reading DeviceQuery(string path)
        {
            calls++;
            Check(InstanceScope.Current == (path == "vgn-A" ? a : b), "Query must enter the selected physical scope.");
            return Read(new Link { Battery = Battery(path == "vgn-A" ? 0 : 23, 0) });
        }
        Query(buckets, channels, false, null, DeviceQuery);
        Check(calls == 2 && buckets[a].ToDevice().Level == 0 && buckets[b].ToDevice().Level == 23,
            "Identical receivers must keep independent percentages, including zero.");
        Check(buckets[a].ToDevice().Name == "VGN F1 MOBA" && buckets[a].ToDevice().ConfirmedModel == "VGN F1 MOBA" &&
            buckets[a].ToDevice().Kind == DeviceKind.Mouse && buckets[a].ToDevice().PeripheralConfirmed,
            "Verified online MOBA must reach the production discovery model.");
        var displayed = new AutoDetectSource().UpdateFromDetection(new() { buckets[a].ToDevice() }).Single();
        Check(displayed.Level == 0 && displayed.Name == "VGN F1 MOBA" && displayed.Connected,
            "Valid zero-percent MOBA must reach the actual UI data source.");
        calls = 0;
        Query(buckets, channels, true, null, DeviceQuery);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "Safe mode must clear stale battery and identity without querying.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        Query(buckets, channels, false, new DevicePreferences(settings), DeviceQuery);
        Check(calls == 1 && buckets[a].ToDevice().Level == -1, "Disabled instances cannot borrow enabled device readings.");
        settings.DisabledRecognitionDeviceIds.Add("hid-3554-F503"); calls = 0;
        Query(buckets, channels, false, new DevicePreferences(settings), DeviceQuery);
        Check(calls == 0, "Legacy disabled recognition IDs must remain effective.");
        Query(new() { [a] = buckets[a] }, new() { Channel("vgn-B") }, false, null, DeviceQuery);
        Check(calls == 0, "Never borrow the control channel of a second receiver.");
        InstanceScope.Register("vgn-A-duplicate", new[] { (@"USB\VID_3554&PID_F503\A", "usbccgp") });
        Query(new() { [a] = buckets[a] }, new() { Channel("vgn-A"), Channel("vgn-A-duplicate") }, false, null, DeviceQuery);
        Check(calls == 0, "Ambiguous control collections must not be queried.");
        foreach (string wrong in new[] { "VGN F1 MOBA", "Wireless Gaming Keyboard", "Other Receiver" })
        {
            var channel = Channel("vgn-A"); channel.Product = wrong;
            Query(new() { [a] = buckets[a] }, new() { channel }, false, null, DeviceQuery);
            Check(calls == 0, "Only the reported receiver descriptor may enter the native reader.");
        }
        var wrongCaps = Channel("vgn-A"); wrongCaps.Feature = 17;
        Query(new() { [a] = buckets[a] }, new() { wrongCaps }, false, null, DeviceQuery);
        Check(calls == 0, "Feature variants must not inherit the interrupt protocol.");
        foreach (int mismatch in Enumerable.Range(0, 6))
        {
            var channel = Channel("vgn-A");
            switch (mismatch)
            {
                case 0: channel.Vid = 0x1234; break;
                case 1: channel.Pid = 0xF506; break;
                case 2: channel.UsagePage = 0xFF01; break;
                case 3: channel.Usage = 1; break;
                case 4: channel.Input = 18; break;
                case 5: channel.Output = 18; break;
            }
            Query(new() { [a] = buckets[a] }, new() { channel }, false, null, DeviceQuery);
            Check(calls == 0, "Vendor, product, usage and interrupt lengths must all match.");
        }
        Query(new() { [a] = buckets[a] }, channels, false, null,
            _ => Read(new Link { Identity = Identity(118, 5) }));
        Check(buckets[a].ToDevice().Level == 61 && buckets[a].ToDevice().ConfirmedModel == "" &&
            !buckets[a].ToDevice().Name.Contains("MOBA"), "Unknown identity must erase prior MOBA branding.");
        Check(VendorProtocol.ReadVgnF1("vgn-A").Level == -1,
            "An unscoped native reader cannot open a device.");
        using (InstanceScope.Enter(b))
            Check(VendorProtocol.ReadVgnF1("vgn-A").Level == -1,
                "Native reader must reject another physical receiver before opening it.");
        return count;
    }

    private static byte[] Online(bool online = true, byte address = 0x11)
    {
        var bytes = new byte[17]; bytes[0] = 8; bytes[1] = 3;
        bytes[6] = online ? (byte)1 : (byte)0;
        bytes[7] = address; bytes[8] = 0x22; bytes[9] = 0x33;
        FixChecksum(bytes); return bytes;
    }
    private static byte[] Battery(int level = 61, byte charge = 1)
    {
        var bytes = new byte[17]; bytes[0] = 8; bytes[1] = 4;
        bytes[6] = (byte)level; bytes[7] = charge; bytes[8] = 0x0E; bytes[9] = 0x90;
        FixChecksum(bytes); return bytes;
    }
    private static byte[] Identity(byte cid = 2, byte mid = 5)
    {
        var bytes = new byte[17]; bytes[0] = 8; bytes[1] = 1; bytes[5] = 8;
        bytes[6] = 0x12; bytes[7] = 0x34; bytes[8] = 0x56; bytes[9] = 0x78;
        bytes[10] = cid; bytes[11] = mid; bytes[12] = 0;
        FixChecksum(bytes); return bytes;
    }
    private static void FixChecksum(byte[] bytes) =>
        bytes[16] = unchecked((byte)(0x55 - bytes.Take(16).Sum(b => b)));

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public byte[]? Battery = VgnF1Regression.Battery(), Identity = VgnF1Regression.Identity();
        public byte[]? OnlineBefore = Online(), OnlineAfter = Online();
        public bool EndlessInput, ThrowOnRead, WriteOk = true;
        public int Reads;
        public List<byte> Commands = new();
        private byte[]? pending;
        private int onlineQueries;
        public long Now { get; private set; }
        public byte[] Read(int timeoutMs)
        {
            Reads++; Now += Math.Max(1, timeoutMs);
            if (ThrowOnRead) throw new System.IO.IOException("Synthetic native read failure.");
            if (EndlessInput) return new byte[17];
            var result = pending; pending = null; return result!;
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            if (request.Length != 17 || request[0] != 8 || request[2] != 0 ||
                (request.Sum(b => b) & 255) != 0x55)
                throw new Exception("Invalid VGN report framing/checksum.");
            if (request[1] == 1)
            {
                if (request[5] != 8 || request.Skip(10).Take(6).Any(b => b != 0))
                    throw new Exception("Identity must use four nonce bytes and four zero bytes.");
            }
            else if (request[1] is not (3 or 4) || request.Skip(2).Take(14).Any(b => b != 0))
                throw new Exception("Unexpected VGN command or nonzero configuration payload.");
            Commands.Add(request[1]);
            pending = request[1] switch
            {
                1 => Identity, 3 => onlineQueries++ == 0 ? OnlineBefore : OnlineAfter, 4 => Battery, _ => null
            };
            return WriteOk;
        }
        public bool SetFeature(byte[] request) => throw new Exception("No Feature writes are authorized.");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("No Feature reads are used.");
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
