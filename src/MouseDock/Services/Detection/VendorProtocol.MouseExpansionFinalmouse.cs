using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionFinal(ushort pid,ReferenceBatteryProtocol.ILink link,
        byte inputId,byte outputId)
    {
        bool ulx=pid==0x100,wired=pid is 0x310 or 0x311;
        int Query(byte command)
        {
            int size=command==0x26?3:command==5?2:1;
            var b=MouseExpansionExchange(link,MouseExpansionPacket(64,outputId,2,(byte)(0x80|command),0),
                b=>
                {
                    if(b.Length<3||b[2]!=command)return false;
                    if(b.Length<4+size||b.Length>64||b[0]!=inputId||b[1]!=size+2||b[3]!=size)
                        throw new IOException("invalid Finalmouse frame");
                    return true;
                },850);
            int value=command==5?b[4]|b[5]<<8:b[4];
            if(command==0x26&&value>100||command is 0x24 or 0x25&&value>1||
                command==5&&value is <2500 or >4600)throw new IOException("invalid Finalmouse response");
            return value;
        }
        bool Online()
        {
            if(wired)return true;
            try { return Query(0x24)==1; }
            catch(MouseExpansionTimeout) when(ulx) { return Query(0x0D)>=129; }
        }
        if(!Online())return MouseExpansionSleep("Finalmouse offline");
        int level=Query(0x26);
        try { Query(0x25); }catch(IOException) { }
        if(!Online())return MouseExpansionSleep("Finalmouse went offline");
        return MouseExpansionValue(level,"Finalmouse reported state of charge");
    }
}
