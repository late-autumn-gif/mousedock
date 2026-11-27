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
        internal ReferenceBatteryProtocol.Reading Read() => M137FeatureProtocol.Read(
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
            }, () => 31);
    }
}
