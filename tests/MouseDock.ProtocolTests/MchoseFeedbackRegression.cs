using System.IO;
using MouseDock.Services.Detection;

internal static class MchoseFeedbackRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("MCHOSE feedback: " + name); count++; }
        bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is IOException or InvalidDataException) { return true; } }
        byte[] Keyboard(int pct, byte flags = 0)
        {
            var b = new byte[20]; b[0] = 19; b[1] = 74; b[2] = 1; b[4] = 2; b[5] = (byte)pct; b[6] = flags;
            b[19] = unchecked((byte)b.Take(19).Sum(v => (int)v)); return b;
        }
        byte[] Headset(int pct, byte status = 2)
        { var b = new byte[64]; b[0] = 0x55; b[1] = 0x65; b[2] = (byte)pct; b[3] = status; return b; }
        for (int pct = 0; pct <= 100; pct++) for (int full = 0; full <= 1; full++) for (int charge = 0; charge <= 1; charge++)
        {
            var result = KeyboardExpansionMchoseBy.Decode(Keyboard(pct, (byte)(full | charge << 4)));
            Check(result.Level == pct && result.ChargingKnown && result.Charging == (charge == 1), "little-endian nibbles across full battery range");
        }
        var reported = Keyboard(100, 1);
        Check(Convert.ToHexString(reported) == "134A0100026401000000000000000000000000C5" &&
            KeyboardExpansionMchoseBy.Decode(reported) is { Level:100, Charging:false, ChargingKnown:true }, "reported full flag is not charging");
        for (int i = 0; i < 20; i++)
        {
            var corrupt = (byte[])reported.Clone(); corrupt[i] ^= 1;
            Check(Reject(() => KeyboardExpansionMchoseBy.Decode(corrupt)), "every corrupted byte fails checksum or envelope");
        }
        var length = Keyboard(60); length[4] = 3; length[19]++;
        Check(Reject(() => KeyboardExpansionMchoseBy.Decode(length)), "wrong payload length even with valid checksum");
        var secret = Keyboard(60); secret[10] = 1; secret[19]++;
        Check(Reject(() => KeyboardExpansionMchoseBy.Decode(secret)), "nonzero reserved payload rejected");

        var request = HeadsetExpansionBolutek.Request();
        Check(HeadsetExpansionBolutek.IsV9BatteryQuery(request), "official BLK read-only query");
        for (int i = 0; i < 64; i++)
        {
            var changed = (byte[])request.Clone(); changed[i] ^= 1;
            Check(!HeadsetExpansionBolutek.IsV9BatteryQuery(changed), "command allowlist covers every byte");
        }
        Check(!HeadsetExpansionBolutek.IsV9BatteryQuery(null!) && !HeadsetExpansionBolutek.IsV9BatteryQuery(new byte[65]), "exact query framing");
        var trace = new BatteryReplay.Trace();
        var link = new Link(n => [Headset(40 + n)]);
        Check(HeadsetExpansionBolutek.ReadV9Reported(new(link, 64, 64, trace:trace)).Level == 42 && link.Writes == 2,
            "reported revision queries twice instead of waiting for absent notifications");
        Check(link.Outputs.All(HeadsetExpansionBolutek.IsV9BatteryQuery), "no firmware/DPI/alternate command emitted");
        var echo = new Link(_ => [request]);
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Reported(new(echo,64,64))) && echo.Writes == 1, "query echo cannot become one percent");
        var missing = new Link(n => n == 1 ? [Headset(60)] : []);
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Reported(new(missing,64,64))) && missing.Writes == 2, "second timeout invalidates first result");
        var asleep = new Link(_ => [Headset(60,26)]);
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Reported(new(asleep,64,64))), "sleeping response cannot supply cached percent");
        var frozen = new Link(_ => [Headset(97,3)]);
        Check(HeadsetExpansionBolutek.ReadV9Reported(new(frozen,64,64)).Level == -1, "frozen external-power percentage remains suppressed");
        var failedWrite = new Link(_ => []) { WriteOk = false };
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Reported(new(failedWrite,64,64))) && failedWrite.Writes == 1, "failed write never tries another transport");
        var flood = new Link(_ => []) { Flood = true };
        Check(Reject(() => HeadsetExpansionBolutek.ReadV9Reported(new(flood,64,64))) && flood.Writes == 0, "stale flood never causes a query");
        var c = new BatteryReplay.Collection("one",1,0x291D,0x385D,"MCHOSE V9 PRO",0xFF90,1,64,64,0,false,false,
            InterfaceLabel:"MI_00/COL_05",UsbRevision:0x12,ReportIds:"IN[55] OUT[] FEATURE[]");
        var q = new BatteryReplay.Query("one",null,"ExtraHeadset:V9Pro","Online",null,42,trace.Snapshot());
        Check(BatteryReplay.Replay(q,[c]).Percent == 42, "active known-query replay uses both transactions");
        Check(BatteryReplay.Replay(q,[c with { Receiver="another" }]).Percent == null, "known-query replay instance isolation");
        Check(BatteryReplay.Replay(q,[c with { UsbRevision=0x13 }]).Percent == null, "reported exception exact version");
        var malformed = trace.Snapshot().Select(s => s.Stage == "expansion-output" ? s with { Hex = Convert.ToHexString(new byte[64]) } : s).ToArray();
        Check(BatteryReplay.Replay(q with { Samples=malformed },[c]).Percent == null, "replay rejects different commands");
        Check(BatteryReplay.Replay(q with { Samples=trace.Snapshot().Take(2).ToArray() },[c]).Percent == null, "single transaction replay remains unknown");
        return count;
    }

    private sealed class Link(Func<int,byte[][]> response) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> input = new();
        internal readonly List<byte[]> Outputs = new();
        internal int Writes;
        internal bool WriteOk = true, Flood;
        public long Now { get; private set; }
        public byte[]? Read(int ms) { Now += Math.Max(1,ms); return input.Count > 0 ? input.Dequeue() : Flood ? new byte[64] : null; }
        public bool Write(byte[] b,int ms)
        { Writes++; Outputs.Add((byte[])b.Clone()); if (!WriteOk) return false; foreach(var r in response(Writes)) input.Enqueue(r); return true; }
        public bool SetFeature(byte[] b) => throw new Exception("Unexpected Feature write");
        public byte[] GetFeature(byte id,int size) => throw new Exception("Unexpected Feature read");
        public void Delay(int ms) => Now += ms;
    }
}
