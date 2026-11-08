using System;
namespace MouseDock.Services.Detection;
internal static class ZeroRainyProtocol
{
    internal static bool RainyName(string product)
    {
        string n=(product??"").Trim().Replace(" ","").Replace("-","").ToUpperInvariant();
        return n is "RAINY75" or "WOBRAINY75" or "WOBKEYRAINY75" or "RAINY75RGB" or "WOBKEYRAINY75RGB";
    }
    internal static bool Candidate(ushort vid,ushort pid,string product) => VendorProtocol.IsAtkZeroBitMouse(vid,pid) ||
        (VendorProtocol.IsRainy75Candidate(vid,pid) && (RainyName(product) ||
        (product?.Trim().ToUpperInvariant() is "WIRELESS GAMING KEYBOARD" or "USB KEYBOARD" or "GAMING KEYBOARD")));
    internal static bool Channel(ushort vid,ushort pid,string product,ushort page,ushort usage,int input,int output,int feature) =>
        Candidate(vid,pid,product) && feature==0 && (VendorProtocol.IsAtkZeroBitMouse(vid,pid)?
            VendorProtocol.IsAtkZeroBatteryCollection(page,usage,(ushort)input,(ushort)output):
            page==0xFF1C && usage==0x92 && input==64 && output==64);
    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link,ushort vid,ushort pid,string product)
    {
        bool atk=VendorProtocol.IsAtkZeroBitMouse(vid,pid),receiver=pid!=0x1154;
        bool rainyWireless=!atk && VendorProtocol.IsRainy75Wireless(vid,pid);
        string model=atk?"ATK ZERO":RainyName(product)?"WOBKEY Rainy 75":null;
        bool identified=atk && !receiver;
        string trace="";long deadline=link.Now+4500;
        byte[] Query(byte[] tx,byte command)
        {
            if(link.Now>=deadline)return null;
            bool drained=false;
            for(int i=0;i<32 && link.Now<deadline;i++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            if(!drained || link.Now>=deadline || !link.Write(tx,(int)Math.Min(300,deadline-link.Now)))return null;
            // The official receiver path waits up to 5 s, unlike wired polling.
            // All exchanges still share this reader's 4.5 s total budget.
            long until=rainyWireless?deadline:Math.Min(deadline,link.Now+650);
            while(link.Now<until)
            {
                var raw=link.Read((int)Math.Max(1,Math.Min(100,until-link.Now)));
                if(raw is not {Length:>0})continue;
                trace=" RX="+Convert.ToHexString(raw.AsSpan(0,Math.Min(64,raw.Length)));
                if(raw.AsSpan().SequenceEqual(tx))continue;
                if(atk && (raw.Length!=64 || raw[2]!=0))continue;
                // Rainy bytes1/2 are a checksum, not ATK's request nonce.
                // Native transport correlates command3; do not require checksum echo.
                if(!atk && raw.Length!=64)continue;
                if(atk?VendorProtocol.AtkReplyOffset(raw,command)>=0:
                    command==0xAA?IsRainyWirelessReady(raw):
                    VendorProtocol.TryParseRainy75OfficialBattery(raw,out _,out _))return raw;
            }
            return null;
        }
        ReferenceBatteryProtocol.Reading Fail(string why)=>ReferenceBatteryProtocol.Reading.Failed(why+trace) with {Model=atk&&!identified?null:model};
        if(atk && receiver)
        {
            for(int i=0;i<2;i++)if(!VendorProtocol.IsAtkZeroPairedModel(Query(VendorProtocol.BuildAtkZeroRequest(0x89,true),0x89)))
                return Fail("ZERO接收器未取得两次CID/MID=1/1；不按公共接收器确认型号");
            identified=true;
        }
        if(rainyWireless)
        {
            // Rainy75.exe FUN_00492580 calls the read-only RF-ready query
            // FUN_0047dd90 before its 0x1A battery request.
            var readyRequest=new byte[64];
            readyRequest[0]=4;readyRequest[1]=0xAA;readyRequest[3]=0xAA;
            if(!IsRainyWirelessReady(Query(readyRequest,0xAA)))
                return Fail("Rainy无线接收器未返回有效就绪状态；未继续查询电量");
        }
        int level=-1;bool charging=false,known=false;
        for(int i=0;i<2;i++)
        {
            var raw=Query(atk?VendorProtocol.BuildAtkZeroRequest(7,receiver):VendorProtocol.BuildRainy75BatteryRequest(),atk?(byte)7:(byte)0x1A);
            bool valid=atk?VendorProtocol.TryParseAtkZeroBattery(raw,out level):VendorProtocol.TryParseRainy75OfficialBattery(raw,out level,out charging);
            if(!valid)return Fail($"第{i+1}/2次未取得有效电量响应");
        }
        if(atk)
        {
            var raw=Query(VendorProtocol.BuildAtkZeroRequest(0x0F,receiver),0x0F);
            known=raw is {Length:64} && raw[7]<=1;charging=known&&raw[7]==1;
        }
        else {known=model!=null;if(!known)charging=false;}
        return new(level,charging,known,charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,
            (atk?"ZERO两次电量响应":"Evision两次有效电量响应，通用名称不确认Rainy型号")+
            (known?"；充电字段已知":"；充电状态未知")+"；不推断独立RF在线位"+trace,model);
    }

    private static bool IsRainyWirelessReady(ReadOnlySpan<byte> response) =>
        response.Length==64 && response[0]==4 && response[3]==0xAA &&
        response[7] is not (0xFE or 0xFF) && response[8]==0xFF;
}
