using System.IO;
using System.Text;
using MouseDock.Services.Detection;

internal static class ReceiverIdentityRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string reason) { count++; if (!ok) throw new Exception(reason); }
        const string instance = "instance-0123456789ABCDEF01234567";
        HidProbe.Node Node(string key = instance, ushort page = 0xFF00, ushort feature = 33) => new()
        {
            InstanceId = key, Path = "selected-feature", Vid = 0x248A, Pid = 0x5B2F,
            Caps = new() { UsagePage = page, Usage = 1, FeatureReportByteLength = feature }
        };
        DeviceProbeResult Capture(Link link, bool safe = false, string key = instance,
            params HidProbe.Node[] nodes) =>
            ReceiverIdentityProtocol.Capture(key, safe, nodes.Length == 0 ? new[] { Node() } : nodes, _ => link);

        var good = new Link(Reply("M620"));
        var report = Capture(good);
        Check(report.Responses == 1 && report.Text.Contains("M620") &&
            report.Text.Contains("AJ159 / AJ159 MC"), "Identity must retain the official M620 model ambiguity.");
        Check(good.Sent.Count == 2 && good.Sent[0].SequenceEqual(Request(0)) &&
            good.Sent[1].SequenceEqual(Request(1)), "SET and GET both need the official header and distinct counters.");
        Check(good.Delays.SequenceEqual(new[] { 16 }) && good.Disposed, "Delay before GET and release the selected handle.");
        Check(report.Jsonl.Contains("0C-01-20-00") && report.Jsonl.Contains("4D-36-32-30"),
            "Keep the actual raw request and response in the exported trace.");
        var snapshots = new List<DeviceProbeResult>();
        var snapshotLink = new Link(Reply("M620"));
        ReceiverIdentityProtocol.Capture(instance, false, new[] { Node() }, _ => snapshotLink, snapshots.Add);
        var sentSnapshot = snapshots.FirstOrDefault(s => s.Exchanges == 1);
        Check(sentSnapshot != null && sentSnapshot.Jsonl.Contains("SET_FEATURE") &&
            sentSnapshot.Responses == 0 && !sentSnapshot.Jsonl.Contains("\"Dir\":\"RX\""),
            "Publish immutable raw progress before native SET can block; later responses must not mutate it.");

        foreach (var pair in new[] { ("M137", "狼蛛 SC580"), ("M139", "AJ139 Pro"), ("M184", "AJ179"), ("M999", "M999") })
            Check(Capture(new Link(Reply(pair.Item1))).Text.Contains(pair.Item2), "Preserve known and unknown identity strings.");
        var malformed = new List<byte[]> { new byte[33], Request(1), Reply("M620")[..12], new byte[34] };
        foreach (int offset in new[] { 0, 1, 2, 3, 8, 9 })
        {
            var frame = Reply("M620"); frame[offset] = 0xFF; malformed.Add(frame);
        }
        foreach (var frame in malformed)
        {
            var link = new Link(frame, frame);
            var rejected = Capture(link);
            Check(!rejected.Text.Contains("候选型号:") && rejected.Responses == 2 && link.Sent.Count == 3,
                "Invalid headers, identity bytes, truncation and echoes must stay unknown after two GET attempts.");
        }
        var retry = new Link(Request(1), Reply("M184"));
        Check(Capture(retry).Text.Contains("AJ179") && retry.Sent[2].SequenceEqual(Request(2)),
            "Retry an empty response once with the next GET sequence.");
        var failed = new Link(Reply("M139")) { SetOk = false };
        Check(Capture(failed).Responses == 0 && failed.Sent.Count == 1, "A failed SET must not proceed to GET.");
        var throws = new Link(Reply("M139")) { ThrowOnGet = true };
        Check(Capture(throws).Text.Contains("test transport failure") && throws.Disposed,
            "A native failure must preserve diagnostic output and release the handle.");
        foreach (byte? id in new byte?[] { null, 0, 6, 13 })
        {
            var wrong = new Link(Reply("M620")) { FeatureReportId = id };
            Check(Capture(wrong).Exchanges == 0 && wrong.Sent.Count == 0 && wrong.Disposed,
                "Never send report12 without matching descriptor evidence.");
        }
        foreach (var nodes in new[] {
            new[] { Node("instance-OTHER") }, new[] { Node(page: 0xFFEF) },
            new[] { Node(feature: 32) }, new[] { Node(), Node() } })
        {
            var untouched = new Link(Reply("M620"));
            Check(Capture(untouched, nodes: nodes).Exchanges == 0 && !untouched.Disposed,
                "Wrong instance, wrong channel and ambiguous channels must not be opened.");
        }
        var another = Node("instance-OTHER"); another.Path = "other-feature";
        string opened = "";
        Check(ReceiverIdentityProtocol.Capture(instance, false, new[] { another, Node() }, node =>
        { opened = node.Path; return new Link(Reply("M139")); }).Responses == 1 && opened == "selected-feature",
            "Two identical VID/PIDs must not cause queries to the unselected receiver.");
        foreach (var setting in new[] { (true, instance), (false, ""), (false, "248A:5B2F") })
        {
            var untouched = new Link(Reply("M620"));
            Check(Capture(untouched, setting.Item1, setting.Item2).Exchanges == 0 && untouched.Sent.Count == 0,
                "Safe mode or missing physical identity must prevent all traffic.");
        }
        Check(!RecognitionPolicy.CanQuery(0x248A, 0x5B2F, "OTHER RECEIVER"),
            "M137 support must not enable unrelated products sharing the receiver ID.");
        Check(VendorProtocol.ProbeDevice(0x248A, 0x5B2F, "Wireless-Receiver", false).Exchanges == 0,
            "The unscoped generic probe must not query this shared VID/PID.");
        return count;
    }

    // Synthetic vectors from AJAZZ Driver (X) 1.0.5.3, not a device capture.
    private static byte[] Request(byte sequence)
    {
        var bytes = new byte[33];
        new byte[] { 12, 1, 32, 0, sequence }.CopyTo(bytes, 0);
        return bytes;
    }
    private static byte[] Reply(string identity)
    {
        var bytes = Request(0);
        Encoding.ASCII.GetBytes(identity).CopyTo(bytes, 8);
        bytes[18] = 90; bytes[19] = 1;
        return bytes;
    }
    private sealed class Link(params byte[][] responses) : ReceiverIdentityProtocol.ILink
    {
        public byte? FeatureReportId { get; set; } = 12;
        public readonly List<byte[]> Sent = new();
        public readonly List<int> Delays = new();
        public bool SetOk = true, ThrowOnGet, Disposed;
        private readonly Queue<byte[]> replies = new(responses);
        public bool SetFeature(byte[] frame) { Sent.Add(frame.ToArray()); return SetOk; }
        public byte[] GetFeature(byte[] frame)
        {
            Sent.Add(frame.ToArray());
            if (ThrowOnGet) throw new IOException("test transport failure");
            return replies.Count > 0 ? replies.Dequeue() : Array.Empty<byte>();
        }
        public void Delay(int milliseconds) => Delays.Add(milliseconds);
        public void Dispose() => Disposed = true;
    }
}
