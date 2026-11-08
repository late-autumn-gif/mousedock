using System;
namespace MouseDock.Services.Detection;
internal static class ZeroRainyProtocol
{
    internal static bool RainyName(string product)
    {
        string n=(product??"").Trim().Replace(" ","").Replace("-","").ToUpperInvariant();
        return n is "RAINY75" or "WOBRAINY75" or "WOBKEYRAINY75" or "RAINY75RGB" or "WOBKEYRAINY75RGB";
    }
    /// <summary>
    /// 接收器模式下的产品串是通用的 "Wireless Gaming Keyboard"，
    /// 但同一 VID:PID 下 VGN S99 用的是自己的串，所以这个通用串仍能定位到 Rainy 75。
    /// 型号名按官方名给出，电量是否读得到另算——这里只解决"型号为空"。
    /// </summary>
    internal static bool RainyReceiverName(string product) =>
        (product??"").Trim().ToUpperInvariant() is "WIRELESS GAMING KEYBOARD";
    internal static string ReceiverModel(string product) =>
        RainyName(product) || RainyReceiverName(product) ? "WOBKEY Rainy 75" : null;
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
        string model=atk?"ATK ZERO":ReceiverModel(product);
        bool identified=atk && !receiver;
        string trace="";long deadline=link.Now+4500;
        byte[] Query(byte[] tx,byte command)
        {
            if(link.Now>=deadline){trace=" 总时长超限";return null;}
            bool drained=false;
            for(int i=0;i<32;i++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            if(!drained){trace=" 输入队列未清空，未发送";return null;}
            if(!link.Write(tx,300)){trace=" TX 写入失败";return null;}
            bool sawAny=false;long until=Math.Min(deadline,link.Now+650);
            while(link.Now<until)
            {
                var raw=link.Read((int)Math.Max(1,Math.Min(100,until-link.Now)));
                if(raw is not {Length:>0})continue;
                sawAny=true;
                trace=" RX="+Convert.ToHexString(raw.AsSpan(0,Math.Min(64,raw.Length)));
                if(raw.AsSpan().SequenceEqual(tx))continue;
                if(atk && (raw.Length!=64 || raw[2]!=0))continue;
                // Rainy bytes1/2 are a checksum, not ATK's request nonce.
                // Native transport correlates command3; do not require checksum echo.
                if(!atk && raw.Length!=64)continue;
                if(atk?VendorProtocol.AtkReplyOffset(raw,command)>=0:
                    VendorProtocol.TryParseRainy75OfficialBattery(raw,out _,out _))return raw;
            }
            if(!sawAny)trace=$" TX={Convert.ToHexString(tx.AsSpan(0,Math.Min(8,tx.Length)))} 无任何应答";
            return null;
        }
        ReferenceBatteryProtocol.Reading Fail(string why)=>ReferenceBatteryProtocol.Reading.Failed(why+trace) with {Model=atk&&!identified?null:model};
        if(atk && receiver)
        {
            for(int i=0;i<2;i++)if(!VendorProtocol.IsAtkZeroPairedModel(Query(VendorProtocol.BuildAtkZeroRequest(0x89,true),0x89)))
                return Fail("ZERO接收器未取得两次CID/MID=1/1；不按公共接收器确认型号");
            identified=true;
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
        // 型号可以由接收器产品串推定，但充电语义只有产品串明确是 Rainy 时才算已知。
        else {known=RainyName(product);if(!known)charging=false;}
        return new(level,charging,known,charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,
            (atk?"ZERO两次电量响应":"Evision两次有效电量响应；型号按接收器产品串推定，待实机确认")+
            (known?"；充电字段已知":"；充电状态未知")+"；不推断独立RF在线位"+trace,model);
    }
}
