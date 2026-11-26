using System;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadSession(string family,ushort vid,ushort pid,
        string product,MouseExpansionLink link) => family=="rapoo-office"?MouseExpansionRapooOffice(link):
            MouseExpansionTransaction(family,vid,pid,product,link,
            link.Inputs.Count == 1 ? link.InputId : (byte)0,
            link.Outputs.Count == 1 ? link.OutputId : (byte)0,
            link.Features.Count == 1 ? link.FeatureId : (byte)0, link.Output, link.Revision,link.Feature,link.Channel.UsagePage,link.Input);

    internal static ReferenceBatteryProtocol.Reading MouseExpansionTransaction(string family,ushort vid,ushort pid,
        string product,ReferenceBatteryProtocol.ILink link,byte inputId=0,byte outputId=0,byte featureId=0,
        int outputLength=65,ushort revision=0,int featureLength=65,int page=0xFF13,int inputLength=65)
    {
        try
        {
            var result=family switch
            {
                "mchose-g3a" when IsMchoseG3A(vid, pid, product) => ReadMchoseG3ABattery(link, (link as MouseExpansionLink)?.Trace),
                "s9" or "vk-m2" => MouseExpansionS9(family, vid, pid, product, link),
                "aula-x5" => MouseExpansionAula(link,inputId),
                "bragi" => MouseExpansionBragi(link),
                "finalmouse" => MouseExpansionFinal(pid,link,inputId,outputId),
                "logitech-x3" => MouseExpansionLogitech(link),
                "infinity100" => MouseExpansionInfinity100(link,featureId),
                "pulsar" => MouseExpansionPulsar(link),
                "sonix" when IsPulsarSonix(vid, pid) && page == 0xFFFF && featureId == 0 && featureLength == 65 =>
                    ReadPulsarSonix(link, pid),
                "rog8k" when IsRog8kReceiver(vid, pid) && inputId == 3 && outputId == 3 &&
                    inputLength is >= 21 and <= 65 && outputLength is >= 21 and <= 65 =>
                    MouseExpansionRog8k(link, inputLength, outputLength),
                "rawm" => MouseExpansionRawm(pid,link),
                "exx" => MouseExpansionExx(pid,link),
                "get10" => MouseExpansionGet10(vid,pid,revision,link),
                "aj159-pro" => MouseExpansionAj159Pro(link),
                "freq" or "freq-sc311" or "mloong" => MouseExpansionFreqRead(family,product,link,inputId,outputId,outputLength),
                "x23-compx" => MouseExpansionX23(vid,pid,link),
                "infinity97" => MouseExpansionInfinity97(link,inputId,outputId,featureId,featureLength,page),
                "ipi-uni" => MouseExpansionIpiUni(link,inputLength,outputLength),
                "mchose-old" => MouseExpansionOldMchose(link),
                "mchose-l7" => MouseExpansionL7(link),
                _ => ReferenceBatteryProtocol.Reading.Failed(family + ": exact report layout or transaction not established")
            };
            return result??ReferenceBatteryProtocol.Reading.Failed(family+": invalid battery percentage");
        }
        catch (IOException ex) { return ReferenceBatteryProtocol.Reading.Failed(family + ": " + ex.Message); }
    }

    private static ReferenceBatteryProtocol.Reading MouseExpansionRog8k(ReferenceBatteryProtocol.ILink link,
        int inputLength, int outputLength)
    {
        var request = MouseExpansionPacket(outputLength, 3, 0x12, 7);
        ReferenceBatteryProtocol.Reading reading = null;
        for (int pass = 0; pass < 2; pass++)
        {
            var raw = MouseExpansionExchange(link, request, b => b.Length == inputLength &&
                MouseExpansionDecode("rog8k", b) != null, 1200);
            reading = MouseExpansionDecode("rog8k", raw);
            if (reading.Status == ReferenceBatteryProtocol.State.Sleep) return reading;
        }
        return reading;
    }

    private static ReferenceBatteryProtocol.Reading ReadPulsarSonix(ReferenceBatteryProtocol.ILink link, ushort pid)
    {
        int ReadRegister(byte register, int maximum)
        {
            var request = MouseExpansionPacket(65, 0, 0, 8, register, 1);
            int checksum = request[2] + request[3] + request[4];
            request[63] = (byte)checksum;
            request[64] = (byte)(checksum >> 8);
            return MouseExpansionFeature(link, request,
                b => b.Length == 65 && MouseExpansionPrefix(b, 0, 1, 8, register, 1) && b[7] <= maximum,
                attempts: 40, delay: 10)[7];
        }

        int level = -1;
        for (int pass = 0; pass < 2; pass++)
        {
            level = ReadRegister(0x81, 100);
            ReadRegister(0x83, 1);
        }
        return MouseExpansionValue(level, "Sonix device-reported percentage", model: PulsarSonixWiredModel(pid));
    }

    private static byte[] MouseExpansionExchange(ReferenceBatteryProtocol.ILink link,byte[] request,
        Func<byte[],bool> match,int budget=1000,Action beforeWrite=null)
    {
        bool empty = false;
        for(int n=0;n<32;n++)if(link.Read(1) is not {Length:>0}) { empty=true;break; }
        if(!empty)throw new IOException("input queue or short query write");
        beforeWrite?.Invoke();
        if(!link.Write(request,300))throw new IOException("input queue or short query write");
        long deadline=link.Now+budget;
        for(int n=0;n<96&&link.Now<deadline;n++)
        {
            var raw=link.Read((int)Math.Min(50,deadline-link.Now));
            if(raw is not {Length:>0} || raw.AsSpan().SequenceEqual(request) ||
                request[0]==0&&raw.Length==request.Length-1&&raw.AsSpan().SequenceEqual(request.AsSpan(1)))continue;
            if(match(raw))return raw;
        }
        throw new MouseExpansionTimeout("no fresh matching response");
    }

    private sealed class MouseExpansionTimeout(string message) : IOException(message);

    private static byte[] MouseExpansionFeature(ReferenceBatteryProtocol.ILink link,byte[] request,
        Func<byte[],bool> match,int attempts=4,int delay=60)
    {
        if(!link.SetFeature(request))throw new IOException("feature query failed");
        for(int n=0;n<attempts;n++)
        {
            link.Delay(delay);
            var raw=link.GetFeature(request[0],request.Length);
            if(raw!=null&&!raw.AsSpan().SequenceEqual(request)&&match(raw))return raw;
        }
        throw new IOException("no fresh matching feature response");
    }

    private static string MouseExpansionFreq(string name)
    {
        string x23Scheme = SelectEHubX23Scheme(name?.Trim(' ', '\t', '\r', '\n'));
        if (x23Scheme != null) return x23Scheme;

        string[] a={"EWEADN A6Pro","EWEADN A6Max","EWEADN A7SE","EWEADN A7","EWEADN A7Pro",
            "EWEADN S7","EWEADN S7SE","EWEADN G5"};
        string[] c={"EWEADN S9SE","EWEADN E7","EWEADN E7 PRO","E9Carbon","E9ProCarbon",
            "EWEADN E9","EWEADN E11","EWEADN E11 PRO","EWEADN A6","EWEADN G2SE A","S7 Plus","EWEADN S2"};
        foreach(string value in a)if(MouseExpansionName(name,value))return "a";
        foreach(string value in c)if(MouseExpansionName(name,value))return "c";
        return MouseExpansionName(name,"LTM8 2.4G")?"m8":null;
    }
    private static string MouseExpansionMloong(string value)
    {
        if(value==null)return null;
        string name=value.ToUpperInvariant(),model=null;
        foreach(string key in new[]{"MX300","MX301","MX302","MX306"})
            if(name.Contains(key)){if(model!=null)return null;model=key;}
        if(model==null)return null;
        if(model!="MX301")return name.Contains("PRO")||name.Contains("V2")||name.Contains("LITE")?null:model;
        if(name.Contains("MX301P2"))return "MX301 PRO V2";
        if(name.Contains("V2"))return null;
        if(name.Contains("MX301P-G"))return "MX301 PRO Gold";
        if(name.Contains("PRO"))return name.Contains("LITE")?"MX301 PRO Lite":"MX301 PRO";
        return name.Contains("LITE")?null:"MX301";
    }
}
