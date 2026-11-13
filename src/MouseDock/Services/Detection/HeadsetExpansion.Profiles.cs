using System;
using System.Linq;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionProfiles
{
    internal static string Find(ushort vid, ushort pid, string product) => (vid, pid) switch
    {
        (0x373B, 0x1262 or 0x12D6) => "AtkN9V2",
        (0x3837, 0x600A) => "Bolutek",
        (0x3837, 0x6011 or 0x6013) => "Mhub",
        (0x291D, 0x385D) when Name(product) == "MCHOSE V9 PRO" => "V9Pro",
        (0x372E, 0x10BB) => "Zhou",
        (0x35BB, 0xA00B or 0xA220) => "Hecate",
        (0x35BB, 0xA217) => "HecateTws",
        (0x03F0, 0x05B7 or 0x0C9D or 0x0FCC or 0x069F or 0x0995) => "Cloud3",
        (0x03F0, 0x018B) => "Cloud2",
        (0x03F0, 0x08BE) => "Alpha2",
        (0x03F0, 0x02CC or 0x06BE) => "Cloud3S",
        (0x0B05, 0x1AFA or 0x1B84) => "Rog",
        (0x35D8, 0x012D or 0x0133) => "Moondrop",
        (0x36F9, 0xF001 or 0xF008 or 0xF010) => "Xiberia",
        (0x040B, 0xF779) or (0x391D, 0x0003) => "Siren",
        (0x3654, 0x4255) => "Dailing",
        (0x0D8C, 0x0334) => "IkfLegacy",
        (0x38A6, 0x0030) => "AulaG9",
        (0x0ECB, 0x2088) => "Jbl",
        (0x1395, 0x0303) => "Epos",
        (0x38B1, 0x3412 or 0x3411 or 0x3418 or 0x3419) => "Ikf",
        (0x1532, 0x0528) => "Razer2020",
        (0x1532, 0x0557) => "RazerControl",
        (0x1532, 0x0555 or 0x0556) => "BlacksharkPa",
        (0x1532, 0x053A) => "BarracudaPa",
        (0x1532, 0x0577 or 0x101B) => "RazerV3Pro",
        (0x1532, 0x057A) => "RazerV3Passive",
        (0x054C, 0x0EBF) => "SonyH5",
        (0x054C, 0x0E53) => "SonyH9Serial",
        (0x373B, 0x11CB or 0x1204) => "AtkN9Serial",
        _ => null
    };

    internal static bool Matches(string family, HidDiscovery.VendorCollectionInfo c)
    {
        if (c == null || Find(c.Vid, c.Pid, c.Product) != family) return false;
        bool io64 = c.Input == 64 && c.Output == 64;
        return family switch
        {
            "AtkN9V2" => Mi(c, 0) && c.UsagePage is 0xFF90 or 0xFF82 && c.Usage == 1 && io64,
            "Bolutek" => Mi(c, 0) && c.UsagePage is 0xFF21 or 0xFF22 or 0xFF82 or 0xFF90 && c.Usage == 1 && io64,
            "V9Pro" => Mi(c, 0) && c.UsagePage == 0xFF90 && c.Usage == 1 && io64,
            "Mhub" => c.UsagePage >= 0xFF00 && io64,
            "Zhou" => Mi(c, 3) && io64,
            "Hecate" or "HecateTws" => Col(c, 3) && c.UsagePage == 0xFF02 && c.Usage == 2 &&
                (c.Pid == 0xA00B ? Mi(c, 3) : c.Pid != 0xA217 || Mi(c, 4)) &&
                c.Input >= (family == "HecateTws" ? 11 : 8) && c.Input <= 64 && c.Output is >= 5 and <= 64,
            "Cloud3" => c.UsagePage == 0xFF13 && c.Usage == 1 && c.Input is >= 62 and <= 64 &&
                (c.Output is >= 62 and <= 64 || c.Feature is >= 62 and <= 64) &&
                (c.Pid is not (0x0FCC or 0x069F or 0x0995) || Mi(c, 3)),
            "Cloud2" => c.UsagePage == 0xFF90 && c.Usage == 0x303 && c.Input == 20 && c.Output == 52,
            "Alpha2" => c.UsagePage == 0xFF13 && c.Usage == 0xFF00 && io64,
            "Cloud3S" => Mi(c, 3) && (c.UsagePage, c.Usage) is (0xFF13, 1) or (0xFFC0, 0x202) or
                (0x1C0, 1) or (0x170F, 0x202) && (c.Input is >= 7 and <= 64 || c.Output == 64),
            "Rog" => c.UsagePage == 0xFF00 && c.Output == 64 && c.Input is >= 9 and <= 64 &&
                (c.Pid != 0x1AFA || Mi(c, 3) && c.Usage == 1 && io64),
            "Moondrop" => c.Input is >= 10 and <= 1024 && c.Output is >= 4 and <= 1024,
            "Xiberia" => io64 && c.Feature == 0 && (c.Pid == 0xF010 ? c.UsagePage >= 0xFF00 :
                Mi(c, 0) && c.UsagePage is 0xFF90 or 0xFF82 && c.Usage == 1),
            "Siren" => Mi(c, 3) && (io64 || c.Vid == 0x040B && c.Input == 13 && c.Output is 64 or 65),
            "Dailing" => Mi(c, 3) && c.UsagePage == 0xFF02 && c.Usage == 2 &&
                c.Input is 64 or 65 && c.Input == c.Output,
            "IkfLegacy" => Mi(c, 3) && (c.UsagePage, c.Usage) is (12, 1) or (0xFF02, 2) && io64,
            "AulaG9" => c.UsagePage == 0xFFAB && c.Input is >= 8 and <= 64,
            "Jbl" => c.UsagePage == 0xFF13 && c.Usage == 1 && c.Input is >= 2 and <= 64,
            "Epos" => Mi(c, 3) && c.UsagePage == 0xFFFF && c.Usage == 1 &&
                c.Input is >= 19 and <= 1024 && c.Output is >= 17 and <= 1024,
            "Ikf" => c.UsagePage == 0xFF00 && c.Usage == 1 && c.Input == 64 &&
                (c.Pid == 0x3412 ? c.Feature == 64 : c.Output == 64) && c.Feature is 0 or 64,
            "Razer2020" => Mi(c, 3) && c.UsagePage == 0xFF00 && c.Usage == 1 && io64 && c.Feature == 64,
            "RazerControl" => c.UsagePage == 0xFF00 && c.Feature == 91,
            "BlacksharkPa" => c.UsagePage is 0xFF00 or 0xFF14 && io64,
            "BarracudaPa" => c.UsagePage == 0xFF00 && io64,
            "RazerV3Pro" => c.UsagePage == 0xFF14 && c.Usage == 1 && io64 && c.Feature == 0 &&
                (c.Pid != 0x101B || Mi(c, 5)),
            "RazerV3Passive" => Mi(c, 5) && c.UsagePage == 0xFF14 && c.Usage == 1 && io64,
            "SonyH5" => Mi(c, 5) && c.UsagePage == 0xFF04 && c.Usage == 1 && io64,
            "SonyH9Serial" or "AtkN9Serial" => Mi(c, family == "SonyH9Serial" ? 3 : 4) &&
                c.UsagePage == 12 && c.Usage == 1 && c.Input is > 0 and <= 1024,
            _ => false
        };
    }

    internal static void Validate(string family, HidDiscovery.VendorCollectionInfo c, ExpansionHidMetadata m)
    {
        void Pair(byte id, int input, int output) =>
            Require(m.HasInput(id, input) && m.HasOutput(id, output), "Required unique input/output report not proven");
        switch (family)
        {
            case "V9Pro":
                Require(m.Revision is 3 or 0x12 or 0x13, "Unapproved V9 Pro revision"); Pair(0x55, 64, 64); break;
            case "AtkN9V2":
            case "Bolutek":
                bool inputOnly = c.UsagePage == 0xFF90 && m.OutputIds.Count == 0 &&
                    (family == "AtkN9V2" ? c.Pid == 0x1262 && m.Revision == 0x12 : m.Revision == 0x21);
                Require(m.HasInput(0x55, 64) && (m.HasOutput(0x55, 64) || inputOnly), "Battery report55 not proven"); break;
            case "Mhub": case "Zhou": case "IkfLegacy": Pair(0x55, 64, 64); break;
            case "Dailing":
                if (c.Input == 65 && m.Revision == 0x100 && c.Feature == 0 &&
                    m.HasInput(0, 64) && m.HasOutput(0, 64))
                    m.UnnumberedInput = true;
                else Pair(0x55, c.Input, c.Output);
                break;
            case "Hecate": case "HecateTws": Pair(0xD0, c.Input, c.Output); break;
            case "Cloud3":
                Require(m.HasInput(0x66, c.Input) &&
                    (c.Output is >= 62 and <= 64 ? m.HasOutput(0x66, c.Output) : m.HasFeature(0x66, c.Feature)),
                    "HyperX report66 transport not proven");
                if (c.Pid is 0x069F or 0x0995 && c.Feature is >= 62 and <= 64)
                    Require(m.HasFeature(0x66, c.Feature), "Core Feature66 mismatch");
                break;
            case "Cloud2": Pair(6, 20, 52); break;
            case "Alpha2": Require(m.HasInput(0x51, 64) && m.HasOutput(0x50, 64), "Alpha2 report51/50 missing"); break;
            case "Cloud3S": Require(m.InputSizes.TryGetValue(12, out int rx) && rx is >= 7 and <= 64 ||
                m.HasOutput(12, 64), "Cloud III S report0C missing"); break;
            case "Rog": Pair(0xCC, c.Input, 64); break;
            case "Moondrop": Pair(0x4B, c.Input, c.Output); break;
            case "Xiberia":
                byte report = c.Pid != 0xF010 && c.UsagePage == 0xFF82 ? (byte)0x41 : (byte)0x55;
                Require(m.HasInput(report, 64) && (m.HasOutput(report, 64) ||
                    report == 0x55 && c.Pid == 0xF001 && m.OutputIds.Count == 0), "Xiberia report layout invalid");
                Require(m.FeatureIds.Count == 0, "Xiberia unexpected Feature report"); break;
            case "Siren": Pair(2, c.Input, c.Output); break;
            case "Jbl": Require(m.HasInput(8, c.Input), "JBL Input8 missing"); break;
            case "AulaG9": Require(m.InputIds.Count == 1 && m.InputIds.Contains(0xAB), "AULA input AB missing"); break;
            case "Epos": Pair(1, c.Input, c.Output); break;
            case "Ikf":
                Require(m.HasInput(3, 64) && (c.Pid == 0x3412 ? m.HasFeature(3, 64) : m.HasOutput(2, 64)) &&
                    (c.Feature == 0 || m.HasFeature(3, 64)), "iKF report transport mismatch"); break;
            case "Razer2020": Require(m.Revision == 0x100 && m.HasFeature(255, 64), "BlackShark 2020 Feature FF mismatch"); break;
            case "RazerControl": Require(m.HasFeature(0, 90), "Razer Feature0/90 missing"); break;
            case "BlacksharkPa": case "RazerV3Pro": case "RazerV3Passive": case "SonyH5": Pair(2, 64, 64); break;
            case "BarracudaPa": Pair(1, 64, 64); break;
            case "SonyH9Serial": case "AtkN9Serial":
                Require(m.InputIds.Count > 0, "Serial anchor input descriptor unavailable"); break;
            default: Require(false, "Unsupported headset metadata family"); break;
        }
    }
}
