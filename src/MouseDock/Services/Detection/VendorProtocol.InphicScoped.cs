using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;
internal static partial class VendorProtocol
{
    internal static bool IsScopedInphic(ushort vid, ushort pid, string product) =>
        IsInphicDevice(vid,pid,product) && ((vid is 0x248A or 0x249A) || IsInphicSplitReceiver(vid,pid));
    internal static bool IsScopedInphicChannel(ushort vid,ushort pid,string product,string path,ushort page,ushort usage,int input,int output) =>
        IsScopedInphic(vid,pid,product) && (IsInphicSplitReceiver(vid,pid)
            ? page==0x000A && usage==0 && input==5 && output==0
            : page>=0xFF00 && input==33 && output==33 && path.Contains("&mi_02",StringComparison.OrdinalIgnoreCase));
    internal static bool IsInphicWakeCollection(ushort vid, ushort pid, string product, ushort page, ushort usage, int feature) =>
        vid == 0x1D57 && pid == 0xFA60 && IsInphicDevice(vid, pid, product) &&
        page == 0x000B && usage == 0 && feature == 262;

    internal static bool IsInphicWakeSibling(string inputPath, string featurePath)
    {
        string parent = InphicCollectionParent(inputPath);
        return parent != null && string.Equals(parent, InphicCollectionParent(featurePath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryInitializeInphic(string path, ushort vid, ushort pid)
    {
        IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        try
        {
            var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            var caps = Caps(handle);
            if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != vid || attributes.ProductID != pid ||
                !IsInphicWakeCollection(vid, pid, HidDiscovery.ReadHidProduct(handle, path), caps.UsagePage, caps.Usage, caps.FeatureReportByteLength))
                return false;
            var request = BuildInphicWakeRequest(caps.FeatureReportByteLength);
            return HidD_SetFeature(handle, request, request.Length);
        }
        finally { CloseHandle(handle); }
    }

    internal static bool TryScopedInphicFrame(byte[] raw,bool pro,out ReferenceBatteryProtocol.Reading reading)
    {
        reading=ReferenceBatteryProtocol.Reading.Failed("非目标型号/无效电量响应");
        if(pro)
        {
            if(raw is not {Length:5} || raw[1]!=149 || raw[2] is not (0x40 or 0x41) || raw[3] is <1 or >3 || raw[4]>100)return false;
            int level=raw[3]==2?100:raw[4];bool charging=raw[3]==3;
            reading=new(level,charging,true,raw[3]==2?ReferenceBatteryProtocol.State.Full:charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,"IN9 Pro 精确5字节状态","英菲克 IN9 Pro","I149");return true;
        }
        if(raw is not {Length:32 or 33} || (raw.Length==33 && raw[0]!=0) || raw[raw.Length==33?3:2]!=1 ||
            !TryParseInphicLegacy(raw,out int pct,out bool chg,out string model) || model=="M900")return false;
        // M900 是 A9 的代号，结构合法也不归 IN9；收录过的型号沿用原 IN9 抬头，新串按「英菲克 + 型号」上屏。
        string display=VendorMap.TryIdentityModel(model,out _,out _)?"英菲克 IN9":"英菲克 "+model;
        reading=new(pct,chg,true,chg?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,"IN9 有效实时身份",display,model);return true;
    }
    internal static ReferenceBatteryProtocol.Reading ReadScopedInphic(string path,ushort vid,ushort pid, string wakePath = null, BatteryReplay.Trace trace = null)
    {
        if(InstanceScope.Current==null || !InstanceScope.Allows(path))return ReferenceBatteryProtocol.Reading.Failed("实例不匹配");
        bool initialize = wakePath != null && InstanceScope.Allows(wakePath) && IsInphicWakeSibling(path, wakePath);
        return ReferenceQueryGate.Run(path,()=>
        {
            bool pro=IsInphicSplitReceiver(vid,pid);
            IntPtr h=CreateFile(path,pro?GENERIC_READ:GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,IntPtr.Zero);
            if(h==IntPtr.Zero || h==new IntPtr(-1))throw new IOException("英菲克专用接口打开失败");
            IntPtr ev=IntPtr.Zero;
            try
            {
                var a=new HIDD_ATTRIBUTES {Size=Marshal.SizeOf<HIDD_ATTRIBUTES>()};var c=Caps(h);
                if(!HidD_GetAttributes(h,ref a) || a.VendorID!=vid || a.ProductID!=pid ||
                    !IsScopedInphicChannel(vid,pid,HidDiscovery.ReadHidProduct(h,path),path,c.UsagePage,c.Usage,c.InputReportByteLength,c.OutputReportByteLength))throw new IOException("实际属性/集合不符");
                trace?.Add("open-and-validate", true);
                ev=CreateEvent(IntPtr.Zero,true,false,IntPtr.Zero);if(ev==IntPtr.Zero)throw new IOException("无法创建异步事件");
                ReferenceBatteryProtocol.Reading first=null;
                string last="无";
                if(pro)
                {
                    bool drained=false;
                    for(int i=0;i<32;i++){var old=new byte[5];if(!Transfer(h,ev,old,false,1,out int got)||got==0){drained=true;break;}}
                    if(!drained)return ReferenceBatteryProtocol.Reading.Failed("旧状态队列未清空，未采用历史上报");
                }
                string wakeStatus = !initialize ? "无匹配初始化集合" :
                    TryInitializeInphic(wakePath, vid, pid) ? "初始化成功" : "初始化失败";
                if (initialize) trace?.Add("initialize", wakeStatus == "初始化成功");
                for(int attempt=0;attempt<2;attempt++)
                {
                    if(!pro)
                    {
                        bool drained=false;
                        for(int i=0;i<32;i++){var old=new byte[33];if(!Transfer(h,ev,old,false,1,out int got) || got==0){drained=true;break;}}
                        if(!drained)return ReferenceBatteryProtocol.Reading.Failed("旧队列未清空");
                        var tx=new byte[33];tx[1]=0x10;
                        bool sent = Transfer(h, ev, tx, true, 300, out int written) && written == 33;
                        trace?.Add("write-identity", sent, raw: tx, keep: true);
                        if (!sent) return ReferenceBatteryProtocol.Reading.Failed("身份请求未完整发送");
                    }
                    long until=Environment.TickCount64+1500;ReferenceBatteryProtocol.Reading current=null;
                    while(Environment.TickCount64<until)
                    {
                        var b=new byte[pro?5:33];
                        if(!Transfer(h,ev,b,false,(int)Math.Max(1,Math.Min(150,until-Environment.TickCount64)),out int n) || n<=0)continue;
                        var raw=b.AsSpan(0,n).ToArray();last=Convert.ToHexString(raw);
                        bool decoded = TryScopedInphicFrame(raw, pro, out current);
                        if (trace != null)
                        {
                            int offset = raw.Length == 33 && raw[0] == 0 ? 1 : 0;
                            bool batteryHeader = pro ? raw.Length == 5 && raw[2] is 0x40 or 0x41 :
                                raw.Length - offset == 32 && raw[offset] == 0x10 &&
                                raw[offset + 1] == 0 && raw[offset + 2] == 1;
                            var evidence = batteryHeader && !pro ? new byte[raw.Length] : raw;
                            if (batteryHeader && !pro)
                            {
                                raw.AsSpan(0, offset + 8).CopyTo(evidence);
                                raw.AsSpan(offset + 12, 3).CopyTo(evidence.AsSpan(offset + 12));
                            }
                            trace.Add("input", decoded, raw: evidence, keep: batteryHeader,
                                result: decoded ? "accepted" : "rejected");
                        }
                        if (decoded) break;
                        current=null;
                    }
                    if(current==null)return ReferenceBatteryProtocol.Reading.Failed("未取得两次有效响应；"+wakeStatus+"；最近RX="+last);
                    if(first!=null && first.Identity!=current.Identity)return ReferenceBatteryProtocol.Reading.Failed("两次身份不同");
                    first=current;
                }
                return first with {Diagnostic=(pro?"IN9 Pro状态（"+wakeStatus+"）":"IN9身份命令10")+"；两次本轮有效响应；最近RX="+last};
            }
            finally{if(ev!=IntPtr.Zero)CloseHandle(ev);CloseHandle(h);}
        },4500);
    }
}
