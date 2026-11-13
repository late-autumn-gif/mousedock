using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionS9(string family,ushort pid,
        ReferenceBatteryProtocol.ILink link)
    {
        bool vk=family=="vk-m2",wired=vk ? pid is 0xA304 or 0xA307 : pid==0x1029;
        ulong expected=vk ? pid is 0xA304 or 0xA305 ? 0x09000000000DUL : 0x090000000003UL
            : 0x09000000000EUL;
        byte[] Query(byte command,byte offset=0)
        {
            var request=MouseExpansionPacket(64,3,0,80,0,(byte)(vk?(command==0x47?1:2):0),command,offset);
            request[1]=(byte)MouseExpansionSum(request.AsSpan(1));
            return MouseExpansionFeature(link,request,b=>b.Length==64&&
                MouseExpansionPrefix(b,3,80,0)&&b[4]==request[4]&&b[5]==offset&&b[3] is >=1 and <=10,4,100);
        }
        void Online()
        {
            var b=Query(0x47);
            if(b[6]!=1)throw new IOException("mouse offline or invalid link state");
        }
        void Identity()
        {
            var b=Query(0x4F,0x80);
            if(b[3]<8)throw new IOException("truncated model UUID");
            ulong uuid=0;
            for(int n=8;n<14;n++)uuid=(uuid<<8)|b[n];
            if(uuid!=expected)throw new IOException("paired model UUID mismatch");
        }
        int level=-1;
        for(int n=0;n<2;n++)
        {
            if(!wired)Online();
            Identity();
            var b=Query(0x4F,0x81);
            if(b[3]<7||(b[6]&127)>100)throw new IOException("invalid battery payload");
            level=b[6]&127;
        }
        Identity();
        if(!wired)Online();
        return MouseExpansionValue(level,"Feature3 reported percent; paired UUID confirmed",
            identity:expected.ToString("X12"));
    }
}
