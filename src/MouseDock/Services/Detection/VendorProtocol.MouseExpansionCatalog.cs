using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static bool MouseExpansionPid(ushort pid, params ushort[] ids) => Array.IndexOf(ids, pid) >= 0;
    private static readonly HashSet<ushort> MouseExpansionWl = new()
    { 0xA867,0xA866,0xA886,0xA885,0xA869,0xA868,0xA881,0xA880,0xA884,0xA883,0xA871,0xA870,
      0xA861,0xA860,0xA857,0xA856,0xA882,0xA864,0xA863,0xA859,0xA873,0xA872,0xA875,0xA874,0xA879,0xA878 };
    private static readonly HashSet<ushort> MouseExpansionRazer = new()
    {0x0062,0x006F,0x0072,0x0077,0x007A,0x007B,0x007D,0x0083,0x0088,0x008F,0x0090,0x0094,
     0x009A,0x009C,0x009E,0x009F,0x00A5,0x00A6,0x00A7,0x00A8,0x00AA,0x00AB,0x00AF,0x00B0,
     0x00B3,0x00B4,0x00B6,0x00B7,0x00B8,0x00B9,0x00BE,0x00BF,0x00C0,0x00C1,0x00C2,0x00C3,
     0x00C5,0x00C8,0x00CC,0x00CD,0x00D1,0x00D4,0x00D7,0x00DF,0x00E5,0x00E6,0x0070,0x0073,
     0x007C,0x0080,0x0086,0x00C4,0x00C7,0x00D0,0x00D3,0x00D6,0x00DA,0x00DB,0x00DE,0x00A4};
    private static string MouseExpansionFamily(ushort vid, ushort pid, string product)
    {
        if (vid == 0x373B && pid == 0x1196) return "hechi";
        if (vid == 0x363C && pid == 0xFF99) return "ausdom";
        if (vid == 0x32C2 && pid == 0x0027) return "sc525";
        if (vid == 0x395A && MouseExpansionPid(pid, 4, 10, 12, 14)) return "scyrox";
        if (vid == 0x3886 && MouseExpansionPid(pid, 0xCA02,0xCA03,0xCA04,0xCBFE,0xCBFF)) return "zynoo";
        if (vid == 0x1915 && pid == 0x0728) return "waizowl";
        if (vid == 0x1915 && MouseExpansionPid(pid,0x232A,0x232B,0x2335)) return "rawm";
        if (vid == 0x1915 && MouseExpansionPid(pid,0xAE8C,0xAE1C,0xAE11)) return "sora-v2";
        if (vid == 0x093A && MouseExpansionPid(pid,0xEB02,0xE010)) return "sora-v3";
        if (vid == 0x093A && MouseExpansionPid(pid,0x522C,0x622C)) return "incott";
        if (vid == 0x046D && pid == 0xC54F) return "logitech-x3";
        if (vid == 0x36A7 && MouseExpansionWl.Contains(pid) || vid == 0x373E && pid == 0x65) return "wl83";
        if (vid == 0x36A7 && MouseExpansionPid(pid,0xA887,0xA888)) return "cube";
        if (vid == 0x373E && MouseExpansionPid(pid,0x6A,0x6B)) return "ko83";
        if (vid == 0x373E && MouseExpansionPid(pid,0x32,0x33,0x54,0x84)) return "luna83";
        if (vid == 0x373E && MouseExpansionPid(pid,0x47,0x22,0x3B)) return "feature83";
        if (vid == 0x37B0 && MouseExpansionPid(pid,0x15,0x48,9,0xF,0x10,0x23,0x2E,0x32) ||
            vid == 0x373E && MouseExpansionPid(pid,0x1C,0x1D,0x1E)) return "lamzu83";
        if (vid == 0x372E && pid == 0x1014) return "ipi";
        if (vid == 0x38EE && MouseExpansionPid(pid,0xC,0xF,0x11,0x13,0x24)) return "akko";
        if (vid == 0x33E4 && pid == 0x5317) return "gwolves-old";
        if (vid == 0x33E4 && MouseExpansionPid(pid,0x3808,0x3817,0x6808,0x6817,0x5608,0x5617,0x3608,0x3617,
            0x5418,0x3854,0x4718,0x5618,0x4719,0x5619,0x5419,0x3619,0x2719,0x4219,0x5219,0x3519)) return "gwolves";
        if (vid == 0x33E4 && MouseExpansionPid(pid,0x5408,0x5417,0x5404,0x5403)) return "hts";
        if (vid == 0x3770 && MouseExpansionPid(pid,0x100,0x200)) return "hitscan";
        if (vid == 0x373B && MouseExpansionPid(pid,0x10C9,0x12E9,0x101B,0x1303)) return "nk";
        if (vid == 0x373B && MouseExpansionPid(pid,0x1154,0x1155,0x124F,0x12CE,0x1312)) return "nordic54";
        if (vid == 0x373B && MouseExpansionPid(pid,0x1031,0x102E,0x11D9,0x11B6) ||
            vid == 0x3554 && MouseExpansionPid(pid,0xF502,0xF503,0xF58A,0xF58C,0xF58E,0xF58F,0xF5F6,0xF5F7,
                0xFB3D,0xFB3E,0xFB5E,0xFB5F,0xF50D)) return "compx";
        if (vid == 0x3710 && MouseExpansionPid(pid,0x5402,0x5403)) return "sonix";
        if (vid == 0x3710 && MouseExpansionPulsarPids.Contains(pid)) return "pulsar";
        if (vid == 0x3554 && MouseExpansionPid(pid,0xF507,0xF508) ||
            vid == 0x25A7 && MouseExpansionPid(pid,0xFA7B,0xFA7C)) return "compx";
        if (vid == 0x361D && MouseExpansionPid(pid,0x100,0x300,0x301,0x310,0x311)) return "finalmouse";
        if (vid == 0x0B05 && pid == 0x1AD0) return "rog8k";
        if (vid == 0x0B05 && MouseExpansionPid(pid,0x1A72,0x1A70,0x1B0C,0x1B0A,0x197F,0x197D,0x1A1A,0x1A18,
            0x1A94,0x1A92,0x1A68,0x1A66,0x1979,0x1977,0x19F4,0x1A8D,0x1AF5,0x1AF3,0x1C57,0x1C56)) return "rog-standard";
        if (vid == 0x3151 && pid == 0x5007) return "infinity100";
        if (vid == 0x0E8D && pid == 0x0703) return "infinity97";
        if (vid == 0x1532 && pid == 0x00F0) return "razer-carbon";
        if (vid == 0x1532 && MouseExpansionRazer.Contains(pid)) return "razer";
        if (vid == 0x391D && MouseExpansionPid(pid,0x102E,0x1A2A,0x1037,0x1A29)) return "vgn-ld";
        if (vid == 0x391D && MouseExpansionPid(pid,0x1A05,0x102D)) return "vgn-master";
        if (vid == 0x391D && pid == 0x1A01) return "vgn-se";
        if (vid == 0x3837 && MouseExpansionPid(pid,0x1014,0x1018,0x101C,0x4026,0x4028,0x4033,0x4034,0x4037,0x4038,0x4040)) return "mchose4d";
        if (vid == 0x3837 && pid==0x100C) return "mchose-old";
        if (vid == 0x5253 && pid==0x1020 && MouseExpansionName(product,"MCHOSE L7 Pro"))
            return "mchose-l7";
        if (vid == 0x24AE && MouseExpansionPid(pid,0x1870,0x1871,0x1872,0x1874,0x1876,0x186C)) return "rapoo-office";
        if (vid == 0x24AE && MouseExpansionPid(pid,0x1423,0x1413)) return "rapoo-short";
        if (vid == 0x24AE && pid == 0x14A5) return "rapoo-active";
        if (vid == 0x24AE && MouseExpansionPid(pid,0x1470,0x1412,0x14A2,0x14A7,0x1B0D,0x14A1,0x185A)) return "rapoo";
        if (vid == 0x3434 && MouseExpansionPid(pid,0xD028,0xD048)) return "keychron";
        if (vid == 0x1E71 && MouseExpansionPid(pid,0x2101,0x2129)) return "nzxt";
        if (vid == 0x320F && pid == 0x706E) return "hecate";
        if (vid == 0x373B && pid == 0x1058) return "aula-v9";
        if (vid == 0x320F && pid == 0x228A) return "delux";
        if (vid == 0x1D57 && MouseExpansionPid(pid,0xFA60,0xFA65)) return "delux-pro";
        if (vid == 0x36F9 && pid == 0xAA20) return "x7";
        if (vid == 0x38A6 && MouseExpansionPid(pid,0x28F9,0x2F1A,0x28FD,0x28E9)) return "aula-x5";
        if (vid == 0x1B1C && pid == 0x1B62) return "bragi";
        if (vid == 0x372E && pid == 0x10F9) return "ipi-uni";
        if (vid == 0x372E && (pid == 0x1029 && MouseExpansionName(product,"EWEADN S9 Pro") ||
            pid == 0x1026 && MouseExpansionName(product,"EWEADN S9") ||
            pid == 0x103D && MouseExpansionName(product,"Gaming 2.4G"))) return "s9";
        if (vid == 0x374A && MouseExpansionPid(pid,0xA304,0xA305,0xA307,0xA308)) return "vk-m2";
        if (vid == 0x374A && pid == 0xA329) return "vk-m5";
        if (vid == 0x3151 && pid == 0x5045) return "royuan-mouse";
        if (vid == 0x3151 && pid == 0x4027) return "aj159-pro";
        if ((vid is 0x248A or 0x249A && pid == 0x5C2F ||
            vid == 0x248A && MouseExpansionPid(pid,0x5C2E,0x5D2E,0x5E2E)) &&
            !MouseExpansionForeignReceiver(product)) return "get10";
        if (vid == 0x1209 && MouseExpansionPid(pid,0x3A87,0x3A88)) return "exx";
        if (vid == 0x04F3 && pid == 0x026F) return "x23-pro";
        if (vid == 0x35BB && pid == 0xD3FB || vid == 0x3554 &&
            MouseExpansionPid(pid,0xF5A0,0xF59F,0xF5B2,0xF5B3,0xF5B5,0xFB35,0xFB36,0xF515,0xFB45,0xFB47,0xFB48))
            return "x23-compx";
        if (vid is 0xA8A4 or 0xA8A5 && pid == 0x2255 && MouseExpansionFreq(product) != null) return "freq";
        if (vid == 0xA8A5 && pid == 0x2255 && MouseExpansionMloong(product) != null) return "mloong";
        if (vid == 0xA8A5 && pid == 0x2255) return "freq-sc311";
        return null;
    }
    private static bool MouseExpansionName(string actual, string expected) =>
        string.Equals(actual?.Trim(' ','\t','\r','\n'), expected, StringComparison.OrdinalIgnoreCase);

    private static bool MouseExpansionForeignReceiver(string product) =>
        product?.Contains("AJ159", StringComparison.OrdinalIgnoreCase) == true ||
        product?.Contains("DELUX", StringComparison.OrdinalIgnoreCase) == true ||
        product?.Contains("多彩", StringComparison.Ordinal) == true;

    private static bool MouseExpansionChannel(string family, HidDiscovery.VendorCollectionInfo c)
    {
        bool At(int mi, int page, int usage) => MouseExpansionMi(c.Path, mi) && c.UsagePage == page && c.Usage == usage;
        return family switch
        {
            "hechi" => At(2,0xFFA0,2), "ausdom" => At(1,0xFFA0,2), "sc525" => At(2,0xFF0F,1),
            "scyrox" => At(2,0xFF00,2), "zynoo" => At(-1,0xFF80,1), "waizowl" => At(3,0xFF0A,1),
            "sora-v3" => c.Usage == 1 && c.UsagePage is 0xFF00 or 0xFF01 or 0xFF02,
            "sora-v2" => At(c.Pid == 0xAE8C ? 1 : -1,0xFFA0,1),
            "dareu" => At(1,12,1), "logitech-x3" => At(2,0xFF43,0x302),
            "cube" or "akko" => At(-1,0xFF1C,0x92),
            "lamzu83" => At(1,0xFFA0,1) || At(1,0xFFFF,1) || At(2,0xFFFF,0),
            "gwolves-old" => At(2,0xFFFF,0),
            "nk" => MouseExpansionMi(c.Path,1) && c.Usage == 2 && c.UsagePage is 0xFF02 or 0xFF04,
            "nordic54" or "vaxee" => At(-1,0xFF05,1),
            "compx" or "hts" or "hitscan" or "pulsar" => At(family is "pulsar" or "hitscan" ? 1 : -1,0xFF02,2),
            "x23-compx" => c.Vid==0x35BB ? MouseExpansionMi(c.Path,1)&&c.Usage==2&&c.UsagePage is 0xFF02 or 0xFF04 : c.UsagePage>=0xFF00,
            "sonix" => At(3,0xFFFF,1), "vgn-ld" => At(-1,0xFFA0,2),
            "vgn-master" => At(1,0xFF02,2),
            "vgn-se" or "freq" or "freq-sc311" or "mloong" => At(family is "vgn-se" or "freq-sc311" ? 2 : -1,0xFF01,0x10),
            "rog8k" => At(2,0xFF01,1), "rog-standard" => MouseExpansionMi(c.Path,0) && c.UsagePage >= 0xFF00,
            "infinity100" or "aj159-pro" => At(2,0xFFFF,2),
            "infinity97" => MouseExpansionMi(c.Path,1) && c.Usage == 1 && c.UsagePage is 0xFF13 or 0xFF14,
            "steel" => At(3,0xFFC0,1), "incott" or "sc900" => At(-1,0xFF05,1),
            "rapoo" or "rapoo-short" or "rapoo-active" => At(1,0xFF00,c.Usage) && c.Usage is 2 or 0x14,
            "rapoo-office" => At(1,0xFF00,0xE),
            "keychron" => MouseExpansionMi(c.Path,4), "nzxt" => At(-1,0xFFCA,1),
            "hecate" or "delux" => At(1,0xFF1C,0x92), "aula-v9" => At(1,0xFF60,0x61),
            "delux-pro" => At(2,0xA,0), "x7" => At(3,0xFF0A,1),
            "aula-x5" => At(-1,0xFFA8,2), "bragi" => At(1,0xFF42,1),
            "ipi-uni" => At(3,0xFF04,1), "vk-m2" => c.Usage == 1 && c.UsagePage is 0xFF00 or 0xFF06,
            "vk-m5" => At(2,0xFFFF,2), "royuan-mouse" => At(-1,0xFFFF,2),
            "get10" => MouseExpansionMi(c.Path,2) && c.Usage == 0 && c.UsagePage is 1 or 0xFFEF,
            "exx" => At(2,0xFF01,0x10), "x23-pro" => At(1,0xA,0),
            "mchose4d" => At(-1,0xFF01,1) || At(-1,0xFF0B,0xC4),
            "mchose-old" => At(2,0xFF01,1),
            "mchose-l7" => At(-1,0xFF01,1),
            "mchose-a5" => c.UsagePage == 0xFFFF && (c.Pid != 0xF024 || MouseExpansionMi(c.Path,2) && c.Usage == 0),
            "razer-carbon" => At(3,12,1),
            "razer" => c.Pid is 0xE5 or 0xE6 ? At(3,12,1) :
                MouseExpansionMi(c.Path,0) && (c.UsagePage >= 0xFF00 || c.UsagePage == 1 && c.Usage == 2),
            _ => c.UsagePage >= 0xFF00
        };
    }
    internal sealed record MouseExpansionLayoutInfo(HidDiscovery.VendorCollectionInfo Channel,
        HashSet<byte> Inputs,HashSet<byte> Outputs,HashSet<byte> Features,ushort Revision)
    {
        internal int Input=>Channel.Input;
        internal int Output=>Channel.Output;
        internal int Feature=>Channel.Feature;
        internal byte InputId=>Inputs.Single();
        internal byte OutputId=>Outputs.Single();
        internal byte FeatureId=>Features.Single();
    }
    private static bool MouseExpansionLayout(string family,MouseExpansionLink link) =>
        MouseExpansionLayout(family,new MouseExpansionLayoutInfo(link.Channel,link.Inputs,link.Outputs,link.Features,link.Revision));

    internal static bool MouseExpansionLayout(string family, MouseExpansionLayoutInfo l)
    {
        bool Io(int size, byte? report = null) => l.Input == size && l.Output == size &&
            l.Inputs.Count == 1 && l.Outputs.Count == 1 &&
            (report == null ? l.InputId == l.OutputId : l.InputId == report && l.OutputId == report);
        bool F(int size, byte report) => l.Feature == size && l.Features.SetEquals(new[] { report });
        return family switch
        {
            "hechi" or "ausdom" => Io(64,11),
            "sc525" => l.Input == 32 && l.Output == 16 && l.Inputs.SetEquals(new byte[]{14}) && l.Outputs.SetEquals(new byte[]{14}),
            "scyrox" => Io(64) && l.InputId != 0, "zynoo" => Io(17,0),
            "nk" => Io(17,8),
            "ipi-uni" => l.Input is >=15 and <=65 && l.Output is >=15 and <=65 &&
                l.Inputs.SetEquals(new byte[]{8})&&l.Outputs.SetEquals(new byte[]{8}),
            "waizowl" => l.Revision == 0x206 && Io(65,0), "sora-v3" => F(16,6),
            "sora-v2" => F(32,5), "dareu" => F(65,10),
            "feature83" or "lamzu83" or "luna83" or "ko83" or "wl83" or "gwolves" or "gwolves-old" or
            "sonix" or "mchose-a5" => F(65,0),
            "ipi" or "s9" or "vk-m2" => F(64,3),
            "compx" or "hts" or "hitscan" or "pulsar" or "x23-compx" or "vgn-master" => Io(17,8),
            "nordic54" => Io(64,8), "akko" or "cube" or "hecate" or "delux" or "aula-v9" => Io(64,4),
            "steel" or "rog-standard" or "vgn-se" or "x7" or "rawm" => Io(65,0),
            "exx" => Io(65,0) && l.Feature==0,
            "freq" or "mloong" => Io(65,0) || Io(64,0xF0),
            "freq-sc311" => l.Revision==0x273 && Io(65,0),
            "vgn-ld" => l.Output is 33 or 64 && l.Input is >=4 and <=65 && l.Outputs.SetEquals(new byte[]{0xA0}) && l.Inputs.Count > 0 && !l.Inputs.Contains(0),
            "rog8k" => l.Input is >=21 and <=65 && l.Output is >=21 and <=65 &&
                l.Inputs.SetEquals(new byte[]{3}) && l.Outputs.SetEquals(new byte[]{3}),
            "vaxee" => F(64,14), "infinity100" or "aj159-pro" or "royuan-mouse" => F(65,0),
            "infinity97" => l.Channel.UsagePage==0xFF13&&F(62,20) ||
                l.Channel.UsagePage==0xFF14&&(l.Feature is >=62 and <=65&&l.Features.SetEquals(new byte[]{20}) ||
                    l.Feature==0&&l.Input==64&&l.Output==64&&l.Inputs.SetEquals(new byte[]{7})&&l.Outputs.SetEquals(new byte[]{6})),
            "vk-m5" => l.Feature is >=10 and <=65 && l.Features.Count == 1,
            "keychron" => l.Input == 64 && F(64,0xB3) && l.Inputs.SetEquals(new byte[]{0xB4}),
            "nzxt" => Io(64,0x4E), "aula-x5" => l.Input == 64 && l.Output == 64 &&
                l.Inputs.Count == 1 && l.InputId is 10 or 11 && l.Outputs.SetEquals(new byte[]{11}),
            "bragi" => Io(65,0), "mchose4d" => Io(64,0x4D),
            "mchose-old" => l.Revision==0x1437&&F(21,0x11),
            "mchose-l7" => l.Input is 16 or 64&&l.Inputs.SetEquals(new byte[]{0x13}),
            "incott" or "sc900" => l.Input is 8 or 33 && l.Inputs.SetEquals(new byte[]{9}),
            "rapoo-short" => l.Input == 13 && l.Inputs.SetEquals(new byte[]{7}),
            "rapoo" => l.Input is 7 or 19 or 20 && l.Inputs.Count == 1,
            "rapoo-active" => l.Channel.Usage==2&&l.Input==19&&l.Inputs.SetEquals(new byte[]{7})&&l.Output==0&&l.Feature==0 ||
                l.Channel.Usage==14&&l.Output==33&&l.Outputs.SetEquals(new byte[]{6})&&l.Input==0&&l.Feature==0,
            "rapoo-office" => l.Output is >=3 and <=65 && l.Outputs.SetEquals(new byte[]{0xBA}),
            "delux-pro" => l.Input is >=5 and <=64 && l.Inputs.SetEquals(new byte[]{3}),
            "x23-pro" => l.Input == 32 && l.Inputs.SetEquals(new byte[]{4}),
            "get10" => Io(33,0) && l.Feature == 0,
            "razer" or "razer-carbon" => F(91,0),
            "logitech-x3" => Io(20,0x11),
            "finalmouse" => l.Channel.UsagePage == 0xFF00 && l.Input == 64 && l.Output == 64 &&
                l.Inputs.SetEquals(new byte[]{(byte)(l.Channel.Pid==0x100?5:2)}) &&
                l.Outputs.SetEquals(new byte[]{(byte)(l.Channel.Pid==0x100?4:1)}),
            _ => false
        };
    }
    private static readonly HashSet<ushort> MouseExpansionPulsarPids = new()
    {21510,21762,21761,21764,21511,22017,22019,13332,13333,29953,29954,29955,29956,29957,29958,
     13574,13575,13576,13577,13584,13586,13587,13588,13589,13590,13591,13592,13593,13604,13605,
     13606,13607,13608,13609,13825,13827,13828,13830,13831,13832,13833,13840,13841,38146,38147,
     38401,29960,29961,29968,29959,30209,30210,30211,30212,30213};
}
