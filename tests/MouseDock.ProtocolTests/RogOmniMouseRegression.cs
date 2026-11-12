using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class RogOmniMouseRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string reason)
        {
            count++;
            if (!condition) throw new Exception("ROG Keris II ACE: " + reason);
        }

        Check(DeviceRoutes.Find(0x0B05, 0x1ACE, "ROG OMNI RECEIVER")?.Kind == DeviceKind.Other,
            "A shared OMNI receiver must not be classified as a keyboard before pairing identity.");
        const string mousePath = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#mouse";
        Check(VendorProtocol.IsRogOmniChannel(mousePath, "ROG OMNI RECEIVER", 0xFF01, 1, 64, 64, 0, 3),
            "The report's MI_02/COL_03 mouse channel must be reachable.");
        Check(!VendorProtocol.IsRogOmniChannel(mousePath, "ROG OMNI RECEIVER", 0xFF00, 1, 64, 64, 0, 3) &&
            !VendorProtocol.IsRogOmniChannel(mousePath, "ROG OMNI RECEIVER", 0xFF01, 1, 64, 64, 65, 3) &&
            !VendorProtocol.IsRogOmniChannel(mousePath.Replace("col03", "col02"), "ROG OMNI RECEIVER",
                0xFF01, 1, 64, 64, 0, 3), "Wrong collection or capabilities cannot enter the mouse channel.");

        var paired = Pair();
        var pairing = new BatteryReplay.Collection("receiver-1", 8, 0x0B05, 0x1ACE, "ROG OMNI RECEIVER",
            0xFF02, 1, 64, 64, 0, false, true, 1);
        var mouse = pairing with { Interface = 12, Page = 0xFF01, OmniReport = 3 };
        var keyboard = pairing with { Interface = 10, Page = 0xFF00, OmniReport = 2 };
        var trace = new BatteryReplay.Trace();
        trace.Add("omni-paired", true, raw: paired, keep: true);
        var oldReport = new BatteryReplay.Query("receiver-1", 10, "RogScopeRxOmni", "Failed", null, null, trace.Snapshot());
        var replay = BatteryReplay.Replay(oldReport, [pairing, keyboard, mouse]);
        Check(replay.Model == "ROG 月刃2 ACE" && replay.Percent == null,
            "The supplied pairing-only report can identify 1B18 but cannot invent a battery value.");

        // Synthetic response vectors follow the official Gear Link mouse fields, not this user's battery.
        var presence = Frame(0, 2, 1);
        var battery = Frame(7, 0, 63);
        battery[10] = 1;
        ReferenceBatteryProtocol.Reading Read(byte[] first, Func<byte, byte, ushort, byte[]> query, BatteryReplay.Trace log) =>
            RogOmniMouseProtocol.Read(first, query, log);
        var expected = new Queue<(byte Report, byte Subcommand, ushort Index, byte[] Reply)>(
            [(3, 0, 2, presence), (3, 7, 0, battery), (1, 0, 0, paired)]);
        byte[] Query(byte report, byte subcommand, ushort index)
        {
            var next = expected.Dequeue();
            Check((report, subcommand, index) == (next.Report, next.Subcommand, next.Index),
                "Mouse queries must use presence, battery and pairing recheck in that order.");
            return next.Reply;
        }
        var reading = Read(paired, Query, trace);
        Check(expected.Count == 0 && reading is { Model: "ROG 月刃2 ACE", Level: 63,
            Charging: true, ChargingKnown: true, Status: ReferenceBatteryProtocol.State.Charging },
            "The mouse transaction must read raw byte 5 and charging byte 10.");
        var query = new BatteryReplay.Query("receiver-1", 12, "RogKerisIIOmni", "Charging",
            reading.Model, reading.Level, trace.Snapshot());
        replay = BatteryReplay.Replay(query, [pairing, mouse]);
        Check(replay is { Model: "ROG 月刃2 ACE", Percent: 63, Charging: true },
            "Sanitized production transactions must replay with the same identity and battery.");
        Check(BatteryReplay.Replay(query, [pairing with { Receiver = "receiver-2" }, mouse]).Percent == null &&
            BatteryReplay.Replay(query, [pairing, mouse with { Page = 0xFF00 }]).Percent == null,
            "Mouse replay must require the same receiver and exact mouse channel.");
        var samples = trace.Snapshot();
        foreach (var incomplete in new[] { samples[..^1], [samples[0], samples[2], samples[1], samples[3]],
            [samples[0], samples[1] with { Ok = false }, samples[2], samples[3]] })
            Check(BatteryReplay.Replay(query with { Samples = incomplete }, [pairing, mouse]).Percent == null,
                "Missing, failed or reordered transaction stages cannot produce a replayed percentage.");

        foreach (var vector in new (byte Level, byte Charging)[] { (0, 0), (100, 0), (100, 1) })
        {
            var value = Frame(7, 0, vector.Level);
            value[6] = 2;
            value[10] = vector.Charging;
            reading = Read(paired, (report, subcommand, _) => report == 1 ? paired : subcommand == 0 ? presence : value, null!);
            Check(reading.Level == vector.Level && reading.Charging == (vector.Charging == 1),
                "Verified online zero, full and charging values must retain their official meanings.");
        }
        foreach (int field in new[] { 11, 12 })
        {
            var emptyBattery = Frame(7, 0, 0);
            emptyBattery[field] = 1;
            var zeroTrace = new BatteryReplay.Trace();
            zeroTrace.Add("omni-paired", true, raw: paired, keep: true);
            reading = Read(paired, (report, subcommand, _) =>
                report == 1 ? paired : subcommand == 0 ? presence : emptyBattery, zeroTrace);
            Check(reading.Level == 0 && BatteryReplay.Replay(query with {
                Samples = zeroTrace.Snapshot(), Status = "Online" }, [pairing, mouse]).Percent == 0,
                "A genuine zero with only the official battery-type/full-effect field must survive capture and replay.");
        }
        var unknownPayload = Frame(7, 0, 0);
        unknownPayload[63] = 1;
        Check(!RogOmniMouseProtocol.TryParseBattery(unknownPayload, out _, out _),
            "Unknown trailing bytes alone cannot validate an otherwise empty power reply.");
        foreach (var invalid in new[] {
            Frame(7, 0, 101), Frame(7, 0, 63)[..63], Frame(7, 0, 0),
            Frame(1, 0, 63), Frame(7, 2, 63), Frame(7, 0, 63, 2), Frame(7, 0, 63, 3, 2)
        })
        {
            reading = Read(paired, (report, subcommand, _) =>
                report == 1 ? paired : subcommand == 0 ? presence : invalid, null!);
            Check(reading.Level == -1 && !reading.ChargingKnown,
                "Malformed, keyboard, echo or out-of-range mouse frames cannot become battery.");
        }
        int calls = 0;
        reading = Read(paired, (_, _, _) => { calls++; return Frame(0, 2, 0); }, null!);
        Check(reading is { Level: -1, Model: "ROG 月刃2 ACE" } && calls == 1,
            "An offline mouse must keep its identity without issuing a battery query.");
        reading = Read(paired, (report, subcommand, _) =>
            report == 1 ? Pair(0x1B7A) : subcommand == 0 ? presence : battery, null!);
        Check(reading.Level == -1 && !reading.ChargingKnown, "Changing the pairing invalidates mouse battery.");
        foreach (var identity in new[] { Pair(0x1234), Pair(0x1B7A), Pair(0x1B18, 0x1B18), Pair(0x1B18, 0x1234) })
        {
            reading = Read(identity, (_, _, _) => throw new Exception("An unconfirmed mouse must not be queried."), null!);
            Check(reading.Level == -1 && reading.Model == null, "Unknown or ambiguous pairings cannot use this mouse reader.");
        }
        reading = Read(paired, (_, _, _) => throw new System.IO.IOException("device removed"), null!);
        Check(reading is { Level: -1, Model: "ROG 月刃2 ACE" }, "Device removal must downgrade without losing verified identity.");

        var bucket = new HidDiscovery.Bucket
        {
            Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER", Kind = DeviceKind.Keyboard,
            HandledBy = HandledBy.RogOmniMouse, Battery = 63, Charging = true,
            InstanceId = "receiver-1"
        };
        var device = bucket.ToDevice();
        Check(device is { Name: "ROG 月刃2 ACE", Kind: DeviceKind.Mouse, Transport: "2.4G",
            Level: 63, Charging: true, ReceiverDiscovered: true, PeripheralConfirmed: true },
            "Normal detection must display the mouse rather than its auxiliary keyboard collections.");
        CheckDiscovery(Check);
        return count;
    }

    private static void CheckDiscovery(Action<bool, string> check)
    {
        InstanceScope.BeginScan();
        const string firstPair = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#A";
        const string firstMouse = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#A";
        const string firstKeyboard = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col02#A";
        const string secondPair = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#B";
        const string secondMouse = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#B";
        string Register(string path, string receiver) => InstanceScope.Register(path,
            [(string.Concat(@"USB\VID_0B05&PID_1ACE\", receiver), "usbccgp")]);
        string first = Register(firstPair, "A"), second = Register(secondPair, "B");
        Register(firstMouse, "A"); Register(firstKeyboard, "A"); Register(secondMouse, "B");
        HidDiscovery.Bucket Bucket(string instance) => new()
        {
            Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER", Kind = DeviceKind.Keyboard, InstanceId = instance
        };
        HidDiscovery.VendorCollectionInfo Channel(string path, ushort page) => new()
        {
            Path = path, Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER",
            UsagePage = page, Usage = 1, Input = 64, Output = 64
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [first] = Bucket(first), [second] = Bucket(second) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> {
            Channel(firstPair, 0xFF02), Channel(firstKeyboard, 0xFF00), Channel(firstMouse, 0xFF01),
            Channel(secondPair, 0xFF02), Channel(secondMouse, 0xFF01)
        };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Query(string pairing, string keyboard, string mouse, BatteryReplay.Trace trace)
        {
            calls++;
            check(InstanceScope.Current == (pairing == firstPair ? first : second),
                "OMNI queries must enter the correct physical receiver scope.");
            check(pairing == firstPair ? mouse == firstMouse && keyboard == firstKeyboard :
                mouse == secondMouse && keyboard == null, "Channels cannot be borrowed across identical receivers.");
            return new(pairing == firstPair ? 42 : 57, false, true, ReferenceBatteryProtocol.State.Online,
                "synthetic mouse response", "ROG 月刃2 ACE");
        }
        void Discover(bool safeMode = false, DevicePreferences preferences = null!) =>
            HidDiscovery.QueryRogScope(buckets, channels, safeMode, preferences, Query);
        Discover();
        check(calls == 2 && buckets[first].ToDevice().Level == 42 && buckets[second].ToDevice().Level == 57 &&
            buckets[first].ToDevice().Kind == DeviceKind.Mouse && buckets[second].ToDevice().Kind == DeviceKind.Mouse,
            "Normal discovery must surface independent mouse readings from two OMNI receivers.");
        calls = 0;
        Discover(true);
        check(calls == 0 && buckets[first].ToDevice().Level == -1 && !buckets[first].ToDevice().PeripheralConfirmed,
            "Safe mode must clear old private readings without issuing commands.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(first);
        Discover(preferences: new DevicePreferences(settings));
        check(calls == 1 && buckets[first].ToDevice().Level == -1 && buckets[second].ToDevice().Level == 57,
            "Disabling one receiver cannot disable or query the other.");
        channels.RemoveAll(c => c.Path == firstPair || c.Path == secondPair);
        calls = 0;
        Discover();
        check(calls == 0 && buckets.Values.All(b => b.ToDevice().Level == -1),
            "A missing pairing channel must clear readings without borrowing a channel from another receiver.");
        const string duplicatePair = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#A-duplicate";
        Register(duplicatePair, "A");
        channels.Add(Channel(firstPair, 0xFF02));
        channels.Add(Channel(duplicatePair, 0xFF02));
        Discover();
        check(calls == 0, "Ambiguous pairing channels must not issue commands.");
    }

    private static byte[] Pair(ushort first = 0x1B18, ushort second = 0)
    {
        var reply = new byte[64];
        reply[0] = 1; reply[1] = 0xA0;
        reply[5] = (byte)first; reply[6] = (byte)(first >> 8);
        reply[9] = (byte)second; reply[10] = (byte)(second >> 8);
        return reply;
    }

    private static byte[] Frame(byte subcommand, ushort index, byte value, byte report = 3, byte charging = 0)
    {
        var reply = new byte[64];
        reply[0] = report; reply[1] = 0x12; reply[2] = subcommand;
        reply[3] = (byte)index; reply[4] = (byte)(index >> 8);
        reply[5] = value; reply[10] = charging;
        return reply;
    }
}
