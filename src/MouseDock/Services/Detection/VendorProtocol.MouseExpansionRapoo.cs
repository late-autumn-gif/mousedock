using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionRapooActive(List<MouseExpansionLink> links)
    {
        if(links.Count!=2)return ReferenceBatteryProtocol.Reading.Failed("Rapoo active: requires one input and one command collection");
        var rx=links.SingleOrDefault(l=>l.Channel.Usage==2);
        var tx=links.SingleOrDefault(l=>l.Channel.Usage==14);
        if(rx==null||tx==null||rx.Revision!=tx.Revision)
            return ReferenceBatteryProtocol.Reading.Failed("Rapoo active: command/input metadata differs");
        ReferenceBatteryProtocol.Reading value=null;
        for(int sample=0;sample<2;sample++)
        {
            if(!rx.Drain()||!tx.Write(MouseExpansionPacket(33,6,0xA5,0xAA),300))
                return ReferenceBatteryProtocol.Reading.Failed("Rapoo active: query failed");
            long deadline=rx.Now+3000;
            value=null;
            for(int n=0;n<60&&rx.Now<deadline;n++)
            {
                var raw=rx.Read(50);
                value=MouseExpansionDecode("rapoo",raw);
                if(value?.Level>=0)break;
            }
            if(value?.Level is not >=0)return ReferenceBatteryProtocol.Reading.Failed("Rapoo active: fresh report7 unavailable");
        }
        return value;
    }

    private static ReferenceBatteryProtocol.Reading MouseExpansionRapooOffice(MouseExpansionLink link)
    {
        ReferenceBatteryProtocol.Reading result=null;
        for(int sample=0;sample<2;sample++)
        {
            link.Delay(100);link.Delay(10);
            if(!link.Write(MouseExpansionPacket(link.Output,0xBA,0xA5,0xAA),300))
                return ReferenceBatteryProtocol.Reading.Failed("Rapoo Office query failed");
            link.Delay(100);link.Delay(10);
            long deadline=link.Now+1000;
            result=null;
            for(int n=0;n<250&&link.Now<deadline;n++)
            {
                var b=link.GetInput(0xBA,32);
                if(b is not {Length:>=3 and <=32})break;
                if(b[0]==2){link.Delay(2);continue;}
                if(b[0]==1&&b[2]<=100)result=MouseExpansionValue(b[2],"Rapoo Office control-input reported percent");
                break;
            }
            if(result==null)return ReferenceBatteryProtocol.Reading.Failed("Rapoo Office busy or invalid battery reply");
        }
        return result;
    }
}
