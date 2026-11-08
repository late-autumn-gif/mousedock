using System.IO;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class RogOmniKeyboardRegression
{
    internal static int Run()
    {
        if (!RecognitionPolicy.CanQuery(0x0B05, 0x1ACE, "ROG OMNI RECEIVER"))
            throw new Exception("The reported ROG OMNI receiver must reach its dedicated keyboard reader.");
        if (RecognitionPolicy.CanQuery(0x248A, 0x5B2F, "OTHER RECEIVER"))
            throw new Exception("The unrelated Wireless-Receiver must remain outside the query policy.");
        int count = 2;
        void Check(bool ok, string message)
        {
            count++;
            if (!ok) throw new Exception(message);
        }
        const string pairPath = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#a&1&0&0000#{test}";
        const string keyPath = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col02#a&1&0&0001#{test}";
        Check(!RecognitionPolicy.CanQuery(0x0B05, 0x1ACE, "OTHER RECEIVER"), "Shared PID requires the exact product.");
        Check(!RecognitionPolicy.CanQuery(0x0B05, 0x1ACF, "ROG OMNI RECEIVER"), "Adjacent PID stays disabled.");
        Check(VendorProtocol.ProbeDevice(0x0B05, 0x1ACE, RogOmniKeyboardProtocol.Model, false).Exchanges == 0,
            "Report export must not issue cross-instance generic OMNI queries.");
        Check(RogOmniKeyboardProtocol.Match(0x0B05, 0x1ACE, "ROG OMNI RECEIVER", pairPath, 0xFF02, 1, 64, 64, 0)
            == RogOmniKeyboardProtocol.Channel.Pairing, "Pair list uses col01/FF02.");
        Check(RogOmniKeyboardProtocol.Match(0x0B05, 0x1ACE, "ROG OMNI RECEIVER", keyPath, 0xFF00, 1, 64, 64, 0)
            == RogOmniKeyboardProtocol.Channel.Keyboard, "Keyboard uses col02/FF00.");
        foreach (var bad in new[] {
            (keyPath.Replace("col02", "col03"), (ushort)0xFF01, 64, 64, 0),
            (keyPath, (ushort)0xFF00, 65, 65, 0),
            (keyPath.Replace("mi_02", "mi_01"), (ushort)0xFF00, 64, 64, 0),
            (keyPath.Replace("col02", "col020"), (ushort)0xFF00, 64, 64, 0),
            (keyPath, (ushort)0xFF00, 64, 64, 51) })
            Check(RogOmniKeyboardProtocol.Match(0x0B05, 0x1ACE, "ROG OMNI RECEIVER", bad.Item1,
                bad.Item2, 1, bad.Item3, bad.Item4, bad.Item5) == null, "Wrong collection must never receive a command.");

        // Synthetic protocol vectors from the cited implementation, not hardware captures.
        Check(RogOmniKeyboardProtocol.HasTarget(Paired()), "RX2 paired PID is 1B7A, not receiver 1ACE.");
        Check(RogOmniKeyboardProtocol.HasTarget(Paired(0x1A94, 0x1B7A)), "A paired mouse before RX2 must not mask the keyboard.");
        foreach (ushort model in new ushort[] { 0, 0x1ACE, 0x1AB0, 0x1A85, 0x1B78 })
            Check(!RogOmniKeyboardProtocol.HasTarget(Paired(model)), "Another model must not be named RX2.");
        Check(!RogOmniKeyboardProtocol.HasTarget(Paired(0, 0x1B7A)), "Stop at the pair-list terminator.");
        Check(!RogOmniKeyboardProtocol.HasTarget(Paired(0x1B7A, 0x1B7A)), "Ambiguous duplicate identity rejected.");
        Check(!RogOmniKeyboardProtocol.HasTarget(Paired()[..9]), "Truncated pair list rejected.");
        var wrongPair = Paired(); wrongPair[0] = 3;
        Check(!RogOmniKeyboardProtocol.HasTarget(wrongPair), "Mouse-channel identity rejected.");

        foreach (int level in new[] { 0, 1, 42, 99, 100 })
        foreach (byte charge in new byte[] { 0, 1 })
        {
            Check(RogOmniKeyboardProtocol.TryBattery(Battery(level, charge), out var reading) &&
                reading.Level == level && reading.Charging == (charge == 1) && reading.ChargingKnown,
                "Keyboard percentage and charging must use bytes 6 and 9 exactly.");
        }
        foreach (var bad in new[] { Battery(101), Battery(255), Battery(42, 2), Battery()[..10], new byte[64] })
            Check(!RogOmniKeyboardProtocol.TryBattery(bad, out _), "Malformed battery is unknown, never zero.");
        var mouseReply = Battery(); mouseReply[0] = 3; mouseReply[2] = 7;
        Check(!RogOmniKeyboardProtocol.TryBattery(mouseReply, out _), "Mouse battery must not be assigned to the keyboard.");
        var unavailable = Battery(); unavailable[1] = 0xFF; unavailable[2] = 0xAA;
        Check(!RogOmniKeyboardProtocol.TryBattery(unavailable, out _), "Offline receiver response is not a battery.");
        var echo = new byte[64]; echo[0] = 2; echo[1] = 0x12; echo[2] = 1;
        Check(!RogOmniKeyboardProtocol.TryBattery(echo, out _), "Request echo is not zero percent.");

        var pair = new Link(1, 0xA0, 0, _ => new[] { Paired() });
        var keyboard = new Link(2, 0x12, 1, _ => new[] { echo, mouseReply, Battery(73, 1) });
        pair.Input.Enqueue(Paired(0x1A85));
        keyboard.Input.Enqueue(Battery(88));
        var result = RogOmniKeyboardProtocol.Read(pair, keyboard);
        Check(result.Level == 73 && result.Charging && result.Model == RogOmniKeyboardProtocol.Model,
            "Discard old queues/echo/mouse traffic and publish this transaction's battery.");
        Check(pair.Writes == 2 && keyboard.Writes == 1, "Confirm pairing both before and after the battery query.");
        var changingFlags = new Link(1, 0xA0, 0, n =>
        {
            var response = Paired(); response[7] = (byte)n;
            return new[] { response };
        });
        Check(RogOmniKeyboardProtocol.Read(changingFlags,
            new Link(2, 0x12, 1, _ => new[] { Battery(63) })).Level == 63,
            "Only paired model IDs are identities; changing slot metadata must not discard a valid battery.");
        var foreign = new Link(1, 0xA0, 0, _ => new[] { Paired(0x1A85) });
        var untouched = new Link(2, 0x12, 1, _ => new[] { Battery() });
        Check(RogOmniKeyboardProtocol.Read(foreign, untouched).Level == -1 && untouched.Writes == 0,
            "Do not query a keyboard until RX2 pairing is confirmed.");
        var changed = new Link(1, 0xA0, 0, n => new[] { Paired(n == 1 ? (ushort)0x1B7A : (ushort)0x1A85) });
        Check(RogOmniKeyboardProtocol.Read(changed, new Link(2, 0x12, 1, _ => new[] { Battery() })).Level == -1,
            "A pairing change must invalidate the in-flight reading.");
        var silent = new Link(2, 0x12, 1, _ => Array.Empty<byte[]>());
        var sleep = RogOmniKeyboardProtocol.Read(new Link(1, 0xA0, 0, _ => new[] { Paired() }), silent);
        Check(sleep.Level == -1 && !sleep.Charging && silent.Now <= 1000, "Timeout is bounded and never fabricates a percentage.");
        var failedWrite = new Link(1, 0xA0, 0, _ => new[] { Paired() }) { WriteOk = false };
        Check(RogOmniKeyboardProtocol.Read(failedWrite, untouched).Level == -1 && untouched.Writes == 0,
            "Failed identity write must not query the keyboard.");
        var flood = new Link(1, 0xA0, 0, _ => new[] { Paired() });
        for (int i = 0; i < 32; i++) flood.Input.Enqueue(Paired());
        Check(RogOmniKeyboardProtocol.Read(flood, untouched).Level == -1 && flood.Writes == 0,
            "A queue that cannot be drained must not start a transaction.");

        InstanceScope.BeginScan();
        string a = Register(pairPath, "A"); Register(keyPath, "A");
        string otherPair = pairPath.Replace("#a&", "#b&"), otherKey = keyPath.Replace("#a&", "#b&");
        string b = Register(otherPair, "B"); Register(otherKey, "B");
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo>
        { Collection(pairPath, 0xFF02), Collection(keyPath, 0xFF00), Collection(otherPair, 0xFF02), Collection(otherKey, 0xFF00) };
        int calls = 0;
        ReferenceBatteryProtocol.Reading Query(string p, string k)
        {
            calls++;
            Check(InstanceScope.Current == (p == pairPath ? a : b), "Native query must enter the receiver's scope.");
            Check((p == pairPath && k == keyPath) || (p == otherPair && k == otherKey), "Never mix channels across receivers.");
            return new(p == pairPath ? 73 : 24, false, true, ReferenceBatteryProtocol.State.Online,
                "synthetic", RogOmniKeyboardProtocol.Model, "1B7A");
        }
        HidDiscovery.QueryRogOmniKeyboards(buckets, channels, false, null, Query);
        Check(calls == 2 && buckets[a].ToDevice().Level == 73 && buckets[b].ToDevice().Level == 24,
            "Identical receivers preserve separate readings.");
        Check(buckets[a].ToDevice().Kind == DeviceKind.Keyboard &&
            buckets[a].ToDevice().ConfirmedModel == RogOmniKeyboardProtocol.Model, "Confirmed RX2 renders as a keyboard.");
        calls = 0;
        HidDiscovery.QueryRogOmniKeyboards(buckets, channels, true, null, Query);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1, "Safe mode clears a prior live reading without querying.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        HidDiscovery.QueryRogOmniKeyboards(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 1 && buckets[a].ToDevice().Level == -1, "Disabling one receiver must not disable or query another.");
        calls = 0;
        settings.DisabledRecognitionDeviceIds.Clear();
        settings.DisabledRecognitionDeviceIds.Add("hid-0B05-1ACE");
        HidDiscovery.QueryRogOmniKeyboards(buckets, channels, false, new DevicePreferences(settings), Query);
        Check(calls == 0, "Legacy disabled IDs still block both instances.");
        calls = 0;
        HidDiscovery.QueryRogOmniKeyboards(new() { [a] = Bucket(a) },
            new() { Collection(pairPath, 0xFF02), Collection(otherKey, 0xFF00) }, false, null, Query);
        Check(calls == 0, "A missing local keyboard channel cannot borrow another receiver's channel.");
        HidDiscovery.QueryRogOmniKeyboards(new() { [a] = buckets[a] }, channels, false, null,
            (_, _) => throw new IOException("synthetic failure"));
        Check(buckets[a].ToDevice().Level == -1 && !buckets[a].ToDevice().Charging, "I/O exceptions downgrade to unknown.");
        HidDiscovery.QueryRogOmniKeyboards(new() { [a] = buckets[a] }, channels, false, null,
            (_, _) => new(87, true, true, ReferenceBatteryProtocol.State.Online, "foreign", "OTHER", "1AB0"));
        Check(buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "Even a successful reader cannot publish another model's battery.");
        return count;
    }

    private static string Register(string path, string id) =>
        InstanceScope.Register(path, new[] { ($@"USB\VID_0B05&PID_1ACE\{id}", "usbccgp") });
    private static HidDiscovery.Bucket Bucket(string id) => new()
    { Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER", InstanceId = id, Kind = DeviceKind.Mouse };
    private static HidDiscovery.VendorCollectionInfo Collection(string path, ushort page) => new()
    { Vid = 0x0B05, Pid = 0x1ACE, Product = "ROG OMNI RECEIVER", Path = path, UsagePage = page, Usage = 1, Input = 64, Output = 64 };
    private static byte[] Paired(ushort first = 0x1B7A, ushort second = 0)
    {
        var b = new byte[64]; b[0] = 1; b[1] = 0xA0;
        b[5] = (byte)first; b[6] = (byte)(first >> 8); b[9] = (byte)second; b[10] = (byte)(second >> 8);
        return b;
    }
    private static byte[] Battery(int level = 42, byte charging = 0)
    {
        var b = new byte[64]; b[0] = 2; b[1] = 0x12; b[2] = 1;
        b[6] = (byte)level; b[7] = 5; b[9] = charging;
        return b;
    }

    private sealed class Link(byte report, byte command, byte sub, Func<int, byte[][]> replies) : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        public int Writes;
        public bool WriteOk = true;
        public Queue<byte[]> Input = new();
        public byte[] Read(int timeoutMs)
        {
            Now += Input.Count == 0 ? timeoutMs : 1;
            return Input.Count == 0 ? null! : Input.Dequeue();
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            if (request.Length != 64 || request[0] != report || request[1] != command || request[2] != sub ||
                request.Skip(3).Any(x => x != 0))
                throw new Exception("Unexpected command or configuration write.");
            Writes++;
            if (WriteOk) foreach (var response in replies(Writes)) Input.Enqueue(response);
            return WriteOk;
        }
        public bool SetFeature(byte[] request) => throw new Exception("No feature writes allowed.");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("No feature reads expected.");
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
