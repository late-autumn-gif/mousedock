using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionInfinity97(ReferenceBatteryProtocol.ILink link,
        byte inputId,byte outputId,byte featureId,int featureLength,int page)
    {
        bool feature=featureLength>0;
        int size=feature?featureLength:64;
        byte[] Query(ushort command,byte route)
        {
            var packet=MouseExpansionPacket(size,feature?featureId:outputId,6,route,5,0x5A,2,0,
                (byte)command,(byte)(command>>8));
            byte[] payload=null;
            bool Match(byte[] b)
            {
                if(b.Length!=size||b[0]!=(feature?featureId:inputId))return false;
                if(b[1]!=0&&(feature?b[2]!=route:b[2]!=0&&b[2]!=route))
                    throw new IOException("Infinity .97 response route changed");
                payload=MouseExpansionRace(b,command,size,feature?featureId:inputId,page==0xFF13&&feature);
                return payload!=null;
            }
            if(feature)MouseExpansionFeature(link,packet,Match,route==0?60:150,20);
            else MouseExpansionExchange(link,packet,Match,route==0?1200:3000);
            return payload;
        }
        byte[] Remote()
        {
            var payload=Query(0x2C94,0);
            if(payload.Length<1||payload[0]!=0||(payload.Length-1)%10!=0)
                throw new IOException("Infinity .97 malformed device list");
            byte[] remote=null;
            for(int n=1;n<payload.Length;n+=10)
            {
                if(payload[n+7]<128)continue;
                if(remote!=null||payload[n+7]!=128)throw new IOException("Infinity .97 ambiguous remote mouse");
                remote=payload.AsSpan(n,10).ToArray();
            }
            return remote;
        }
        var before=Remote();
        if(before==null)return MouseExpansionSleep("Infinity .97 no connected remote");
        var b=Query(0x30CF,128);
        int level;
        if(b.Length>=7)
        {
            if(b[1]>1)throw new IOException("Infinity .97 invalid charging field");
            if(b[4]==0)return MouseExpansionSleep("Infinity .97 remote mouse offline");
            level=b[2];
        }
        else if(b.Length==3)level=b[1]|b[2]<<8;
        else throw new IOException("Infinity .97 unknown battery layout");
        var after=Remote();
        if(after==null||!before.AsSpan().SequenceEqual(after))throw new IOException("Infinity .97 remote identity changed");
        return MouseExpansionValue(level,"Infinity .97 reported percent; RACE remote identity confirmed");
    }

    internal static byte[] MouseExpansionRace(byte[] b,ushort command,int size,byte report,bool notification=false)
    {
        if(b==null||b.Length!=size||size<6||b[0]!=report)throw new IOException("invalid RACE report length or ID");
        int count=b[1];
        if(count==0)return null;
        if(count>59||count>b.Length-3)throw new IOException("RACE envelope overflow");
        int at=3,end=3+count;
        while(at<end)
        {
            if(end-at<6||b[at]!=5)throw new IOException("truncated RACE header");
            int length=(b[at+2]|b[at+3]<<8)+4;
            if(length<6||length>end-at)throw new IOException("truncated RACE payload");
            bool kind=b[at+1]==0x5B||notification&&b[at+1]==0x5D;
            if(kind&&(b[at+4]|b[at+5]<<8)==command)
            {
                if(length==6||b[at+6]!=0)throw new IOException("RACE command rejected");
                return b.AsSpan(at+6,length-6).ToArray();
            }
            at+=length;
        }
        return null;
    }
}
