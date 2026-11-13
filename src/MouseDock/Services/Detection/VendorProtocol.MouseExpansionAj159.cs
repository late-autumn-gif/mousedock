using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionAj159Pro(ReferenceBatteryProtocol.ILink link)
    {
        byte[] Packet(byte command)
        {
            var packet=MouseExpansionPacket(65,0,command);
            if(command==0xF6)packet[2]=5;
            if(command==0x8F)packet[8]=0x70;
            return packet;
        }
        byte[] Status()
        {
            var b=MouseExpansionFeature(link,Packet(0xF7),b=>b.Length==65&&b[0]==0,1,10).AsSpan(1).ToArray();
            if(b[0]>1||b[3]>1||b[4]!=0||b[5]>1||b[2]>100||b.AsSpan(0,6).IndexOfAnyExcept((byte)0)<0)
                throw new IOException("AJ159 PRO offline or invalid status");
            return b;
        }
        void Ready(int index)
        {
            for(int n=0;n<5;n++){link.Delay(100);if(Status()[index]==1)return;}
            throw new IOException("AJ159 PRO receiver not ready");
        }
        Status();
        if(!link.SetFeature(Packet(0xF6)))throw new IOException("AJ159 PRO forwarding failed");
        Ready(5);
        if(!link.SetFeature(Packet(0x8F)))throw new IOException("AJ159 PRO identity query failed");
        Ready(0);
        var identity=MouseExpansionFeature(link,Packet(0xFC),b=>b.Length==65&&b[0]==0&&b[1]==0x8F,1,10);
        uint model=(uint)(identity[2]|identity[3]<<8|identity[4]<<16|identity[5]<<24);
        if(model!=1754)throw new IOException("AJ159 PRO paired model mismatch");
        return MouseExpansionValue(Status()[2],"AJ159 PRO reported percent; documented temporary forwarding session",
            model:"AJAZZ AJ159 PRO",identity:"1754");
    }
}
