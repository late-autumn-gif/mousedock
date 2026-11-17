using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static Result ReplayG3AVt7(Query query, Collection[] collections)
    {
        bool g3a = query.Protocol == "ExtraMouse:mchose-g3a";
        string family = g3a ? "mchose-g3a" : "rapoo-vt7";
        string model = g3a ? VendorProtocol.MchoseG3AName : VendorProtocol.RapooVt7Model;
        string prefix = g3a ? "g3a" : "vt7";
        string label = g3a ? "MI_02" : "MI_01";
        string reports = g3a ? "IN[00] OUT[00] FEATURE[]" : "IN[07] OUT[07] FEATURE[]";
        bool eligible = collections.Count(c => c.Receiver == query.Receiver && !c.Excluded &&
            (!query.Interface.HasValue || c.Interface == query.Interface) && c.Mi02 == g3a &&
            (c.InterfaceLabel == label || c.InterfaceLabel?.StartsWith(label + "/COL_", StringComparison.Ordinal) == true) &&
            c.ReportIds == reports && c.Input == (g3a ? 65 : 19) && c.Output == c.Input && c.Feature == 0 &&
            VendorProtocol.MouseExpansionChannel(family,
                new HidDiscovery.VendorCollectionInfo
                {
                    Vid = c.Vid, Pid = c.Pid, Product = c.Product, Path = "&" + label,
                    UsagePage = c.Page, Usage = c.Usage, Input = (ushort)c.Input, Output = (ushort)c.Output, Feature = 0
                })) == 1;
        if (!eligible) return Unavailable("collection-mismatch");

        string responseStage = g3a ? "g3a-response" : "vt7-status";
        var steps = (query.Samples ?? Array.Empty<Sample>())
            .Where(s => s.Stage?.StartsWith(prefix + "-", StringComparison.Ordinal) == true).ToArray();
        if (steps.Length != (g3a ? 5 : 2) || steps[0].Stage != prefix + "-open" || !steps[0].Ok)
            return Unavailable(query.Status);

        int level = -1;
        bool charging = false;
        for (int i = 1; i < steps.Length; i++)
        {
            var sample = steps[i];
            if (!sample.Ok || sample.Length is < 1 or > 65 || sample.Hex?.Length != sample.Length * 2)
                return Unavailable(query.Status);
            byte[] frame;
            try { frame = Convert.FromHexString(sample.Hex); }
            catch (FormatException) { return Unavailable(query.Status); }
            if (g3a && i % 2 == 1)
            {
                if (sample.Stage != "g3a-request" || !frame.AsSpan().SequenceEqual(VendorProtocol.MchoseG3ABatteryRequest()))
                    return Unavailable(query.Status);
                continue;
            }
            if (sample.Stage != responseStage || !(g3a
                ? VendorProtocol.TryMchoseG3ABattery(frame, out level, out charging)
                : frame.Length == 19 && VendorProtocol.TryParseRapooVt7Battery(frame, out level, out charging)))
                return Unavailable(query.Status);
        }
        return query.ObservedPercent == level && query.Model == model && query.Status == (charging ? "Charging" : "Online")
            ? new(query.Receiver, query.Interface, query.Protocol, query.Status, model, level, charging, "captured")
            : Unavailable(query.Status);

        Result Unavailable(string status) =>
            new(query.Receiver, query.Interface, query.Protocol, status, query.Model, null, null, "unavailable");
    }
}
