using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static bool ExtraHeadsetCandidate(ushort vid, ushort pid, string product) =>
        HeadsetExpansionProfiles.Find(vid, pid, product) != null;
    internal static string ExtraHeadsetFamily(ushort vid, ushort pid, string product) =>
        HeadsetExpansionProfiles.Find(vid, pid, product);
    internal static ReferenceBatteryProtocol.Reading ReadExtraHeadset(ushort vid, ushort pid, string product,
        IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace)
    {
        string family = ExtraHeadsetFamily(vid, pid, product);
        if (family == null || string.IsNullOrEmpty(InstanceScope.Current) || channels == null)
            return ReferenceBatteryProtocol.Reading.Failed("No scoped headset profile");
        var selected = channels.Where(c => c != null && c.Vid == vid && c.Pid == pid &&
            InstanceScope.Allows(c.Path) && HeadsetExpansionProfiles.Matches(family, c)).ToArray();
        if (selected.Length == 0 || selected.Length > (family == "Cloud3S" ? 4 : 16) ||
            selected.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            return ReferenceBatteryProtocol.Reading.Failed("Missing/ambiguous headset collection snapshot");
        return ReferenceQueryGate.Run(InstanceScope.Current + ":headset-expansion", () =>
        {
            var links = new List<ExpansionHidLink>();
            try
            {
                var rules = new ExpansionHidRules(family, c => HeadsetExpansionProfiles.Matches(family, c),
                    (c, m) => HeadsetExpansionProfiles.Validate(family, c, m),
                    family is "AulaG9" or "Jbl" or "SonyH9Serial" or "AtkN9Serial");
                foreach (var c in selected)
                {
                    try { links.Add(new ExpansionHidLink(rules, c, trace)); }
                    catch (Exception ex)
                    {
                        trace?.Add("expansion-channel-rejected", false, result: ex.Message);
                        if (selected.Length == 1 || family is "Cloud3S" or "Xiberia") throw;
                    }
                }
                if (family == "Cloud3S")
                {
                    var writers = links.Where(l => l.Metadata.HasOutput(12, 64)).ToArray();
                    var readers = links.Where(l => l.Metadata.InputSizes.TryGetValue(12, out int size) &&
                        size is >= 7 and <= 64).ToArray();
                    Require(writers.Length == 1 && readers.Length > 0, "Cloud III S writer/readers ambiguous");
                    return HeadsetExpansionCloud3S.Read(writers[0].Wire(), readers.Select(l => l.Wire()).ToArray());
                }
                if (family == "Xiberia")
                {
                    Require(links.Count is >= 1 and <= 2 && links.Select(l => l.Channel.UsagePage).Distinct().Count() == links.Count,
                        "Xiberia collection ambiguity");
                    var writers = links.Where(l => l.Metadata.HasInput(0x55, 64)).ToArray();
                    Require(writers.Length == 1, "Xiberia requires one report55 writer");
                    return HeadsetExpansionXiberia.Read(writers[0].Wire(),
                        links.Select(l => (l.Metadata.HasInput(0x55, 64) ? (byte)0x55 : (byte)0x41, l.Wire())).ToArray(),
                        pid is 0xF001 or 0xF008);
                }
                Require(links.Count == 1, "No unique verified headset battery collection");
                var wire = links[0].Wire(family == "Epos" ? 14000 : 10000);
                return family switch
                {
                    "AtkN9V2" or "Bolutek" or "Mhub" or "V9Pro" or "Zhou" => HeadsetExpansionBolutek.Read(wire, family),
                    "Cloud3" or "Cloud2" or "Alpha2" => HeadsetExpansionHyperX.Read(wire, family),
                    "Rog" => HeadsetExpansionRog.Read(wire),
                    "Hecate" or "HecateTws" => HeadsetExpansionHecate.Read(wire, family == "HecateTws"),
                    "AulaG9" or "Jbl" => HeadsetExpansionPassive.Read(wire, family),
                    "Siren" => HeadsetExpansionPassive.ReadSiren(wire),
                    "Dailing" or "IkfLegacy" => HeadsetExpansionDailing.Read(wire),
                    "Moondrop" => HeadsetExpansionMoondrop.Read(wire),
                    "Epos" => HeadsetExpansionEpos.Read(wire),
                    "Ikf" => HeadsetExpansionIkf.Read(wire, pid),
                    "Razer2020" => HeadsetExpansionRazerFeature.Read2020(wire),
                    "RazerControl" => HeadsetExpansionRazerFeature.Read2023(wire),
                    "BlacksharkPa" or "BarracudaPa" => HeadsetExpansionRazerPa.Read(wire, family == "BlacksharkPa"),
                    "RazerV3Pro" => HeadsetExpansionRazerV3.ReadPro(wire, pid == 0x101B),
                    "RazerV3Passive" => HeadsetExpansionRazerV3.ReadPassive(wire),
                    "SonyH5" => HeadsetExpansionSony.ReadH5(wire),
                    "SonyH9Serial" or "AtkN9Serial" => ReadSerialHeadsetNative(family, links[0].Channel, trace),
                    _ => ReferenceBatteryProtocol.Reading.Failed("Unsupported headset family")
                };
            }
            catch (Exception ex) { return ReferenceBatteryProtocol.Reading.Failed(family + ": " + ex.Message); }
            finally { foreach (var link in links) link.Dispose(); }
        }, family == "Epos" ? 14500 : family is "SonyH9Serial" or "AtkN9Serial" ? 12500 : 10500);
    }
}
