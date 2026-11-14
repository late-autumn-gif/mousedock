using System.IO;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ScyroxLegacyRegression
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception("Scyrox legacy: "+message);count++; }
        bool Reject(Action action) { try{action();return false;}catch(Exception ex)when(ex is InvalidDataException or IOException){return true;} }
        byte[] Reply(byte cmd,int value,byte charge=0,byte peer=7)
        {
            var b=new byte[17];b[0]=8;b[1]=cmd;b[5]=4;b[6]=(byte)value;
            if(cmd==3){b[7]=peer;b[8]=8;b[9]=9;}else{b[7]=charge;b[8]=14;b[9]=128;}
            b[16]=unchecked((byte)(0x55-b.Take(16).Sum(v=>(int)v)));return b;
        }
        Check(DeviceRoutes.Find(0x3554,0xF5F7,"SCYROX 8K Dongle") is {Kind:DeviceKind.Mouse,Reader:ScyroxLegacyProtocol.Reader},"receiver classified as mouse with dedicated reader");
        Check(DeviceRoutes.IsKnown(0x3554,0xF5F7,"SCYROX 8K Dongle"),"read permission uses actual reader");
        foreach(var (vid,pid,name) in new (ushort,ushort,string)[]{(0x3554,0xF5F6,"SCYROX 8K Dongle"),(0x3554,0xF5F8,"SCYROX 8K Dongle"),(0x395A,6,"SCYROX 8K Dongle"),(0x3554,0xF5F7,"Foreign receiver")})
            Check(!ScyroxLegacyProtocol.IsCandidate(vid,pid,name),"wrong channel family or product not admitted");
        Check(ExtraDeviceRoutes.Find(0x3554,0xF5F7,"Foreign receiver")==null,"foreign product cannot fall back to generic COMPX");
        Check(Reject(()=>ScyroxLegacyProtocol.Request(9)),"no reset or settings command");
        for(int level=0;level<=100;level++)for(byte charge=0;charge<=1;charge++)
        {
            var r=ScyroxLegacyProtocol.Decode(Reply(4,level,charge));
            Check(r.Level==level && r.ChargingKnown && r.Charging==(charge==1),"full percent and charging range");
        }
        foreach(int size in new[]{0,1,8,16,18,49,64})Check(Reject(()=>ScyroxLegacyProtocol.Decode(new byte[size])),"exact 17-byte layout");
        foreach(int level in new[]{101,255})Check(Reject(()=>ScyroxLegacyProtocol.Decode(Reply(4,level))),"percentage range");
        Check(Reject(()=>ScyroxLegacyProtocol.Decode(Reply(4,60,2))),"unknown charging flag");
        Check(Reject(()=>ScyroxLegacyProtocol.Decode(ScyroxLegacyProtocol.Request(4))),"echo never becomes zero");
        for(int i=0;i<17;i++) {var bad=Reply(4,60);bad[i]^=1;Check(!ScyroxLegacyProtocol.Valid(bad,4),"checksum validates every byte");}

        var channel=new HidDiscovery.VendorCollectionInfo {Vid=0x3554,Pid=0xF5F7,Product="SCYROX 8K Dongle",
            Path=@"test&mi_01&col05#scyrox",UsagePage=0xFF02,Usage=2,Input=17,Output=17};
        var metadata=new ExpansionHidMetadata {Revision=0x0216,InputIds=new(){8},OutputIds=new(){8},FeatureIds=new(),
            InputSizes=new(){[8]=17},OutputSizes=new(){[8]=17},FeatureSizes=new()};
        ScyroxLegacyProtocol.Validate(channel,metadata);count++;
        metadata.Revision++;Check(Reject(()=>ScyroxLegacyProtocol.Validate(channel,metadata)),"unreported revision not guessed");metadata.Revision--;
        channel.Input=49;Check(!ScyroxLegacyProtocol.Matches(channel),"49-byte auxiliary report excluded");channel.Input=17;
        channel.UsagePage=0xFF06;Check(!ScyroxLegacyProtocol.Matches(channel),"auxiliary usage excluded");channel.UsagePage=0xFF02;

        int batteries=0;var trace=new BatteryReplay.Trace();
        var link=new Link(packet=>[Reply(packet[1],packet[1]==3?1:60+ ++batteries)]);
        Check(ScyroxLegacyProtocol.Read(new(link,17,17,trace:trace,budget:6000)) is {Level:62,Charging:false,ChargingKnown:true},"two fresh battery replies and final online check");
        Check(link.Outputs.Select(b=>b[1]).SequenceEqual(new byte[]{3,4,3,4,3}),"only official GET online and battery sequence");
        Check(link.Outputs.All(b=>b.AsSpan().SequenceEqual(ScyroxLegacyProtocol.Request(b[1]))),"all request bytes fixed");
        foreach(var sample in trace.Snapshot().Where(s=>s.Stage=="expansion-input" && s.Ok))
        {
            var b=Convert.FromHexString(sample.Hex!);
            Check(b.Skip(b[1]==3?7:8).Take(b[1]==3?9:8).All(v=>v==0),"peripheral address, voltage and unknown bytes omitted");
        }
        var empty=new Link(_=>[]);Check(Reject(()=>ScyroxLegacyProtocol.Read(new(empty,17,17))) && empty.Now<=1000,"missing first response is bounded");
        var echo=new Link(packet=>[packet]);Check(Reject(()=>ScyroxLegacyProtocol.Read(new(echo,17,17))),"request echo cannot confirm online");
        var flood=new Link(_=>[]){Flood=true};Check(Reject(()=>ScyroxLegacyProtocol.Read(new(flood,17,17)))&&flood.Outputs.Count==0,"old queue flood sends nothing");
        var offline=new Link(packet=>[Reply(packet[1],0)]);Check(ScyroxLegacyProtocol.Read(new(offline,17,17)).Level<0 && offline.Outputs.Count==1,"offline mouse prevents battery GET");
        int online=0;var changed=new Link(p=>[Reply(p[1],p[1]==3?1:70,peer:(byte)(p[1]==3&&++online==3?9:7))]);
        Check(Reject(()=>ScyroxLegacyProtocol.Read(new(changed,17,17))),"paired peripheral changed before final online");
        online=0;var disconnected=new Link(p=>[Reply(p[1],p[1]==3&&++online==3?0:p[1]==3?1:70)]);
        Check(ScyroxLegacyProtocol.Read(new(disconnected,17,17)).Level<0,"final offline invalidates earlier percentage");
        int batteryCount=0;var missingSecond=new Link(p=>p[1]==4&&++batteryCount==2?[]:[Reply(p[1],p[1]==3?1:70)]);
        Check(Reject(()=>ScyroxLegacyProtocol.Read(new(missingSecond,17,17))),"missing second battery reply rejected");

        var capture=new BatteryReplay.Collection("r",5,0x3554,0xF5F7,"SCYROX 8K Dongle",0xFF02,2,17,17,0,false,false,
            InterfaceLabel:"MI_01/COL_05",UsbRevision:0x0216,ReportIds:"IN[08] OUT[08] FEATURE[]");
        var query=new BatteryReplay.Query("r",null,ScyroxLegacyProtocol.Reader,"Online",null,62,trace.Snapshot());
        Check(BatteryReplay.Replay(query,[capture]) is {Percent:62,Charging:false,ValueKind:"captured-redacted"},"sanitized real-parser replay");
        Check(BatteryReplay.Replay(query,[capture with {Receiver="other"}]).Percent==null,"replay does not cross receivers");
        Check(BatteryReplay.Replay(query,[capture with {UsbRevision=0x0217}]).Percent==null,"replay exact revision");
        Check(BatteryReplay.Replay(query with {Samples=trace.Snapshot().SkipLast(1).ToArray()},[capture]).Percent==null,"missing final online not captured");
        Check(BatteryReplay.Replay(query with {Status="Failed"},[capture]).Percent==null,"failed capture never adopts samples");

        InstanceScope.BeginScan();
        string a=InstanceScope.Register(channel.Path,new[]{(@"USB\VID_3554&PID_F5F7\SYNTHETIC_A","usbccgp")});
        string otherPath=channel.Path+"other";
        InstanceScope.Register(otherPath,new[]{(@"USB\VID_3554&PID_F5F7\SYNTHETIC_B","usbccgp")});
        var bucket=new HidDiscovery.Bucket {Vid=channel.Vid,Pid=channel.Pid,Product=channel.Product,InstanceId=a,Kind=DeviceKind.Other};
        var buckets=new Dictionary<string,HidDiscovery.Bucket>{[a]=bucket};int calls=0;
        ReferenceBatteryProtocol.Reading Query(ExtraDeviceRoutes.Route route,ushort vid,ushort pid,string product,
            IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels,BatteryReplay.Trace t)
        {calls++;Check(channels.Count==1&&InstanceScope.Allows(channel.Path)&&!InstanceScope.Allows(otherPath),"physical receiver isolation");return ScyroxLegacyProtocol.Decode(Reply(4,62));}
        var list=new List<HidDiscovery.VendorCollectionInfo>{channel};
        HidDiscovery.QueryExtraDevices(buckets,list,false,null,Query);
        var device=new AutoDetectSource().UpdateFromDetection(new(){bucket.ToDevice()}).Single();
        Check(device.Kind==DeviceKind.Mouse && device.Level==62 && device.LevelText.Contains("62") && !device.Name.Contains("V6"),"normal DeviceInfo pipeline without inventing exact model");
        HidDiscovery.QueryExtraDevices(buckets,list,true,null,Query);Check(calls==1&&bucket.Battery<0,"safe mode clears fresh result without I/O");
        HidDiscovery.QueryExtraDevices(buckets,list,false,new DevicePreferences(new(){DisabledRecognitionDeviceIds=new(){a}}),Query);Check(calls==1,"disabled instance not queried");
        HidDiscovery.QueryExtraDevices(buckets,new(),false,null,Query);Check(calls==1,"missing collection never queries");
        Check(VendorProtocol.ReadScyroxLegacy(0x3554,0xF5F7,channel.Product,list,new()).Level<0,"unscoped native call rejected before open");
        return count;
    }
    private sealed class Link(Func<byte[],byte[][]> reply) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> input=new();
        internal readonly List<byte[]> Outputs=new();internal bool Flood;
        public long Now{get;private set;}
        public byte[]? Read(int ms){Now+=Math.Max(1,ms);return input.Count>0?input.Dequeue():Flood?new byte[17]:null;}
        public bool Write(byte[] p,int ms){Outputs.Add((byte[])p.Clone());foreach(var b in reply(p))input.Enqueue(b);return true;}
        public bool SetFeature(byte[] p)=>throw new Exception("Unexpected Feature write");
        public byte[] GetFeature(byte id,int size)=>throw new Exception("Unexpected Feature read");
        public void Delay(int ms)=>Now+=ms;
    }
}
