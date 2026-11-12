using MouseDock.Services.Detection;
using MouseDock.Services;
using MouseDock.Models;
internal static class ModernMouseRegression
{
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now{get;private set;}public int Writes;public Queue<byte[]> Input=new();public Func<int,byte[][]> Reply=_=>Array.Empty<byte[]>();
        public byte[]? Read(int timeout){Now+=Math.Max(1,timeout);return Input.Count>0?Input.Dequeue():null;}
        public bool Write(byte[] tx,int timeout){Writes++;foreach(var frame in Reply(Writes))Input.Enqueue(frame);return true;}
        public bool SetFeature(byte[] b)=>throw new Exception();public byte[] GetFeature(byte id,int n)=>throw new Exception();public void Delay(int ms)=>throw new Exception();
    }
    private static byte[] Frame(ushort pid,int level,int ch)
    {
        var r=new byte[64];r[0]=0x4D;r[1]=1;r[2]=1;r[3]=14;r[5]=9;r[8]=0x37;r[9]=0x38;r[10]=(byte)pid;r[11]=(byte)(pid>>8);r[19]=(byte)ch;r[20]=(byte)level;CheckSum(r);return r;
    }
    private static void CheckSum(byte[] r){byte c=0;for(int i=2;i<8+r[3];i++)c^=r[i];r[8+r[3]]=c;}
    internal static int Run()
    {
        int count=0;void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
        foreach(var (name,p) in VendorProtocol.ModernMouseProfiles)
        {
            Check(DeviceRoutes.IsKnown(0x3837,p.Wired,name),"Wired exact model admitted");
            foreach(var receiver in p.Receivers)Check(VendorProtocol.IsScopedModernMouse(0x3837,receiver,name),"Official receiver admitted");
            Check(!VendorProtocol.IsScopedModernMouse(0x3837,p.Wired,name+" MAX"),"Suffix cannot expand model");
            for(int level=0;level<=100;level++)for(int ch=0;ch<2;ch++)
            {
                var raw=Frame(p.Wired,level,ch);
                Check(VendorProtocol.TryModernScopedFrame(raw,name,out int value,out bool charging)&&value==level&&charging==(ch==1),"Official percentage/charge fields");
            }
            var wrong=Frame(0x1018,76,0);Check(!VendorProtocol.TryModernScopedFrame(wrong,name,out _,out _),"Receiver ID is not paired model proof");
            wrong=Frame(p.Wired,101,0);Check(!VendorProtocol.TryModernScopedFrame(wrong,name,out _,out _),"Out of range");
            wrong=Frame(p.Wired,76,0);wrong[6]=1;CheckSum(wrong);Check(!VendorProtocol.TryModernScopedFrame(wrong,name,out _,out _),"Unexpected business status");
            wrong=Frame(p.Wired,76,0);wrong[7]=1;CheckSum(wrong);Check(!VendorProtocol.TryModernScopedFrame(wrong,name,out _,out _),"Unrelated sequence");
            wrong=Frame(p.Wired,76,0);wrong[20]^=1;Check(!VendorProtocol.TryModernScopedFrame(wrong,name,out _,out _),"Bad XOR");
            Check(!VendorProtocol.IsScopedModernChannel(0x3837,p.Wired,name,0xFF0B,0xC4,61,61,61),"Auxiliary61 collection not queried");
            var link=new Link{Reply=n=>new[]{VendorProtocol.BuildMchoseModernRequest(),Frame(p.Wired,n==1?35:76,0)}};
            Check(VendorProtocol.ReadModernMouseExchange(link,name).Level==76&&link.Writes==2,"Two fresh model-specific transactions");
            link=new Link{Reply=n=>n==1?new[]{Frame(p.Wired,76,0)}:Array.Empty<byte[]>()};
            Check(VendorProtocol.ReadModernMouseExchange(link,name).Level<0,"Missing second reply not promoted");
        }
        Check(!DeviceRoutes.IsKnown(0x3837,0x1018,"USB Receiver"),"Generic receiver not sufficient");
        Check(VendorProtocol.IsScopedModernChannel(0x3837,0x1018,"MCHOSE K5 Ultra",0xFF01,1,64,64,0),
            "K5 Ultra Output/Input collection does not require Feature");
        Check(!VendorProtocol.IsScopedModernChannel(0x3837,0x1018,"MCHOSE K5 Pro",0xFF01,1,64,64,0),
            "Other receivers retain their collection constraints");
        Check(VendorProtocol.AllowsModernReport(new HashSet<byte> { 0x4D, 0x4E }, 2, true) &&
            !VendorProtocol.AllowsModernReport(new HashSet<byte> { 0x4D, 0x4E }, 2, false) &&
            !VendorProtocol.AllowsModernReport(new HashSet<byte> { 0 }, 1, true),
            "K5 Ultra accepts 4D alongside another ID but never guesses 4D");
        const string a7V3Pro = "MCHOSE A7 V3 Pro";
        HashSet<byte> proInput = [0x11, 0x13, 0x4D];
        HashSet<byte> proOutput = [0x13, 0x4D];
        Check(VendorProtocol.AllowsA7V3ProReports(0x3837, 0x1018, a7V3Pro,
            "MI_02/COL_02", proInput, proOutput), "A7 V3 Pro admits the observed receiver report IDs");
        Check(VendorProtocol.AllowsA7V3ProReports(0x3837, 0x4031, a7V3Pro,
            "MI_02/COL_02", proInput, proOutput), "A7 V3 Pro admits the observed direct USB report IDs");
        Check(!VendorProtocol.AllowsA7V3ProReports(0x3837, 0x1018, "MCHOSE A7 V3 Pro+",
            "MI_02/COL_02", proInput, proOutput), "A7 V3 Pro receiver rule does not match Pro+");
        Check(!VendorProtocol.AllowsA7V3ProReports(0x3837, 0x1014, a7V3Pro,
            "MI_02/COL_02", proInput, proOutput), "A7 V3 Pro receiver rule requires the measured receiver PID");
        Check(!VendorProtocol.AllowsA7V3ProReports(0x3837, 0x1018, a7V3Pro,
            "MI_02/COL_01", proInput, proOutput), "A7 V3 Pro receiver rule rejects the auxiliary collection");
        Check(!VendorProtocol.AllowsA7V3ProReports(0x3837, 0x1018, a7V3Pro,
            "MI_02/COL_02", proInput, [0x13]), "A7 V3 Pro receiver rule still requires 4D in both directions");
        var proWireless = Frame(0x4026, 83, 0);
        proWireless[18] = 2;
        CheckSum(proWireless);
        Check(VendorProtocol.TryModernScopedFrame(proWireless, a7V3Pro, out int proLevel, out bool proCharging, 2) &&
            proLevel == 83 && !proCharging, "A7 V3 Pro accepts observed wireless firmware identity in scope");
        Check(!VendorProtocol.TryModernScopedFrame(proWireless, a7V3Pro, out _, out _) &&
            !VendorProtocol.TryModernScopedFrame(proWireless, a7V3Pro, out _, out _, 1) &&
            !VendorProtocol.TryModernScopedFrame(proWireless, "MCHOSE A7 V3 Pro+", out _, out _, 2),
            "Firmware identity requires matching descriptor scope, transport and model");
        var proWired = Frame(0x4026, 96, 1);
        proWired[18] = 1;
        CheckSum(proWired);
        Check(VendorProtocol.TryModernScopedFrame(proWired, a7V3Pro, out proLevel, out proCharging, 1) &&
            proLevel == 96 && proCharging, "A7 V3 Pro accepts observed wired firmware identity and charging flag");
        proWired[20] ^= 1;
        Check(!VendorProtocol.TryModernScopedFrame(proWired, a7V3Pro, out _, out _, 1),
            "Firmware identity does not bypass XOR validation");
        const string nameA="MCHOSE K5 Ultra";InstanceScope.BeginScan();
        string a=InstanceScope.Register("fake#mhub#a",new[]{(@"USB\VID_3837&PID_1018\A","usbccgp")});
        string b=InstanceScope.Register("fake#mhub#b",new[]{(@"USB\VID_3837&PID_1018\B","usbccgp")});
        var bucket=new HidDiscovery.Bucket{Vid=0x3837,Pid=0x1018,Product=nameA,InstanceId=a,Kind=DeviceKind.Mouse};
        var buckets=new Dictionary<string,HidDiscovery.Bucket>{[a]=bucket};
        HidDiscovery.VendorCollectionInfo Channel(string path)=>new(){Vid=0x3837,Pid=0x1018,Product=nameA,Path=path,UsagePage=0xFF01,Usage=1,Input=64,Output=64,Feature=0};
        var channels=new List<HidDiscovery.VendorCollectionInfo>{Channel("fake#mhub#a"),Channel("fake#mhub#a"),Channel("fake#mhub#b")};int calls=0;
        ReferenceBatteryProtocol.Reading Query(string path,ushort vid,ushort pid){calls++;Check(path=="fake#mhub#a"&&InstanceScope.Current!=null,"Scoped path only");return new(76,false,true,ReferenceBatteryProtocol.State.Online,"mock",nameA,nameA);}
        HidDiscovery.QueryScopedModernMouse(buckets,channels,false,null,Query);
        Check(calls==1&&bucket.ToDevice().Level==76&&bucket.ToDevice().ConfirmedModel==nameA&&!bucket.ToDevice().PeripheralConfirmed,"One physical instance, model and RF stages distinct");
        calls=0;HidDiscovery.QueryScopedModernMouse(buckets,channels,true,null,Query);Check(calls==0&&bucket.ToDevice().Level<0,"Safe mode");
        var settings=new AppSettings{DisabledRecognitionDeviceIds=new(){a}};
        HidDiscovery.QueryScopedModernMouse(buckets,channels,false,new DevicePreferences(settings),Query);Check(calls==0,"Disabled device");
        channels.Add(Channel("fake#mhub#duplicate"));InstanceScope.Register("fake#mhub#duplicate",new[]{(@"USB\VID_3837&PID_1018\A","usbccgp")});
        HidDiscovery.QueryScopedModernMouse(buckets,channels,false,null,Query);Check(calls==0,"Multiple matching paths rejected");
        return count;
    }
}

