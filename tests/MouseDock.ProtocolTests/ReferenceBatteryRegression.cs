using System.IO;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using Protocol = MouseDock.Services.Detection.ReferenceProfiles.Protocol;
using Reading = MouseDock.Services.Detection.ReferenceBatteryProtocol.Reading;
using State = MouseDock.Services.Detection.ReferenceBatteryProtocol.State;

internal static class ReferenceBatteryRegression
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool value, string reason)
        {
            assertions++;
            if (!value) throw new Exception("Reference port: " + reason);
        }

        // Match the public device identities where independently documented,
        // with exact collection and length constraints from the compatibility contract.
        var expectedProfiles = new (ushort Vid, ushort Pid, ushort Page, ushort Usage, Protocol Protocol, bool ReadIdentity)[]
        {
            (0x3554, 0xF517, 0xFF02, 2, Protocol.Compx, false),
            (0x395A, 0x0006, 0xFF00, 2, Protocol.ScyroxV12, false),
            (0x373B, 0x11D8, 0xFF02, 2, Protocol.Compx, false),
            (0x373B, 0x1278, 0xFF02, 2, Protocol.Compx, true),
            (0x24AE, 0x1453, 0xFF00, 2, Protocol.Rapoo, false),
            (0x1915, 0xAE8A, 0xFFA0, 1, Protocol.SoraV2, false)
        };
        Check(ReferenceProfiles.All.Count == expectedProfiles.Length, "Receiver allowlist size");
        foreach (var expected in expectedProfiles)
        {
            var profile = ReferenceProfiles.Find(expected.Vid, expected.Pid);
            Check(profile != null && profile.Page == expected.Page && profile.Usage == expected.Usage &&
                profile.Battery == expected.Protocol && profile.ReadIdentity == expected.ReadIdentity,
                $"Exact receiver route {expected.Vid:X4}:{expected.Pid:X4}");
        }

        var identityRows = File.ReadLines(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "atk-hub-3.2.28-identities.tsv")).Skip(1).ToArray();
        Check(identityRows.Length == 118, "ATK HUB identity coverage");
        foreach (string row in identityRows)
        {
            var columns = row.Split('\t');
            byte cid = byte.Parse(columns[0]);
            byte mid = byte.Parse(columns[1]);
            string display = AtkMouseIdentityCatalog.Find(cid, mid);
            if (cid == 1 && mid == 55)
                Check(columns[2] == "ATK PHANTOM ULTIMATE" && display == "ATK 幻影 Ultimate",
                    "Preserve the localized PHANTOM display name");
            else
                Check(string.Equals(WithoutWhitespace(display), WithoutWhitespace(columns[2]),
                    StringComparison.OrdinalIgnoreCase), $"ATK HUB identity {cid},{mid}");
        }

        static string? WithoutWhitespace(string? value) =>
            value == null ? null : string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

        Check(ReferenceBatteryProtocol.Request(Protocol.Rapoo) == null,
            "VT9S V2 uses passive battery reports");
        CheckVendorBatteryFrames(Check);
        CheckRapooBatteryFrames(Check);

        foreach (var profile in ReferenceProfiles.All)
        {
            var (input, output, feature) = Lengths(profile.Battery);
            Check(DeviceRoutes.IsKnown(profile.Vid, profile.Pid, "Wireless-Receiver"), "Finite reference candidate admitted");
            Check(ReferenceProfiles.AcceptsChannel(profile, profile.Page, profile.Usage, input, output, feature), "Exact capability route");
            Check(!ReferenceProfiles.AcceptsChannel(profile, 1, 6, input, output, feature), "Keyboard collection cannot receive mouse commands");
            Check(!ReferenceProfiles.AcceptsChannel(profile, profile.Page, (ushort)(profile.Usage + 1), input, output, feature), "Wrong usage rejected");
            Check(!ReferenceProfiles.AcceptsChannel(profile, profile.Page, profile.Usage, 13, 13, 13), "Unrelated native lengths rejected");
        }
        Check(ReferenceProfiles.Find(0x24AE, 0x1423) == null, "VT2 MAX does not enter VT9S route");
        Check(ReferenceProfiles.Find(0x1915, 0xAE8C) == null, "Different Sora generation does not enter AE8A route");
        Check(ReferenceProfiles.Find(0x373B, 0x1155) == null, "Nordic ATK receiver does not enter COMPX ZERO route");

        var compx = ReferenceProfiles.Find(0x3554, 0xF517)!;
        var compxLink = new ScriptedLink();
        compxLink.Queue.Enqueue(CompxReply(99)); // pre-query stale frame
        compxLink.OnWrite = bytes =>
        {
            compxLink.Queue.Enqueue(new byte[] { 8, 4 });
            compxLink.Queue.Enqueue(bytes.ToArray()); // request echo must be skipped
            compxLink.Queue.Enqueue(CompxReply(0, true));
        };
        var battery = ReferenceBatteryProtocol.Read(compx, compxLink);
        Check(battery.Level == 0 && battery.Charging && battery.ChargingKnown, "Fresh 0%, including charging, survives stale/invalid/echo frames");
        Check(compxLink.Writes.Count == 1 && compxLink.FeatureCalls.Count == 0, "COMPX uses one Output request");

        var shortWrite = new ScriptedLink { WriteOk = false };
        Check(ReferenceBatteryProtocol.Read(compx, shortWrite).Level == -1 && shortWrite.ReadTimeouts.Count == 1,
            "Short writes abort before accepting any response");
        var flood = new ScriptedLink();
        for (int i = 0; i < 32; i++) flood.Queue.Enqueue(CompxReply(80));
        Check(ReferenceBatteryProtocol.Read(compx, flood).Level == -1 && flood.Writes.Count == 0,
            "An undrained queue must not get a query or a fabricated reading");
        var echo = new ScriptedLink();
        echo.OnWrite = bytes => echo.Queue.Enqueue(bytes.ToArray());
        Check(ReferenceBatteryProtocol.Read(compx, echo).Level == -1 && echo.Now <= 1301,
            "Echo-only streams time out within the protocol window");

        var scyrox = ReferenceProfiles.Find(0x395A, 6)!;
        var scyroxLink = new ScriptedLink();
        scyroxLink.OnWrite = bytes =>
        {
            scyroxLink.Queue.Enqueue(bytes.ToArray());
            scyroxLink.Queue.Enqueue(ScyroxReply(100));
        };
        var scyroxBattery = ReferenceBatteryProtocol.Read(scyrox, scyroxLink);
        Check(scyroxBattery.Level == 100 && !scyroxBattery.ChargingKnown && !scyroxBattery.Charging,
            "Scyrox preserves unknown charging rather than inferring a flag");

        var sora = ReferenceProfiles.Find(0x1915, 0xAE8A)!;
        var soraLink = new ScriptedLink { FeatureReply = SoraReply(42, true) };
        var soraBattery = ReferenceBatteryProtocol.Read(sora, soraLink);
        Check(soraBattery.Level == 42 && soraBattery.Charging, "Sora Feature roundtrip");
        Check(soraLink.Writes.Count == 0 && soraLink.ReadTimeouts.Count == 0 &&
            soraLink.FeatureCalls.SequenceEqual(new[] { "set:32", "delay:15", "get:5:32" }), "Sora exact Feature ordering and delay");
        var expectedQuery = new byte[32];
        expectedQuery[0] = 5;
        expectedQuery[1] = 0x15;
        expectedQuery[4] = 1;
        expectedQuery[7] = 4;
        Check(SoraV2BatteryProtocol.CreateQuery().SequenceEqual(expectedQuery), "Official battery query including native report ID");
        var zero = new ScriptedLink { FeatureReply = SoraReply(0) };
        Check(ReferenceBatteryProtocol.Read(sora, zero).Level == 0 &&
            zero.FeatureCalls.SequenceEqual(new[] { "set:32", "delay:15", "get:5:32", "delay:15", "set:32", "delay:5", "get:5:32" }),
            "Sora rechecks zero exactly once and preserves confirmed zero");
        var badCharge = SoraReply(25);
        badCharge[10] = 2;
        var wrongCommand = SoraReply(25);
        wrongCommand[1] = 0x14;
        foreach (var invalid in new[] { expectedQuery, SoraReply(255), badCharge, wrongCommand, new byte[8] })
            Check(!SoraV2BatteryProtocol.TryDecode(invalid, out var rejected) && rejected.Level == -1,
                "Malformed or echoed Sora report cannot become a reading");
        var soraSleep = new ScriptedLink { FeatureReply = SoraReply(255, true, false) };
        Check(ReferenceBatteryProtocol.Read(sora, soraSleep).Status == State.Sleep, "Offline Sora does not expose its stale percentage byte");
        var soraFailure = new ScriptedLink { FeatureOk = false };
        Check(ReferenceBatteryProtocol.Read(sora, soraFailure).Level == -1 && soraFailure.FeatureCalls.Count == 1,
            "Failed SetFeature must not read a cached Feature response");

        var rapoo = ReferenceProfiles.Find(0x24AE, 0x1453)!;
        foreach (int length in new[] { 7, 19, 20 })
        {
            var link = new ScriptedLink();
            link.Queue.Enqueue(new byte[] { 7, 1 });
            link.Queue.Enqueue(RapooReply(length, 42, 2));
            var result = ReferenceBatteryProtocol.Read(rapoo, link);
            Check(result.Level == 42 && result.Charging && link.Writes.Count == 0 && link.FeatureCalls.Count == 0,
                $"Rapoo {length} byte route is purely passive");
        }
        var rapooSleep = new ScriptedLink();
        rapooSleep.Queue.Enqueue(RapooReply(20, 255, 0));
        var sleep = ReferenceBatteryProtocol.Read(rapoo, rapooSleep);
        Check(sleep.Status == State.Sleep && sleep.Level == -1 && !sleep.ChargingKnown, "Rapoo receiver present does not imply peripheral awake");

        var f1 = ReferenceProfiles.Find(0x373B, 0x1278)!;
        var f1Link = new ScriptedLink();
        f1Link.OnWrite = bytes => f1Link.Queue.Enqueue(bytes[1] == 4 ? CompxReply(42) : IdentityReply(1, 62));
        var identified = ReferenceBatteryProtocol.Read(f1, f1Link);
        Check(identified.Level == 42 && identified.Model == "ATK F1 V3 Ultimate+" && identified.Identity == "1,62" &&
            f1Link.Writes.Count == 2 && f1Link.Writes[1][1] == 0x10, "Optional ATK live CID/MID identity port");
        var unknownLink = new ScriptedLink();
        unknownLink.OnWrite = bytes => unknownLink.Queue.Enqueue(bytes[1] == 4 ? CompxReply(42) : IdentityReply(255, 255));
        var unknown = ReferenceBatteryProtocol.Read(f1, unknownLink);
        Check(unknown.Level == 42 && unknown.Model == null && unknown.Identity == "255,255", "Unknown live identity never inherits a model name");
        var identityTimeout = new ScriptedLink();
        identityTimeout.OnWrite = bytes => { if (bytes[1] == 4) identityTimeout.Queue.Enqueue(CompxReply(42)); };
        Check(ReferenceBatteryProtocol.Read(f1, identityTimeout).Level == 42, "Optional identity timeout preserves the validated battery");
        Check(!ReferenceBatteryProtocol.TryIdentity(ReferenceBatteryProtocol.CompxCommand(0x10), out _, out _), "Identity request echo rejected");
        var corruptIdentity = IdentityReply(1, 62); corruptIdentity[16]++;
        Check(!ReferenceBatteryProtocol.TryIdentity(corruptIdentity, out _, out _), "Identity checksum validated");
        var oversizedIdentity = IdentityReply(1, 62); oversizedIdentity[5]=255;
        oversizedIdentity[16]=unchecked((byte)(0x55-oversizedIdentity.Take(16).Sum(x=>(int)x)));
        Check(!ReferenceBatteryProtocol.TryIdentity(oversizedIdentity, out _, out _), "Identity payload length must fit the report");

        assertions += DiscoveryChecks(Check);
        GateChecks(Check);
        return assertions;
    }

    private static int DiscoveryChecks(Action<bool, string> check)
    {
        InstanceScope.BeginScan();
        foreach (var profile in ReferenceProfiles.All)
        {
            string path = profile.Model + "-A", otherPath = profile.Model + "-B";
            string id = InstanceScope.Register(path, new[] { ($@"USB\VID_{profile.Vid:X4}&PID_{profile.Pid:X4}\A", "usbccgp") });
            string otherId = InstanceScope.Register(otherPath, new[] { ($@"USB\VID_{profile.Vid:X4}&PID_{profile.Pid:X4}\B", "usbccgp") });
            var buckets = new Dictionary<string, HidDiscovery.Bucket>
            {
                [id] = new() { Vid = profile.Vid, Pid = profile.Pid, InstanceId = id, Product = "Wireless-Receiver", Kind = DeviceKind.Mouse, ReferenceModel = profile.Model },
                [otherId] = new() { Vid = profile.Vid, Pid = profile.Pid, InstanceId = otherId, Product = "Wireless-Receiver", Kind = DeviceKind.Mouse, ReferenceModel = profile.Model }
            };
            var (input, output, feature) = Lengths(profile.Battery);
            var collections = new List<HidDiscovery.VendorCollectionInfo>
            {
                new() { Path=path, Vid=profile.Vid, Pid=profile.Pid, UsagePage=profile.Page, Usage=profile.Usage, Input=input, Output=output, Feature=feature },
                new() { Path=otherPath, Vid=profile.Vid, Pid=profile.Pid, UsagePage=profile.Page, Usage=profile.Usage, Input=input, Output=output, Feature=feature }
            };
            int calls = 0;
            Reading Read(ReferenceProfiles.Profile p, string selected)
            {
                calls++;
                check(p == profile && InstanceScope.Current == (selected == path ? id : otherId), "Discovery scopes the selected protocol and physical instance");
                return new(selected == path ? 0 : 100, false, true, State.Online, "scripted live report");
            }
            HidDiscovery.QueryReferenceCollections(buckets, collections, false, null, Read);
            check(calls == 2 && buckets[id].ToDevice().Level == 0 && buckets[otherId].ToDevice().Level == 100,
                "Two identical receivers retain independent fresh readings");
            check(buckets[id].ToDevice().Transport == "2.4G" && buckets[id].ToDevice().RawName == "Wireless-Receiver" &&
                buckets[id].ToDevice().PeripheralConfirmed, "Discovery retains raw name and receiver/peripheral evidence");
            calls = 0;
            HidDiscovery.QueryReferenceCollections(buckets, collections, true, null, Read);
            check(calls == 0, "Safe mode emits no reference transaction");
            var disabled = new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds = new() { id, otherId } });
            HidDiscovery.QueryReferenceCollections(buckets, collections, false, disabled, Read);
            check(calls == 0, "Disabled instance emits no reference transaction");
            collections[0].Usage = 6; collections[1].Input = 13; collections[1].Feature = 13;
            HidDiscovery.QueryReferenceCollections(buckets, collections, false, null, Read);
            check(calls == 0, "Wrong usages and incompatible reports never reach the native reader");
        }
        var f1 = new HidDiscovery.Bucket { Vid=0x373B, Pid=0x1278, Kind=DeviceKind.Mouse,
            Product="Wireless-Receiver", ReferenceModel="ATK F1 V3 Ultimate+", ReferenceIdentity="255,255" }.ToDevice();
        check(!f1.Name.Contains("F1 V3") && f1.ConfirmedModel == "", "Unknown runtime identity supersedes descriptor model in UI evidence");
        return 0; // The supplied check callback owns the assertion counter.
    }

    private static void GateChecks(Action<bool, string> check)
    {
        using var release = new ManualResetEventSlim();
        using var started = new CountdownEvent(ReferenceQueryGate.MaxPending);
        int completed = 0, unexpected = 0;
        string key = "regression-" + Guid.NewGuid();
        try
        {
            for (int i = 0; i < ReferenceQueryGate.MaxPending; i++)
            {
                var result = ReferenceQueryGate.Run(key + i, () =>
                {
                    started.Signal();
                    release.Wait();
                    Interlocked.Increment(ref completed);
                    return new(99, false, true, State.Online, "late");
                }, 20);
                check(result.Level == -1, "Timed-out workers do not return late percentages");
            }
            check(started.Wait(3000), "Bounded test workers started");
            var duplicate = ReferenceQueryGate.Run(key + 0, () => { unexpected++; return Reading.Failed("unexpected"); }, 20);
            var overflow = ReferenceQueryGate.Run(key + "overflow", () => { unexpected++; return Reading.Failed("unexpected"); }, 20);
            check(duplicate.Level == -1 && overflow.Level == -1 && unexpected == 0, "No duplicate pending request or unbounded worker accumulation");
        }
        finally { release.Set(); }
        check(SpinWait.SpinUntil(() => Volatile.Read(ref completed) == ReferenceQueryGate.MaxPending, 3000), "Workers release owned resources after completion");
        check(SpinWait.SpinUntil(() => ReferenceQueryGate.PendingCount == 0, 3000), "Native worker tasks actually completed before a new poll");
        var fresh = ReferenceQueryGate.Run(key + 0, () => new(42, false, true, State.Online, "fresh"), 1000);
        check(fresh.Level == 42, "A later poll starts a fresh transaction rather than promoting the late 99% result");
    }

    private static void CheckVendorBatteryFrames(Action<bool, string> check)
    {
        // ATK HUB COMPX and Scyrox S-Center report fields, with Windows report IDs.
        check(ReferenceBatteryProtocol.Request(Protocol.Compx).AsSpan().SequenceEqual(
            Convert.FromHexString("0804000000000000000000000000000049")), "COMPX battery request");
        check(ReferenceBatteryProtocol.CompxCommand(0x10).AsSpan().SequenceEqual(
            Convert.FromHexString("081000000000000000000000000000003D")), "COMPX identity request");
        var scyroxQuery = new byte[64];
        scyroxQuery[0] = 8;
        scyroxQuery[1] = 1;
        scyroxQuery[^1] = 9;
        check(ReferenceBatteryProtocol.Request(Protocol.ScyroxV12).AsSpan().SequenceEqual(scyroxQuery),
            "S-Center current configuration request");

        void Expect(Protocol protocol, byte[] report, int level, State status, bool chargingKnown, string label)
        {
            bool accepted = ReferenceBatteryProtocol.TryDecode(protocol, report, out var result);
            check(accepted == (level >= 0), label + " acceptance");
            check(result.Level == level && result.Status == status &&
                result.ChargingKnown == chargingKnown && result.Charging == (status == State.Charging),
                label + " decoded evidence");
        }

        for (int level = 0; level <= byte.MaxValue; level++)
        {
            bool valid = level <= 100;
            for (byte flags = 0; flags < 4; flags++)
            {
                var report = CompxReply((byte)level);
                report[7] = flags;
                report[^1] = unchecked((byte)(report[^1] - flags));
                State status = !valid ? State.Invalid : (flags & 1) == 0 ? State.Online : State.Charging;
                Expect(Protocol.Compx, report, valid ? level : -1, status, valid,
                    $"COMPX level={level}, flags={flags}");
            }
            Expect(Protocol.ScyroxV12, ScyroxReply((byte)level), valid ? level : -1,
                valid ? State.Online : State.Invalid, false, $"Scyrox level={level}");
        }

        foreach (Protocol protocol in new[] { Protocol.Compx, Protocol.ScyroxV12 })
        {
            var good = protocol == Protocol.Compx ? CompxReply(42) : ScyroxReply(42);
            Expect(protocol, ReferenceBatteryProtocol.Request(protocol), -1, State.Invalid, false, $"{protocol} echo");
            for (int size = 0; size < good.Length; size++)
                Expect(protocol, good[..size], -1, State.Invalid, false, $"{protocol} truncated size={size}");
            foreach (int field in new[] { 0, 1 })
            {
                var wrongHeader = good.ToArray();
                wrongHeader[field] = 0;
                if (protocol == Protocol.Compx)
                    wrongHeader[^1] = unchecked((byte)(wrongHeader[^1] + good[field]));
                Expect(protocol, wrongHeader, -1, State.Invalid, false, $"{protocol} wrong header byte {field}");
            }
        }

        var corrupt = CompxReply(42);
        corrupt[^1] ^= 1;
        Expect(Protocol.Compx, corrupt, -1, State.Invalid, false, "COMPX checksum");
        var failed = CompxReply(42);
        failed[2] = 255;
        failed[^1] = unchecked((byte)(failed[^1] + 1));
        Expect(Protocol.Compx, failed, -1, State.Invalid, false, "COMPX failure response");

        for (byte profile = 0; profile < 4; profile++)
        for (byte dpiCount = 1; dpiCount <= 5; dpiCount++)
        {
            var report = ScyroxReply(42);
            report[4] = report[5] = profile;
            report[7] = dpiCount;
            Expect(Protocol.ScyroxV12, report, 42, State.Online, false, "Scyrox active profile");
        }
        foreach (var (field, value) in new (int, byte)[] { (3, 0), (4, 4), (5, 1), (7, 0), (7, 6), (62, 1) })
        {
            var report = ScyroxReply(42);
            report[field] = value;
            Expect(Protocol.ScyroxV12, report, -1, State.Invalid, false, $"Scyrox invalid field {field}={value}");
        }
    }

    private static void CheckRapooBatteryFrames(Action<bool, string> check)
    {
        void Expect(byte[] report, State status, int level)
        {
            bool accepted = RapooVt9sBattery.TryDecode(report, out var result);
            string label = $"VT9S V2 {Convert.ToHexString(report)}";
            check(accepted == (status != State.Invalid), label + " acceptance");
            check(result.Status == status && result.Level == level &&
                result.Charging == (status == State.Charging) &&
                result.ChargingKnown == (status is State.Online or State.Charging),
                label + " decoded evidence");
        }

        foreach (int length in new[] { 7, 19, 20 })
        for (int level = 0; level <= byte.MaxValue; level++)
        foreach (byte flag in new byte[] { 0, 1, 2, 3, 255 })
        {
            State status = flag switch
            {
                0 when length != 7 => State.Sleep,
                1 when level <= 100 => State.Online,
                2 when level <= 100 => State.Charging,
                _ => State.Invalid
            };
            Expect(RapooReply(length, (byte)level, flag), status,
                status is State.Online or State.Charging ? level : -1);
        }

        foreach (int length in new[] { 7, 19, 20 })
        {
            var wrongReportId = RapooReply(length, 42, 1);
            wrongReportId[0] = 0;
            Expect(wrongReportId, State.Invalid, -1);
        }
        var wrongMarker = RapooReply(7, 42, 1);
        wrongMarker[1] = 0xB1;
        Expect(wrongMarker, State.Invalid, -1);
        for (byte transport = 0; transport < 16; transport++)
        {
            var report = RapooReply(20, 42, 1);
            report[1] = (byte)(0xF0 | transport);
            Expect(report, transport <= 2 ? State.Online : State.Invalid, transport <= 2 ? 42 : -1);
        }
        for (int size = 0; size < 22; size++)
        {
            if (size is 7 or 19 or 20) continue;
            var report = new byte[size];
            RapooReply(20, 42, 1).AsSpan(0, Math.Min(20, size)).CopyTo(report);
            Expect(report, State.Invalid, -1);
        }
    }

    private static (ushort Input, ushort Output, ushort Feature) Lengths(Protocol protocol) => protocol switch
    {
        Protocol.Compx => (17,17,0), Protocol.ScyroxV12 => (64,64,0),
        Protocol.SoraV2 => (0,0,32), Protocol.Rapoo => (20,0,0), _ => throw new Exception()
    };
    private static byte[] CompxReply(byte level, bool charging = false)
    {
        var b = new byte[17]; b[0]=8; b[1]=4; b[5]=2; b[6]=level; b[7]=charging ? (byte)1 : (byte)0;
        b[16]=unchecked((byte)(0x55-b.Take(16).Sum(x=>(int)x))); return b;
    }
    private static byte[] IdentityReply(byte cid, byte mid)
    {
        var b = CompxReply(0); b[1]=0x10; b[6]=cid; b[7]=mid;
        b[16]=unchecked((byte)(0x55-b.Take(16).Sum(x=>(int)x))); return b;
    }
    private static byte[] ScyroxReply(byte level)
    {
        var b = new byte[64]; b[0]=8; b[1]=1; b[3]=1; b[7]=1; b[20]=level; return b;
    }
    private static byte[] SoraReply(byte level, bool charging = false, bool online = true)
    {
        var b = new byte[32]; b[0]=5; b[1]=0x15; b[9]=level; b[10]=charging ? (byte)1 : (byte)0; b[12]=online ? (byte)1 : (byte)0; return b;
    }
    private static byte[] RapooReply(int length, byte level, byte status)
    {
        var b = new byte[length];
        if (length==7) { b[0]=0xBB; b[1]=0xB0; b[5]=status; b[6]=level; }
        else { b[0]=7; b[1]=1; b[7]=status; b[8]=level; }
        return b;
    }
    private sealed class ScriptedLink : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        public Queue<byte[]> Queue { get; } = new();
        public List<byte[]> Writes { get; } = new();
        public List<int> ReadTimeouts { get; } = new();
        public List<string> FeatureCalls { get; } = new();
        public Action<byte[]>? OnWrite;
        public bool WriteOk = true, FeatureOk = true;
        public byte[]? FeatureReply;
        public byte[]? Read(int timeoutMs)
        {
            ReadTimeouts.Add(timeoutMs); Now += Math.Max(1, timeoutMs);
            return Queue.Count > 0 ? Queue.Dequeue() : null;
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            Writes.Add(request.ToArray());
            if (WriteOk) OnWrite?.Invoke(request);
            return WriteOk;
        }
        public bool SetFeature(byte[] request) { FeatureCalls.Add("set:"+request.Length); return FeatureOk; }
        public byte[]? GetFeature(byte id, int length) { FeatureCalls.Add($"get:{id}:{length}"); return FeatureReply; }
        public void Delay(int milliseconds) { FeatureCalls.Add("delay:"+milliseconds); Now += milliseconds; }
    }
}

