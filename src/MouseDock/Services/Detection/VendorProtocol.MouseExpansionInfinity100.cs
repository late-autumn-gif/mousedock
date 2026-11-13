using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionInfinity100(ReferenceBatteryProtocol.ILink link,byte rid)
    {
        bool forwarding=false;
        bool primaryFailed=false;
        byte[] Send(byte command,bool response=false,byte argument=0)
        {
            var request=MouseExpansionPacket(65,rid,command,argument);
            if(command==0xD6)request[8]=(byte)(255-MouseExpansionSum(request.AsSpan(1,7)));
            if(!response)
            {
                if(!link.SetFeature(request))throw new IOException("Infinity forwarding query failed");
                link.Delay(5);
                return null;
            }
            return MouseExpansionFeature(link,request,b=>b.Length==65&&b[0]==rid&&
                b.AsSpan(1).IndexOfAnyExcept((byte)0)>=0,1,30).AsSpan(1).ToArray();
        }
        byte[] Poll(Func<byte[],bool> ready,int budget)
        {
            long deadline=link.Now+budget;
            for(int n=0;n<80&&link.Now<deadline;n++)
            {
                var b=Send(0xF7,true);
                if(ready(b))return b;
                link.Delay(100);
            }
            throw new IOException("Infinity forwarding timeout");
        }
        try
        {
            var status=Send(0xF7,true);
            if(status[4]==1)return MouseExpansionSleep("Infinity .100 mouse offline");
            if(status[4]!=0)throw new IOException("unknown Infinity link status");
            forwarding=true;
            Send(0xF6,argument:5);
            status=Poll(b=>b[5]==1||b[0]==0&&b[4]==0&&b[6]==2,3000);
            if(status[4]!=0)throw new IOException("Infinity mouse went offline");
            Send(0xFE,argument:64);
            Send(0xD6);
            Poll(b=>b[0] is 1 or 0xD6,5000);
            link.Delay(10);
            var b=Send(0xFC,true);
            if(b[0]!=0xD6||b[1]>2||b[2]==255||b[3]>100)throw new IOException("invalid Infinity battery frame");
            if(b[2]==0)return MouseExpansionSleep("Infinity mouse offline");
            return MouseExpansionValue(b[3],"Infinity .100 reported percent; temporary read forwarding");
        }
        catch
        {
            primaryFailed=true;
            throw;
        }
        finally
        {
            if(forwarding)
            {
                try { Send(0xF6); }catch(IOException) when(primaryFailed) { }
            }
        }
    }
}
