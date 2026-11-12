using System.Buffers.Binary;
using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class ScyroxV12DpiRegression
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Scyrox V12M DPI: " + description);
            assertions++;
        }

        var link = new Link();
        var state = ScyroxV12Dpi.ReadOrChange(link, null);
        Check(state is { Profile: 1, Index: 1, Count: 3, Writable: true } && state.Values[state.Index] == 800,
            "the terminator selects the live profile instead of the last streamed profile");
        var changed = ScyroxV12Dpi.ReadOrChange(link, new(state!, 2));
        Check(changed is { Index: 2 } && changed.Values[2] == 1600 && link.Selections == 1, "set and read back");
        var selection = link.LastSelection!;
        Check(selection.AsSpan(0, 12).SequenceEqual(new byte[] { 8, 10, 0, 8, 2, 3, 0x90, 1, 0x20, 3, 0x40, 6 }) &&
            selection[63] == 0x19, "official length, preserved DPI values and additive output checksum");
        Check(ScyroxV12Dpi.ReadOrChange(link, new(changed!, 2)) != null && link.Selections == 1, "no duplicate write");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { Pid = 3 }, null) == null, "different paired mouse");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { Connected = false }, null) == null, "sleeping or disconnected mouse");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { ShortStatus = true }, null) == null, "truncated status");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { WrongReport = true }, null) == null, "wrong report ID");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { End = false }, null) == null, "stream times out without its terminator");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { BadDpi = true }, null) == null, "out-of-range DPI");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { Duplicate = true }, null) == null, "ambiguous profile stream");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { Error = true }, null) == null, "transport error flag");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { IdentitySwapAt = 2 }, null) == null, "pair changed during read");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { IdentitySwapAt = 3 }, new(state!, 2)) == null,
            "pair changed after writing");
        var replaced = new Link { IdentitySwapAt = 1 };
        Check(ScyroxV12Dpi.ReadOrChange(replaced, new(state!, 2)) == null && replaced.Selections == 0,
            "a replacement mouse with the same model and DPI table cannot use the old menu");
        var split = new Link { Split = true };
        var splitState = ScyroxV12Dpi.ReadOrChange(split, null);
        Check(splitState is { SeparateAxes: true, Writable: false } && splitState.Values[1] == 900, "independent X/Y reads X only");
        Check(ScyroxV12Dpi.ReadOrChange(split, new(state!, 2)) == null && split.Selections == 0, "no writes in split mode");
        var changedTable = new Link { ChangedTable = true };
        Check(ScyroxV12Dpi.ReadOrChange(changedTable, new(state!, 2)) == null && changedTable.Selections == 0,
            "stale DPI menu cannot write changed profile values");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { IgnoreWrite = true }, new(state!, 2)) == null, "acknowledged but unapplied");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { NoAck = true }, new(state!, 2)) == null, "missing acknowledgement");
        Check(ScyroxV12Dpi.ReadOrChange(new Link { EmptyIdentity = true }, null) is { Writable: false }, "unknown physical identity is read-only");

        InstanceScope.BeginScan();
        var parents = new[] { (@"USB\VID_395A&PID_0006\A", "usbccgp") };
        string id = InstanceScope.Register("scyrox-a", parents);
        InstanceScope.Register("scyrox-b", parents);
        var bucket = new HidDiscovery.Bucket { Vid = 0x395A, Pid = 6, InstanceId = id,
            ReferenceModel = "Scyrox V12M", Kind = DeviceKind.Mouse, Product = "Wireless-Receiver" };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [id] = bucket };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        { Path = path, Vid = 0x395A, Pid = 6, UsagePage = 0xFF00, Usage = 2, Input = 64, Output = 64 };
        ReferenceBatteryProtocol.Reading Battery(ReferenceProfiles.Profile profile, string path) =>
            new(70, false, false, ReferenceBatteryProtocol.State.Online, "scripted battery");
        ReferenceBatteryProtocol.Reading Dpi(ReferenceProfiles.Profile profile, string path) =>
            Battery(profile, path) with { Dpi = state };
        HidDiscovery.QueryReferenceCollections(buckets, [Channel("scyrox-a"), Channel("scyrox-b")], false, null, Battery, Dpi);
        Check(bucket.DpiSettings == null && bucket.Battery == 70, "ambiguous vendor collections keep the battery path only");
        HidDiscovery.QueryReferenceCollections(buckets, [Channel("scyrox-a")], true, null, Battery, Dpi);
        Check(bucket.DpiSettings == null, "safe mode never reads DPI");
        HidDiscovery.QueryReferenceCollections(buckets, [Channel("scyrox-a")], false, null, Battery, Dpi);
        var ui = new AutoDetectSource().UpdateFromDetection([bucket.ToDevice()]).Single();
        Check(ui.DpiLabel == "800 DPI" && ui.CanChangeDpi, "scoped profile reaches the main-panel model");
        return assertions;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> frames = new();
        private byte index = 1;
        private int statusQueries;
        internal int Selections;
        internal byte[]? LastSelection;
        internal ushort Pid = 5;
        internal int IdentitySwapAt = int.MaxValue;
        internal bool End = true, Connected = true, ShortStatus, WrongReport, Error, BadDpi, Duplicate, Split,
            ChangedTable, IgnoreWrite, NoAck, EmptyIdentity;

        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += frames.Count == 0 ? timeoutMs : 1;
            return frames.Count > 0 ? frames.Dequeue() : null;
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            switch (request[1])
            {
                case 0x2E:
                    var status = Frame(0x2E, 25);
                    status[6] = Connected ? (byte)1 : (byte)0;
                    BinaryPrimitives.WriteUInt16LittleEndian(status.AsSpan(12), 0x395A);
                    BinaryPrimitives.WriteUInt16LittleEndian(status.AsSpan(14), Pid);
                    if (!EmptyIdentity) status[16] = ++statusQueries >= IdentitySwapAt ? (byte)'B' : (byte)'A';
                    if (WrongReport) status[0] = 7;
                    frames.Enqueue(ShortStatus ? status[..22] : status);
                    break;
                case 1:
                    QueueProfile(0, 0);
                    QueueProfile(1, index);
                    QueueProfile(2, 0);
                    if (Duplicate) QueueProfile(1, 0);
                    if (End) frames.Enqueue(Frame(1, 0));
                    break;
                case 0x0A:
                    Selections++;
                    LastSelection = request;
                    if (!IgnoreWrite) index = request[4];
                    if (!NoAck) frames.Enqueue(Frame(0x0A, 0));
                    break;
            }
            return true;
        }

        private void QueueProfile(byte profile, byte stage)
        {
            var config = Frame(1, 38);
            config[4] = 1;
            config[5] = profile;
            config[6] = stage;
            config[7] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(config.AsSpan(8), (ushort)(BadDpi ? 99 : ChangedTable ? 500 : 400));
            BinaryPrimitives.WriteUInt16LittleEndian(config.AsSpan(10), (ushort)(profile == 1 ? 800 : 500));
            BinaryPrimitives.WriteUInt16LittleEndian(config.AsSpan(12), 1600);
            if (Error) config[62] = 1;
            frames.Enqueue(config);
            var flags = Frame(0x25, 3);
            if (Split)
            {
                flags[5] = 1;
                var axes = Frame(0x23, 5);
                BinaryPrimitives.WriteUInt16LittleEndian(axes.AsSpan(9), 900);
                BinaryPrimitives.WriteUInt16LittleEndian(axes.AsSpan(11), 1200);
                frames.Enqueue(axes);
            }
            frames.Enqueue(flags);
        }

        private static byte[] Frame(byte command, byte length)
        {
            var frame = new byte[64];
            frame[0] = 8;
            frame[1] = command;
            frame[3] = length;
            return frame;
        }

        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
