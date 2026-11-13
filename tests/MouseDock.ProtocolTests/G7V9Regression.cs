using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
internal static class G7V9Regression
{
    internal static int Run()
    {
        int n=0;void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
        Check(DeviceRoutes.IsKnown(0x3837,0x1018,"MCHOSE G7"),"G7 enabled");
        Check(!VendorProtocol.IsScopedG7(0x3837,0x1019,"MCHOSE G7"),"Adjacent PID gated");
        Check(!VendorProtocol.IsScopedG7(0x3837,0x1018,"MCHOSE G7 MAX"),"Other G7 model gated");
        Check(!VendorProtocol.IsScopedG7(0x3837,0x1018,"MCHOSE K5 Ultra"),"Shared ID not a model");
        Check(!VendorProtocol.IsScopedG7Channel(0x3837,0x1018,"MCHOSE G7",0xFF01,0x10,64,65,0),"Mixed layout not guessed");
        Check(!VendorProtocol.IsScopedG7Channel(0x3837,0x1018,"MCHOSE G7",0xFF01,0x10,64,64,0),"Only official native65 layout is enabled");
        foreach(int size in new[]{64,65})for(int pct=0;pct<=100;pct++)for(int chg=0;chg<=1;chg++)
        {
            var raw=Reply(size,(byte)pct,(byte)chg);
            Check(VendorProtocol.TryScopedG7Reply(raw,size,out int level,out bool charging)&&level==pct&&charging==(chg==1),"G7 field decoding");
        }
        Check(!VendorProtocol.TryScopedG7Reply(Reply(65,101,0),65,out _,out _),"Out of range rejected");
        var malformed=Reply(65,50,0);malformed[0]=0xAA;
        Check(!VendorProtocol.TryScopedG7Reply(malformed,65,out _,out _),"65-byte alignment cannot shift by scanning");
        var link=new Link();link.Input.Enqueue(Reply(65,99,0));
        link.OnWrite=packet=>new[]{packet,Reply(65,101,0),Reply(65,link.Writes==1?(byte)30:(byte)42,1)};
        var r=VendorProtocol.ReadG7Exchange(link,65,"MCHOSE G7");
        Check(r.Level==42 && r.Charging && link.Writes==2,"Drain, echo, invalid replies and two fresh responses");
        var one=new Link();one.OnWrite=_=>one.Writes==1?new[]{Reply(65,40,0)}:Array.Empty<byte[]>();
        Check(VendorProtocol.ReadG7Exchange(one,65,"MCHOSE G7").Level==-1,"One response cannot promote a reading");
        var flood=new Link();for(int i=0;i<32;i++)flood.Input.Enqueue(Reply(65,80,0));
        Check(VendorProtocol.ReadG7Exchange(flood,65,"MCHOSE G7").Level==-1&&flood.Writes==0,"Flood sends nothing");
        Check(ExtraDeviceRoutes.Find(0x291D,0x385D,"MCHOSE V9 PRO") is
            { Kind: DeviceKind.Headset, Family: "V9Pro" } &&
            ExtraDeviceRoutes.Find(0x291D,0x385D,"MCHOSE V9 PRO MAX") == null,
            "V9 uses its exact new headset reader without admitting a different model");
        var v9=new AudioEndpointInfo("v9","扬声器 (MCHOSE V9 PRO)",false,Guid.Empty,1,70,false);
        Check(v9.IsHeadset && v9.Name=="扬声器 (MCHOSE V9 PRO)","V9 speaker-form audio endpoint keeps original name");
        Check(!(v9 with {Name="扬声器 (MCHOSE S9)"}).IsHeadset,"Shared S9 not renamed as V9");
        Check(!(v9 with {Name="扬声器 (MCHOSE V9 PRO MAX)"}).IsHeadset,"Different V9 model not aliased");
        Check(!(v9 with {Capture=true}).IsHeadset,"Capture endpoint never becomes playback");
        Check(HeadsetAudioPolicy.MicrophoneFor(v9,Array.Empty<AudioEndpointInfo>(),null)==null,"No default microphone fallback");
        Check(VendorProtocol.ProbeDevice(0x291D,0x385D,"MCHOSE V9 PRO",false).Exchanges==0,"V9 report export cannot activate battery");
        InstanceScope.BeginScan();const string path="test#G7#A",other="test#G7#B";
        string a=InstanceScope.Register(path,new[]{(@"USB\VID_3837&PID_1018\SYNTHETIC_A","usbccgp")});
        string b=InstanceScope.Register(other,new[]{(@"USB\VID_3837&PID_1018\SYNTHETIC_B","usbccgp")});
        var bucket=new HidDiscovery.Bucket {Vid=0x3837,Pid=0x1018,Product="MCHOSE G7",InstanceId=a,Kind=DeviceKind.Mouse};
        var buckets=new Dictionary<string,HidDiscovery.Bucket>{[a]=bucket};
        HidDiscovery.VendorCollectionInfo Channel(string p)=>new(){Vid=0x3837,Pid=0x1018,Product="MCHOSE G7",Path=p,UsagePage=0xFF01,Usage=0x10,Input=65,Output=65};
        var channels=new List<HidDiscovery.VendorCollectionInfo>{Channel(path),Channel(path),Channel(other)};int queries=0;
        ReferenceBatteryProtocol.Reading Query(string p,ushort vid,ushort pid){queries++;Check(p==path&&InstanceScope.Allows(path)&&!InstanceScope.Allows(other),"G7 instance isolation");return new(42,false,true,ReferenceBatteryProtocol.State.Online,"fake","MCHOSE G7");}
        HidDiscovery.QueryScopedG7(buckets,channels,false,null,Query);
        Check(queries==1&&bucket.ToDevice().Level==42&&!bucket.ToDevice().PeripheralConfirmed,"One instance query; battery not RF status");
        queries=0;HidDiscovery.QueryScopedG7(buckets,channels,true,null,Query);
        Check(queries==0&&bucket.Battery==-1,"Safe mode clears fresh result");
        var prefs=new DevicePreferences(new AppSettings {DisabledRecognitionDeviceIds=new(){a}});
        HidDiscovery.QueryScopedG7(buckets,channels,false,prefs,Query);Check(queries==0,"Disabled G7 not queried");
        channels.RemoveAll(c=>c.Path==path);HidDiscovery.QueryScopedG7(buckets,channels,false,null,Query);
        Check(queries==0&&bucket.Battery==-1,"Other receiver cannot lend channel");
        Check(VendorProtocol.ReadScopedG7(path,0x3837,0x1018).Level==-1,"Unscoped native call sends nothing");
        return n;
    }
    private static byte[] Reply(int size,byte level,byte charging){var b=new byte[size];int at=size==65?1:0;b[at]=0xAA;b[at+1]=0x30;b[at+8]=level;b[at+9]=charging;return b;}
    private sealed class Link:ReferenceBatteryProtocol.ILink
    {
        public long Now{get;private set;}public int Writes;public Queue<byte[]>Input=new();public Func<byte[],byte[][]>? OnWrite;
        public byte[]? Read(int ms){Now+=Math.Max(1,ms);return Input.Count>0?Input.Dequeue():null;}
        public bool Write(byte[] p,int ms){Writes++;foreach(var r in OnWrite?.Invoke(p)??Array.Empty<byte[]>())Input.Enqueue(r);return true;}
        public bool SetFeature(byte[] b)=>throw new Exception();public byte[] GetFeature(byte id,int n)=>throw new Exception();public void Delay(int n)=>throw new Exception();
    }
}

