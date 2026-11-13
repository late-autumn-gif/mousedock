using System;
using System.IO;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionLogitech(ReferenceBatteryProtocol.ILink link)
    {
        byte tag=(byte)RandomNumberGenerator.GetInt32(1,16);
        byte[] Call(byte feature,byte function,params byte[] arguments)
        {
            tag=(byte)(tag%15+1);
            var request=MouseExpansionPacket(20,0x11,1,feature,(byte)(function<<4|tag));
            arguments.CopyTo(request,4);
            var reply=MouseExpansionExchange(link,request,b=>b.Length==20&&b[0]==0x11&&b[1]==1&&
                (b[2]==feature&&b[3]==request[3]||b[2] is 0x8F or 0xFF&&b[3]==feature&&b[4]==request[3]));
            if(reply[2] is 0x8F or 0xFF)throw new IOException("HID++ query rejected");
            return reply;
        }
        byte index=Call(0,0,0x10,4)[4];
        if(index==0)throw new IOException("HID++ unified battery unavailable");
        var b=Call(index,1);
        if(b[4] is <1 or >100||b[6]>4)throw new IOException("invalid HID++ battery state");
        return MouseExpansionValue(b[4],"HID++ unified battery reported percent; matched transaction tag");
    }
}
