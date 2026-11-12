using System;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static Result ReplayRazerBlackShark(Query query, ushort pid)
    {
        var samples = query.Samples ?? Array.Empty<Sample>();
        int position = 0;
        byte? previousSequence = null;
        byte? Next(string stage, byte domain, byte command)
        {
            if (position >= samples.Length) return null;
            var sent = samples[position++];
            byte seq = sent.Sequence;
            if (sent.Stage != "write-" + stage || sent.Length != 64 || seq > 31 ||
                (previousSequence.HasValue && seq != ((previousSequence.Value + 1) & 31)) ||
                sent.Hex != Convert.ToHexString(RazerBlackSharkProtocol.Request(seq, domain, command))) return null;
            previousSequence = seq;
            if (!sent.Ok) return null;
            for (; position < samples.Length && !samples[position].Stage.StartsWith("write-", StringComparison.Ordinal); position++)
            {
                var sample = samples[position];
                if (sample.Stage != "input-" + stage || !sample.Ok || sample.Length != 64 ||
                    sample.Sequence != seq || sample.Hex?.Length != 128) continue;
                byte[] raw;
                try { raw = Convert.FromHexString(sample.Hex); }
                catch (FormatException) { continue; }
                if (!RazerBlackSharkProtocol.TryValue(raw, seq, domain, command,
                        pid == RazerBlackSharkProtocol.WiredPid, out byte value)) continue;
                position++;
                return value;
            }
            return null;
        }
        var reading = RazerBlackSharkProtocol.ReadValues(pid, Next);
        bool verified = reading.Level >= 0 && query.Status is "Online" or "Charging";
        return new(query.Receiver, query.Interface, query.Protocol, query.Status, RazerBlackSharkProtocol.Model,
            verified ? reading.Level : null, verified && reading.ChargingKnown ? reading.Charging : null,
            verified ? "captured" : "unavailable");
    }
}
