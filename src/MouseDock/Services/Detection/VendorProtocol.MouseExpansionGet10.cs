using System;
using System.IO;
using System.Text;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionGet10(ushort vid,ushort pid,ushort revision,
        ReferenceBatteryProtocol.ILink link)
    {
        bool legacy=vid==0x249A&&pid==0x5C2F&&revision==0x183;
        string previous=null;
        int level=-1;
        for(int n=0;n<2;n++)
        {
            var b=MouseExpansionExchange(link,MouseExpansionPacket(33,0,0x10),
                b=>b.Length==32&&MouseExpansionPrefix(b,0x10,0,1,11),300);
            string model=Encoding.ASCII.GetString(b,4,4);
            if(model=="M183"||legacy&&model=="M179")
            {
                if(b[12]>1||b[13]>100||b[14]!=1)throw new IOException("GET10 offline or invalid status");
                if(model=="M179"&&MouseExpansionSum(b.AsSpan(4,27))!=b[31])throw new IOException("GET10 checksum mismatch");
                level=b[13];
            }
            else if(legacy&&model=="M620"&&b[13]<=100)level=b[13];
            else throw new IOException("GET10 unconfirmed device model");
            if(previous!=null&&previous!=model)throw new IOException("GET10 model changed");
            previous=model;
        }
        return MouseExpansionValue(level,"GET10 hardware-reported percent; fresh live model reply",identity:previous);
    }
}
