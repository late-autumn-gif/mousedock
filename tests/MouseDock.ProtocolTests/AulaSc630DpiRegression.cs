using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class AulaSc630DpiRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("SC630 Pro DPI: " + message);
            checks++;
        }

        var link = new Link();
        var state = link.Run();
        Check(state is { Index: 1, Count: 3, Writable: true } && state.HardwareStages.SequenceEqual(new[] { 1, 3, 8 }) &&
            state.Values.All(value => value == -1), "sparse mask retains hardware numbering and unknown DPI");
        Check(new DeviceInfo { Dpi = -1, DpiSettings = state }.DpiLabel == "DPI — · 档位 3" && state!.OptionLabel(2) == "档位 8",
            "panel and menu display the confirmed hardware stage");
        Check(link.Run(new(state!, 2)) is { Index: 2 } && link.Writes == 1, "write and read back selected hardware stage");
        var write = link.LastWrite!;
        Check(write[8] == 20 && write[9] == 0x85 && write[10] == 8 && write.AsSpan(11, 18).SequenceEqual(Link.Original.AsSpan(2)) &&
            !AulaSc630Dpi.IsRequest(write, false), "only the stage byte changes and background route cannot write");
        Check(new Link { Short = true }.Run() == null, "truncated response");
        Check(new Link { WrongSequence = true }.Run() == null, "wrong sequence");
        Check(new Link { NoSequence = true }.Run() is { Index: 1 }, "stable sequence-zero firmware reply");
        Check(new Link { Stage = 2 }.Run() == null, "disabled current stage");
        Check(new Link { ChangeTable = true }.Run(new(state!, 2)) == null, "changed table after write");
        Check(new Link { IgnoreWrite = true }.Run(new(state!, 2)) == null, "feature success without applied stage");
        var offline = new Link { OfflineAt = 1 };
        Check(offline.Run(new(state!, 2)) == null && offline.Writes == 0, "offline before change");
        Check(new Link { OfflineAt = 2 }.Run(new(state!, 2)) == null, "offline during readback");
        Check(new Link { Timeout = true }.Run() == null, "no reply");
        var stale = new Link();
        Check(stale.Run(new(state! with { Identity = "old" }, 2)) == null && stale.Writes == 0, "stale menu");
        return checks;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        internal static readonly byte[] Original = [0x85, 3, 16, 0, 24, 0, 32, 0, 64, 0, 96, 0, 192, 0, 64, 0, 128, 0, 0x81, 0x37];
        internal byte Stage = 3;
        internal int Writes, OfflineAt = int.MaxValue;
        internal bool Short, WrongSequence, NoSequence, ChangeTable, IgnoreWrite, Timeout;
        internal byte[]? LastWrite;
        private int presence, seq;
        private byte[]? pending;

        internal MouseDpiState? Run(MouseDpiChange? change = null) => AulaSc630Dpi.ReadOrChange(this, () => (byte)++seq,
            change, () => ++presence < OfflineAt);

        public long Now { get; private set; }
        public bool SetFeature(byte[] request)
        {
            if (!AulaSc630Dpi.IsRequest(request, true)) throw new InvalidOperationException("Invalid SC630 frame");
            if (request[3] == 6)
            {
                Writes++;
                LastWrite = request;
                if (!IgnoreWrite) Stage = request[10];
            }
            else pending = request;
            return true;
        }
        public byte[]? GetFeature(byte reportId, int length)
        {
            if (Timeout || pending == null) return null;
            byte[] frame = (byte[])pending.Clone();
            Original.CopyTo(frame, 9);
            frame[10] = Stage;
            if (ChangeTable && Writes != 0) frame[11]++;
            if (WrongSequence) frame[4]++;
            if (NoSequence) frame[4] = 0;
            return Short ? frame[..40] : frame;
        }
        public void Delay(int milliseconds) => Now += milliseconds;
        public byte[]? Read(int timeoutMs) => throw new NotSupportedException();
        public bool Write(byte[] request, int timeoutMs) => throw new NotSupportedException();
    }
}
