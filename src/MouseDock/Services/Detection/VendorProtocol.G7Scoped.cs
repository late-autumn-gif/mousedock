using System;
using System.IO;
using System.Runtime.InteropServices;
namespace MouseDock.Services.Detection;
internal static partial class VendorProtocol
{
    internal static bool IsScopedG7(ushort vid,ushort pid,string product) => IsMchoseG7(vid,pid,product) &&
        ((vid==0x3837 && pid is 0x1014 or 0x1018) || (vid==0xA8A4 && pid==0x2255));
    internal static bool IsScopedG7Channel(ushort vid,ushort pid,string name,ushort page,ushort usage,int input,int output,int feature) =>
        IsScopedG7(vid,pid,name) && page==0xFF01 && usage==0x10 && input==output && input is 64 or 65 && feature==0;
    internal static bool TryScopedG7Reply(byte[] raw,int size,out int level,out bool charging)
    {
        level=-1;charging=false;
        if(raw==null || raw.Length!=size || size is not (64 or 65) || (size==65 && raw[0]!=0))return false;
        return TryParseMchoseG7Battery(raw,out level,out charging);
    }
    internal static ReferenceBatteryProtocol.Reading ReadG7Exchange(ReferenceBatteryProtocol.ILink link,int size,string product)
    {
        if(size is not (64 or 65))return ReferenceBatteryProtocol.Reading.Failed("不支持的G7报告布局");
        var packet=BuildMchoseG7BatteryRequest(size);int level=-1;bool charging=false;string trace="";
        for(int attempt=0;attempt<2;attempt++)
        {
            bool drained=false;
            for(int i=0;i<32;i++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            if(!drained)return ReferenceBatteryProtocol.Reading.Failed("G7旧队列未清空");
            if(!link.Write(packet,300))return ReferenceBatteryProtocol.Reading.Failed("G7请求未完整发送");
            long until=link.Now+1000;bool found=false;string last="无";
            while(link.Now<until)
            {
                var raw=link.Read((int)Math.Max(1,Math.Min(100,until-link.Now)));
                if(raw is not {Length:>0})continue;
                last=Convert.ToHexString(raw.AsSpan(0,Math.Min(65,raw.Length)));
                if(raw.AsSpan().SequenceEqual(packet) || !TryScopedG7Reply(raw,size,out level,out charging))continue;
                found=true;trace+=" RX="+last;break;
            }
            if(!found)return ReferenceBatteryProtocol.Reading.Failed($"G7第{attempt+1}/2次缺少有效响应，最近RX={last}"+trace);
        }
        return new(level,charging,true,charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,
            "G7两次本轮电量响应；不推断独立RF在线位"+trace,product.Trim());
    }
    internal static ReferenceBatteryProtocol.Reading ReadScopedG7(string path,ushort vid,ushort pid)
    {
        if(InstanceScope.Current==null || !InstanceScope.Allows(path))return ReferenceBatteryProtocol.Reading.Failed("G7实例不符");
        return ReferenceQueryGate.Run(path,()=>
        {
            IntPtr h=CreateFile(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,IntPtr.Zero);
            if(h==IntPtr.Zero || h==new IntPtr(-1))throw new IOException("G7专用接口打开失败");
            IntPtr ev=IntPtr.Zero;
            try
            {
                var a=new HIDD_ATTRIBUTES{Size=Marshal.SizeOf<HIDD_ATTRIBUTES>()};var c=Caps(h);string product=HidDiscovery.ReadHidProduct(h,path);
                if(!HidD_GetAttributes(h,ref a) || a.VendorID!=vid || a.ProductID!=pid || !IsScopedG7Channel(vid,pid,product,c.UsagePage,c.Usage,c.InputReportByteLength,c.OutputReportByteLength,c.FeatureReportByteLength))throw new IOException("G7实际型号/集合不符");
                if(!HidD_GetPreparsedData(h,out var prep))throw new IOException("无法核对G7报告能力");
                try
                {
                    bool Check(int type,ushort values,ushort buttons,byte expected) => values+buttons==0 ||
                        (TryGetReportId(prep,type,values,buttons,out byte id) && id==expected);
                    if(!Check(0,c.NumberInputValueCaps,c.NumberInputButtonCaps,c.InputReportByteLength==65?(byte)0:(byte)0xAA) ||
                        !Check(1,c.NumberOutputValueCaps,c.NumberOutputButtonCaps,c.OutputReportByteLength==65?(byte)0:(byte)0x55))throw new IOException("G7报告编号与所选协议布局不符或不唯一");
                }
                finally{HidD_FreePreparsedData(prep);}
                ev=CreateEvent(IntPtr.Zero,true,false,IntPtr.Zero);if(ev==IntPtr.Zero)throw new IOException("G7事件创建失败");
                return ReadG7Exchange(new G7Link(h,ev,c.InputReportByteLength),c.InputReportByteLength,product);
            }
            finally{if(ev!=IntPtr.Zero)CloseHandle(ev);CloseHandle(h);}
        },3500);
    }
    private sealed class G7Link(IntPtr handle,IntPtr signal,int size):ReferenceBatteryProtocol.ILink
    {
        public long Now=>Environment.TickCount64;
        public byte[] Read(int timeout){var b=new byte[size];return Transfer(handle,signal,b,false,timeout,out int n)&&n>0?b.AsSpan(0,n).ToArray():null;}
        public bool Write(byte[] b,int timeout)=>Transfer(handle,signal,b,true,timeout,out int n)&&n==b.Length;
        public bool SetFeature(byte[] b)=>throw new NotSupportedException();
        public byte[] GetFeature(byte id,int length)=>throw new NotSupportedException();
        public void Delay(int ms)=>throw new NotSupportedException();
    }
}
