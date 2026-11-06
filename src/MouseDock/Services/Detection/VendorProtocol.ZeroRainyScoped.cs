using System;
using System.IO;
using System.Runtime.InteropServices;
namespace MouseDock.Services.Detection;
internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadZeroRainy(string path,ushort vid,ushort pid)
    {
        if(InstanceScope.Current==null || !InstanceScope.Allows(path))return ReferenceBatteryProtocol.Reading.Failed("缺少精确设备实例");
        return ReferenceQueryGate.Run(path,()=>
        {
            IntPtr h=CreateFile(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,IntPtr.Zero);
            if(h==IntPtr.Zero || h==new IntPtr(-1))throw new IOException("ZERO/Rainy接口打开失败");
            IntPtr ev=IntPtr.Zero;
            try
            {
                var a=new HIDD_ATTRIBUTES{Size=Marshal.SizeOf<HIDD_ATTRIBUTES>()};var c=Caps(h);string product=HidDiscovery.ReadHidProduct(h,path);
                if(!HidD_GetAttributes(h,ref a) || a.VendorID!=vid || a.ProductID!=pid || !ZeroRainyProtocol.Channel(vid,pid,product,c.UsagePage,c.Usage,c.InputReportByteLength,c.OutputReportByteLength,c.FeatureReportByteLength))throw new IOException("ZERO/Rainy实际属性/集合不符");
                if(!HidD_GetPreparsedData(h,out var prep))throw new IOException("无法核对报告能力");
                try
                {
                    byte expected=IsAtkZeroBitMouse(vid,pid)?(byte)8:(byte)4;
                    bool Check(int type,ushort values,ushort buttons)=>values+buttons==0 ||
                        (TryGetReportId(prep,type,values,buttons,out byte id)&&id==expected);
                    if(!Check(0,c.NumberInputValueCaps,c.NumberInputButtonCaps)||!Check(1,c.NumberOutputValueCaps,c.NumberOutputButtonCaps))throw new IOException("报告编号与协议不符或不唯一");
                }
                finally{HidD_FreePreparsedData(prep);}
                ev=CreateEvent(IntPtr.Zero,true,false,IntPtr.Zero);if(ev==IntPtr.Zero)throw new IOException("事件创建失败");
                return ZeroRainyProtocol.Read(new ZeroRainyLink(h,ev),vid,pid,product);
            }
            finally{if(ev!=IntPtr.Zero)CloseHandle(ev);CloseHandle(h);}
        },5500);
    }
    private sealed class ZeroRainyLink(IntPtr h,IntPtr ev):ReferenceBatteryProtocol.ILink
    {
        public long Now=>Environment.TickCount64;
        public byte[] Read(int ms){var b=new byte[64];return Transfer(h,ev,b,false,ms,out int n)&&n>0?b.AsSpan(0,n).ToArray():null;}
        public bool Write(byte[] b,int ms)=>Transfer(h,ev,b,true,ms,out int n)&&n==b.Length;
        public bool SetFeature(byte[] b)=>throw new NotSupportedException();public byte[] GetFeature(byte id,int length)=>throw new NotSupportedException();public void Delay(int ms)=>throw new NotSupportedException();
    }
}
