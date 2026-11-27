using System.Text;
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
                updated.Status == ReferenceBatteryProtocol.State.Online,
                $"A valid {levels.First} to {levels.Second} update must publish the second percentage");
        }
        var trace = new BatteryReplay.Trace();
        var unconfirmedZero = new Link(Frame("M137", 72), Frame("M137", 0));
        var unknown = unconfirmedZero.Read(trace);
        Check(unknown.Level == -1 && unknown.Identity == "M137" && unknown.Model == "狼蛛 SC580" &&
              unknown.Status == ReferenceBatteryProtocol.State.Invalid,
            "An isolated SC580 zero leaves battery unknown and preserves the confirmed identity");
        var samples = trace.Snapshot();
        Check(samples.Where(s => s.Stage == "get-feature").Select(s => s.Result)
                  .SequenceEqual(new[] { "M137: 72%", "M137: 0%" }) &&
              samples.All(s => s.Hex == null) && samples[^1].Stage == "zero-confirmation" && !samples[^1].Ok,
            "The export trace captures both percentages and the rejection without raw bytes");
        Check(new Link(Frame("M137", 72), Frame("M137", 72)).Read().Level == 72,
            "A following valid scan recovers immediately");
        Check(new Link(Frame("M139", 72), Frame("M139", 0)).Read().Level == 0,
            "Other models on the shared receiver keep their existing zero handling");
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
        var retry = new Link(null!, Frame("M137", 72), Frame("M137", 72));
        Check(retry.Read().Level == 72 && retry.Sets == 2 && retry.Gets == 3,
            "A transient native GET failure can recover using the remaining GET attempt");
        var unavailable = new Link(null!, null!);
        Check(unavailable.Read().Level == -1 && unavailable.Sets == 1 && unavailable.Gets == 2,
            "Consecutive native GET failures stop within the existing query budget");
        var failed = new Link(Frame("M137", 50), Frame("M137", 50)) { WriteOk = false };
        Check(failed.Read().Level == -1 && failed.Gets == 0, "SET failure stops before GET");
        return count;
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
                if (replies.Dequeue() is not { } reply) return false;
                reply.CopyTo(packet, 0);
                return true;
            },
            milliseconds =>
            {
                if (milliseconds != 16) throw new Exception("Unexpected M137 delay");
            }, () => 31, trace);
    }
}
