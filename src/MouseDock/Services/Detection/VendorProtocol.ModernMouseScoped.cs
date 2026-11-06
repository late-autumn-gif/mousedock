using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
namespace MouseDock.Services.Detection;
internal static partial class VendorProtocol
{
    internal static readonly Dictionary<string,(ushort Wired,ushort[] Receivers)> ModernMouseProfiles=new(StringComparer.OrdinalIgnoreCase)
    {
        ["MCHOSE A5 V3 Pro"]=(0x4035, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A5 V3 Ultra"]=(0x4042, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A5 V3 Ultra+"]=(0x4026, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A5 V3 Ultra+(3955)"]=(0x4034, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K7 V2 Pro"]=(0x403b, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K7 V2 Pro+"]=(0x4027, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K7 V2 Ultra"]=(0x403c, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K7 V2 Ultra+"]=(0x4028, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3"]=(0x4030, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3 Pro"]=(0x4031, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3 Pro+"]=(0x4032, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3 Ultra"]=(0x403d, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3 Ultra+"]=(0x4033, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3-X"]=(0x403a, new ushort[]{0x1014,0x1018}),
        ["MCHOSE A7 V3 Magnetic"]=(0x403f, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K5 V2"]=(0x4040, new ushort[]{0x101c}),
        ["MCHOSE K5 Pro"]=(0x4037, new ushort[]{0x1014,0x1018}),
        ["MCHOSE K5 Ultra"]=(0x4038, new ushort[]{0x1014,0x1018}),
        ["MCHOSE R7 Ultra"]=(0x4036, new ushort[]{0x1014,0x1018}),
    };
    internal static bool IsScopedModernMouse(ushort vid,ushort pid,string product)=>vid==0x3837&&ModernMouseProfiles.TryGetValue((product??"").Trim(),out var p)&&(pid==p.Wired||p.Receivers.Contains(pid));
    internal static bool IsScopedModernChannel(ushort vid,ushort pid,string name,ushort page,ushort usage,int input,int output,int feature)=>
        IsScopedModernMouse(vid,pid,name)&&page==0xFF01&&usage==1&&input==64&&output==64&&feature==64;
    internal static bool TryModernScopedFrame(byte[] raw,string product,out int level,out bool charging)
    {
        level=-1;charging=false;
        if(raw==null||raw.Length!=64||!ModernMouseProfiles.TryGetValue((product??"").Trim(),out var p)||raw[0]!=0x4D||raw[1]!=1||raw[2]!=1||raw[3]<14||raw[3]>55||raw[4]!=0||raw[5]!=9||raw[6]!=0||raw[7]!=0)return false;
        byte check=0;for(int i=2;i<8+raw[3];i++)check^=raw[i];
        if(check!=raw[8+raw[3]]||raw[8]!=0x37||raw[9]!=0x38||(raw[10]|raw[11]<<8)!=p.Wired||raw[19]>1||raw[20]>100)return false;
        level=raw[20];charging=raw[19]==1;return true;
    }
    internal static ReferenceBatteryProtocol.Reading ReadModernMouseExchange(ReferenceBatteryProtocol.ILink link,string product)
    {
        if(!ModernMouseProfiles.ContainsKey((product??"").Trim()))return ReferenceBatteryProtocol.Reading.Failed("未收录的MHUB型号");
        long deadline=link.Now+3000;int level=-1;bool charging=false;string trace="";
        for(int round=0;round<2;round++)
        {
            bool drained=false;for(int i=0;i<32;i++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            if(!drained||link.Now>=deadline)return ReferenceBatteryProtocol.Reading.Failed("MHUB旧输入未排空或超时");
            var tx=BuildMchoseModernRequest();if(!link.Write(tx,300))return ReferenceBatteryProtocol.Reading.Failed("MHUB请求未完整发送");
            long end=Math.Min(deadline,link.Now+1000);bool ok=false;
            while(link.Now<end){var raw=link.Read((int)Math.Max(1,Math.Min(100,end-link.Now)));if(raw is not {Length:>0})continue;
                trace=" RX="+Convert.ToHexString(raw.AsSpan(0,Math.Min(raw.Length,64)));
                if(!raw.AsSpan().SequenceEqual(tx)&&TryModernScopedFrame(raw,product,out level,out charging)){ok=true;break;}}
            if(!ok)return ReferenceBatteryProtocol.Reading.Failed($"MHUB第{round+1}/2次未取得匹配型号且校验正确的设备信息"+trace);
        }
        return new(level,charging,true,charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,
            "MHUB4D/0900两次身份匹配百分比；connectStatus语义未确认，不推断RF在线"+trace,product.Trim(),product.Trim());
    }
    internal static ReferenceBatteryProtocol.Reading ReadScopedModernMouse(string path,ushort vid,ushort pid)
    {
        if(InstanceScope.Current==null||!InstanceScope.Allows(path))return ReferenceBatteryProtocol.Reading.Failed("MHUB物理实例不符");
        return ReferenceQueryGate.Run(path,()=>{
            IntPtr h=CreateFile(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,IntPtr.Zero);
            if(h==IntPtr.Zero||h==new IntPtr(-1))throw new IOException("MHUB接口打开失败");
            IntPtr signal=IntPtr.Zero;
            try{
                var a=new HIDD_ATTRIBUTES{Size=Marshal.SizeOf<HIDD_ATTRIBUTES>()};var c=Caps(h);string product=HidDiscovery.ReadHidProduct(h,path);
                if(!HidD_GetAttributes(h,ref a)||a.VendorID!=vid||a.ProductID!=pid||!IsScopedModernChannel(vid,pid,product,c.UsagePage,c.Usage,c.InputReportByteLength,c.OutputReportByteLength,c.FeatureReportByteLength))throw new IOException("MHUB打开后身份/集合不符");
                if(!HidD_GetPreparsedData(h,out var prep))throw new IOException("MHUB报告描述符不可读");
                try{
                    bool Check(int type,ushort values,ushort buttons)=>values+buttons==0||(TryGetReportId(prep,type,values,buttons,out byte id)&&id==0x4D);
                    if(!Check(0,c.NumberInputValueCaps,c.NumberInputButtonCaps)||!Check(1,c.NumberOutputValueCaps,c.NumberOutputButtonCaps))throw new IOException("MHUB报告编号不是唯一4D");
                }finally{HidD_FreePreparsedData(prep);}
                signal=CreateEvent(IntPtr.Zero,true,false,IntPtr.Zero);if(signal==IntPtr.Zero)throw new IOException("MHUB事件创建失败");
                return ReadModernMouseExchange(new G7Link(h,signal,64),product);
            }finally{if(signal!=IntPtr.Zero)CloseHandle(signal);CloseHandle(h);}
        },4000);
    }
}
