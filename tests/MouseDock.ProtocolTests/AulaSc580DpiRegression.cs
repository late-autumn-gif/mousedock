using System.Text;
using MouseDock.Services.Detection;

internal static class AulaSc580DpiRegression
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("SC580 DPI: " + reason);
            assertions++;
        }

        byte[] reply = [0, 0x13, 0, 1, 25, 0x06, 0x10, 0x20, 0x30, 0x40, 0x80, 0xDC, ..new byte[21]];
        var link = new Link(reply);
        var reading = new ReferenceBatteryProtocol.Reading(65, false, true, ReferenceBatteryProtocol.State.Online,
            "fixture", "狼蛛 SC580", "M137");
        var dpi = AulaSc580Dpi.Read(link, reading);
        Check(dpi is { Index: 0, Count: 6, Writable: false } && dpi.Values.SequenceEqual(new[] { 800, 1600, 2400, 3200, 6400, 12000 }),
            "historical capture prefix and official two-step sensor table");
        Check(link.Writes.SequenceEqual(new byte[] { 0x13, 0x30 }), "only the captured read and paired-model confirmation");
        var last = (byte[])reply.Clone();
        last[5] = 0x56;
        var active = AulaSc580Dpi.Read(new Link(last), reading);
        Check(active is { Index: 5 } && active.Values[active.Index] == 12000, "active index is the high nibble");
        last[5] = 0x66;
        Check(AulaSc580Dpi.Read(new Link(last), reading) == null, "invalid current slot");
        Check(AulaSc580Dpi.Read(new Link(reply[..32]), reading) == null, "truncated report");
        Check(AulaSc580Dpi.Read(new Link(AulaSc580Dpi.Request()), reading) == null, "request echo rejected");
        Check(AulaSc580Dpi.Read(new Link(reply) { Identity = "M230" }, reading) == null, "receiver changes paired model");
        Check(AulaSc580Dpi.Read(new Link(reply) { Online = false }, reading) == null, "offline after read");
        var wrong = new Link(reply);
        Check(AulaSc580Dpi.Read(wrong, reading with { Identity = "M230" }) == null && wrong.Writes.Count == 0,
            "other model on shared receiver receives no DPI query");
        reply[11] = 221;
        Check(AulaSc580Dpi.Read(new Link(reply), reading) == null, "codes above the shared sensor range are unknown");
        return assertions;
    }

    private sealed class Link(byte[] dpi) : ReferenceBatteryProtocol.ILink
    {
        internal readonly List<byte> Writes = new();
        internal string Identity = "M137";
        internal bool Online = true;
        private byte[]? next;
        public long Now { get; private set; }

        public byte[]? Read(int timeoutMs)
        {
            Now += next == null ? timeoutMs : 1;
            var reply = next;
            next = null;
            return reply;
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            Writes.Add(request[1]);
            if (request[1] == 0x13) next = dpi;
            else
            {
                next = new byte[33];
                next[1] = 0x30;
                next[3] = 1;
                next[4] = 11;
                Encoding.ASCII.GetBytes(Identity).CopyTo(next, 5);
                next[14] = 65;
                next[15] = Online ? (byte)1 : (byte)0;
            }
            return true;
        }

        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
