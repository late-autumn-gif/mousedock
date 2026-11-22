using System.IO;
using MouseDock.Services.Detection;

internal static class Cloud3SRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Cloud III S: " + name);
            count++;
        }
        bool Reject(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }
        HidDiscovery.VendorCollectionInfo Channel(ushort pid, int col, ushort page, ushort usage,
            ushort input, ushort output, ushort feature = 0, int mi = 3) => new()
        {
            Vid = 0x03F0, Pid = pid, Product = "HyperX Cloud III S Wireless",
            Path = $@"\\?\hid#vid_03f0&pid_{pid:x4}&mi_{mi:x2}&col{col:x2}#fixture",
            UsagePage = page, Usage = usage, Input = input, Output = output, Feature = feature
        };
        ExpansionHidMetadata Metadata(byte[] inputs, byte[] outputs, byte[]? features = null) => new()
        {
            Revision = 0x4122,
            InputIds = new(inputs), OutputIds = new(outputs), FeatureIds = new(features ?? []),
            InputSizes = inputs.Length == 1 ? new() { [inputs[0]] = 64 } : new(),
            OutputSizes = outputs.Length == 1 ? new() { [outputs[0]] = 64 } : new(),
            FeatureSizes = new()
        };

        foreach (ushort pid in new ushort[] { 0x02CC, 0x06BE })
        {
            // Descriptor identities from the feedback report; no device response is fabricated here.
            var channels = new[]
            {
                Channel(pid, 4, 0xFFC0, 0x0202, 32, 32),
                Channel(pid, 5, 0x01C0, 1, 64, 64),
                Channel(pid, 6, 0x170F, 0x0202, 8, 8),
                Channel(pid, 1, 0x000C, 1, 2, 0),
                Channel(pid, 2, 0xFF13, 1, 62, 62),
                Channel(pid, 3, 0x000B, 5, 2, 2)
            };
            var selected = channels.Where(c => HeadsetExpansionProfiles.Matches("Cloud3S", c)).ToArray();
            Check(selected.Length == 1 && ReferenceEquals(selected[0], channels[1]),
                $"{pid:X4} selects the battery control collection before opening auxiliary collections");
            var metadata = Metadata([0x0C, 0x0D], [0x0C, 0x0D]);
            Check(!Reject(() => HeadsetExpansionProfiles.Validate("Cloud3S", selected[0], metadata)),
                $"{pid:X4} accepts report0C with report0D despite absent per-report length maps");
            Check(!Reject(() => HeadsetExpansionProfiles.Validate("Cloud3S", selected[0], Metadata([0x0C], [0x0C]))),
                $"{pid:X4} retains the single-report control layout");
            foreach (var malformed in new[]
            {
                Metadata([0x0D], [0x0C]), Metadata([0x0C], [0x0D]),
                Metadata([0x0C, 0x09], [0x0C]), Metadata([0x0C], [0x0C, 0x09]),
                Metadata([0x0C], [0x0C], [0x0C])
            })
                Check(Reject(() => HeadsetExpansionProfiles.Validate("Cloud3S", selected[0], malformed)),
                    $"{pid:X4} rejects missing battery reports or unrelated descriptor reports");
            foreach (var wrong in new[]
            {
                Channel(pid, 5, 0x01C0, 1, 64, 64, mi: 2),
                Channel(pid, 5, 0x01C0, 2, 64, 64),
                Channel(pid, 5, 0x01C0, 1, 63, 64),
                Channel(pid, 5, 0x01C0, 1, 64, 63),
                Channel(pid, 5, 0x01C0, 1, 64, 64, 64),
                Channel(0x1234, 5, 0x01C0, 1, 64, 64)
            })
                Check(!HeadsetExpansionProfiles.Matches("Cloud3S", wrong),
                    $"{pid:X4} keeps model, interface, usage and native buffer boundaries");

            InstanceScope.BeginScan();
            string instance = "";
            foreach (var channel in channels)
                instance = InstanceScope.Register(channel.Path, new[]
                    { ($@"USB\VID_03F0&PID_{pid:X4}\fixture", "usbccgp") });
            using (InstanceScope.Enter(instance))
                Check(VendorProtocol.ReadExtraHeadset(0x03F0, pid, channels[1].Product,
                    new[] { channels[1], channels[1] }, null).Status == ReferenceBatteryProtocol.State.Failed,
                    $"{pid:X4} rejects duplicate control collections before native I/O");
            using (InstanceScope.Enter("instance-other"))
                Check(VendorProtocol.ReadExtraHeadset(0x03F0, pid, channels[1].Product,
                    channels, null).Status == ReferenceBatteryProtocol.State.Failed,
                    $"{pid:X4} never borrows the other physical receiver's control collection");
        }
        InstanceScope.BeginScan();

        int samples = 0;
        var input = new Input(packet =>
        {
            Check(packet.Length == 64 && packet.AsSpan(0, 5).SequenceEqual(new byte[] { 12, 2, 3, 1, 0 }) &&
                packet[5] is 6 or 0x48 && packet.AsSpan(6).IndexOfAnyExcept((byte)0) < 0,
                "only existing read queries are sent on report0C");
            var reply = new byte[64];
            new byte[] { 12, 2, 3, 1, 0, packet[5],
                packet[5] == 6 ? (byte)(40 + ++samples) : (byte)1 }.CopyTo(reply, 0);
            return [new byte[] { 13, 2, 3, 1, 0, 6, 99 }, reply];
        });
        var wire = new ExpansionWire(input, 64, 64, metadata: Metadata([12, 13], [12, 13]));
        Check(HeadsetExpansionCloud3S.Read(wire, [wire]) is { Level: 42, Charging: true } && samples == 2,
            "report0D notifications do not replace the second fresh report0C battery response");
        return count;
    }

    private sealed class Input(Func<byte[], IEnumerable<byte[]>> respond) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> queue = new();
        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += queue.Count == 0 ? Math.Max(1, timeoutMs) : 1;
            return queue.Count > 0 ? queue.Dequeue() : null;
        }
        public bool Write(byte[] packet, int timeoutMs)
        {
            foreach (var frame in respond(packet)) queue.Enqueue(frame);
            return true;
        }
        public bool SetFeature(byte[] packet) => throw new InvalidOperationException("Unexpected Feature write");
        public byte[]? GetFeature(byte id, int size) => throw new InvalidOperationException("Unexpected Feature read");
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
