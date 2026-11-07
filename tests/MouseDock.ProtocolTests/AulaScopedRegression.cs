using System.Text;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class AulaScopedRegression
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string why) { count++; if(!ok) throw new Exception("AULA: "+why); }
        Check(AulaBatteryProtocol.Match(0x089D,0x062F,"Wireless-Receiver",0xFF02,0,33,33,0)==AulaBatteryProtocol.Route.Sc580Receiver,"Historical native SC580 signature");
        foreach(var wrong in new (ushort Page,ushort Usage,int Input,int Output)[] { (0xFF01,0,33,33),(0xFF02,1,33,33),(0xFF02,0,32,33),(1,2,33,33) })
            Check(AulaBatteryProtocol.Match(0x089D,0x062F,"SC580",wrong.Page,wrong.Usage,wrong.Input,wrong.Output,0)==null,"Wrong SC580 channel rejected");
        Check(AulaBatteryProtocol.Match(0x089D,0x062E,"SC580",0xFF02,0,33,33,0)==null,"Adjacent PID not authorized");
        Check(AulaBatteryProtocol.Match(1,2,"AULA-SC630Pro Gaming Mouse",0xFF01,1,0,0,41)==AulaBatteryProtocol.Route.Sc630Pro,"SC630 official product and Feature collection");
        Check(RecognitionPolicy.CanQuery(1,2,"AULA-SC630Pro Gaming Mouse") && !RecognitionPolicy.CanQuery(1,2,"SC630PRO"),"No guessed SC630 alias or ID");
        Check(!RecognitionPolicy.CanQuery(0,0,"AULA-SC630Pro Gaming Mouse"),"Missing IDs not admitted");
        foreach(string wrong in new[] { "SC630", "AULA SC630 Pro", "USB Receiver", "AULA-SC630Pro Gaming Mouse V2" })
            Check(AulaBatteryProtocol.Match(1,2,wrong,0xFF01,1,0,0,41)==null,"Other product not queried");
        Check(AulaBatteryProtocol.Match(1,2,"AULA-SC630Pro Gaming Mouse",0xFF01,2,0,0,41)==null,"SC630 usage must be one");
        Check(AulaBatteryProtocol.Match(1,2,"AULA-SC630Pro Gaming Mouse",0xFF01,1,0,0,42)==null,"SC630 wrong feature length");
        Check(AulaBatteryProtocol.Match(0x089D,0x062F,"AULA-SC630Pro Gaming Mouse",0xFF02,0,33,33,0)==null,"Explicit SC630 name never selects SC580 shared-ID route");
        Check(Convert.ToHexString(AulaBatteryProtocol.IdentifyRequest())=="00300001"+new string('0',58),"Only official battery Identify command");
        Check(AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc580Receiver,false,0,0,0),"Physical SC580 constant-only collection uses captured report0 contract");
        Check(AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc630Pro,false,0,0,0),"Official SC630 transport establishes fixed report6 even for constant-only caps");
        Check(AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc580Receiver,true,0,1,0),"Populated SC580 ID table must confirm report0");
        Check(!AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc580Receiver,true,6,1,0),"Different SC580 report ID rejected");
        Check(!AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc630Pro,false,0,1,0),"Failure reading populated caps is not constant-only evidence");
        Check(!AulaBatteryProtocol.AcceptsProfileReportId(AulaBatteryProtocol.Route.Sc630Pro,true,5,0,1),"Different SC630 feature ID rejected");
        foreach(bool numbered in new[] { false,true })
        foreach(string model in new[] { "M137","M230" })
        for(int percent=0;percent<=100;percent++)
        for(byte charge=0;charge<=1;charge++)
        {
            var raw=Identify((byte)percent,charge,1,model,numbered);
            Check(AulaBatteryProtocol.TryDecodeIdentify(raw,out var r) && r.Level==percent && r.Charging==(charge==1) && r.Identity==model,
                "Captured Identify offsets and all percent/charge combinations");
            Check(r.Model==(model=="M137"?"狼蛛 SC580":"前行者 X23 标准版"),"Shared receiver never overrides returned identity");
        }
        foreach(int index in new[] { 1,2,3,4,12,13,14 })
        {
            var invalid=Identify(65,0); invalid[index]=255;
            Check(!AulaBatteryProtocol.TryDecodeIdentify(invalid,out var r) && r.Level==-1,"Malformed Identify cannot become battery");
        }
        Check(!AulaBatteryProtocol.TryDecodeIdentify(new byte[32],out _),"Blank reply rejected");
        Check(!AulaBatteryProtocol.TryDecodeIdentify(Identify(65,0,1,"M999"),out _),"Unknown model not guessed");
        Check(AulaBatteryProtocol.TryDecodeIdentify(Identify(65,1,0),out var sleep) && sleep.Level==-1 && !sleep.Charging,"Offline embedded battery is not fresh");
        Check(AulaBatteryProtocol.TryDecodeIdentify(NotReady(),out sleep) && sleep.Status==ReferenceBatteryProtocol.State.Sleep,"Receiver not-ready recognized");
        var link=new Link(); link.Input.Enqueue(Identify(99,0));
        link.WriteResponse=packet=>new[] { packet, new byte[32], Identify(11,0,1,"M999"),Identify((byte)(link.Writes.Count==1?17:42),1) };
        var trace = new BatteryReplay.Trace();
        var read=AulaBatteryProtocol.ReadIdentify(link, trace);
        var replayCollection = new BatteryReplay.Collection("receiver-1", 1, 0x089D, 0x062F,
            "Wireless Receiver", 0xFF02, 0, 33, 33, 0, false, true);
        var replayQuery = new BatteryReplay.Query("receiver-1", 1, "Sc580Receiver", read.Status.ToString(),
            read.Model, 99, trace.Snapshot());
        var replay = BatteryReplay.Replay(replayQuery, new[] { replayCollection });
        Check(replay is { Model: "狼蛛 SC580", Percent: 42, Charging: true, ValueKind: "captured" },
            "Replay uses raw replies rather than recorded display percent");
        Check(trace.Snapshot().Any(s => s.Stage == "input" && !s.Ok && s.Length == 32 && s.Hex == null) &&
            trace.Snapshot().Any(s => s.Stage == "input" && !s.Ok && s.Hex?.Contains("4D393939") == true),
            "Unrelated input keeps only length while an unknown Identify model keeps its protocol frame");
        Check(BatteryReplay.Replay(replayQuery, new[] { replayCollection with { Receiver = "receiver-2" } }).Percent == null,
            "Replay never uses another receiver's interface");
        Check(BatteryReplay.Replay(replayQuery with { Samples = Array.Empty<BatteryReplay.Sample>() },
            new[] { replayCollection }).Percent == null, "No reply cannot become an estimate");
        Check(read.Level==42 && read.Charging && read.Identity=="M137" && link.Writes.Count==2,"Drain/echo/unknown rejected before two fresh same-model replies");
        Check(link.Writes.All(p=>p.AsSpan().SequenceEqual(AulaBatteryProtocol.IdentifyRequest())),"No subscription, DPI or wake configuration commands");
        var retry=new Link(); retry.WriteResponse=_=>new[] { retry.Writes.Count==1?NotReady():Identify(0,0) };
        Check(AulaBatteryProtocol.ReadIdentify(retry).Level==0 && retry.Writes.Count==3,"Not-ready response retries within finite budget and accepts real zero");
        var delayed = new Link();
        delayed.WriteResponse = _ => new[] { NotReady(), Identify(63, 0) };
        Check(AulaBatteryProtocol.ReadIdentify(delayed).Level == -1,
            "A not-ready response invalidates any earlier online reply");
        var waking = new Link();
        waking.WriteResponse = _ => waking.Writes.Count == 1
            ? new[] { NotReady(), Identify(63, 0) } : new[] { Identify(64, 0) };
        Check(AulaBatteryProtocol.ReadIdentify(waking).Level == 64 && waking.Writes.Count == 2,
            "An online reply after a transient not-ready frame in the same exchange is retained");
        var changed=new Link(); changed.WriteResponse=_=>new[] { Identify(50,0,1,changed.Writes.Count==1?"M137":"M230") };
        Check(AulaBatteryProtocol.ReadIdentify(changed).Level==-1,"Identity changing between replies rejects model and battery");
        var missing=new Link(); missing.WriteResponse=_=>missing.Writes.Count==1?new[] { Identify(44,0) }:Array.Empty<byte[]>();
        Check(AulaBatteryProtocol.ReadIdentify(missing) is { Level: -1, Identity: "M137" },"One valid online reply identifies the model with unknown battery");
        var sleeping=new Link(); sleeping.WriteResponse=_=>new[] { Identify(65,0,0) };
        Check(AulaBatteryProtocol.ReadIdentify(sleeping).Level==-1,"Sleeping mouse never supplies fresh cached percentage");
        var flood=new Link(); for(int i=0;i<32;i++)flood.Input.Enqueue(Identify(90,0));
        Check(AulaBatteryProtocol.ReadIdentify(flood).Level==-1 && flood.Writes.Count==0,"Input flood aborts before request");
        var shortWrite=new Link { WriteOk=false };
        Check(AulaBatteryProtocol.ReadIdentify(shortWrite).Level==-1 && shortWrite.Writes.Count==4,"Failed writes are retried within a fixed budget");

        for(int percent=0;percent<=100;percent++)
        for(int charge=0;charge<=1;charge++)
        {
            var reply=Feature(27,(byte)percent,charge==1);
            Check(VendorProtocol.TryParseAulaSc630Battery(reply,27,out int p,out bool c) && p==percent && c==(charge==1),"Official packed feature percentage and charging");
        }
        var sc=new Link(); sc.FeatureResponse=packet=>Feature(packet[4],(byte)(sc.Features.Count==1?17:42),true);
        byte seq=0;
        var featureTrace = new BatteryReplay.Trace();
        read=AulaBatteryProtocol.ReadSc630(sc,()=>++seq, featureTrace);
        var scCollection = replayCollection with { Vid = 1, Pid = 2, Page = 0xFF01, Usage = 1,
            Product = "AULA-SC630Pro Gaming Mouse", Feature = 41 };
        var featureQuery = replayQuery with { Protocol = "Sc630Pro", Samples = featureTrace.Snapshot() };
        Check(BatteryReplay.Replay(featureQuery, new[] { scCollection }) is { Model: "AULA SC630 Pro", Percent: 42 },
            "SC630 replay validates each feature reply against its sequence");
        Check(BatteryReplay.Replay(featureQuery with { Samples = featureTrace.Snapshot()
            .Select(sample => sample with { Sequence = 99 }).ToArray() }, new[] { scCollection }).Percent == null,
            "Replay rejects wrong feature sequence");
        Check(read.Level==42 && read.Charging && sc.Features.Count==2 && sc.Writes.Count==0 && sc.Delays.SequenceEqual(new[] {130,130}),"SC630 only two Set/GetFeature6/41 battery requests, official delay");
        Check(sc.Features[0][4]!=sc.Features[1][4] && sc.Features.All(p=>p.AsSpan().SequenceEqual(VendorProtocol.BuildAulaSc630BatteryRequest(p[4]))),"Per-query nonzero sequence and exact battery header");
        var noEcho=new Link(); noEcho.FeatureResponse=_=>Feature(0,0,false);
        Check(AulaBatteryProtocol.ReadSc630(noEcho,()=>1).Level==0,"Official no-sequence-echo variant accepted and diagnostic labeled");
        foreach(string error in new[] { "echo","wrong-sequence","offline","range","short","wrong-command","missing-second" })
        {
            var bad=new Link(); bad.FeatureResponse=packet=>
            {
                var reply=Feature(packet[4],50,false);
                switch(error)
                {
                    case "echo": return packet;
                    case "wrong-sequence": reply[4]=(byte)(packet[4]+1);break;
                    case "offline": reply[9]=0;break;
                    case "range": reply[10]=101;break;
                    case "short": return reply[..40];
                    case "wrong-command": reply[3]=0x82;break;
                    case "missing-second": if(bad.Features.Count>1)return null;break;
                }
                return reply;
            };
            seq=5;
            Check(AulaBatteryProtocol.ReadSc630(bad,()=>++seq).Level==-1,"SC630 rejects "+error);
        }
        var zeroSeq=new Link();
        Check(AulaBatteryProtocol.ReadSc630(zeroSeq,()=>0).Level==-1 && zeroSeq.Features.Count==0,"Zero sequence sends nothing");

        InstanceScope.BeginScan();
        const string path="test#vid_089d&pid_062f&mi_02#A";
        const string second="test#vid_089d&pid_062f&mi_02#B";
        string a=InstanceScope.Register(path,new[] { (@"USB\VID_089D&PID_062F\SYNTHETIC_A","usbccgp") });
        string b=InstanceScope.Register(second,new[] { (@"USB\VID_089D&PID_062F\SYNTHETIC_B","usbccgp") });
        var ba=new HidDiscovery.Bucket { Vid=0x089D,Pid=0x062F,Product="Wireless-Receiver",InstanceId=a,Kind=DeviceKind.Keyboard,Identity="M137",Battery=99 };
        var bb=new HidDiscovery.Bucket { Vid=0x089D,Pid=0x062F,Product="Wireless-Receiver",InstanceId=b,Kind=DeviceKind.Mouse };
        var buckets=new Dictionary<string,HidDiscovery.Bucket> { [a]=ba,[b]=bb };
        HidDiscovery.VendorCollectionInfo Channel(string p)=>new() { Path=p,Vid=0x089D,Pid=0x062F,Product="Wireless-Receiver",UsagePage=0xFF02,Usage=0,Input=33,Output=33 };
        var channels=new List<HidDiscovery.VendorCollectionInfo> { Channel(path),Channel(path),Channel(second) };
        int queries=0;
        ReferenceBatteryProtocol.Reading Query(AulaBatteryProtocol.Route route,string selected,ushort vid,ushort pid,string product)
        {
            queries++;
            Check(route==AulaBatteryProtocol.Route.Sc580Receiver && InstanceScope.Allows(selected) &&
                !InstanceScope.Allows(selected==path?second:path),"Dedicated reader gets one exact physical instance");
            return selected==path?new(42,true,true,ReferenceBatteryProtocol.State.Charging,"synthetic","狼蛛 SC580","M137"):
                new(0,false,true,ReferenceBatteryProtocol.State.Online,"synthetic","前行者 X23 标准版","M230");
        }
        HidDiscovery.QueryAulaCollections(buckets,channels,false,null,Query);
        Check(queries==2 && ba.ToDevice().Name=="狼蛛 SC580" && bb.ToDevice().Name=="前行者 X23 标准版","Same ID routes to independent live identities and battery");
        Check(ba.ToDevice().RawName=="Wireless-Receiver" && ba.ToDevice().ConfirmedModel=="狼蛛 SC580" && ba.ToDevice().Kind==DeviceKind.Mouse && ba.ToDevice().PeripheralConfirmed,"Raw and confirmed names remain separate; keyboard collection is auxiliary");
        Check(ba.ToDevice().PhysicalKey!=bb.ToDevice().PhysicalKey && ba.ToDevice().Level==42 && bb.ToDevice().Level==0,"Never merge by model/VID");
        queries=0;
        HidDiscovery.QueryAulaCollections(buckets,channels,true,null,Query);
        // 产品行为变更（2026-11-08）：未确认型号的公版接收器不再回退到 USB 产品串
        // "Wireless-Receiver"，改为「品牌 + 2.4G 接收器（型号未确认）」，类型按鼠标。
        // 安全模式仍然不提升任何旧身份或旧电量。
        Check(queries==0 && ba.ToDevice().Level==-1 && !ba.ToDevice().Charging && ba.ToDevice().ConfirmedModel=="" &&
              ba.ToDevice().Name=="狼蛛 2.4G 接收器（型号未确认）" && ba.ToDevice().Kind==DeviceKind.Mouse,
              "Safe scan cannot promote previous identity/battery");
        var prefs=new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds=new() { a,b } });
        HidDiscovery.QueryAulaCollections(buckets,channels,false,prefs,Query);
        Check(queries==0,"Disabled instances have no battery I/O");
        channels.RemoveAll(c=>c.Path==path);
        HidDiscovery.QueryAulaCollections(buckets,channels,false,null,Query);
        Check(queries==1 && ba.Battery==-1,"Other same-ID receiver cannot supply missing collection");
        channels.Add(Channel(path)); channels.Add(Channel(path+"duplicate"));
        InstanceScope.Register(path+"duplicate",new[] { (@"USB\VID_089D&PID_062F\SYNTHETIC_A","usbccgp") });
        queries=0;
        HidDiscovery.QueryAulaCollections(buckets,channels,false,null,Query);
        Check(queries==1 && ba.Battery==-1,"Ambiguous distinct private paths are not guessed");

        string scpath="test#sc630#C";
        string scid=InstanceScope.Register(scpath,new[] { (@"USB\VID_0001&PID_0002\SYNTHETIC_C","usbccgp") });
        var bc=new HidDiscovery.Bucket { Vid=1,Pid=2,Product="USB Receiver",Sc630Product="AULA-SC630Pro Gaming Mouse",InstanceId=scid,Kind=DeviceKind.Mouse };
        var scbuckets=new Dictionary<string,HidDiscovery.Bucket> { [scid]=bc };
        var sccollections=new List<HidDiscovery.VendorCollectionInfo> { new() { Vid=1,Pid=2,Product=null,Path=scpath,UsagePage=0xFF01,Usage=1,Feature=41 } };
        HidDiscovery.QueryAulaCollections(scbuckets,sccollections,false,null,(_,selected,_,_,product)=>
        {
            Check(selected==scpath && InstanceScope.Current==scid && VendorProtocol.IsAulaSc630(product),"Unnamed SC630 feature collection uses exact product evidence from the same instance");
            return ReferenceBatteryProtocol.Reading.Failed("no physical response");
        });
        Check(bc.ToDevice().ConfirmedModel=="AULA SC630 Pro" && bc.ToDevice().Level==-1 && !bc.ToDevice().PeripheralConfirmed && !bc.ToDevice().ReceiverDiscovered,"Descriptor model does not prove battery, radio receiver or live peripheral");
        var collision=new HidDiscovery.Bucket { Vid=0x3554,Pid=0xF517,Product=bc.Sc630Product,InstanceId=scid,Kind=DeviceKind.Mouse };
        HidDiscovery.QueryReferenceCollections(new() { [scid]=collision },new(),false,null,(_,_)=>throw new Exception("SC630 must not enter a different VID/PID profile"));
        Check(collision.Battery==-1 && collision.BatteryFailures.Count==0,"Exact SC630 name excludes reference-profile collision");
        Check(!VendorProtocol.ProbeDevice(0x089D,0x062F,"Alias",false).Text.Contains("接口路径数"),"SC580 export avoids cross-instance probes");
        Check(VendorProtocol.ProbeDevice(1,2,"Alias",false,scanOnly:true).Exchanges==0,"SC630 raw-name gate suppresses export probes despite alias");
        Check(VendorProtocol.ReadAulaBattery(AulaBatteryProtocol.Route.Sc580Receiver,path,0x089D,0x062F).Level==-1,"Unscoped native call fails before opening device");
        return count;
    }

    private static byte[] Identify(byte percent,byte charge,byte online=1,string model="M137",bool numbered=false)
    {
        var b=new byte[32]; b[0]=0x30;b[2]=1;b[3]=11;Encoding.ASCII.GetBytes(model).CopyTo(b,4);b[12]=charge;b[13]=percent;b[14]=online;
        return numbered?new byte[] {0}.Concat(b).ToArray():b;
    }
    private static byte[] NotReady() { var b=new byte[32];b[0]=0x30;b[1]=2;b[2]=1;return b; }
    private static byte[] Feature(byte seq,byte percent,bool charge)
    { var b=VendorProtocol.BuildAulaSc630BatteryRequest(seq);b[9]=1;b[10]=(byte)(percent|(charge?128:0));return b; }
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now {get;private set;}
        public Queue<byte[]> Input=new();
        public List<byte[]> Writes=new(),Features=new();
        public List<int> Delays=new();
        public bool WriteOk=true;
        public Func<byte[],byte[][]>? WriteResponse;
        public Func<byte[],byte[]?>? FeatureResponse;
        public byte[]? Read(int ms)
        {
            if (Input.Count > 0) { Now++; return Input.Dequeue(); }
            Now += Math.Max(1, ms);
            return null;
        }
        public bool Write(byte[] request,int timeoutMs)
        { Writes.Add(request.ToArray());if(WriteOk)foreach(var b in WriteResponse?.Invoke(request)??Array.Empty<byte[]>())Input.Enqueue(b);return WriteOk; }
        public bool SetFeature(byte[] request) { Features.Add(request.ToArray());return true; }
        public byte[]? GetFeature(byte id,int length)
        { if(id!=6 || length!=41)throw new Exception("Unexpected feature");return FeatureResponse?.Invoke(Features[^1]); }
        public void Delay(int ms) { Now+=ms;Delays.Add(ms); }
    }
}
