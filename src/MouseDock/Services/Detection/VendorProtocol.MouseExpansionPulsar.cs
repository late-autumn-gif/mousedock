using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionPulsar(ReferenceBatteryProtocol.ILink link)
    {
        bool holdAttempted=false;
        byte[] Query(byte command,byte hold=0)
        {
            var request=MouseExpansionCompx(command,hold);
            return MouseExpansionExchange(link,request,b=>b.Length==17&&b[0]==8&&b[1]==command&&b[2]==0&&
                MouseExpansionSum(b)==0x55,600,()=>{if(command==3&&hold==1)holdAttempted=true;});
        }
        try
        {
            byte[] online=null;
            for(int n=0;n<3;n++)
            {
                online=Query(3,1);
                if(online[10]!=1)break;
                if(n==2)throw new IOException("Pulsar receiver busy");
            }
            if(online[6]!=1)throw new IOException("Pulsar mouse offline");
            int level=-1;
            for(int n=0;n<2;n++)
            {
                var b=Query(4);
                if(b[6]>100||b[7]>1)throw new IOException("invalid Pulsar battery fields");
                int voltage=b[8]<<8|b[9];
                if(voltage!=0&&voltage is <2500 or >5000)throw new IOException("invalid Pulsar voltage");
                level=b[6];
            }
            return MouseExpansionValue(level,"Pulsar hardware-reported percent; reference voltage smoothing not applied");
        }
        finally
        {
            if(holdAttempted&&!link.Write(MouseExpansionCompx(3,0),300))throw new IOException("Pulsar session release failed");
        }
    }
}
