using System;
using System.Collections.Generic;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionIpiUni(ReferenceBatteryProtocol.ILink link,int input,int output)
    {
        long deadline=link.Now+8000;
        byte[] Exchange(byte command,byte sub=0,int index=-1)
        {
            var packet=MouseExpansionPacket(output,8,command,sub,0,(byte)(index<0?0:1));
            int checksum=command==0xFD?3:index<0?5:6;
            if(index>=0)packet[5]=(byte)index;
            packet[checksum]=(byte)(255-MouseExpansionSum(packet.AsSpan(0,checksum)));
            if(link.Now>=deadline)throw new IOException("IPI transaction budget exhausted");
            return MouseExpansionExchange(link,packet,b=>b.Length==input&&
                MouseExpansionPrefix(b,8,command,sub),Math.Min(1500,(int)(deadline-link.Now)));
        }
        var initial=Exchange(0xFD);
        int package=initial[12];
        if(MouseExpansionSum(initial.AsSpan(0,14))!=255||package<10||package>Math.Min(input,output))
            throw new IOException("invalid IPI negotiated packet size");
        (byte[],int,int) Frame(byte[] b,byte command,byte sub)
        {
            if(!MouseExpansionPrefix(b,8,command,sub)||b[3] is not (0 or 16))throw new IOException("invalid IPI frame header");
            int start=b[3]==0?5:8,length=b[3]==0?b[4]:b[7];
            int total=b[3]==0?1:b[4]|(b[6]>>4)<<8,current=b[3]==0?0:b[5]|(b[6]&15)<<8;
            int end=start+length;
            if(total is <1 or >8||current>=total||length is <1 or >64||end>=Math.Min(input,package)||
                MouseExpansionSum(b.AsSpan(0,end+1))!=255)throw new IOException("invalid IPI fragment sequence, length or checksum");
            return(b.AsSpan(start,length).ToArray(),total,current);
        }
        byte[] Payload(byte command,byte sub,int maximum)
        {
            var first=Frame(Exchange(command,sub),command,sub);
            if(first.Item3!=0)throw new IOException("IPI first fragment missing");
            var payload=new List<byte>(first.Item1);
            if(payload.Count>maximum)throw new IOException("IPI payload too large");
            for(int n=1;n<first.Item2;n++)
            {
                var part=Frame(Exchange(command,sub,n),command,sub);
                if(part.Item2!=first.Item2||part.Item3!=n)throw new IOException("IPI fragments changed or reordered");
                payload.AddRange(part.Item1);
                if(payload.Count>maximum)throw new IOException("IPI payload too large");
            }
            return payload.ToArray();
        }
        void Identity()
        {
            var b=Payload(1,1,64);
            if(b.Length<10||!b.AsSpan(4,6).SequenceEqual(new byte[]{0x23,0,0,0,0,2}))
                throw new IOException("IPI paired mouse UUID mismatch");
        }
        Identity();
        int level=-1;
        for(int n=0;n<2;n++)
        {
            var b=Payload(2,0,4);
            if(b.Length!=4||b[1]!=1||b[3]>1||(b[2]&127)>100)throw new IOException("IPI offline or invalid battery status");
            level=b[2]&127;
        }
        Identity();
        return MouseExpansionValue(level,"IPI reported percent; bounded fragments and paired UUID confirmed",identity:"230000000002");
    }
}
