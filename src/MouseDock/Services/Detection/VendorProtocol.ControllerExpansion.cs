using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static bool ExtraControllerCandidate(ushort vid, ushort pid, string product) =>
        ExtraControllerFamily(vid, pid, product) != null;
    internal static string ExtraControllerFamily(ushort vid, ushort pid, string product) => (vid, pid) switch
    {
        (0x054C, 0x05C4 or 0x09CC or 0x05C5 or 0x0BA0) => "SonyDs4",
        (0x054C, 0x0CE6 or 0x0DF2) => "SonyDs5",
        (0x2DC8, 0x6000 or 0x6100 or 0x6001 or 0x6101 or 0x6003 or 0x6006 or 0x6009) => "EightBitDo",
        (0x057E, 0x2006 or 0x2007 or 0x2009) => "Nintendo",
        (0x37D7, 0x2401) => "Vader5",
        (0x37D7, 0x2501) => "Apex5",
        (0x4131, 0x3519) => "Leadjoy",
        _ => null
    };

    internal static bool ExtraControllerChannel(string family, HidDiscovery.VendorCollectionInfo c)
    {
        if (c == null || ExtraControllerFamily(c.Vid, c.Pid, c.Product) != family) return false;
        bool bt = ControllerExpansionProtocol.BluetoothPath(c.Path);
        return family switch
        {
            "SonyDs4" or "SonyDs5" => c.UsagePage == 1 && c.Usage is 4 or 5 && c.Input == (bt ? 78 : 64),
            "EightBitDo" => c.UsagePage == 1 && c.Usage is 4 or 5 && c.Input is >= 15 and <= 512,
            "Nintendo" => bt && c.UsagePage == 1 && c.Usage is 4 or 5 && c.Input is >= 49 and <= 512,
            "Vader5" => c.UsagePage == 0xFFA0 && c.Usage == 1 && c.Input is >= 32 and <= 65 &&
                c.Output is >= 32 and <= 65,
            "Apex5" => Mi(c, 2) && c.UsagePage == 0xFFA0 && c.Usage == 1 && c.Input == 32 && c.Output == 32,
            "Leadjoy" => Mi(c, 4) && c.UsagePage == 0xFF80 && c.Usage == 1 && c.Input == 65 && c.Output == 65,
            _ => false
        };
    }

    internal static ReferenceBatteryProtocol.Reading ReadExtraController(ushort vid, ushort pid,
        string product, IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels, BatteryReplay.Trace trace)
    {
        string family = ExtraControllerFamily(vid, pid, product);
        if (family == null || string.IsNullOrEmpty(InstanceScope.Current) || channels == null)
            return ReferenceBatteryProtocol.Reading.Failed("No scoped controller profile");
        var selected = channels.Where(c => c != null && c.Vid == vid && c.Pid == pid &&
            InstanceScope.Allows(c.Path) && ExtraControllerChannel(family, c)).ToArray();
        if (selected.Length != 1) return ReferenceBatteryProtocol.Reading.Failed("Missing or ambiguous controller collection");
        var c = selected[0];
        bool bt = ControllerExpansionProtocol.BluetoothPath(c.Path);
        bool passive = family is "SonyDs4" or "SonyDs5" or "EightBitDo" || family == "Nintendo" && c.Output != 49;
        var rules = new ExpansionHidRules(family, x => ExtraControllerChannel(family, x), (x, m) =>
        {
            if (family is "Vader5" or "Leadjoy")
                Require(m.InputIds.SetEquals(new byte[] { 0 }) && m.OutputIds.SetEquals(new byte[] { 0 }) &&
                    m.FeatureIds.All(id => id == 0) && m.HasInput(0, x.Input - 1) && m.HasOutput(0, x.Output - 1),
                    "Controller unnumbered reports not proven");
            else if (family == "Apex5")
                Require(m.HasInput(4, 32) && m.HasOutput(3, 32) && m.OutputIds.SetEquals(new byte[] { 3 }),
                    "Apex report4/input or report3/output not proven");
            else
                Require(m.InputIds.Contains(family == "Nintendo" ? (byte)0x30 :
                    family == "EightBitDo" ? bt ? (byte)1 : (byte)4 :
                    bt ? family == "SonyDs5" ? (byte)0x31 : (byte)0x11 : (byte)1),
                    "Controller expected input report missing");
        }, passive, family is "Vader5" or "Leadjoy");
        return ReferenceQueryGate.Run(InstanceScope.Current + ":controller-expansion", () =>
        {
            using var link = new ExpansionHidLink(rules, c, trace);
            var wire = link.Wire(5000);
            if (family is "Vader5" or "Apex5" or "Leadjoy")
            {
                ReferenceBatteryProtocol.Reading result = null;
                for (int i = 0; i < 2; i++)
                {
                    var packet = family == "Leadjoy" ? ControllerExpansionLeadjoy.Request() :
                        ControllerExpansionFlydigi.Request(c.Output, family == "Apex5");
                    var b = wire.Exchange(packet, b => ControllerExpansionProtocol.Decode(family, b).Status !=
                        ReferenceBatteryProtocol.State.Failed);
                    result = ControllerExpansionProtocol.Decode(family, b);
                }
                return result;
            }
            long start = wire.Link.Now;
            bool sent = false;
            byte[] report = null;
            for (int seen = 0; seen < 256 && wire.Link.Now - start < 1000; seen++)
            {
                if (!sent && family == "Nintendo" && c.Output == 49 && wire.Link.Now - start >= 100)
                {
                    Require(link.Metadata.OutputIds.Contains(1), "Nintendo output report1 missing");
                    wire.Send(ControllerExpansionNintendo.Request()); sent = true;
                }
                var b = wire.Link.Read(50);
                if (b is not { Length: > 0 }) continue;
                bool valid = ControllerExpansionProtocol.Decode(family, b, bt, pid == 0x0BA0).Status !=
                    ReferenceBatteryProtocol.State.Failed;
                trace?.Add("controller-input", valid, raw: b, keep: true);
                if (valid) { report = b; break; }
            }
            Require(report != null, "No fresh controller battery input");
            return ControllerExpansionProtocol.Decode(family, report, bt, pid == 0x0BA0);
        }, 5500);
    }
}
