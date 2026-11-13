using System;
using System.Linq;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadFeature(string family, ushort vid,
        ushort pid, MouseExpansionLink l)
    {
        if(family is "razer" or "razer-carbon") return MouseExpansionReadRazer(family,pid,l);
        if(family=="sora-v3")
        {
            byte[] Command(byte command)=>l.FeatureExchange(MouseExpansionPacket(16,6,command,0,0,1),
                b=>b.Length==16&&b[0]==6&&b[1]==command);
            if(pid==0xEB02)
            {
                var online=Command(0x13);
                if(online==null||online[8]!=1)return MouseExpansionSleep("Sora V3 receiver offline");
            }
            var charge=Command(0x11);
            if(charge!=null&&charge[8]>1)return ReferenceBatteryProtocol.Reading.Failed("Sora V3 charge response invalid");
            var first=Command(0x12);
            var raw=Command(0x12);
            if(first==null||raw==null||first[8]>100||raw[8]>100)
                return ReferenceBatteryProtocol.Reading.Failed("Sora V3 battery response missing or invalid");
            if(pid==0xEB02)
            {
                var after=Command(0x13);
                if(after==null)return ReferenceBatteryProtocol.Reading.Failed("Sora V3 final online response missing");
                if(after[8]!=1)return MouseExpansionSleep("Sora V3 went offline");
            }
            return MouseExpansionValue(raw[8],"Sora V3 device-reported percentage");
        }
        if(family=="sora-v2")
        {
            var request=MouseExpansionPacket(32,5,0x15,0,0,1);
            if(pid==0xAE8C)request[7]=4;
            ReferenceBatteryProtocol.Reading value=null;
            for(int i=0;i<2;i++)
            {
                var raw=l.FeatureExchange(request,b=>b.Length==32&&b[0]==5&&b[1]==0x15&&b[9]<=100);
                if(raw==null)return ReferenceBatteryProtocol.Reading.Failed("Sora V2 battery response missing");
                if(raw[12]==0)return MouseExpansionSleep("Sora V2 receiver offline");
                value=MouseExpansionValue(raw[9],"Sora V2 device-reported percentage");
            }
            return value;
        }
        if(family=="dareu")
        {
            var raw=l.FeatureExchange(MouseExpansionPacket(65,10,0,3,7,0x80),
                b=>b.Length==65&&b[0]==10&&(b[1]&0xF0)==0&&(b[1]&15)==2&&b[3]==7&&b[4]==0x80&&
                    b[7]<=2&&b[8] is >0 and <=100,20,10);
            return raw==null?ReferenceBatteryProtocol.Reading.Failed("A980 Pro battery remained unavailable"):
                MouseExpansionValue(raw[8],"Dareu estimated battery percentage");
        }
        if(family=="ipi")
        {
            var request=MouseExpansionPacket(64,3,0,0x50,0,2,0x4F,0x81);
            request[1]=(byte)MouseExpansionSum(request.AsSpan(2));
            var first=l.FeatureExchange(request,b=>MouseExpansionDecode("ipi",b)!=null,3,100);
            if(first==null)return ReferenceBatteryProtocol.Reading.Failed("IPI first battery reply missing");
            var raw=l.FeatureExchange(request,b=>MouseExpansionDecode("ipi",b)!=null,3,100);
            return raw==null?ReferenceBatteryProtocol.Reading.Failed("IPI Feature3 battery missing"):
                MouseExpansionDecode("ipi",raw);
        }
        if(family=="sonix")
        {
            int Read(byte register)
            {
                var request=MouseExpansionPacket(65,0,0,8,register,1);
                int sum=request.AsSpan(1,62).ToArray().Sum(x=>(int)x);
                request[63]=(byte)sum;request[64]=(byte)(sum>>8);
                var raw=l.FeatureExchange(request,b=>b.Length==65&&MouseExpansionPrefix(b,0,1,8,register,1)&&
                    b[7] <= (register==0x81?100:1),40,10);
                return raw==null?-1:raw[7];
            }
            int level=-1;
            for(int i=0;i<2;i++)
            {
                level=Read(0x81);
                if(level<0||Read(0x83)<0)return ReferenceBatteryProtocol.Reading.Failed("Sonix power register unavailable");
            }
            return MouseExpansionValue(level,"Sonix device-reported percentage");
        }
        if(family=="vaxee")
        {
            int Read(byte command)
            {
                var request=MouseExpansionPacket(64,14,0xA5,command,1,1);
                var raw=l.FeatureExchange(request,b=>b.Length is >=6 and <=64&&
                    MouseExpansionPrefix(b,14,0xA5,command)&&b[4]==1&&
                    (command!=11||b[5]<=20),8,100);
                return raw==null?-1:raw[5];
            }
            int level=Read(11);
            if(level<0)return ReferenceBatteryProtocol.Reading.Failed("VAXEE battery level unavailable");
            Read(16);
            return MouseExpansionValue(level*5,"VAXEE coarse 5% battery steps");
        }
        if(family is "vk-m5" or "royuan-mouse")
        {
            int level=-1;
            for(int i=0;i<2;i++)
            {
                var request=MouseExpansionPacket(l.Feature,l.FeatureId,0xF7);
                var raw=l.FeatureExchange(request,b=>
                {
                    if(b.Length!=l.Feature||b[0]!=l.FeatureId)return false;
                    var body=b.AsSpan(1);
                    if(body.Length<9||body[0]>1||body[4]>1||body[5]>1||body[2]>100||body[4]!=0)return false;
                    if(family=="vk-m5")return body[8]==0&&body[6] is 2 or 3;
                    return body.Length==64&&body[3]<=1&&body.ToArray().Any(v=>v!=0);
                },2,10);
                if(raw==null)return ReferenceBatteryProtocol.Reading.Failed("Mouse receiver F7 state unavailable");
                level=raw[3];
            }
            return MouseExpansionValue(level,"Receiver F7 device-reported mouse percentage");
        }
        if(family is "gwolves" or "gwolves-old")
        {
            var request=family=="gwolves-old"?MouseExpansionPacket(65,0,0,2,0x8F,1):
                MouseExpansionPacket(65,0,0,0,2,2,0,0x83);
            var raw=l.FeatureExchange(request,b=>b.Length==65&&b[0]==0&&b[1]==0xA1&&
                (family=="gwolves-old"?b[2]==2&&b[3]==0x8F&&b[5]<=1&&b[6]<=100:
                    b[3]==2&&b[4]==2&&b[6]==0x83&&b[7]<=1&&b[8]<=100),8,50);
            return raw==null?ReferenceBatteryProtocol.Reading.Failed("G-Wolves Feature battery unavailable"):
                MouseExpansionValue(raw[family=="gwolves-old"?6:8],"G-Wolves device-reported percentage");
        }
        byte target=(byte)(family=="ko83"&&pid==0x6A||family=="luna83"&&pid is 0x32 or 0x54?0:2);
        var packet=new byte[65];packet[3]=target;packet[4]=2;packet[6]=0x83;
        ReferenceBatteryProtocol.Reading result=null;
        int firstLowBand=0;
        for(int i=0;i<2;i++)
        {
            var raw=l.FeatureExchange(packet,b=>
            {
                if(family=="wl83"&&b.Length==65&&b[0]==0&&b[4]==2&&b[6]==0x83&&b[1]==0xA0)return true;
                if(family=="wl83"&&b.Length==65&&b[1]==0xA2)b[1]=0xA1;
                return MouseExpansionDecode("feature83",b)!=null;
            },3,100);
            if(raw==null)return ReferenceBatteryProtocol.Reading.Failed("Feature83 battery response missing");
            if(raw[1]==0xA0)return MouseExpansionSleep("WLMOUSE receiver offline");
            result=MouseExpansionDecode("feature83",raw);
            if(result==null)return ReferenceBatteryProtocol.Reading.Failed("Feature83 response invalid");
            if(family=="ko83"&&pid==0x6B&&result.Level<=30)
            {
                int band=result.Level==0?3:result.Level<=5?2:1;
                if(i==1&&band!=firstLowBand)return ReferenceBatteryProtocol.Reading.Failed("KO-ONE low battery band not confirmed");
                firstLowBand=band;
            }
            if(family=="ko83"&&pid==0x6B&&result.Level<=30&&i==0)
                for(int wait=0;wait<50;wait++)l.Delay(100);
        }
        return result;
    }
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadRazer(string family,ushort pid,MouseExpansionLink l)
    {
        int Read(byte command)
        {
            byte transaction=MouseExpansionPid(pid,0x72,0x73,0x7C,0x7D)?(byte)0x3F:
                MouseExpansionPid(pid,0x7A,0x7B,0x83)?(byte)0xFF:(byte)0x1F;
            var previous=l.GetFeature(0,91);
            if(previous==null||previous.Length!=91)return -1;
            if(family=="razer-carbon")
            {
                transaction=(byte)RandomNumberGenerator.GetInt32(31);
                if(transaction==previous[2])transaction=(byte)((transaction+1)%31);
            }
            var packet=MouseExpansionPacket(91,0,0,transaction,0,0,0,2,7,command);
            byte checksum=0;
            foreach(byte value in packet.AsSpan(3,86))checksum^=value;
            packet[89]=checksum;
            var raw=l.FeatureExchange(packet,b=>
            {
                if(b.Length!=91||b[0]!=0||b[1]!=2||b[2]!=transaction||!b.AsSpan(3,6).SequenceEqual(packet.AsSpan(3,6)))return false;
                byte xor=0;foreach(byte value in b.AsSpan(3,86))xor^=value;
                if(xor!=b[89]||family=="razer-carbon"&&b[9]!=0)return false;
                if(pid==0xA4&&b.AsSpan().SequenceEqual(previous))return false;
                return true;
            },family=="razer-carbon"?40:50,5);
            return raw==null?-1:raw[10];
        }
        if(family=="razer-carbon")
        {
            int charge=Read(0x84);
            if(charge is <0 or >1)return ReferenceBatteryProtocol.Reading.Failed("Razer Carbon charging state unavailable");
        }
        int level=Read(0x80);
        if(level<0)return ReferenceBatteryProtocol.Reading.Failed("Razer battery response unavailable");
        if(family=="razer-carbon"&&level==255)return MouseExpansionSleep("Razer Carbon battery unavailable");
        return MouseExpansionValue(family=="razer-carbon"?level*100/255:(int)Math.Round(level*100.0/255),
            "Razer device-reported 0..255 battery scale");
    }
}
