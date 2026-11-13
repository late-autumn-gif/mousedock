using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MouseDock.Services.Detection;

internal sealed record DpiSignature(ushort Page, ushort Usage, int Input, int Output, int Feature,
    IReadOnlySet<byte> InputIds, IReadOnlySet<byte> OutputIds, IReadOnlySet<byte> FeatureIds,
    IReadOnlyDictionary<byte,int> FeatureValueByteLowerBounds)
{
    internal string Key => $"{Page:X4}/{Usage:X4}/{Input}/{Output}/{Feature}/" +
        string.Join(",",InputIds.Order())+"/"+string.Join(",",OutputIds.Order())+"/"+
        string.Join(",",FeatureIds.Order())+"/"+string.Join(",",FeatureValueByteLowerBounds.OrderBy(p=>p.Key).Select(p=>$"{p.Key}:{p.Value}"));
    internal bool Main(DpiProfile profile, int usbInterface)
    {
        if(Page!=profile.Page||Usage!=profile.Usage)return false;
        return profile.Family switch
        {
            DpiFamily.Atk or DpiFamily.Z7 or DpiFamily.Catalog => Input==17&&Output==17&&InputIds.SetEquals(new byte[]{8})&&OutputIds.SetEquals(new byte[]{8}),
            DpiFamily.Pulsar => usbInterface==1&&Input==17&&Output==17&&InputIds.SetEquals(new byte[]{8})&&OutputIds.SetEquals(new byte[]{8}),
            DpiFamily.Scyrox => Input==64&&Output==64&&InputIds.SetEquals(new byte[]{8})&&OutputIds.SetEquals(new byte[]{8}),
            DpiFamily.Sora => false,
            DpiFamily.Rapoo => Input is 19 or 20&&InputIds.Contains(7),
            _=>false
        };
    }
    internal bool RapooOutput => Page==0xFF00&&Usage==14&&Output==33&&OutputIds.SetEquals(new byte[]{6});
    internal bool RapooFeature => Page==0xFF00&&Usage==15&&Feature is 32 or 33&&FeatureIds.SetEquals(new byte[]{8});
}

internal sealed record DpiChannel(string InstanceId,string Path,ushort Vid,ushort Pid,int Interface,DpiSignature Signature);
internal sealed record DpiConnection(DpiProfile Profile,string InstanceId,DpiChannel Main,DpiChannel Output,DpiChannel Feature)
{
    internal string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|",new[]{Main,Output,Feature}.Select(c=>c==null?"":c.Path.ToUpperInvariant()+"/"+c.Signature.Key)))));

    internal static DpiConnection Resolve(string instance,ushort vid,ushort pid,IEnumerable<DpiChannel> channels)
    {
        var profile=DpiProfiles.Native(vid,pid)??throw new InvalidOperationException("此型号尚未开放原生 DPI 控制");
        var selected=channels.Where(c=>c.Vid==vid&&c.Pid==pid&&c.InstanceId==instance).ToArray();
        DpiChannel One(Func<DpiChannel,bool> predicate,bool optional=false)
        {
            var matches=selected.Where(predicate).GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
            if(matches.Length>1)throw new InvalidOperationException("发现多个 DPI 控制接口，未选择写入通道");
            return matches.Length==1?matches[0]:optional?null:throw new InvalidOperationException("没有满足报告能力的 DPI 接口");
        }
        var main=One(c=>c.Signature.Main(profile,c.Interface));
        if(profile.Family!=DpiFamily.Rapoo)return new(profile,instance,main,main,main);
        var output=One(c=>c.Interface==main.Interface&&c.Signature.RapooOutput,true);
        var feature=One(c=>c.Interface==main.Interface&&c.Signature.RapooFeature,true);
        return new(profile,instance,main,output,feature);
    }
}

public static partial class HidDiscovery
{
    internal static T RunDpiOperation<T>(string instance, Func<T> operation)
    {
        lock (ScanGate)
        {
            using var lease = HidIoLease.TryAcquire(instance);
            if (lease == null) throw new IOException("此设备的上一项通信尚未结束，请稍后重新读取");
            return operation();
        }
    }

    // Metadata only: this path does not invoke the battery scan or issue vendor commands.
    internal static IReadOnlyList<DpiChannel> EnumerateDpiChannels(ushort vid,ushort pid)
    {
        var result=new List<DpiChannel>();
        Guid guid=GUID_DEVINTERFACE_HID;
        IntPtr set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
        if(set==IntPtr.Zero||set==new IntPtr(-1))throw new IOException("无法枚举 HID 接口");
        try
        {
            for(uint index=0;;index++)
            {
                var item=new SP_DEVICE_INTERFACE_DATA{cbSize=Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,index,ref item))break;
                var info=new SP_DEVINFO_DATA{cbSize=Marshal.SizeOf<SP_DEVINFO_DATA>()};
                SetupDiGetDeviceInterfaceDetail(set,ref item,IntPtr.Zero,0,out int required,ref info);
                if(required<8||required>65536)continue;
                IntPtr detail=Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref item,detail,required,out _,ref info))continue;
                    string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                    if(string.IsNullOrEmpty(path)||path.Contains("BTH",StringComparison.OrdinalIgnoreCase))continue;
                    HidInventory.ParsePath(path,out ushort candidateVid,out ushort candidatePid,out string label);
                    if(candidateVid!=vid||candidatePid!=pid)continue;
                    var ancestors=ReadAncestors(info.DevInst,out bool integrated);
                    if(ExclusionReason(ancestors,integrated)!=null)continue;
                    string instance=InstanceScope.Register(path,ancestors);
                    if(!ancestors.Any(a=>a.InstanceId.StartsWith("USB\\VID_",StringComparison.OrdinalIgnoreCase)&&
                        !a.InstanceId.Contains("&MI_",StringComparison.OrdinalIgnoreCase)))continue;
                    IntPtr handle=CreateFile(path,0,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,0,IntPtr.Zero);
                    if(handle==IntPtr.Zero||handle==new IntPtr(-1))continue;
                    try
                    {
                        var attr=new HIDD_ATTRIBUTES{Size=Marshal.SizeOf<HIDD_ATTRIBUTES>()};
                        if(!HidD_GetAttributes(handle,ref attr)||attr.VendorID!=vid||attr.ProductID!=pid)continue;
                        var signature=DpiNativeLink.ReadSignature(handle);
                        int usbInterface=-1;
                        if(label.Length>=5)int.TryParse(label.AsSpan(3,2),System.Globalization.NumberStyles.HexNumber,null,out usbInterface);
                        result.Add(new(instance,path,vid,pid,usbInterface,signature));
                    }
                    catch(IOException) { }
                    finally {CloseHandle(handle);}
                }
                finally {Marshal.FreeHGlobal(detail);}
            }
        }
        finally {SetupDiDestroyDeviceInfoList(set);}
        return result;
    }
}

internal sealed class DpiNativeLink : IDpiLink,IDisposable
{
    private readonly DpiConnection connection;
    private readonly Action guard;
    private readonly Dictionary<string,(IntPtr Handle,IntPtr Event)> handles=new(StringComparer.OrdinalIgnoreCase);
    private const uint ReadAccess=0x80000000,WriteAccess=0x40000000,Overlapped=0x40000000;
    internal DpiNativeLink(DpiConnection connection,Action guard=null)
    {
        if(connection.Profile.Family==DpiFamily.Sora)
            throw new IOException("Sora DPI 报告布局尚未核对，未开放原生控制");
        this.connection=connection;this.guard=guard??(()=>{});
        try
        {
            foreach(var channel in new[]{connection.Main,connection.Output,connection.Feature}.Where(c=>c!=null).DistinctBy(c=>c.Path,StringComparer.OrdinalIgnoreCase))
            {
                bool featureOnly=connection.Profile.Family==DpiFamily.Sora||channel==connection.Feature&&connection.Profile.Family==DpiFamily.Rapoo;
                uint access=connection.Profile.Family==DpiFamily.Rapoo&&channel==connection.Main?ReadAccess:ReadAccess|WriteAccess;
                IntPtr h=CreateFile(channel.Path,access,3,IntPtr.Zero,3,featureOnly?0:Overlapped,IntPtr.Zero);
                if(h==IntPtr.Zero||h==new IntPtr(-1))throw new IOException("DPI 接口打开失败 Win32="+Marshal.GetLastWin32Error());
                IntPtr signal=IntPtr.Zero;
                try
                {
                    var a=new Attributes{Size=Marshal.SizeOf<Attributes>()};
                    if(!HidD_GetAttributes(h,ref a)||a.Vid!=channel.Vid||a.Pid!=channel.Pid||
                        ReadSignature(h).Key!=channel.Signature.Key)throw new IOException("打开后的 DPI 接口身份或报告能力变化");
                    if(!featureOnly)
                    {
                        signal=CreateEvent(IntPtr.Zero,true,false,IntPtr.Zero);
                        if(signal==IntPtr.Zero)throw new IOException("无法创建 DPI 通信事件");
                    }
                    handles.Add(channel.Path,(h,signal));
                }
                catch {if(signal!=IntPtr.Zero)CloseHandle(signal);CloseHandle(h);throw;}
            }
        }
        catch {Dispose();throw;}
    }
    public long Now=>Environment.TickCount64;
    public byte[] Read(int timeoutMs)
    {
        guard();
        var channel=connection.Main;
        if(channel.Signature.Input<=0)return null;
        var target=handles[channel.Path];var b=new byte[channel.Signature.Input];
        return VendorProtocol.Transfer(target.Handle,target.Event,b,false,Math.Max(1,timeoutMs),out int got)&&got>0?b.AsSpan(0,got).ToArray():null;
    }
    public bool Write(byte[] packet,int timeoutMs)
    {
        bool release=connection.Profile.Family==DpiFamily.Pulsar&&packet is {Length:17}&&
            packet[0]==8&&packet[1]==3&&packet[6]==0;
        if(!release)guard();
        var channel=connection.Output??throw new IOException("没有独立的 DPI 输出接口");
        if(packet==null||packet.Length!=channel.Signature.Output||!channel.Signature.OutputIds.Contains(packet[0]))
            throw new IOException("DPI 输出报告编号或长度未获批准");
        var target=handles[channel.Path];
        return VendorProtocol.Transfer(target.Handle,target.Event,packet,true,timeoutMs,out int got)&&got==packet.Length;
    }
    public bool SetFeature(byte[] packet)
    {
        guard();
        throw new IOException("DPI Feature 写入尚未取得精确报告布局，未开放原生控制");
    }
    public byte[] GetFeature(byte reportId,int length)
    {
        guard();
        var channel=connection.Feature??throw new IOException("没有独立的 DPI Feature 接口");
        bool rapoo=connection.Profile.Family==DpiFamily.Rapoo&&reportId==8&&length==33;
        if(!rapoo)throw new IOException("此 DPI Feature 读取未开放");
        var b=new byte[channel.Signature.Feature];b[0]=reportId;
        if(!HidD_GetFeature(handles[channel.Path].Handle,b,b.Length))throw new IOException("DPI Feature 读取失败 Win32="+Marshal.GetLastWin32Error());
        return b.AsSpan(0,Math.Min(length,b.Length)).ToArray();
    }
    public void Delay(int milliseconds)=>System.Threading.Thread.Sleep(milliseconds);
    public void Dispose()
    {
        foreach(var item in handles.Values){if(item.Event!=IntPtr.Zero)CloseHandle(item.Event);CloseHandle(item.Handle);}
        handles.Clear();
    }

    internal static DpiSignature ReadSignature(IntPtr h)
    {
        if(!HidD_GetPreparsedData(h,out var prep))throw new IOException("无法读取 DPI 报告能力");
        try
        {
            var c=new Caps();
            if(HidP_GetCaps(prep,ref c)!=0x110000)throw new IOException("DPI 报告能力无效");
            var input=VendorProtocol.ReadDescriptorReportIds(prep,0,c.InputValues,c.InputButtons);
            var output=VendorProtocol.ReadDescriptorReportIds(prep,1,c.OutputValues,c.OutputButtons);
            var feature=VendorProtocol.ReadDescriptorReportIds(prep,2,c.FeatureValues,c.FeatureButtons);
            var lowerBounds=new Dictionary<byte,int>();
            if(c.FeatureValues>0)
            {
                ushort count=c.FeatureValues;var buffer=new byte[count*72];
                if(HidP_GetValueCaps(2,buffer,ref count,prep)==0x110000&&count<=c.FeatureValues)
                {
                    lowerBounds=FeatureValueByteLowerBounds(buffer,count);
                }
            }
            return new(c.Page,c.Usage,c.Input,c.Output,c.Feature,input,output,feature,lowerBounds);
        }
        finally {HidD_FreePreparsedData(prep);}
    }
    // Value caps omit constant fields. These totals are evidence lower bounds, never exact report sizes.
    internal static Dictionary<byte,int> FeatureValueByteLowerBounds(byte[] caps,int count)
    {
        if(caps==null||count<0||count>caps.Length/72)throw new IOException("DPI Feature 能力数据无效");
        var bits=new Dictionary<byte,int>();
        for(int i=0;i<count;i++)
        {
            int offset=i*72;
            if(caps[offset+3]!=0)throw new IOException("DPI Feature 能力存在别名，未采用");
            int size=caps[offset+18]|caps[offset+19]<<8,items=caps[offset+20]|caps[offset+21]<<8;
            byte id=caps[offset+2];
            long sum=bits.GetValueOrDefault(id)+(long)size*items;
            if(sum>int.MaxValue-7)throw new IOException("DPI Feature 能力大小无效");
            bits[id]=(int)sum;
        }
        return bits.ToDictionary(p=>p.Key,p=>1+(p.Value+7)/8);
    }
    [StructLayout(LayoutKind.Sequential)]private struct Attributes{public int Size;public ushort Vid,Pid,Version;}
    [StructLayout(LayoutKind.Sequential)]private struct Caps
    {
        public ushort Usage,Page,Input,Output,Feature;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)]public ushort[] Reserved;
        public ushort Nodes,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]private static extern IntPtr CreateEvent(IntPtr security,bool manual,bool initial,IntPtr name);
    [DllImport("hid.dll")]private static extern bool HidD_GetAttributes(IntPtr h,ref Attributes a);
    [DllImport("hid.dll")]private static extern bool HidD_GetPreparsedData(IntPtr h,out IntPtr prep);
    [DllImport("hid.dll")]private static extern bool HidD_FreePreparsedData(IntPtr prep);
    [DllImport("hid.dll")]private static extern int HidP_GetCaps(IntPtr prep,ref Caps caps);
    [DllImport("hid.dll")]private static extern int HidP_GetValueCaps(int type,byte[] caps,ref ushort length,IntPtr prep);
    [DllImport("hid.dll",SetLastError=true)]private static extern bool HidD_GetFeature(IntPtr h,byte[] b,int length);
}
