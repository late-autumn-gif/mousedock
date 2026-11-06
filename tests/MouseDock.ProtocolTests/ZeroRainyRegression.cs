using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
internal static class ZeroRainyRegression
{
    internal static int Run()
    {
        int n=0;void Check(bool ok,string why){n++;if(!ok)throw new Exception(why);}
        Check(RecognitionPolicy.CanQuery(0x373B,0x1155,"Receiver"),"ZERO candidate enabled");
        Check(RecognitionPolicy.CanQuery(0x373B,0x11D8,"ATK Zero"),"Existing verified COMPX profile preserved");
        Check(ZeroRainyProtocol.Candidate(0x320F,0x5088,"Wireless Gaming Keyboard"),"Generic receiver is candidate, not identity");
        foreach(string other in new[]{"ATK V75X","VGN V98 Pro","Rainy68","WOB Other","VXE V75"})Check(!ZeroRainyProtocol.Candidate(0x320F,0x5088,other),"Other shared-ID products gated");
        Check(!ZeroRainyProtocol.Channel(0x320F,0x5088,"Rainy75",0xFF1C,0x92,64,64,64),"Wrong feature caps rejected");
        var atk=new Link(true);var r=ZeroRainyProtocol.Read(atk,0x373B,0x1155,"Receiver");
        Check(r.Level==42 && r.Model=="ATK ZERO" && r.Charging && atk.Commands.SequenceEqual(new byte[]{0x89,0x89,7,7,15}),"Two identities, two batteries, separate charge query");
        var badId=new Link(true){WrongModel=true};r=ZeroRainyProtocol.Read(badId,0x373B,0x1155,"Receiver");
        Check(r.Level==-1 && r.Model==null && !badId.Commands.Contains((byte)7),"CID/MID0/0 not ZERO and no battery query");
        var pending=new Link(true){Pending=true};Check(ZeroRainyProtocol.Read(pending,0x373B,0x1155,"Receiver").Level==42,"Not-ready frame skipped before actual response");
        var missing=new Link(true){MissingSecond=true};Check(ZeroRainyProtocol.Read(missing,0x373B,0x1155,"Receiver").Level==-1,"Second battery required");
        var noCharge=new Link(true){BadCharge=true};r=ZeroRainyProtocol.Read(noCharge,0x373B,0x1155,"Receiver");Check(r.Level==42&&!r.ChargingKnown&&!r.Charging,"Invalid charge cannot create charging");
        foreach(int pct in Enumerable.Range(0,101))foreach(byte state in new byte[]{0,1,2})
        {
            var rainy=new Link(false){Percent=(byte)pct,Charge=state};r=ZeroRainyProtocol.Read(rainy,0x320F,0x5055,"WOB Rainy75");
            Check(r.Level==pct && r.Charging==(state==1) && r.Model=="WOBKEY Rainy 75","Rainy percentage and its own charge enum");
        }
        var generic=new Link(false){Charge=2};r=ZeroRainyProtocol.Read(generic,0x320F,0x5088,"Wireless Gaming Keyboard");
        Check(r.Level==42&&r.Model==null&&!r.ChargingKnown&&!r.Charging,"Shared protocol does not establish Rainy identity/charge semantics");
        var correlation=new Link(false){WrongCode=true};Check(ZeroRainyProtocol.Read(correlation,0x320F,0x5088,"Rainy75").Level==42,"Rainy checksum is not a request nonce and need not echo20");
        var opcode=new Link(false){WrongOpcode=true};Check(ZeroRainyProtocol.Read(opcode,0x320F,0x5088,"Rainy75").Level==-1,"Wrong Rainy opcode rejected");
        InstanceScope.BeginScan();const string aPath="test#rainy#A",bPath="test#rainy#B";
        string a=InstanceScope.Register(aPath,new[]{(@"USB\VID_320F&PID_5088\SYNTHETIC_A","usbccgp")});
        string b=InstanceScope.Register(bPath,new[]{(@"USB\VID_320F&PID_5088\SYNTHETIC_B","usbccgp")});
        var bucket=new HidDiscovery.Bucket{Vid=0x320F,Pid=0x5088,Product="Wireless Gaming Keyboard",InstanceId=a,Kind=DeviceKind.Keyboard};
        var buckets=new Dictionary<string,HidDiscovery.Bucket>{[a]=bucket};
        HidDiscovery.VendorCollectionInfo Channel(string p)=>new(){Vid=0x320F,Pid=0x5088,Product=bucket.Product,Path=p,UsagePage=0xFF1C,Usage=0x92,Input=64,Output=64};
        var channels=new List<HidDiscovery.VendorCollectionInfo>{Channel(aPath),Channel(aPath),Channel(bPath)};int calls=0;
        ReferenceBatteryProtocol.Reading Query(string p,ushort vid,ushort pid){calls++;Check(p==aPath&&InstanceScope.Allows(p)&&!InstanceScope.Allows(bPath),"Exact physical instance");return new(42,false,false,ReferenceBatteryProtocol.State.Online,"synthetic");}
        HidDiscovery.QueryZeroRainy(buckets,channels,false,null,Query);var d=bucket.ToDevice();
        Check(calls==1&&d.Level==42&&d.Name==bucket.Product&&d.ConfirmedModel==""&&!d.PeripheralConfirmed,"Generic keyboard keeps original identity with independent battery");
        calls=0;HidDiscovery.QueryZeroRainy(buckets,channels,true,null,Query);Check(calls==0&&bucket.Battery==-1,"Safe mode no query");
        var prefs=new DevicePreferences(new AppSettings{DisabledRecognitionDeviceIds=new(){a}});
        HidDiscovery.QueryZeroRainy(buckets,channels,false,prefs,Query);Check(calls==0,"Disabled instance no query");
        channels.RemoveAll(c=>c.Path==aPath);HidDiscovery.QueryZeroRainy(buckets,channels,false,null,Query);Check(calls==0,"Other receiver cannot lend collection");
        Check(VendorProtocol.ProbeDevice(0x320F,0x5088,"Alias",false).Exchanges==0,"Export cannot reopen all shared-PID devices");
        Check(VendorProtocol.ReadZeroRainy(aPath,0x320F,0x5088).Level==-1,"Unscoped native call rejected");
        return n;
    }
    private sealed class Link(bool atk):ReferenceBatteryProtocol.ILink
    {
        public long Now{get;private set;}public Queue<byte[]> Input=new();public List<byte>Commands=new();
        public bool WrongModel,Pending,MissingSecond,BadCharge,WrongCode,WrongOpcode;public byte Percent=42,Charge=1;private int batteries;
        public byte[]? Read(int ms){Now+=Math.Max(1,ms);return Input.Count>0?Input.Dequeue():null;}
        public bool Write(byte[] tx,int ms)
        {
            byte command=atk?tx[6]:(byte)0x1A;Commands.Add(command);Input.Enqueue(tx.ToArray());
            var r=new byte[64];
            if(atk){r[0]=8;r[5]=command;if(command==0x89){r[8]=WrongModel?(byte)0:(byte)1;r[9]=WrongModel?(byte)0:(byte)1;}else if(command==7){batteries++;if(MissingSecond&&batteries>1)return true;r[7]=Percent;}else r[7]=BadCharge?(byte)255:Charge;
                if(Pending){var waiting=r.ToArray();waiting[2]=1;Input.Enqueue(waiting);}}
            else{r=tx.ToArray();r[7]=1;r[8]=Percent;r[9]=Charge;if(WrongCode)r[1]++;if(WrongOpcode)r[3]=0xFF;}
            Input.Enqueue(r);return true;
        }
        public bool SetFeature(byte[] b)=>throw new Exception();public byte[] GetFeature(byte id,int n)=>throw new Exception();public void Delay(int ms)=>throw new Exception();
    }
}
