using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionExx(ushort pid,ReferenceBatteryProtocol.ILink link)
    {
        bool wired=pid==0x3A87;
        long deadline=link.Now+2500;
        byte[] Request(byte command)
        {
            var packet=MouseExpansionPacket(65,0,command,0,1);
            if(command==0)new byte[]{8,0xEF,9,0xB0,0x8A,0xEE,0xC8,0x3D,0x7C}.CopyTo(packet,4);
            return packet;
        }
        ReferenceBatteryProtocol.Reading Battery(int level,int charge) =>
            level<=100&&charge<=2?MouseExpansionValue(level,"EXX reported mouse percent; 3955 device identity confirmed"):
                throw new IOException("invalid EXX battery fields");
        (byte[],ReferenceBatteryProtocol.Reading) Exchange(byte command)
        {
            bool drained=false;
            for(int n=0;n<16;n++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            var request=Request(command);
            if(!drained||link.Now>=deadline||!link.Write(request,300))throw new IOException("EXX query write or deadline");
            ReferenceBatteryProtocol.Reading notification=null;
            byte[] info=null;
            for(int n=0;n<24&&link.Now<deadline;n++)
            {
                var b=link.Read((int)Math.Min(75,deadline-link.Now));
                if(b==null)continue;
                if(b.Length!=64)throw new IOException("invalid EXX report length");
                if(b.AsSpan().SequenceEqual(request.AsSpan(1)))continue;
                if(b[0]==0xD1&&command==0x30)
                {
                    int online,level,charge;
                    if(b[3]==3)
                    {
                        if(b[1]!=0||b[2]!=0)throw new IOException("invalid EXX notification header");
                        online=b[4];level=b[5];charge=b[6];
                    }
                    else {online=b[1];level=b[2];charge=b[3];}
                    if(online>1)throw new IOException("invalid EXX online flag");
                    var candidate=Battery(level,charge);
                    notification=online==0?MouseExpansionSleep("EXX paired mouse offline"):candidate;
                }
                else if(b[0]==command)
                {
                    if(command==0)
                    {
                        if(b[1]!=0||b[3]!=1||b[4]!=1)throw new IOException("EXX read-session login rejected");
                        return(b,null);
                    }
                    if(b[1]==5)return(b,null);
                    if(b[3] is <11 or >60||!b.AsSpan(4,4).SequenceEqual(new byte[]{51,57,53,53})||
                        b[1] is not (0 or 2)||b[14]>1)throw new IOException("invalid EXX device-info identity");
                    if(wired)
                    {
                        if(b[1]!=0||b[3]>=14&&b[11]!=0xBB)throw new IOException("invalid EXX wired mouse role");
                        return(b,Battery(b[13],b[12]));
                    }
                    if(b[1]==2||b[14]==0)return(b,MouseExpansionSleep("EXX paired mouse offline"));
                    if(b[3]>=14&&b[11]==0xBB)return(b,Battery(b[13],b[12]));
                    if(b[3]>=14&&b[11]!=0xAA)throw new IOException("unknown EXX device role");
                    info=b;
                }
                if(info!=null&&notification!=null)return(info,notification);
            }
            throw new IOException("no fresh EXX mouse battery reply");
        }
        var result=Exchange(0x30);
        if(result.Item1[1]==5)
        {
            Exchange(0);
            result=Exchange(0x30);
            if(result.Item1[1]==5)throw new IOException("EXX access rejected");
        }
        return result.Item2??ReferenceBatteryProtocol.Reading.Failed("EXX receiver supplied no mouse battery");
    }
}
