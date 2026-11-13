using System;
using System.IO;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionX23(ushort vid,ushort pid,ReferenceBatteryProtocol.ILink link)
    {
        byte[] Query(byte command)
        {
            var request=MouseExpansionCompx(command);
            if(command==1)
            {
                request[5]=8;
                RandomNumberGenerator.Fill(request.AsSpan(6,4));
                request[16]=(byte)(0x55-MouseExpansionSum(request.AsSpan(0,16)));
            }
            return MouseExpansionExchange(link,request,b=>b.Length==17&&MouseExpansionPrefix(b,8,command,0)&&
                MouseExpansionSum(b)==0x55&&b[5] is >=1 and <=10&&
                (command!=1||b[5]>=7)&&(command!=4||b[5]>=2&&b[6]<=100&&b[7]<=1),1200);
        }
        bool Allowed(byte cid,byte mid,byte kind)
        {
            if(vid==0x35BB)return cid==45&&mid==5&&kind is 0 or 1 or 4 or 5;
            if(cid!=41)return false;
            return pid switch
            {
                0xF5A0=>mid==2&&kind is 2 or 3,
                0xF59F or 0xF5B2 or 0xF5B5=>mid==4&&kind is 0 or 1 or 4 or 5,
                0xF5B3=>mid==4&&kind is 2 or 3,
                0xFB35=>mid is 13 or 19 or 20 or 23&&kind==5,
                0xFB36=>mid is 13 or 19 or 20 or 23&&kind==3,
                0xF515=>mid is 8 or 10 or 11 or 16 or 17 or 18 or 22&&kind==3,
                0xFB45=>mid==3&&kind==0,0xFB47=>mid==3&&kind==3,0xFB48=>mid==3&&kind==5,
                _=>false
            };
        }
        var identity=Query(1);
        byte cid=identity[10],mid=identity[11],kind=identity[12];
        if(!Allowed(cid,mid,kind))throw new IOException("receiver did not identify an allowed paired mouse");
        int level=-1;
        for(int n=0;n<2;n++)
        {
            if(Query(3)[6]!=1)throw new IOException("paired mouse offline or invalid live flag");
            level=Query(4)[6];
        }
        var after=Query(1);
        if(after[10]!=cid||after[11]!=mid||after[12]!=kind)throw new IOException("paired mouse identity changed");
        return MouseExpansionValue(level,"COMPX reported percent; challenge identity and online checks confirmed",
            identity:$"{cid}:{mid}:{kind}");
    }
}
