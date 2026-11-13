using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionProfiles
{
    internal static string Name(string product)
    {
        string value = (product ?? "").Trim(' ', '\t', '\r', '\n');
        return string.Create(value.Length, value, static (target, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                target[i] = source[i] is >= 'a' and <= 'z' ? (char)(source[i] - 32) : source[i];
        });
    }
    private static bool Named(string product, params string[] names) =>
        names.Any(n => Name(n) == Name(product));
    internal sealed record Profile(string Family, ushort Vid, ushort Pid, string Product)
    {
        internal bool Matches(HidDiscovery.VendorCollectionInfo c)
        {
            if (c.Vid != Vid || c.Pid != Pid || Name(c.Product) != Name(Product)) return false;
            bool Mi(int number) => c.Path?.Contains("&mi_" + number.ToString("X2"), StringComparison.OrdinalIgnoreCase) == true;
            bool Page(ushort page, ushort usage) => c.UsagePage == page && c.Usage == usage;
            bool Io(int low, int high) => c.Input >= low && c.Input <= high && c.Output >= low && c.Output <= high;
            return Family switch
            {
                "AtkHfd" => Page(0xFF60,0x61) && c.Input == 33 && c.Output == 33 && c.Feature == 0,
                "AulaHub" => (Named(Product,"AULA NOVA98 Dongle")
                    ? c.UsagePage == 0xFF80 && c.Usage is 1 or 0x71
                    : Named(Product,"F75 Master Dongle") ? Page(0xFF60,0x61) : Page(0xFF68,0x61)) &&
                    c.Input is 33 or 65 && c.Output == c.Input,
                "Weikav" => Mi(3) && Page(0xFF60,0x61) && c.Input is 33 or 65 && c.Output == c.Input,
                "Nova" => Mi(3) && Page(0xFF60,0x61) && (Vid == 0x0C45 ||
                    Named(Product,"VK 75 Wireless Receiver","VK99Pro Wireless Receiver")
                    ? c.Input is 33 or 65 && c.Output == c.Input : Io(33,65)),
                "Shared024F" => Mi(3) && Page(0xFF60,0x61) && c.Input is 33 or 65 && c.Output == c.Input ||
                    Mi(1) && c.UsagePage is 0xFF02 or 0xFF04 && c.Usage == 2 &&
                    (c.Input is >= 20 and <= 65 || c.Output is >= 20 and <= 65),
                "Relay" => Mi(2) && Page(0xFFFF,2) && c.Feature is >= 10 and <= 65,
                "Irok" or "RoyuanLegacy" => Mi(2) && Page(0xFFFF,2) && c.Feature == 65,
                "Neon" => Mi(1) && c.Path.Contains("&col01#",StringComparison.OrdinalIgnoreCase) &&
                    c.UsagePage >= 0xFF00 && c.Feature == 65,
                "Cherry" => Mi(1) && Page(0xFF1C,0x92) && Io(11,65),
                "MchoseBy" => Mi(1) && c.UsagePage is 0xFF02 or 0xFF04 && c.Usage == 2 &&
                    (c.Input is >= 20 and <= 65 || c.Output is >= 20 and <= 65),
                "Glw" => (Named(Product,"MCHOSE K99 V3 2.4G") || Mi(1)) && Page(1,0) &&
                    c.Input == 65 && c.Output == 65,
                "Qhw" => Mi(2) && Page(0xFF80,1) && c.Input is 32 or 64 && c.Output == c.Input,
                "Vgn" => Mi(1) && Page(0xFF1C,0x92) && c.Input is >= 14 and <= 65 && c.Output is >= 8 and <= 65,
                "AtkV75" => Mi(1) && Page(0xFF1C,0x92) && c.Input is 64 or 65 && c.Output is 64 or 65,
                "VgnV3" => Mi(1) && c.UsagePage >= 0xFF00 && Io(20,65),
                "VgnV4" => Mi(Pid == 0x2A04 ? 1 : 3) && Page(0xFF60,0x61) && Io(64,65),
                "Exx" => Page(0xFF60,0x61) && Io(64,65),
                "RogDirect" => (Pid == 0x196B || Mi(1)) && Page(0xFF00,1) && c.Input == 65 && c.Output == 65,
                "RogOmni" => Mi(2) && c.Usage == 1 && c.Input == 64 && c.Output == 64 &&
                    (c.UsagePage == 0xFF00 && (Pid == 0x1AD0 || c.Path.Contains("&col02#",StringComparison.OrdinalIgnoreCase)) ||
                     c.UsagePage == 0xFF02 && (Pid == 0x1AD0 || c.Path.Contains("&col01#",StringComparison.OrdinalIgnoreCase))),
                "Logi" => Mi(2) && c.UsagePage == 0xFF00 &&
                    (c.Usage == 1 && c.Input == 7 && c.Output == 7 || c.Usage == 2 && c.Input == 20 && c.Output == 20),
                "Lofree" => Page(0xFF1C,0x92) && c.Input == 32 && c.Output == 32,
                "Razer" => c.UsagePage >= 0xFF00 && c.Feature == 91,
                "Langtu" => Page(0xFF01,0xFF01) && c.Input == 65 && c.Output == 65,
                "RapooEvent" => Mi(2) && Page(0xFF00,2) && c.Input == 10,
                "Fa09" => Mi(1) && Page(0xFF02,2) && c.Input == 20,
                "Pmo" => Mi(1) && Page(0xFF60,0x61) && c.Input is 33 or 65 && c.Output == c.Input,
                "Ipi" => Mi(2) && Page(0xFF60,0x61) && Io(20,65),
                "Titan" => Mi(1) && (Page(0xFF87,0x20) || Page(0xFF88,0x21) || Page(0xFF00,1)) &&
                    c.Input == 32 && c.Output == 32,
                "Dareu" => Mi(Pid == 0x0211 ? 1 : 3) && Page(0x0C,1) && c.Feature == 65,
                _ => false
            };
        }
        internal bool RevisionAllowed(ushort revision, string product)
        {
            if (Family == "Nova" && Vid == 0x0C45) return revision == 0x0136;
            if (Family is not ("MchoseBy" or "Shared024F")) return true;
            if (Vid == 0x41E4 && Named(product,"MCHOSE 2.4G Wireless"))
                return revision is 0x2008 or 0x3000 or 0x3001;
            if (Vid == 0x3837 && Named(product,"MCHOSE 2.4G Wireless","2.4G Wireless Receiver"))
                return revision == 0x0119;
            if (Family == "MchoseBy" && Vid == 0x05AC && Named(product,"2.4G Wireless Receiver"))
                return revision == 0x2007;
            return true;
        }
        internal void ValidateIds(HidDiscovery.VendorCollectionInfo c, HashSet<byte> input,
            HashSet<byte> output, HashSet<byte> feature, out byte inputId, out byte outputId, out byte featureId)
        {
            inputId = outputId = featureId = 0;
            bool One(HashSet<byte> ids, byte id) => ids.Count == 1 && ids.Contains(id);
            if (Family == "MchoseBy" || Family == "Shared024F" && c.UsagePage is 0xFF02 or 0xFF04)
            {
                Require((input.Contains(19) || output.Contains(19)) &&
                    input.All(id => id is 19 or 20) && output.All(id => id is 19 or 20),
                    "MCHOSE split report map mismatch");
                inputId = outputId = 19;
                return;
            }
            if (Family is "Relay" or "Irok" or "Neon" or "RoyuanLegacy" or "Dareu" or "Razer")
            {
                Require(feature.Count == 1, "Keyboard requires unique Feature report");
                featureId = feature.Single();
                Require(Family == "Relay" || featureId == (Family == "Dareu" && Pid == 0x0211 ? 10 : 0),
                    "Keyboard Feature ID mismatch");
                if (Family is "Irok" or "Neon" or "RoyuanLegacy")
                    Require(input.All(id => id == 0) && output.All(id => id == 0),
                        "ROYUAN has unexpected numbered reports");
                return;
            }
            if (Family == "RapooEvent")
            {
                Require(input.Count == 1 && (input.Contains(0) || input.Contains(0xBD)), "Rapoo report ID mismatch");
                inputId = input.Single(); return;
            }
            byte expected = Family switch { "Cherry" or "Vgn" or "AtkV75" or "Lofree" => 4,
                "Fa09" or "VgnV3" => 19, "Qhw" => 77, "Ipi" => 9, "Titan" => 6,
                "RogOmni" => (byte)(c.UsagePage == 0xFF02 ? 1 : 2),
                "Logi" => (byte)(c.Usage == 1 ? 0x10 : 0x11), _ => 0 };
            if (Family is "VgnV4" or "Exx" or "Pmo")
            {
                Require(input.Count == 1 && output.Count == 1 && (Family == "Pmo" || input.Single() != 0) &&
                    input.SetEquals(output), "VGN V4 report map is ambiguous");
                expected = input.Single();
            }
            Require(One(input, expected) && (Family == "Fa09" || One(output, expected)),
                "Keyboard input/output report ID mismatch");
            if (Family is "AtkHfd" or "Cherry")
                Require(feature.Count == 0, "Unexpected Feature report");
            if (expected == 0) Require(feature.All(id => id == 0), "Unexpected numbered Feature report");
            inputId = outputId = expected;
        }
    }
    internal static Profile Find(ushort vid, ushort pid, string product)
    {
        string family = (vid,pid) switch
        {
            (0x373B,0x111E or 0x111F or 0x114D or 0x114E or 0x1151 or 0x1152 or 0x1231) => "AtkHfd",
            (0x0C45,0xFEFC or 0xFEFE or 0xFEF9) when KeyboardExpansionAulaHub.HasProfile(pid,product) => "AulaHub",
            (0x0C45,0xFEF9) when Named(product,"NUT75 2.4G","NUT75 2.4G WIRELESS") => "Weikav",
            (0x05AC,0x024F) when Named(product,"2.4G Wireless Receiver") => "Shared024F",
            (0x05AC,0x024F) when Named(product,"2.4G Dongle","F98Pro V3 Dongle","LT75PRO-2.4G",
                "VK 75 Wireless Receiver","VK99Pro Wireless Receiver") => "Nova",
            (0x0C45,0xFEFE) when Named(product,"2.4G Dongle") => "Nova",
            (0x3151,0x5026) or (0x374A,0xA263 or 0xA278 or 0xA281) => "Relay",
            (0x3151,0x5038) when !Named(product,"MonsGeek 2.4G Wireless Keyboard") => "Irok",
            (0x385D,0x2A05) when Named(product,"VGN Neon75 Air 2.4G","VGN Neon75 Air 2.4G Dongle") => "Neon",
            (0x3151,0x4015) => "RoyuanLegacy",
            (0x046A,0x01E0 or 0x01DE) => "Cherry",
            (0x41E4,0x2001) when Named(product,"MCHOSE 2.4G Wireless-G87","2.4G Wireless Receiver-G87",
                "MCHOSE G87","MCHOSE 2.4G Wireless","MCHOSE 2.4G Wireless-G75_V2","2.4G Wireless Receiver-G75_V2") => "MchoseBy",
            (0x3837,0x201B) when Named(product,"MCHOSE 2.4G Wireless-G87_V2","2.4G Wireless Receiver-G87_V2",
                "MCHOSE 2.4G Wireless","2.4G Wireless Receiver") => "MchoseBy",
            (0x05AC,0x024F) when Named(product,"MCHOSE 2.4G Wireless-K99","2.4G Wireless Receiver-K99") => "MchoseBy",
            (0x3837,0x3033) when Named(product,"MCHOSE K99 V3 2.4G","MCHOSE G87 V2 2.4G","MCHOSE G75 V2 2.4G") => "Glw",
            (0x3837,0x1015) when Named(product,"MCHOSE G98 V3 2.4G") => "Qhw",
            (0x320F,0x5088) when Named(product,"ATK V75X 2.4G Dongle","VXE V75X 2.4G dongle") => "AtkV75",
            (0x320F,0x5088) when Named(product,"VGN V98 PRO 2.4G Dongle","VGN N75 PRO 2.4G Dongle",
                "VXE V87 2.4G dongle","VGN V98pro V2 2.4G Dongle","VGN VXE75 2.4G Dongle","VGN S99 2.4G Dongle") => "Vgn",
            (0x258A,0x024F or 0x027F) when Named(product,"VGN V98pro V3 2.4G Dongle") => "VgnV3",
            (0x391D,0x2A03) when Named(product,"VGN V98Pro V4 8K 2.4G Dongle") => "VgnV4",
            (0x391D,0x2A04) when Named(product,"VGN V98Pro V4 1K 2.4G Dongle") => "VgnV4",
            (0x391D,0x2A0A) when Named(product,"VGN V98Pro V4 8K TIMI") => "VgnV4",
            (0x372E,0x10EB) or (0x258A,0x02D8) => "Exx",
            (0x0B05,0x1A85 or 0x1C24 or 0x1C25 or 0x196B) => "RogDirect",
            (0x0B05,0x1ACE or 0x1AD0) => "RogOmni",
            (0x046D,0xC52B or 0xC532 or 0xC548 or 0xC547) => "Logi",
            (0x388D,0x0025) => "Lofree",
            (0x1532,0x025C) => "Razer",
            (0x1A2C,0x8FFF) => "Langtu",
            (0x24AE,0x1924) => "RapooEvent",
            (0x3554,0xFA09) => "Fa09",
            (0x36B0,0x3002) => "Pmo",
            (0x372E,0x106C) => "Ipi",
            (0x056D,0xC077) => "Titan",
            (0x260D,0x0037 or 0x0051 or 0x0211) => "Dareu",
            _ => null
        };
        return family == null ? null : new(family,vid,pid,product);
    }
    internal static ReferenceBatteryProtocol.Reading Read(Profile p, KeyboardExpansionWire wire) => p.Family switch
    {
        "AtkHfd" => KeyboardExpansionAtk.Read(wire, p.Pid is 0x111E or 0x114D or 0x1151 or 0x1231),
        "AulaHub" => KeyboardExpansionAulaHub.Read(wire, p.Pid, p.Product, false),
        "Weikav" => KeyboardExpansionAulaHub.Read(wire, p.Pid, p.Product, true),
        "Nova" => KeyboardExpansionNova.Read(wire),
        "Shared024F" => wire.InputId == 19 ? KeyboardExpansionMchoseBy.Read(wire) : KeyboardExpansionNova.Read(wire),
        "Relay" => KeyboardExpansionRoyuan.ReadRelay(wire,p.Vid == 0x374A),
        "Irok" or "Neon" or "RoyuanLegacy" => KeyboardExpansionRoyuan.Read(wire,p.Family),
        "Cherry" => KeyboardExpansionCherry.Read(wire),
        "Glw" => KeyboardExpansionGlw.Read(wire),
        "Qhw" => KeyboardExpansionQhw.Read(wire),
        "Vgn" or "AtkV75" or "VgnV3" or "VgnV4" or "Exx" => KeyboardExpansionVgn.Read(wire,p.Family),
        "RogDirect" => KeyboardExpansionRog.ReadDirect(wire,p.Pid != 0x196B),
        "Lofree" => KeyboardExpansionLofree.Read(wire),
        "Razer" => KeyboardExpansionRazer.Read(wire),
        "Langtu" => KeyboardExpansionLangtu.Read(wire),
        "RapooEvent" => KeyboardExpansionNotifications.Read(wire,true),
        "Fa09" => KeyboardExpansionFa09.Read(wire),
        "Pmo" => KeyboardExpansionPmo.Read(wire),
        "Ipi" => KeyboardExpansionIpi.Read(wire),
        "Titan" => KeyboardExpansionTitan.Read(wire),
        "Dareu" => KeyboardExpansionDareu.Read(wire,p.Pid),
        _ => ReferenceBatteryProtocol.Reading.Failed("No keyboard transaction")
    };
}
