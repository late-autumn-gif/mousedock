using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class M137TransactionsRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message)
        {
            count++;
            if (!ok) throw new Exception("SC580 transactions: " + message);
        }
        var link = new Link(Frame("M137", 82), Frame("M137", 82));
        var reading = link.Read();
        Check(reading.Level == 82 && reading.Identity == "M137" && reading.Model == "狼蛛 SC580",
            "Two valid same-model transactions publish the device percentage");
        Check(!reading.ChargingKnown && !reading.Charging,
            "No unverified byte becomes a charging flag");
        Check(link.Sets == 2 && link.Gets == 2,
            "Two independent transactions are still required");
        Check(link.Requests.All(p => p.Length == 33 &&
            p.AsSpan(0, 4).SequenceEqual(new byte[] { 12, 1, 32, 0 }) &&
            p.AsSpan(5).IndexOfAnyExcept((byte)0) < 0),
            "Only existing read-only GetInfo requests are sent");
        Check(link.Requests.Select(p => p[4]).SequenceEqual(new byte[] { 0, 1, 2, 3 }),
            "Prepared SET and GET buffers retain the official sequence order");

        foreach (var levels in new (int First, int Second)[] { (79, 80), (80, 79), (0, 1), (99, 100), (0, 0) })
        {
            var changing = new Link(Frame("M137", levels.First), Frame("M137", levels.Second));
            var updated = changing.Read();
            Check(updated.Level == levels.Second && updated.Identity == "M137" &&
                updated.Status == ReferenceBatteryProtocol.State.Online && changing.Sets == 2,
                $"A valid {levels.First} to {levels.Second} update must publish the second percentage");
        }

        var transientZero = new Link(Frame("M137", 72), Frame("M137", 0), Frame("M137", 71));
        Check(transientZero.Read() is { Level: 71, Identity: "M137" } && transientZero.Sets == 3,
            "A lone SC580 zero must be rechecked using a fresh GetInfo transaction");
        var confirmedZero = new Link(Frame("M137", 1), Frame("M137", 0), Frame("M137", 0));
        Check(confirmedZero.Read() is { Level: 0, Identity: "M137" } && confirmedZero.Sets == 3,
            "Two consecutive zero readings must still be accepted");
        Check(new Link(Frame("M137", 72), Frame("M137", 0)).Read().Level == -1,
            "A timeout while confirming zero must not publish a live percentage");
        Check(new Link(Frame("M137", 72), Frame("M137", 0), Frame("M139", 0)).Read().Level == -1,
            "A different model cannot confirm the SC580 zero");
        var otherModel = new Link(Frame("M139", 72), Frame("M139", 0));
        Check(otherModel.Read() is { Level: 0, Identity: "M139" } && otherModel.Sets == 2,
            "The SC580 zero recheck does not apply to another receiver model");

        Check(new Link(Frame("M137", 50), Frame("M139", 50)).Read().Level == -1,
            "A changed model is rejected even when its percentage is unchanged");
        Check(new Link(Frame("M137", 50)).Read().Level == -1,
            "A missing second transaction cannot publish the first percentage");
        Check(new Link(Frame("M137", 50), Frame("M137", 101), Frame("M137", 255)).Read().Level == -1,
            "Invalid second-transaction percentages never publish");
        Check(new Link(Frame("M999", 50), Frame("M999", 50)).Read().Level == -1,
            "An unrecognized identity stays unknown");
        Check(new Link(new byte[33], Frame("M137", 50), Frame("M137", 50)).Read().Level == 50,
            "A rejected GET can retry without losing the independent second transaction");
        var failed = new Link(Frame("M137", 50), Frame("M137", 50)) { WriteOk = false };
        Check(failed.Read().Level == -1 && failed.Gets == 0, "SET failure stops before GET");
        CheckFeedbackReport(Check);
        return count;
    }

    private static void CheckFeedbackReport(Action<bool, string> check)
    {
        const string path = @"\\?\hid#vid_248a&pid_5b2f&mi_01&col04#PRIVATE-SC580-SERIAL";
        const string product = "Wireless-Receiver";
        InstanceScope.BeginScan();
        string instance = InstanceScope.Register(path, new[] { (@"USB\VID_248A&PID_5B2F\PRIVATE-SC580-SERIAL", "USB") });
        var channel = new HidDiscovery.VendorCollectionInfo
        {
            Path = path, Vid = 0x248A, Pid = 0x5B2F, Product = product,
            UsagePage = 0xFF00, Usage = 1, Input = 0, Output = 0, Feature = 33
        };
        var bucket = new HidDiscovery.Bucket { InstanceId = instance, Vid = 0x248A, Pid = 0x5B2F, Product = product };
        var aliases = new Dictionary<string, string>();
        var previousScan = BatteryReplay.Current;
        string file = Path.Combine(Path.GetTempPath(), $"mousedock-sc580-{Guid.NewGuid():N}.zip");
        try
        {
            BatteryReplay.Capture Capture(Link link, bool safeMode = false)
            {
                var scan = new BatteryReplay.Scan(aliases);
                scan.AddCollection(instance, path, 0x248A, 0x5B2F, product, 0xFF00, 1, 0, 0, 33, false,
                    reportIds: "IN[] OUT[] FEATURE[0C]");
                BatteryReplay.Current = scan;
                HidDiscovery.QueryM137Feature(new() { [instance] = bucket }, new() { channel }, safeMode, null,
                    (_, trace) => link.Read(trace));
                return scan.Finish(new[] { instance });
            }

            var first = Frame("M137", 72);
            "PRIVATE-ID"u8.CopyTo(first.AsSpan(20));
            var recovered = Capture(new Link(first, Frame("M137", 0), Frame("M137", 71)));
            check(recovered.Queries.Length == 1, "The SC580 query must be included in the selected-device report");
            var recovery = recovered.Queries.Single();
            check(recovery.Interface == recovered.Collections.Single().Interface && recovery.ObservedPercent == 71 &&
                recovery.Samples.Select(s => s.Stage).SequenceEqual(new[]
                    { "set-feature", "get-feature", "set-feature", "get-feature", "set-feature", "get-feature" }),
                "The report binds all three transactions to the actual control collection");
            var reads = recovery.Samples.Where(s => s.Stage == "get-feature").ToArray();
            check(reads.Select(s => Convert.FromHexString(s.Hex!)[18]).SequenceEqual(new byte[] { 72, 0, 71 }) &&
                Convert.FromHexString(reads[0].Hex!).AsSpan(19).IndexOfAnyExcept((byte)0) < 0 &&
                reads.All(s => s.At.HasValue), "Replies preserve all battery samples and omit private bytes");
            var unknown = Capture(new Link(first, Frame("M137", 0)));
            var failure = unknown.Queries.Single();
            check(failure.ObservedPercent == null &&
                failure.Samples.Last() is { Stage: "get-feature", NativeError: 31, Hex: null },
                "A failed recheck retains its stage and native error without reporting zero");
            var skip = new Link();
            check(Capture(skip, safeMode: true).Queries.Single() is { Status: "safe-mode", Samples.Length: 0 } &&
                skip.Sets == 0 && skip.Gets == 0, "Safe mode records the skip without I/O");

            var device = new DeviceInfo { Id = instance, Name = "SC580" };
            check(FeedbackService.TryExportReport(new AppSettings(), new[] { device }, file, device,
                new[] { recovered, unknown }).Succeeded, "SC580 ZIP export succeeds");
            using var archive = ZipFile.OpenRead(file);
            using var text = new StreamReader(archive.GetEntry(BatteryReplay.EntryName)!.Open());
            string evidence = text.ReadToEnd();
            check(!evidence.Contains("PRIVATE-SC580-SERIAL") && !evidence.Contains(instance),
                "The ZIP omits native paths and physical instance IDs");
            using var replay = JsonDocument.Parse(BatteryReplay.ReplayFile(file));
            var rounds = replay.RootElement.GetProperty("Rounds");
            check(rounds[0].GetProperty("Results")[0].GetProperty("Percent").GetInt32() == 71 &&
                rounds[0].GetProperty("Results")[0].GetProperty("Charging").ValueKind == JsonValueKind.Null &&
                rounds[1].GetProperty("Results")[0].GetProperty("Percent").ValueKind == JsonValueKind.Null,
                "Offline ZIP replay distinguishes recovery from failure without guessing charging");
            check(BatteryReplay.Replay(recovery with { Samples = recovery.Samples[..4], ObservedPercent = 0 },
                recovered.Collections).Percent == null, "Offline replay needs a complete zero recheck");
            check(BatteryReplay.Replay(recovery, new[]
                { recovered.Collections[0] with { Receiver = "receiver-other" } }).Percent == null,
                "Offline replay cannot borrow a collection from another receiver");
            check(BatteryReplay.Replay(recovery with { Samples = recovery.Samples.Select((sample, index) =>
                index == 3 ? sample with { Sequence = 1 } : sample).ToArray() }, recovered.Collections).Percent == null,
                "Offline replay rejects a reply with the wrong request sequence");
            var retry = Capture(new Link(new byte[33], first, Frame("M137", 71)));
            check(retry.Queries[0].Samples[1] is { Result: "invalid-frame", Hex: null } &&
                BatteryReplay.Replay(retry.Queries[0], retry.Collections).Percent == 71,
                "An invalid reply keeps its retry position without exporting unrecognized bytes");
        }
        finally
        {
            File.Delete(file);
            BatteryReplay.Current = previousScan;
            InstanceScope.BeginScan();
        }
    }

    private static byte[] Frame(string model, int percent)
    {
        var bytes = new byte[33];
        new byte[] { 12, 1, 32, 0 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(model).CopyTo(bytes, 8);
        bytes[18] = (byte)percent;
        return bytes;
    }

    private sealed class Link(params byte[][] frames)
    {
        private readonly Queue<byte[]> replies = new(frames);
        internal readonly List<byte[]> Requests = new();
        internal int Sets, Gets;
        internal bool WriteOk = true;
        internal ReferenceBatteryProtocol.Reading Read(BatteryReplay.Trace? trace = null) => M137FeatureProtocol.Read(
            packet =>
            {
                Requests.Add(packet.ToArray()); Sets++;
                return WriteOk;
            },
            packet =>
            {
                Requests.Add(packet.ToArray()); Gets++;
                if (replies.Count == 0) return false;
                replies.Dequeue().CopyTo(packet, 0);
                return true;
            },
            milliseconds =>
            {
                if (milliseconds != 16) throw new Exception("Unexpected M137 delay");
            }, () => 31, trace);
    }
}
