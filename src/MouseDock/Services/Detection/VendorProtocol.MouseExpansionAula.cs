using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionAula(ReferenceBatteryProtocol.ILink link,byte inputId)
    {
        ushort[] models={0x28F7,0x28FB,0x2EE0,0x28FF,0x28E7,0x2EF4,0x2EFE,0x2EF6,0x2EEE,0x2EF2,
            0x2F14,0x28E8,0x2F08,0x2F0C,0x2F10};
        byte[] Query(byte command) => MouseExpansionExchange(link,MouseExpansionPacket(64,11,command),
            b=>b.Length==64&&b[0]==inputId&&b[1]==command&&
                !(b.AsSpan(1).SequenceEqual(MouseExpansionPacket(64,11,command).AsSpan(1)))&&
                (command==0xD0||b[3]==0),800);
        void Online()
        {
            if(Query(0xD0)[4]!=1)throw new IOException("AULA paired mouse offline");
        }
        ushort Identity()
        {
            var b=Query(0xA0);
            ushort vid=(ushort)(b[4]|b[5]<<8),pid=(ushort)(b[6]|b[7]<<8);
            if(vid!=0x38A6||Array.IndexOf(models,pid)<0)throw new IOException("unconfirmed paired AULA model");
            return pid;
        }
        Online();
        ushort paired=Identity();
        int level=-1;
        for(int n=0;n<2;n++)
        {
            var b=Query(0x81);
            if(b[17]>1||b[18]>100||b[19]>1)throw new IOException("invalid AULA battery fields");
            level=b[18];
        }
        if(Identity()!=paired)throw new IOException("AULA paired model changed");
        Online();
        return MouseExpansionValue(level,"AULA FFA8 reported percent; stable paired identity",identity:$"38A6:{paired:X4}");
    }
}
