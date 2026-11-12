using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class ReportedReceiversRegression
{
    private const string Mouse = "MCHOSE A7 Ultra";
    private const string Keyboard = "MonsGeek 2.4G Wireless Keyboard";

    internal static int Run()
    {
        int count = 0;
        void Check(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException("Reported receivers: " + label);
            count++;
        }

        byte[] MouseFrame(int level, byte mode = 9, byte model = 0x11, byte charge = 1)
        {
            var body = new byte[19];
            body[0] = 0x53;
            body[1] = 0x52;
            body[2] = model;
            body[8] = mode;
            body[9] = (byte)level;
            body[10] = charge;
            return new byte[] { 0x11, 0xF9 }.Concat(body.Select(value => (byte)(value ^ 255))).ToArray();
        }

        var mouse = MouseFrame(57);
        var keyboard = new byte[] { 5, 0x88, 0, 0, 62, 3 };
        var badCommand = (byte[])mouse.Clone();
        badCommand[1] = 0xF8;
        Check(VendorProtocol.TryParseA7UltraBattery(mouse, out int level, out bool charging) &&
              level == 57 && charging, "mouse percentage and charging");
        Check(VendorProtocol.TryParseA7UltraBattery(MouseFrame(0, charge: 0), out level, out charging) &&
              level == 0 && !charging, "mouse 0 percent");
        Check(VendorProtocol.TryParseA7UltraBattery(MouseFrame(100), out level, out _) && level == 100,
            "mouse 100 percent");
        foreach (var bad in new[] { MouseFrame(101), MouseFrame(57, 1), MouseFrame(57, model: 0x10),
                     MouseFrame(57, charge: 2), badCommand, mouse[..20], Array.Empty<byte>() })
            Check(!VendorProtocol.TryParseA7UltraBattery(bad, out level, out _) && level == -1, "invalid mouse response");

        Check(VendorProtocol.TryParseMonsGeekBatteryEvent(keyboard, out level, out charging) &&
              level == 62 && charging, "keyboard event");
        var padded = new byte[32];
        keyboard.CopyTo(padded, 0);
        padded[4] = 0;
        padded[5] = 1;
        Check(VendorProtocol.TryParseMonsGeekBatteryEvent(padded, out level, out charging) &&
              level == 0 && !charging, "keyboard Windows input buffer");
        foreach (var bad in new[] { new byte[] { 1, 0x88, 0, 0, 62, 3 }, new byte[] { 5, 0x87, 0, 0, 62, 3 },
                     new byte[] { 5, 0x88, 0, 0, 62, 2 }, new byte[] { 5, 0x88, 0, 0, 101, 1 },
                     new byte[] { 5, 0x88, 1, 0, 62, 1 }, keyboard[..5] })
            Check(!VendorProtocol.TryParseMonsGeekBatteryEvent(bad, out level, out _) && level == -1, "invalid keyboard response");

        var empty = new FakeInput();
        Check(VendorProtocol.ReadMonsGeekEvent(empty, null).Level == -1, "timeout remains unknown");
        var input = new FakeInput([5, 0x87, 0, 0, 62, 3], keyboard);
        Check(VendorProtocol.ReadMonsGeekEvent(input, null).Level == 62, "ignore other private events");

        Check(DeviceRoutes.Find(0x5253, 0x1020, Mouse)?.Kind == DeviceKind.Mouse, "mouse route");
        Check(DeviceRoutes.Find(0x3151, 0x5038, Keyboard)?.Kind == DeviceKind.Keyboard, "keyboard route");
        Check(!DeviceRoutes.IsKnown(0x5253, 0x1020, "MCHOSE A7 Pro") &&
              !DeviceRoutes.IsKnown(0x3151, 0x5037, Keyboard), "other product or receiver stays outside routes");

        InstanceScope.BeginScan();
        const string path = @"\\?\hid#vid_5253&pid_1020&mi_02&col02#fixture";
        const string other = @"\\?\hid#vid_5253&pid_1020&mi_02&col02#other";
        const string duplicate = @"\\?\hid#vid_5253&pid_1020&mi_02&col02#duplicate";
        string instance = InstanceScope.Register(path, [(@"USB\VID_5253&PID_1020\fixture", "")]);
        InstanceScope.Register(duplicate, [(@"USB\VID_5253&PID_1020\fixture", "")]);
        InstanceScope.Register(other, [(@"USB\VID_5253&PID_1020\other", "")]);
        HidDiscovery.VendorCollectionInfo Channel(string id) => new()
        {
            Path = id, Vid = 0x5253, Pid = 0x1020, Product = Mouse,
            UsagePage = 0xFF01, Usage = 1, Input = 65, Output = 65, Feature = 65
        };
        var bucket = new HidDiscovery.Bucket
        {
            Vid = 0x5253, Pid = 0x1020, Product = Mouse, Kind = DeviceKind.Mouse, InstanceId = instance
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [instance] = bucket };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Read(string route, HidDiscovery.VendorCollectionInfo channel, BatteryReplay.Trace trace)
        {
            calls++;
            Check(route == VendorProtocol.A7UltraRoute && channel.Path == path &&
                  InstanceScope.Allows(path) && !InstanceScope.Allows(other), "reader receives only the matching instance");
            return new(57, true, true, ReferenceBatteryProtocol.State.Charging, "synthetic", Mouse);
        }
        HidDiscovery.QueryReportedReceivers(buckets, [Channel(path)], true, null, Read);
        HidDiscovery.QueryReportedReceivers(buckets, [Channel(other)], false, null, Read);
        HidDiscovery.QueryReportedReceivers(buckets, [Channel(path), Channel(duplicate)], false, null, Read);
        Check(calls == 0 && bucket.Battery == -1, "safe mode, other instance and ambiguity skip query");
        HidDiscovery.QueryReportedReceivers(buckets, [Channel(path), Channel(other)], false, null, Read);
        var detected = bucket.ToDevice();
        Check(calls == 1 && detected.Level == 57 && detected.Charging && detected.ConfirmedModel == Mouse &&
              detected.Transport == "2.4G", "valid reading reaches normal device display");

        const string keyboardPath = @"\\?\hid#vid_3151&pid_5038&mi_01&col05#keyboard";
        string keyboardInstance = InstanceScope.Register(keyboardPath, [(@"USB\VID_3151&PID_5038\keyboard", "")]);
        var keyboardBucket = new HidDiscovery.Bucket
        {
            Vid = 0x3151, Pid = 0x5038, Product = Keyboard, Kind = DeviceKind.Keyboard, InstanceId = keyboardInstance
        };
        var keyboardBuckets = new Dictionary<string, HidDiscovery.Bucket> { [keyboardInstance] = keyboardBucket };
        var keyboardHid = new HidDiscovery.VendorCollectionInfo
        {
            Path = keyboardPath, Vid = 0x3151, Pid = 0x5038, Product = Keyboard,
            UsagePage = 0xFFFF, Usage = 1, Input = 32
        };
        HidDiscovery.QueryReportedReceivers(keyboardBuckets, [keyboardHid], false, null,
            (_, _, trace) => VendorProtocol.ReadMonsGeekEvent(new FakeInput(), trace));
        Check(keyboardBucket.ToDevice().Level == -1, "keyboard timeout reaches normal display as unknown");
        HidDiscovery.QueryReportedReceivers(keyboardBuckets, [keyboardHid], false, null,
            (_, _, trace) => VendorProtocol.ReadMonsGeekEvent(new FakeInput(keyboard), trace));
        Check(keyboardBucket.ToDevice() is { Level: 62, Charging: true, Kind: DeviceKind.Keyboard,
            Transport: "2.4G", ConfirmedModel: "" }, "keyboard reading reaches normal display without a guessed model");

        var mouseChannel = new BatteryReplay.Collection("receiver-1", 3, 0x5253, 0x1020, Mouse,
            0xFF01, 1, 65, 65, 65, false, true, InterfaceLabel: "MI_02/COL_02");
        var keyboardChannel = new BatteryReplay.Collection("receiver-1", 4, 0x3151, 0x5038, Keyboard,
            0xFFFF, 1, 32, 0, 0, false, false, InterfaceLabel: "MI_01/COL_05");
        BatteryReplay.Sample Sample(string stage, byte[] frame) => new(stage, true, Length: frame.Length,
            Hex: Convert.ToHexString(frame));
        var mouseRead = new BatteryReplay.Query("receiver-1", 3, VendorProtocol.A7UltraRoute, "Charging", Mouse, 57,
            [new("open", true), Sample("set-feature", VendorProtocol.BuildMchoseA7V2Request()), Sample("get-feature", mouse)]);
        var keyboardRead = new BatteryReplay.Query("receiver-1", 4, VendorProtocol.MonsGeekRoute, "Charging", null, 62,
            [new("open", true), Sample("battery-event", keyboard)]);
        Check(BatteryReplay.Replay(mouseRead, [mouseChannel]).Percent == 57, "mouse replay");
        Check(BatteryReplay.Replay(keyboardRead, [keyboardChannel]) is { Percent: 62, Charging: true, Model: null },
            "keyboard replay does not infer a model");
        Check(BatteryReplay.Replay(keyboardRead, [keyboardChannel with { Receiver = "receiver-2" }]).Percent == null,
            "cross-instance replay rejected");
        Check(BatteryReplay.Replay(mouseRead with { Samples = [new("open", true), Sample("get-feature", mouse)] },
            [mouseChannel]).Percent == null, "response without request rejected");
        return count;
    }

    private sealed class FakeInput(params byte[][] reports) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> input = new(reports);
        public long Now { get; private set; }
        public byte[]? Read(int timeout)
        {
            if (input.TryDequeue(out var frame)) { Now++; return frame; }
            Now += timeout;
            return null;
        }
        public bool Write(byte[] report, int timeout) => throw new InvalidOperationException();
        public bool SetFeature(byte[] report) => throw new InvalidOperationException();
        public byte[] GetFeature(byte reportId, int length) => throw new InvalidOperationException();
        public void Delay(int ms) => throw new InvalidOperationException();
    }
}
