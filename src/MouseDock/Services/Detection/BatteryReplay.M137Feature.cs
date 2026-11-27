using System;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static bool IsM137FeatureChannel(Collection channel) =>
        M137FeatureProtocol.IsCandidate(channel.Vid, channel.Pid, channel.Product) &&
        M137FeatureProtocol.MatchesChannel(channel.Vid, channel.Pid, channel.Page, channel.Usage,
            channel.Input, channel.Output, channel.Feature);

    private static Result ReplayM137Feature(Query query)
    {
        var samples = query.Samples ?? Array.Empty<Sample>();
        int position = 0;
        bool Transfer(string stage, byte[] packet)
        {
            if (position >= samples.Length) return false;
            var sample = samples[position++];
            if (!sample.Ok || sample.Stage != stage || sample.Sequence != packet[4]) return false;
            if (stage == "get-feature" && sample is { Result: "invalid-frame", Hex: null, Length: 0 })
            {
                Array.Clear(packet);
                return true;
            }
            if (sample.Length != packet.Length || sample.Hex?.Length != packet.Length * 2) return false;
            if (stage == "set-feature") return sample.Hex == Convert.ToHexString(packet);
            try { Convert.FromHexString(sample.Hex).CopyTo(packet, 0); return true; }
            catch (FormatException) { return false; }
        }

        var reading = M137FeatureProtocol.Read(packet => Transfer("set-feature", packet),
            packet => Transfer("get-feature", packet), _ => { }, () => 0);
        bool verified = position == samples.Length && reading.Level >= 0 &&
            query.Status == "Online" && query.ObservedPercent == reading.Level;
        return new(query.Receiver, query.Interface, query.Protocol, query.Status, verified ? reading.Model : null,
            verified ? reading.Level : null, null, verified ? "captured" : "unavailable");
    }
}
