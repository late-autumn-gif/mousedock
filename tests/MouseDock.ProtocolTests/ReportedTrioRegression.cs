using System.IO;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ReportedTrioRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Reported trio: " + message); count++; }
        bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException) { return true; } }
        byte[] Rapoo(int pct, int status = 1) { var b = new byte[19]; b[0] = 7; b[7] = (byte)status; b[8] = (byte)pct; b[18] = 0x80; return b; }
        byte[] V9(int pct, int status = 2) { var b = new byte[64]; b[0] = 0x55; b[1] = 0x65; b[2] = (byte)pct; b[3] = (byte)status; return b; }
        byte[] By(int pct) { var b = new byte[20]; b[0] = 19; b[1] = 74; b[2] = 1; b[4] = 2; b[5] = (byte)pct; b[19] = unchecked((byte)b.Take(19).Sum(v => (int)v)); return b; }
        Check(ExtraDeviceRoutes.Find(0x24AE, 0x1417, "Rapoo Gaming Device") is
            { Family: "RapooVt3Max", Kind: DeviceKind.Mouse, Transport: "2.4G" }, "VT3 MAX own route");
        Check(!RapooVt3MaxProtocol.IsCandidate(0x24AE, 0x1413) && !RapooVt3MaxProtocol.IsCandidate(0x24AF, 0x1417) &&
            !RapooVt3MaxProtocol.IsCandidate(0x24AE, 0x4617), "no adjacent, other vendor or untested wired descriptor");
        Check(DeviceRoutes.IsKnown(0x24AE, 0x1417, "Rapoo Gaming Device"), "new route admitted by query policy");
        Check(ExtraDeviceRoutes.Find(0x41E4, 0x2001, "MCHOSE 2.4G Wireless") is
            { Kind: DeviceKind.Keyboard, Family: "MchoseBy" }, "G89 generic product uses keyboard route");
        for (int pct = 0; pct <= 100; pct++)
        {
            for (int state = 1; state <= 3; state++)
            {
                var r = RapooVt3MaxProtocol.Decode(Rapoo(pct, state));
                Check(r.Level == pct && r.ChargingKnown && r.Charging == (state != 1), "VT3 entire battery and charging range");
            }
            Check(HeadsetExpansionBolutek.Decode(V9(pct), true).Level == pct, "V9 whole on-battery range");
            Check(KeyboardExpansionMchoseBy.Decode(By(pct)).Level == pct, "G89 whole battery range");
        }
        foreach (int pct in new[] { 101, 255 }) Check(Reject(() => RapooVt3MaxProtocol.Decode(Rapoo(pct))), "VT3 rejects percent range");
        foreach (int status in new[] { 0, 4, 255 }) Check(Reject(() => RapooVt3MaxProtocol.Decode(Rapoo(50, status))), "VT3 rejects unavailable state");
        foreach (int size in new[] { 0, 8, 18, 20, 64 }) Check(Reject(() => RapooVt3MaxProtocol.Decode(new byte[size])), "VT3 exact size");
        var wrongId = Rapoo(80); wrongId[0] = 6; Check(Reject(() => RapooVt3MaxProtocol.Decode(wrongId)), "VT3 wrong report");
        var offline = Rapoo(80); offline[18] = 0; Check(Reject(() => RapooVt3MaxProtocol.Decode(offline)), "VT3 offline receiver cannot lend percentage");
        var echo = KeyboardExpansionMchoseBy.Request(20); Check(Reject(() => KeyboardExpansionMchoseBy.Decode(echo)), "G89 echo not zero battery");

        // Descriptor layouts transcribed from the user's report; no capture or private path is committed.
        HidDiscovery.VendorCollectionInfo Channel(ushort vid, ushort pid, string name, string path, ushort page, ushort usage, int size) =>
            new() { Vid = vid, Pid = pid, Product = name, Path = path, UsagePage = page, Usage = usage, Input = (ushort)size, Output = (ushort)size };
        ExpansionHidMetadata Meta(byte id, int size, ushort revision, bool output = true) => new()
        { Revision = revision, InputIds = new() { id }, OutputIds = output ? new() { id } : new(), FeatureIds = new(),
            InputSizes = new() { [id] = size }, OutputSizes = output ? new() { [id] = size } : new(), FeatureSizes = new() };
        var v9Channel = Channel(0x291D, 0x385D, "MCHOSE V9 PRO", @"test&mi_00&col05#v9", 0xFF90, 1, 64);
        var v9meta = Meta(0x55, 64, 0x12, false);
        HeadsetExpansionProfiles.ValidateV9Passive(v9Channel, v9meta); count++;
        Check(Reject(() => HeadsetExpansionProfiles.Validate("V9Pro", v9Channel, v9meta)), "input-only cannot pass active V9 validation");
        v9meta.Revision = 0x13; Check(Reject(() => HeadsetExpansionProfiles.ValidateV9Passive(v9Channel, v9meta)), "passive exception exact revision"); v9meta.Revision = 0x12;
        v9Channel.Product = "MCHOSE S9"; Check(Reject(() => HeadsetExpansionProfiles.ValidateV9Passive(v9Channel, v9meta)), "shared audio PID requires exact model"); v9Channel.Product = "MCHOSE V9 PRO";
        v9meta.OutputIds.Add(0x41); Check(Reject(() => HeadsetExpansionProfiles.ValidateV9Passive(v9Channel, v9meta)), "unrelated output map rejected"); v9meta.OutputIds.Clear();
        var rapooChannel = Channel(0x24AE, 0x1417, "Rapoo Gaming Device", @"test&mi_01&col09#vt3", 0xFF00, 2, 19);
        var rapooMeta = Meta(7, 19, 0x0105); RapooVt3MaxProtocol.Validate(rapooChannel, rapooMeta); count++;
        rapooMeta.Revision++; Check(Reject(() => RapooVt3MaxProtocol.Validate(rapooChannel, rapooMeta)), "VT3 revision constrained"); rapooMeta.Revision--;
        rapooChannel.Usage = 14; Check(!RapooVt3MaxProtocol.Matches(rapooChannel), "firmware channel excluded"); rapooChannel.Usage = 2;

        var trace = new BatteryReplay.Trace();
        var detailed = Rapoo(45); detailed[2] = 42; detailed[13] = 99;
        var passive = new Link(Rapoo(44), detailed);
        Check(RapooVt3MaxProtocol.Read(new(passive, 19, 19, trace: trace)).Level == 45 && passive.Writes == 0, "VT3 two fresh passive notifications");
        Check(trace.Snapshot().Where(s => s.Stage == "expansion-input").Select(s => Convert.FromHexString(s.Hex!))
            .All(b => b[2] == 0 && b[13] == 0), "VT3 diagnostic omits DPI and config fields");
        var single = new Link(Rapoo(80)); Check(Reject(() => RapooVt3MaxProtocol.Read(new(single, 19, 19))) && single.Now <= 3100, "single report times out bounded");
        var empty = new Link(); Check(Reject(() => RapooVt3MaxProtocol.Read(new(empty, 19, 19))) && empty.Writes == 0, "no notifications is unknown");
        var flood = new Link() { Flood = true }; Check(Reject(() => RapooVt3MaxProtocol.Read(new(flood, 19, 19))) && flood.Writes == 0, "undrainable old queue no writes");
        var badSecond = new Link(Rapoo(50), offline); Check(Reject(() => RapooVt3MaxProtocol.Read(new(badSecond, 19, 19))), "second offline invalidates prior percentage");
        var snapshotTrace = new BatteryReplay.Trace();
        var snapshotLink = new Link(); int snapshotCalls = 0;
        Check(RapooVt3MaxProtocol.ReadWithSnapshot(new(snapshotLink, 19, 19, trace: snapshotTrace),
            () => Rapoo(++snapshotCalls == 1 ? 44 : 45)).Level == 45 && snapshotCalls == 2 && snapshotLink.Writes == 0,
            "standard current-state fallback after notification timeout uses second snapshot without writes");
        int noFallback = 0;
        Check(RapooVt3MaxProtocol.ReadWithSnapshot(new(new Link(Rapoo(50), Rapoo(51)), 19, 19),
            () => { noFallback++; return Rapoo(99); }).Level == 51 && noFallback == 0, "working notifications retain priority");
        foreach (var invalid in new byte[][] { null!, new byte[0], new byte[18], wrongId, offline, Rapoo(101), Rapoo(50, 0) })
        {
            int invalidCalls = 0;
            Check(Reject(() => RapooVt3MaxProtocol.ReadWithSnapshot(new(new Link(), 19, 19),
                () => ++invalidCalls == 1 ? Rapoo(60) : invalid)), "invalid/missing second current snapshot rejects previous reading");
        }
        Check(Reject(() => RapooVt3MaxProtocol.ReadWithSnapshot(new(new Link(offline), 19, 19),
            () => { noFallback++; return Rapoo(90); })) && noFallback == 0, "invalid notification does not activate fallback");
        Check(Reject(() => RapooVt3MaxProtocol.ReadWithSnapshot(new(new Link() { Flood = true }, 19, 19),
            () => { noFallback++; return Rapoo(90); })) && noFallback == 0, "undrained queue does not activate fallback");
        var v9trace = new BatteryReplay.Trace(); var v9link = new Link(V9(70), V9(71));
        Check(HeadsetExpansionBolutek.ReadV9Passive(new(v9link, 64, 64, trace: v9trace)).Level == 71 && v9link.Writes == 0, "V9 input-only never writes");
        var frozen = new Link(V9(97, 3), V9(70));
        Check(HeadsetExpansionBolutek.ReadV9Passive(new(frozen, 64, 64)).Level == -1, "V9 frozen first response cannot confirm battery");
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Passive(new(new Link(V9(70), V9(70, 26)), 64, 64))), "V9 sleep second report invalidates battery");
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Passive(new(new Link(V9(70)), 64, 64))), "V9 requires second notification");
        var bytrace = new BatteryReplay.Trace(); var bylink = new Link { OnWrite = n => By(80 + n) };
        Check(KeyboardExpansionMchoseBy.Read(new(bylink, 20, 20, 0, 19, 19, 0, trace: bytrace)).Level == 82 && bylink.Writes == 2,
            "G89 actual transaction path, second current percentage");
        var bytimeout = new Link { OnWrite = n => n == 1 ? By(80) : null };
        Check(Reject(() => KeyboardExpansionMchoseBy.Read(new(bytimeout, 20, 20, 0, 19, 19, 0))), "G89 missing second response rejected");

        BatteryReplay.Collection Capture(string receiver, int id, HidDiscovery.VendorCollectionInfo c, string label, ushort revision, string ids) =>
            new(receiver, id, c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature, false, false,
                InterfaceLabel: label, UsbRevision: revision, ReportIds: ids);
        var bychannel = Channel(0x41E4, 0x2001, "MCHOSE 2.4G Wireless", @"test&mi_01&col01#g89", 0xFF02, 2, 20);
        var collections = new[] { Capture("r1", 1, rapooChannel, "MI_01/COL_09", 0x0105, "IN[07] OUT[07] FEATURE[]"),
            Capture("r2", 2, v9Channel, "MI_00/COL_05", 0x12, "IN[55] OUT[] FEATURE[]"),
            Capture("r3", 3, bychannel, "MI_01/COL_01", 0x3000, "IN[13] OUT[13] FEATURE[]") };
        foreach (var (receiver, protocol, samples, expected) in new[] {
            ("r1", "ExtraMouse:RapooVt3Max", trace.Snapshot(), 45), ("r2", "ExtraHeadset:V9Pro", v9trace.Snapshot(), 71),
            ("r1", "ExtraMouse:RapooVt3Max", snapshotTrace.Snapshot(), 45),
            ("r3", "ExtraKeyboard:MchoseBy", bytrace.Snapshot(), 82) })
        {
            var q = new BatteryReplay.Query(receiver, null, protocol, "Online", null, expected, samples);
            Check(BatteryReplay.Replay(q, collections) is { ValueKind: "captured" } result && result.Percent == expected, "real parser trace replay " + protocol);
            Check(BatteryReplay.Replay(q with { Receiver = "wrong" }, collections).Percent == null, "replay physical instance boundary");
            Check(BatteryReplay.Replay(q with { Samples = samples.Take(samples.Length - 1).ToArray() }, collections).Percent == null, "truncated transaction not captured");
            Check(BatteryReplay.Replay(q with { Status = "Failed" }, collections).Percent == null, "failed capture never adopts earlier frames");
            Check(BatteryReplay.Replay(q, collections.Concat(new[] { collections.Single(c => c.Receiver == receiver) with { Interface = 99 } }).ToArray()).Percent == null,
                "duplicate channel replay is ambiguous");
            var corrupt = samples.Select(s => s.Hex != null ? s with { Hex = "not-hex" } : s).ToArray();
            Check(BatteryReplay.Replay(q with { Samples = corrupt }, collections).Percent == null, "malformed report replay rejected");
        }

        InstanceScope.BeginScan();
        string a = InstanceScope.Register(rapooChannel.Path, new[] { (@"USB\VID_24AE&PID_1417\SYNTHETIC_A", "usbccgp") });
        string otherPath = rapooChannel.Path + "other";
        string b = InstanceScope.Register(otherPath, new[] { (@"USB\VID_24AE&PID_1417\SYNTHETIC_B", "usbccgp") });
        var bucket = new HidDiscovery.Bucket { Vid = 0x24AE, Pid = 0x1417, Product = "Rapoo Gaming Device", Kind = DeviceKind.Mouse, InstanceId = a };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = bucket }; int calls = 0;
        ReferenceBatteryProtocol.Reading Query(ExtraDeviceRoutes.Route route, ushort vid, ushort pid, string product,
            IReadOnlyList<HidDiscovery.VendorCollectionInfo> cs, BatteryReplay.Trace t)
        { calls++; Check(cs.Count == 1 && cs[0].Path == rapooChannel.Path && InstanceScope.Allows(cs[0].Path) && !InstanceScope.Allows(otherPath), "normal discovery isolates receivers"); return RapooVt3MaxProtocol.Decode(Rapoo(65)); }
        var sibling = Channel(0x24AE, 0x1417, "Rapoo Gaming Device", otherPath, 0xFF00, 2, 19);
        var channels = new List<HidDiscovery.VendorCollectionInfo> { rapooChannel, rapooChannel, sibling };
        HidDiscovery.QueryExtraDevices(buckets, channels, false, null, Query);
        var shown = new AutoDetectSource().UpdateFromDetection(new[] { bucket.ToDevice() }.ToList()).Single();
        Check(calls == 1 && shown.Level == 65 && shown.Kind == DeviceKind.Mouse && shown.Name == RapooVt3MaxProtocol.Model, "normal DeviceInfo display pipeline");
        HidDiscovery.QueryExtraDevices(buckets, channels, true, null, Query);
        Check(calls == 1 && bucket.ToDevice().Level == -1, "safe mode clears reading without I/O");
        HidDiscovery.QueryExtraDevices(buckets, channels, false, new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds = new() { a } }), Query);
        Check(calls == 1, "disabled instance never read");
        channels.RemoveAll(c => c.Path == rapooChannel.Path);
        HidDiscovery.QueryExtraDevices(buckets, channels, false, null, Query);
        Check(calls == 1 && bucket.ToDevice().Level == -1, "sibling cannot lend missing channel");
        Check(VendorProtocol.ReadRapooVt3Max(0x24AE, 0x1417, "Rapoo Gaming Device", channels, new()).Level == -1, "unscoped native call never opens hardware");
        foreach (var c in new[] { bychannel, v9Channel })
        {
            InstanceScope.BeginScan();
            string id = InstanceScope.Register(c.Path, new[] { ($@"USB\VID_{c.Vid:X4}&PID_{c.Pid:X4}\SYNTHETIC", "usbccgp") });
            var target = new HidDiscovery.Bucket { Vid = c.Vid, Pid = c.Pid, Product = c.Product, InstanceId = id, Kind = DeviceKind.Mouse };
            var one = new Dictionary<string, HidDiscovery.Bucket> { [id] = target };
            ReferenceBatteryProtocol.Reading Current(ExtraDeviceRoutes.Route route, ushort vid, ushort pid, string product,
                IReadOnlyList<HidDiscovery.VendorCollectionInfo> cs, BatteryReplay.Trace t) => c == bychannel
                    ? KeyboardExpansionMchoseBy.Decode(By(67)) : HeadsetExpansionBolutek.Decode(V9(68), true);
            HidDiscovery.QueryExtraDevices(one, new() { c }, false, null, Current);
            var device = new AutoDetectSource().UpdateFromDetection(new[] { target.ToDevice() }.ToList()).Single();
            Check(device.Kind == (c == bychannel ? DeviceKind.Keyboard : DeviceKind.Headset) && device.Level == (c == bychannel ? 67 : 68),
                "G89/V9 production display path and classification");
            if (c == bychannel) Check(!device.Name.Contains("G89"), "generic G89 product is not globally renamed to user model");
        }
        return count;
    }

    private sealed class Link(params byte[][] notifications) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> queue = new(notifications);
        internal Func<int, byte[]?>? OnWrite;
        internal bool Flood;
        internal int Writes;
        public long Now { get; private set; }
        public byte[]? Read(int ms) { Now += Math.Max(1, ms); if (Flood) return new byte[19]; return ms == 1 || queue.Count == 0 ? null : queue.Dequeue(); }
        public bool Write(byte[] b, int ms) { Writes++; var r = OnWrite?.Invoke(Writes); if (r != null) queue.Enqueue(r); return true; }
        public bool SetFeature(byte[] b) => throw new Exception("Unexpected Feature write");
        public byte[] GetFeature(byte id, int length) => throw new Exception("Unexpected Feature read");
        public void Delay(int ms) => Now += ms;
    }
}
