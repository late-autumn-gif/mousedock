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
                "s9" or "vk-m2" => MouseExpansionS9(family,pid,link),
                "aula-x5" => MouseExpansionAula(link,inputId),
                "bragi" => MouseExpansionBragi(link),
                "finalmouse" => MouseExpansionFinal(pid,link,inputId,outputId),
                "logitech-x3" => MouseExpansionLogitech(link),
                "infinity100" => MouseExpansionInfinity100(link,featureId),
                "pulsar" => MouseExpansionPulsar(link),
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
        string[] a={"EWEADN X23","EWEADN X23Max","EWEADN X23L carbon","EWEADN A6Pro","EWEADN A6Max",
            "EWEADN A7SE","EWEADN A7","EWEADN A7Pro","EWEADN S7","EWEADN S7SE","EWEADN G5"};
        string[] c={"EWEADN X23Pro","EWEADN S9SE","EWEADN E7","EWEADN E7 PRO","E9Carbon","E9ProCarbon",
            "EWEADN E9","EWEADN E11","EWEADN E11 PRO","EWEADN A6","EWEADN G2SE A","S7 Plus","EWEADN S2"};
        foreach(string value in a)if(MouseExpansionName(name,value))return "a";
        foreach(string value in c)if(MouseExpansionName(name,value))return "c";
        if(MouseExpansionName(name,"EWEADN X23SE")||MouseExpansionName(name,"EWEADN X23 carbon"))return "dual";
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
