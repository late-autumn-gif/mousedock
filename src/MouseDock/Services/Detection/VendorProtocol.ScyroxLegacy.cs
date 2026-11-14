using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadScyroxLegacy(ushort vid,ushort pid,string product,
        IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels,BatteryReplay.Trace trace)
    {
        if (!ScyroxLegacyProtocol.IsCandidate(vid,pid,product) || string.IsNullOrEmpty(InstanceScope.Current))
            return ReferenceBatteryProtocol.Reading.Failed("Scyrox physical instance missing");
        var selected = channels?.Where(c => ScyroxLegacyProtocol.Matches(c) && InstanceScope.Allows(c.Path)).ToArray();
        if (selected?.Length != 1) return ReferenceBatteryProtocol.Reading.Failed("Scyrox missing/ambiguous report08 channel");
        return ReferenceQueryGate.Run(InstanceScope.Current+":scyrox-legacy",() =>
        {
            using var link = new ExpansionHidLink(new("ScyroxLegacy",ScyroxLegacyProtocol.Matches,
                ScyroxLegacyProtocol.Validate,OutputAllowed: b => b != null &&
                    (b.AsSpan().SequenceEqual(ScyroxLegacyProtocol.Request(3)) || b.AsSpan().SequenceEqual(ScyroxLegacyProtocol.Request(4)))),selected[0],trace);
            return ScyroxLegacyProtocol.Read(link.Wire(6000));
        },6500);
    }
}
