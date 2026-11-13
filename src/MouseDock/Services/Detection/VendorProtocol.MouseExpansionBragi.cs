using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionBragi(ReferenceBatteryProtocol.ILink link)
    {
        long deadline=link.Now+7000;
        int Get(byte child,byte property)
        {
            if(link.Now>=deadline)throw new IOException("Bragi query budget exhausted");
            var b=MouseExpansionExchange(link,MouseExpansionPacket(65,0,(byte)(8|child),2,property),
                b=>b.Length==64&&b[0]==child&&b[1]==2,Math.Min(2000,(int)(deadline-link.Now)));
            if(b[2]!=0)throw new IOException("Bragi property rejected");
            return b[3]|b[4]<<8|b[5]<<16;
        }
        int Children()
        {
            int flags=Get(0,0x36);
            if(flags>255)throw new IOException("invalid Bragi child bitfield");
            return flags&254;
        }
        bool Identity(byte child)=>Get(child,0x11)==0x1B1C&&Get(child,0x12)==0x1B4C;
        int present=Children(),selected=0;
        for(byte child=1;child<=7;child++)
        {
            if((present&(1<<child))==0||!Identity(child))continue;
            if(selected!=0)throw new IOException("ambiguous Ironclaw children");
            selected=child;
        }
        if(selected==0)throw new IOException("no online Ironclaw child");
        int first=Get((byte)selected,0x0F),second=Get((byte)selected,0x0F);
        if(first!=second||second>1000||!Identity((byte)selected)||Children()!=present)
            throw new IOException("Bragi battery or child identity changed");
        return MouseExpansionValue(second/10,"Bragi reported tenths-percent floored; stable child identity",
            model:"Corsair IRONCLAW RGB WIRELESS",identity:"1B1C:1B4C:"+selected);
    }
}
