using System;
using System.IO;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading ReadPulsarNordicReceiver(ReferenceBatteryProtocol.ILink link)
    {
        byte[] Query(byte command, int payloadSize) => MouseExpansionExchange(link, MouseExpansionCompx(command),
            b => b.Length == 17 && MouseExpansionPrefix(b, 8, command, 0) && b[5] == payloadSize &&
                MouseExpansionSum(b) == 0x55, 600);

        var before = Query(3, 1);
        if (before[6] != 1) return MouseExpansionSleep("Pulsar receiver did not report an online mouse");

        int level = -1;
        for (int pass = 0; pass < 2; pass++)
        {
            var battery = Query(4, 2);
            int voltage = battery[8] << 8 | battery[9];
            if (battery[6] > 100 || battery[7] > 1 || (voltage != 0 && (voltage < 2500 || voltage > 5000)))
                return ReferenceBatteryProtocol.Reading.Failed("Invalid Pulsar Nordic battery reply");
            level = battery[6];
        }

        var after = Query(3, 1);
        if (after[6] != 1) return MouseExpansionSleep("Pulsar mouse went offline");
        return MouseExpansionValue(level, "Pulsar Nordic device-reported percentage");
    }

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
