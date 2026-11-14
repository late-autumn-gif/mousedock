using System;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static Result ReplayReportedTrio(Query query, Collection[] collections)
    {
        Result Missing(string status = null) => new(query.Receiver, query.Interface, query.Protocol,
            status ?? query.Status, query.Model, null, null, "unavailable");
        bool rapoo = query.Protocol == "ExtraMouse:RapooVt3Max";
        bool v9 = query.Protocol == "ExtraHeadset:V9Pro";
        var candidates = collections.Where(c => c.Receiver == query.Receiver && !c.Excluded &&
            (!query.Interface.HasValue || c.Interface == query.Interface) &&
            (rapoo ? c.Vid == 0x24AE && c.Pid == 0x1417 && c.InterfaceLabel == "MI_01/COL_09" &&
                c.Page == 0xFF00 && c.Usage == 2 && c.Input == 19 && c.Output == 19 && c.Feature == 0 &&
                c.UsbRevision == 0x0105 && c.ReportIds == "IN[07] OUT[07] FEATURE[]" :
            v9 ? VendorProtocol.IsMchoseV9Pro(c.Vid, c.Pid, c.Product) && c.InterfaceLabel == "MI_00/COL_05" &&
                c.Page == 0xFF90 && c.Usage == 1 && c.Input == 64 && c.Output == 64 && c.Feature == 0 &&
                c.UsbRevision == 0x12 && c.ReportIds == "IN[55] OUT[] FEATURE[]" :
            c.Vid == 0x41E4 && c.Pid == 0x2001 && c.Product == "MCHOSE 2.4G Wireless" &&
                c.InterfaceLabel == "MI_01/COL_01" && c.Page == 0xFF02 && c.Usage == 2 &&
                c.Input == 20 && c.Output == 20 && c.Feature == 0 && c.UsbRevision == 0x3000 &&
                c.ReportIds == "IN[13] OUT[13] FEATURE[]")).ToArray();
        if (candidates.Length != 1) return Missing("collection-mismatch");
        if (query.Status is not ("Online" or "Charging")) return Missing();
        var samples = query.Samples ?? Array.Empty<Sample>();
        if (samples.Any(s => s == null || s.Stage == null)) return Missing("invalid-captured-frame");
        byte[] Bytes(Sample sample)
        {
            if (!sample.Ok || sample.Hex == null || sample.Hex.Length != sample.Length * 2 || sample.Length > 65)
                throw new InvalidDataException("Missing captured frame");
            return Convert.FromHexString(sample.Hex);
        }
        try
        {
            ReferenceBatteryProtocol.Reading result = null;
            if (rapoo || v9)
            {
                if (rapoo && samples.Any(s => s.Stage == "vt3-snapshot-start"))
                {
                    int begin = Array.FindIndex(samples, s => s.Stage == "vt3-snapshot-start" && s.Ok);
                    int finish = Array.FindIndex(samples, s => s.Stage == "vt3-snapshot-complete" && s.Ok);
                    if (begin < 0 || finish <= begin || samples.Any(s => s.Stage.Contains("output") || s.Stage.Contains("feature")))
                        return Missing();
                    var snapshots = samples.Skip(begin + 1).Take(finish - begin - 1)
                        .Where(s => s.Stage == "vt3-input-snapshot").ToArray();
                    if (snapshots.Length != 2) return Missing();
                    foreach (var snapshot in snapshots) result = RapooVt3MaxProtocol.Decode(Bytes(snapshot));
                    return new(query.Receiver, candidates[0].Interface, query.Protocol, query.Status,
                        result.Model, result.Level, result.ChargingKnown ? result.Charging : null, "captured");
                }
                if (v9 && samples.Any(s => s.Stage == "expansion-output"))
                {
                    var exchanges = samples.Where(s => s.Stage == "expansion-output" || s.Stage == "expansion-input" && s.Ok).ToArray();
                    if (exchanges.Length != 4) return Missing();
                    for (int i = 0; i < 4; i += 2)
                    {
                        if (exchanges[i].Stage != "expansion-output" || exchanges[i + 1].Stage != "expansion-input" ||
                            !HeadsetExpansionBolutek.IsV9BatteryQuery(Bytes(exchanges[i]))) return Missing();
                        result = HeadsetExpansionBolutek.Decode(Bytes(exchanges[i + 1]), v9Pro: true);
                        if (result.Level < 0) return Missing();
                    }
                    return new(query.Receiver, candidates[0].Interface, query.Protocol, query.Status,
                        result.Model, result.Level, result.ChargingKnown ? result.Charging : null, "captured");
                }
                int start = Array.FindIndex(samples, s => s.Stage == "battery-passive-drained" && s.Ok);
                int end = Array.FindIndex(samples, s => s.Stage == "battery-passive-complete" && s.Ok);
                if (start < 0 || end <= start || samples.Any(s => s.Stage.Contains("output") || s.Stage.Contains("feature")))
                    return Missing();
                var input = samples.Skip(start + 1).Take(end - start - 1).Where(s => s.Stage == "expansion-input" && s.Ok).ToArray();
                if (input.Length != 2) return Missing();
                foreach (var sample in input)
                {
                    result = rapoo ? RapooVt3MaxProtocol.Decode(Bytes(sample)) : HeadsetExpansionBolutek.Decode(Bytes(sample), v9Pro: true);
                    if (result.Level < 0) return Missing();
                }
            }
            else
            {
                var transaction = samples.Where(s => s.Stage == "keyboard-output" || s.Stage == "keyboard-input" && s.Ok).ToArray();
                if (transaction.Length != 4) return Missing();
                for (int i = 0; i < 4; i += 2)
                {
                    if (transaction[i].Stage != "keyboard-output" || transaction[i + 1].Stage != "keyboard-input" ||
                        !Bytes(transaction[i]).AsSpan().SequenceEqual(KeyboardExpansionMchoseBy.Request(20))) return Missing();
                    result = KeyboardExpansionMchoseBy.Decode(Bytes(transaction[i + 1]));
                }
            }
            return result?.Level is >= 0 and <= 100 ? new(query.Receiver, candidates[0].Interface,
                query.Protocol, query.Status, result.Model, result.Level,
                result.ChargingKnown ? result.Charging : null, "captured") : Missing();
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException)
        { return Missing("invalid-captured-frame"); }
    }
}
