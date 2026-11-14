using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadRapooVt3Max(ushort vid, ushort pid, string product,
        IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace)
    {
        if (!RapooVt3MaxProtocol.IsCandidate(vid, pid) || string.IsNullOrEmpty(InstanceScope.Current))
            return ReferenceBatteryProtocol.Reading.Failed("VT3 MAX physical instance missing");
        var selected = channels?.Where(c => RapooVt3MaxProtocol.Matches(c) && InstanceScope.Allows(c.Path)).ToArray();
        if (selected?.Length != 1) return ReferenceBatteryProtocol.Reading.Failed("VT3 MAX missing/ambiguous report7 channel");
        return ReferenceQueryGate.Run(InstanceScope.Current + ":vt3max", () =>
        {
            var rules = new ExpansionHidRules("RapooVt3Max", RapooVt3MaxProtocol.Matches,
                RapooVt3MaxProtocol.Validate, Passive: true);
            using var link = new ExpansionHidLink(rules, selected[0], trace);
            return RapooVt3MaxProtocol.ReadWithSnapshot(link.Wire(4000), link.GetRapooCurrentInput);
        }, 4500);
    }
}
