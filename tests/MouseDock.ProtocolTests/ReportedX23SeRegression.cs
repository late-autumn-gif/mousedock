using System.IO;
using System.Text.Json;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ReportedX23SeRegression
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string reason) { count++; if (!ok) throw new Exception("X23SE: "+reason); }
        using var oracle=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","x23se-freq-oracle.json")));
        var root=oracle.RootElement;
        Check(root.GetProperty("sourceSha256").GetString()=="DE9A0C6B8680ADE90CBC9B1A82DBDBAACAD88C302176D52099327ED75838BD8A","Trusted reference identity");
        Check(Convert.ToHexString(VendorProtocol.BuildFreqSchemeAPacket(42,65))==root.GetProperty("requestHex").GetString(),"65/65 dual selects scheme a and matches original transaction bytes");
        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            bool accepted=VendorProtocol.TryParseFreqFrame(Convert.FromHexString(vector.GetProperty("hex").GetString()!),"a",7,65,out int level,out bool charging);
            Check(accepted==vector.GetProperty("accepted").GetBoolean(),"Original decoder acceptance: "+vector.GetProperty("label").GetString());
            if (accepted) Check(level==vector.GetProperty("level").GetInt32() && charging==vector.GetProperty("charging").GetBoolean(),"Original percentage/charging semantics");
            else Check(level==-1 && !charging,"Rejected bytes cannot become percentages");
        }
        Check(RecognitionPolicy.CanQuery(0xA8A5,0x2255,"EWEADN X23SE"),"Explicit requested profile enabled");
        Check(VendorProtocol.IsReportedX23Se(0xA8A5,0x2255,"  eweadn  x23se  "),"Harmless case/space differences accepted");
        foreach (string name in new[] { "EWEADN X23E","EWEADN X23Pro","EWEADN X23","USB Receiver","EWEADN X23SE V2" })
            Check(!RecognitionPolicy.CanQuery(0xA8A5,0x2255,name),"Other models sharing an ID remain gated: "+name);
        Check(!RecognitionPolicy.CanQuery(0xA8A4,0x2255,"EWEADN X23SE"),"Wired variant not enabled from a wireless-only report");
        foreach (bool safe in new[] { false, true })
        {
            var probe=VendorProtocol.ProbeDevice(0xA8A5,0x2255,"User alias",safe);
            Check(probe.Exchanges==0 && probe.Responses==0 && probe.Text.Contains("不运行跨实例通用探针"),"Report export never opens shared-ID interfaces outside instance scope");
        }
        const string path="test#vid_a8a5&pid_2255&mi_02#A";
        bool Gate(ushort page,ushort usage,int input,int output,int feature,string p=path) =>
            VendorProtocol.IsReportedX23SeCollection(0xA8A5,0x2255,"EWEADN X23SE",p,page,usage,input,output,feature);
        Check(Gate(0xFF01,0x10,65,65,0),"Reported collection signature accepted");
        Check(!Gate(0xFF06,2,64,64,0) && !Gate(0xFF05,2,64,0,0),"Other private interfaces are not probed");
        Check(!Gate(0xFF01,0x10,65,65,0,path.Replace("mi_02","mi_01")),"MI_01 is not MI_02");
        Check(!Gate(0xFF01,0x10,64,64,0) && !Gate(0xFF01,0x10,65,65,32),"Different firmware signatures remain gated");

        byte[] Caps(params byte[] ids)
        {
            var bytes=new byte[72*ids.Length];
            for (int i=0;i<ids.Length;i++) bytes[i*72+2]=ids[i];
            return bytes;
        }
        Check(VendorProtocol.TrySingleFreqReportId(Caps(7,7),2,Caps(7),1,out byte id) && id==7,"Unique ID across value and button caps");
        Check(!VendorProtocol.TrySingleFreqReportId(Caps(7),1,Caps(42),1,out _),"A second ID in button caps must not be ignored");
        Check(VendorProtocol.TrySingleFreqReportId(Array.Empty<byte>(),0,Caps(42),1,out id) && id==42,"Button-only report IDs are supported");
        Check(!VendorProtocol.TrySingleFreqReportId(Array.Empty<byte>(),0,Array.Empty<byte>(),0,out _),"Absent descriptors are not guessed");
        Check(!VendorProtocol.TrySingleFreqReportId(Caps(7),2,Array.Empty<byte>(),0,out _),"Truncated capability arrays rejected");
        Check(VendorProtocol.BuildFreqSchemeAPacket(7,10)==null,"Report buffer must fit ID plus all ten request bytes");
        Check(!VendorProtocol.TryParseFreqFrame(Array.Empty<byte>(),"a",7,0,out _,out _),"Empty input rejected without indexing");
        Check(!VendorProtocol.TryParseFreqFrame(Raw(42,0),"dual",7,65,out _,out _),"An unresolved scheme cannot decode as a");
        Check(!VendorProtocol.IsFreqBatteryFrame(Raw(42,0,8),"a",7),"Wrong report ID is filtered before decoding");

        var link=new Link(); link.Input.Enqueue(Raw(99,0));
        link.OnWrite=packet=>
        {
            link.Input.Enqueue(packet.ToArray());
            link.Input.Enqueue(Raw(44,0,8));
            link.Input.Enqueue(Raw(101,0));
            link.Input.Enqueue(Raw(link.Writes.Count==1?(byte)17:(byte)42,link.Writes.Count==1?(byte)0:(byte)1));
        };
        var reading=VendorProtocol.ReadReportedFreqExchange(link,7,42);
        Check(reading.Level==42 && reading.Charging && link.Writes.Count==root.GetProperty("transactionWrites").GetInt32(),"Two new exchanges required; stale, echo and unrelated frames skipped");
        Check(link.Writes.All(p=>p.Length==65 && p[0]==42 && p[1]==0x55 && p[2]==0x30),"Only the descriptor-numbered read-only battery request is written");
        var zero=new Link(); zero.OnWrite=_=>zero.Input.Enqueue(Raw(0,0));
        Check(VendorProtocol.ReadReportedFreqExchange(zero,7,42).Level==0,"0% is a valid battery, not offline");
        var missing=new Link(); missing.OnWrite=_=> { if(missing.Writes.Count==1) missing.Input.Enqueue(Raw(17,0)); };
        var failed=VendorProtocol.ReadReportedFreqExchange(missing,7,42);
        Check(failed.Level==-1 && !failed.Charging && failed.Diagnostic.Contains("2/2"),"Missing second response never promotes the first reading");
        var echo=new Link(); echo.OnWrite=p=>echo.Input.Enqueue(p.ToArray());
        var echoed=VendorProtocol.ReadReportedFreqExchange(echo,7,42);
        Check(echoed.Level==-1 && echoed.Diagnostic.Contains("回显=1"),"Echo-only input times out without battery");
        var unnumbered=new Link();
        Check(VendorProtocol.ReadReportedFreqExchange(unnumbered,0,42).Level==-1 && unnumbered.Writes.Count==0,"Unnumbered input is not aligned by guessing");
        Check(VendorProtocol.ReadReportedFreqExchange(unnumbered,7,0).Level==-1 && unnumbered.Writes.Count==0,"Unknown output ID sends nothing");
        var shortWrite=new Link { WriteOk=false };
        Check(VendorProtocol.ReadReportedFreqExchange(shortWrite,7,42).Level==-1 && shortWrite.Writes.Count==1,"Short output writes fail closed");
        var flood=new Link(); for(int i=0;i<32;i++) flood.Input.Enqueue(Raw(99,0));
        Check(VendorProtocol.ReadReportedFreqExchange(flood,7,42).Level==-1 && flood.Writes.Count==0,"Undrained input cannot become a current response");

        // Sanitized facts from the old 2026-11-05 09:23 report. No user's path or serial is retained.
        InstanceScope.BeginScan();
        var parent=new[] { (@"USB\VID_A8A5&PID_2255\SYNTHETIC_A","usbccgp") };
        string instance=InstanceScope.Register(path,parent);
        string mousePath="test#vid_a8a5&pid_2255&mi_00&col01#A";
        string keyPath="test#vid_a8a5&pid_2255&mi_01&col01#A";
        Check(InstanceScope.Register(mousePath,parent)==instance && InstanceScope.Register(keyPath,parent)==instance,"Three reported USB interfaces merge by their physical parent");
        string foreign=path.Replace("#A","#B");
        InstanceScope.Register(foreign,new[] { (@"USB\VID_A8A5&PID_2255\SYNTHETIC_B","usbccgp") });
        var bucket=new HidDiscovery.Bucket { Vid=0xA8A5,Pid=0x2255,Product="EWEADN X23SE",InstanceId=instance };
        foreach(var usage in new (ushort Page,ushort Usage)[] { (1,6),(1,2),(0xFF01,0x10),(0xFF06,2) })
        {
            if(!HidDiscovery.TryDeviceKind(bucket.Vid,bucket.Pid,usage.Page,usage.Usage,out var kind,bucket.Product)) continue;
            bucket.Counts[kind]=bucket.Counts.GetValueOrDefault(kind)+1;
            if(UsageMap.Priority(kind)>UsageMap.Priority(bucket.Kind)) bucket.Kind=kind;
        }
        var buckets=new Dictionary<string,HidDiscovery.Bucket> { [instance]=bucket };
        var channel=new HidDiscovery.VendorCollectionInfo { Path=path,Vid=0xA8A5,Pid=0x2255,Product="EWEADN X23SE",UsagePage=0xFF01,Usage=0x10,Input=65,Output=65,Feature=0 };
        var channels=new List<HidDiscovery.VendorCollectionInfo> { channel,channel,
            new() { Path=foreign,Vid=0xA8A5,Pid=0x2255,Product="EWEADN X23SE",UsagePage=0xFF01,Usage=0x10,Input=65,Output=65 } };
        int queries=0;
        ReferenceBatteryProtocol.Reading Query(string selected)
        {
            queries++;
            Check(selected==path && InstanceScope.Current==instance && !InstanceScope.Allows(foreign),"Native query route is confined to the correct physical receiver");
            var io=new Link(); io.OnWrite=_=>io.Input.Enqueue(Raw(42,1));
            return VendorProtocol.ReadReportedFreqExchange(io,7,42);
        }
        HidDiscovery.QueryReportedX23SeCollections(buckets,channels,false,null,Query);
        var device=bucket.ToDevice();
        Check(queries==1 && buckets.Count==1 && device.Name=="EWEADN X23SE" && device.RawName==device.Name && device.Kind==DeviceKind.Mouse,"Report replays as one correctly named mouse, never a keyboard");
        Check(device.Level==42 && device.Charging && device.BatteryRead && device.Transport=="2.4G","Discovery routes battery and charging result to that receiver");
        Check(device.ReceiverDiscovered && !device.PeripheralConfirmed,"Valid receiver battery response is not proof of radio peripheral online state");
        queries=0;
        HidDiscovery.QueryReportedX23SeCollections(buckets,channels,true,null,Query);
        Check(queries==0 && bucket.Battery==-1,"Safe mode issues no query and cannot retain a fresh old result");
        var preferences=new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds=new() { instance } });
        HidDiscovery.QueryReportedX23SeCollections(buckets,channels,false,preferences,Query);
        Check(queries==0,"Disabled instance stays gated");
        channels.RemoveAll(c=>c.Path==path);
        HidDiscovery.QueryReportedX23SeCollections(buckets,channels,false,null,Query);
        Check(queries==0 && bucket.Battery==-1,"An identical other receiver cannot supply this receiver's missing channel");
        Check(HidDiscovery.ExclusionReason(new[] { (@"HID\GVINPUT&COL01\SYNTHETIC","mouhid"),(@"ROOT\HIDCLASS\SYNTHETIC","gvinput") })!=null,"Reported virtual multitouch bus remains excluded");
        Check(HidDiscovery.ExclusionReason(new[] { (@"HID\SYNTHETIC\I2C","mouhid"),(@"ACPI\SYNTHETIC\I2C","hidi2c") })!=null,"Reported built-in I2C input remains excluded");
        return count;
    }

    private static byte[] Raw(byte level,byte charge,byte inputId=7)
    {
        var b=new byte[65]; b[0]=inputId;b[1]=0xAA;b[2]=0x30;b[9]=level;b[10]=charge;return b;
    }
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        public Queue<byte[]> Input=new();
        public List<byte[]> Writes=new();
        public Action<byte[]>? OnWrite;
        public bool WriteOk=true;
        public byte[]? Read(int ms) { Now+=Math.Max(1,ms);return Input.Count>0?Input.Dequeue():null; }
        public bool Write(byte[] request,int timeoutMs) { Writes.Add(request.ToArray());if(WriteOk)OnWrite?.Invoke(request);return WriteOk; }
        public bool SetFeature(byte[] request)=>throw new Exception("FREQ must not use Feature");
        public byte[] GetFeature(byte id,int length)=>throw new Exception("FREQ must not use Feature");
        public void Delay(int milliseconds)=>throw new Exception("No generation probe is needed on this 65/65 route");
    }
}
