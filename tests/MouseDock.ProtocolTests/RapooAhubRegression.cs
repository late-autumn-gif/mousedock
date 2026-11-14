using System.IO;
using MouseDock.Services.Detection;

internal static class RapooAhubRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("AHUB: " + name); count++; }
        bool Reject(Action action) { try { action(); return false; } catch (Exception e) when (e is InvalidDataException or ArgumentException) { return true; } }
        byte[] Reply(byte[] request, int slot = 2)
        {
            var b = new byte[33]; b[0] = 8; b[1] = 1;
            if (request[2] == 164)
            {
                b[2] = 164; b[3] = request[3];
                if (request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(268, 1))) b[5] = (byte)slot;
                else if (request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(2200, 1))) b[5] = 3;
            }
            return b;
        }
        byte[] NativeReply(byte[] request, int slot = 2)
        {
            var b = new byte[33]; Reply(request, slot).AsSpan(1).CopyTo(b);
            if (request[2] == 164) b[1] = b[2] = b[3] = 0;
            return b;
        }
        var realRejected = Convert.FromHexString("010000004300000000000000000000000000000000000000000000000000000000");
        Check(RapooAhubProtocol.ValidateReply(RapooAhubProtocol.Names[0], realRejected)[4] == 0x43,
            "actual receiver ACK-first rejected response is a valid configuration read, not battery");
        Check(RapooAhubProtocol.MaskReply(realRejected, false)[4] == 0, "configuration name is fully redacted from actual frame");
        var nativeTrace = new BatteryReplay.Trace(); nativeTrace.Add("ahub-session-start", true);
        int nativeRequests = 0;
        RapooAhubProtocol.Initialize(request =>
        {
            nativeRequests++;
            nativeTrace.Add("ahub-read-output", true, raw: request, keep: true);
            var response = NativeReply(request);
            nativeTrace.Add("ahub-read-ack", true, raw: RapooAhubProtocol.MaskReply(response,
                request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(268, 1)) ||
                request.AsSpan().SequenceEqual(RapooAhubProtocol.ReadMemory(2200, 1))), keep: true);
            return response;
        });
        Check(nativeRequests == 15, "ACK-first layout completes the entire initialization");
        foreach (bool tagged in new[] { false, true })
        {
            var good = tagged ? Reply(RapooAhubProtocol.Names[0]) : NativeReply(RapooAhubProtocol.Names[0]);
            int offset = tagged ? 1 : 0;
            good[offset] = 2;
            Check(RapooAhubProtocol.Busy(good) && Reject(() => RapooAhubProtocol.ValidateReply(RapooAhubProtocol.Names[0], good)), "both busy layouts retry rather than succeed");
            good[offset] = 0;
            Check(!RapooAhubProtocol.Busy(good) && Reject(() => RapooAhubProtocol.ValidateReply(RapooAhubProtocol.Names[0], good)), "invalid ACK rejected");
        }
        foreach (var bad in new byte[][] { null!, new byte[0], new byte[32], new byte[34], new byte[33] })
            Check(Reject(() => RapooAhubProtocol.ValidateReply(RapooAhubProtocol.Names[0], bad)), "missing, short, oversized and invalid framing rejected");
        Check(Reject(() => RapooAhubProtocol.Initialize(r => NativeReply(r, 8))), "native out-of-range config index rejected");
        var trace = new BatteryReplay.Trace(); trace.Add("ahub-session-start", true);
        int requests = 0;
        RapooAhubProtocol.Initialize(request =>
        {
            Check(RapooAhubProtocol.Allowed(request), "all official initialization reads admitted"); requests++;
            trace.Add("ahub-read-output", true, raw: request, keep: true);
            var response = Reply(request); trace.Add("ahub-read-ack", true, raw: response, keep: true);
            return response;
        });
        Check(requests == 15, "eight names, current index and name, DPI, four firmware reads");
        foreach (var request in RapooAhubProtocol.Names.Concat(RapooAhubProtocol.Firmware)
            .Concat(new[] { RapooAhubProtocol.ReadMemory(268, 1), RapooAhubProtocol.ReadMemory(2200, 1) }))
        {
            for (int offset = 0; offset < request.Length; offset++)
            {
                var changed = (byte[])request.Clone(); changed[offset] ^= 0x80;
                Check(!RapooAhubProtocol.Allowed(changed), "non-whitelisted byte mutation rejected");
            }
            foreach (int size in new[] { 0, 32, 34, 513 }) Check(!RapooAhubProtocol.Allowed(new byte[size]), "wrong capacity rejected");
            var response = Reply(request); response[1] = 2;
            Check(Reject(() => RapooAhubProtocol.ValidateReply(request, response)), "busy not successful acknowledgement");
            Check(Reject(() => RapooAhubProtocol.ValidateReply(request, request)), "echo rejected");
        }
        Check(Reject(() => RapooAhubProtocol.Initialize(r => Reply(r, 8))), "configuration slot overflow rejects initialization");
        var now = DateTimeOffset.UtcNow;
        byte[] Frame(int pct = 62) { var b = new byte[19]; b[0] = 7; b[7] = 1; b[8] = (byte)pct; b[18] = 128; return b; }
        var sample = new BatteryReplay.Sample("ahub-battery-input", true, Length: 19, Hex: Convert.ToHexString(Frame()), At: now);
        var transactionTrace = new BatteryReplay.Trace();
        Check(RapooAhubReader.Execute(new(new Input(Frame()), 19, 19), NativeReplyDefault, transactionTrace).Level == 62,
            "existing per-refresh transaction runs native initialization and fresh notification");
        byte[] NativeReplyDefault(byte[] request) => NativeReply(request);
        var offline = Frame(); offline[18] = 0;
        Check(Reject(() => RapooAhubReader.Execute(new(new Input(offline), 19, 19), NativeReplyDefault, new())), "offline notification rejected");
        var emptyTrace = new BatteryReplay.Trace();
        bool timedOut = false;
        try { RapooAhubReader.Execute(new(new Input(null), 19, 19), NativeReplyDefault, emptyTrace); }
        catch (IOException) { timedOut = true; }
        Check(timedOut && !emptyTrace.Snapshot().Any(s => s.Stage == "ahub-battery-complete"), "missing notification never adopts config response as battery");
        var failedTrace = new BatteryReplay.Trace();
        Check(Reject(() => RapooAhubReader.Execute(new(new Input(Frame()), 19, 19), _ => new byte[33], failedTrace)) &&
            !failedTrace.Snapshot().Any(s => s.Stage == "ahub-initialized"), "invalid initialization stops current transaction");
        trace.Add("ahub-initialized", true); trace.Append(new[] { sample! }); trace.Add("ahub-battery-complete", true);
        BatteryReplay.Collection C(int id, ushort usage, int input, int output, int feature, string label, string ids) =>
            new("r1", id, 0x24AE, 0x1417, "Rapoo Gaming Device", 0xFF00, usage, input, output, feature, false, false,
                InterfaceLabel: label, UsbRevision: 0x0105, ReportIds: ids);
        var collections = new[] { C(1, 2, 19, 19, 0, "MI_01/COL_09", "IN[07] OUT[07] FEATURE[]"),
            C(2, 14, 33, 33, 0, "MI_01/COL_05", "IN[06] OUT[06] FEATURE[]"),
            C(3, 15, 0, 0, 33, "MI_01/COL_06", "IN[] OUT[] FEATURE[08]") };
        var q = new BatteryReplay.Query("r1", null, "ExtraMouse:RapooVt3Max", "Online", null, 62, trace.Snapshot());
        nativeTrace.Add("ahub-initialized", true); nativeTrace.Append(new[] { sample! }); nativeTrace.Add("ahub-battery-complete", true);
        var nativeQuery = q with { Samples = nativeTrace.Snapshot() };
        Check(BatteryReplay.Replay(nativeQuery, collections).Percent == 62, "native ACK-first masked full transaction replays");
        for (int i = 0; i < nativeQuery.Samples.Length; i++)
            Check(BatteryReplay.Replay(nativeQuery with { Samples = nativeQuery.Samples.Where((_, n) => n != i).ToArray() }, collections).Percent == null,
                "native incomplete transcript rejected " + i);
        Check(BatteryReplay.Replay(q, collections).Percent == 62, "full official read trace plus one event replays");
        Check(BatteryReplay.Replay(q with { Status = "Failed" }, collections).Percent == null, "failed final status rejects frame");
        Check(BatteryReplay.Replay(q, collections.Take(2).ToArray()).Percent == null, "missing response channel rejects");
        Check(BatteryReplay.Replay(q, collections.Append(collections[1] with { Interface = 4 }).ToArray()).Percent == null, "ambiguous control rejected");
        Check(BatteryReplay.Replay(q with { Receiver = "other" }, collections).Percent == null, "physical instance isolation");
        Check(BatteryReplay.Replay(q with { Samples = q.Samples.Select(s => s.Stage == "ahub-battery-input"
            ? s with { At = now.AddSeconds(-31) } : s).ToArray() }, collections).Percent == null, "stale captured notification rejected");
        Check(BatteryReplay.Replay(q with { Samples = q.Samples.Select(s => s.Stage == "ahub-session-start"
            ? s with { Ok = false } : s).ToArray() }, collections).Percent == null, "failed session start rejected");
        Check(BatteryReplay.Replay(q with { Samples = q.Samples.Select(s => s.Stage == "ahub-read-output"
            ? s with { Hex = Convert.ToHexString(new byte[33]) } : s).ToArray() }, collections).Percent == null, "non-whitelisted replay command rejected");
        for (int i = 0; i < q.Samples.Length; i++)
            Check(BatteryReplay.Replay(q with { Samples = q.Samples.Where((_, n) => n != i).ToArray() }, collections).Percent == null,
                "incomplete initialization/event trace rejects " + i);
        return count;
    }

    private sealed class Input(byte[]? report) : ReferenceBatteryProtocol.ILink
    {
        private byte[]? pending = report;
        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += timeoutMs;
            if (timeoutMs == 1) return null;
            var value = pending; pending = null; return value;
        }
        public void Delay(int ms) => Now += ms;
        public bool Write(byte[] b, int ms) => throw new Exception("Unexpected input write");
        public bool SetFeature(byte[] b) => throw new Exception("Unexpected input Feature write");
        public byte[] GetFeature(byte id, int length) => throw new Exception("Unexpected input Feature read");
    }
}
