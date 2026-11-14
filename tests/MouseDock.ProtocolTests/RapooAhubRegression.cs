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
        var inbox = new RapooAhubInbox();
        inbox.Push(Frame(), now);
        var sample = inbox.Take(now);
        Check(sample != null && sample.At == now && !inbox.HasValue && inbox.Take(now) == null, "one official notification consumed exactly once with original time");
        inbox.Push(Frame(), now); Check(inbox.Take(now.AddSeconds(31)) == null, "old notification not fresh");
        inbox.Push(Frame(), now); Check(inbox.Take(now.AddSeconds(-1)) == null, "future timestamp rejected");
        inbox.Push(Frame(), now); var offline = Frame(); offline[18] = 0;
        Check(Reject(() => inbox.Push(offline, now)) && !inbox.HasValue, "offline clears prior value");
        inbox.Push(Frame(60), now); inbox.Push(Frame(61), now);
        Check(Convert.FromHexString(inbox.Take(now)!.Hex!)[8] == 61, "latest notification wins bounded buffer");
        trace.Add("ahub-initialized", true); trace.Append(new[] { sample! }); trace.Add("ahub-battery-complete", true);
        BatteryReplay.Collection C(int id, ushort usage, int input, int output, int feature, string label, string ids) =>
            new("r1", id, 0x24AE, 0x1417, "Rapoo Gaming Device", 0xFF00, usage, input, output, feature, false, false,
                InterfaceLabel: label, UsbRevision: 0x0105, ReportIds: ids);
        var collections = new[] { C(1, 2, 19, 19, 0, "MI_01/COL_09", "IN[07] OUT[07] FEATURE[]"),
            C(2, 14, 33, 33, 0, "MI_01/COL_05", "IN[06] OUT[06] FEATURE[]"),
            C(3, 15, 0, 0, 33, "MI_01/COL_06", "IN[] OUT[] FEATURE[08]") };
        var q = new BatteryReplay.Query("r1", null, "ExtraMouse:RapooVt3Max", "Online", null, 62, trace.Snapshot());
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
}
