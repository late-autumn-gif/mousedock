using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionOldMchose(ReferenceBatteryProtocol.ILink link)
    {
        int level=-1;
        for(int sample=0;sample<2;sample++)
        {
            var packet=MouseExpansionPacket(21,0x11,0xF9);
            packet.AsSpan(2).Fill(255);
            var raw=MouseExpansionFeature(link,packet,b=>b.Length==21&&MouseExpansionPrefix(b,0x11,0xF9),4,100);
            var body=raw.AsSpan(2).ToArray();
            for(int n=0;n<body.Length;n++)body[n]^=255;
            if((body[0]|body[1]<<8)!=0x41E4||(body[2]|body[3]<<8)!=0x1101||body[10]>1||
                (body[8]&7) is not (0 or 1 or 2)||body[9]>100)
                throw new IOException("old MHUB A5 V2 identity or fields mismatch");
            if((body[8]&7)==1&&(body[8]&8)==0)return MouseExpansionSleep("old MHUB mouse offline");
            level=body[9];
        }
        return MouseExpansionValue(level,"Old MHUB reported percent; exact A5 V2 revision and paired identity confirmed",
            identity:"41E4:1101");
    }
    private static ReferenceBatteryProtocol.Reading MouseExpansionL7(ReferenceBatteryProtocol.ILink link)
    {
        bool drained=false;
        for(int n=0;n<32;n++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
        if(!drained)throw new IOException("L7 notification queue did not drain");
        long deadline=link.Now+6000;
        int count=0;
        for(int n=0;n<80&&link.Now<deadline;n++)
        {
            var b=link.Read(100);
            if(b==null)continue;
            if(b.Length is not (16 or 64)||!MouseExpansionPrefix(b,0x13,0x1D)||(b[5]^255)>100)
            {count=0;continue;}
            if(++count==2)return MouseExpansionValue(b[5]^255,"L7 fresh passive notification; reported inverted percentage");
        }
        throw new IOException("no two fresh L7 battery notifications");
    }
}
