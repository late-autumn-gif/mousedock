using System.Text;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class OriginalGpwScopedRegression
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string why) { count++; if(!ok)throw new Exception("GPW: "+why); }
        Check(RecognitionPolicy.CanQuery(0x046D,0xC539,"USB Receiver") && RecognitionPolicy.CanQuery(0x046D,0xC088,"PRO Wireless Gaming Mouse"),"Finite original GPW routes enabled");
        Check(!RecognitionPolicy.CanQuery(0x046D,0xC53F,"USB Receiver") && !RecognitionPolicy.CanQuery(0x046D,0xC545,"SUPERLIGHT"),"Other Logitech receivers not admitted");
        Check(OriginalGpwProtocol.IsChannel(0x046D,0xC539,0xFF00,2,20,20,0),"Reported C539 long collection");
        foreach(var wrong in new (ushort Page,ushort Usage,int In,int Out,int Feature)[] { (0xFF00,1,7,7,0),(0xFF00,4,32,32,0),(0xFF00,2,20,20,20),(1,2,20,20,0),(0xFF00,1,20,20,0) })
            Check(!OriginalGpwProtocol.IsChannel(0x046D,0xC539,wrong.Page,wrong.Usage,wrong.In,wrong.Out,wrong.Feature),"Short, DFU, feature or unrelated collection rejected");
        var voltage=Voltage(4096,0);
        Check(OriginalGpwProtocol.TryVoltage(voltage,out int level,out bool charging) && level==93 && !charging,"4096mV is estimated93%, not high-byte16%");
        Check(OriginalGpwProtocol.TryVoltage(Voltage(3800,0),out level,out _) && level==47,"Existing original-GPW energy model");
        Check(OriginalGpwProtocol.TryVoltage(Voltage(4118,0x80),out level,out charging) && level==50 && charging,"Charging curve differs from discharge");
        Check(!OriginalGpwProtocol.TryVoltage(Voltage(4200,0x80),out level,out charging) && level==-1 && !charging,"Charging plateau cannot invent 68..100%");
        Check(OriginalGpwProtocol.TryVoltage(Voltage(4200,0x81),out level,out charging) && level==100 && !charging,"Explicit full state is100, not active charging");
        foreach(int mv in new[] { 0,1000,2799,4501,65535 })
            Check(!OriginalGpwProtocol.TryVoltage(Voltage(mv,0x81),out _,out _),"Invalid voltage cannot use full shortcut");
        for(int flags=0x83;flags<=0x87;flags++)
            Check(!OriginalGpwProtocol.TryVoltage(Voltage(4118,(byte)flags),out _,out _),"Unknown charging state rejected");
        Check(!OriginalGpwProtocol.TryVoltage(Voltage(3800,0x40),out _,out _),"Reserved flag rejected");
        Check(!OriginalGpwProtocol.TryVoltage(voltage[..7],out _,out _),"Truncated short response cannot become voltage");
        var info=DeviceInfo();
        Check(OriginalGpwProtocol.TryDeviceIdentity(info,out string identity) && identity=="HID++:WPID=4079,USB=C088","Device-info transport-specific identity, independently of device name");
        info[11]=0x50;
        Check(!OriginalGpwProtocol.TryDeviceIdentity(info,out _),"Wrong WPID cannot borrow correct USB ID");
        info=DeviceInfo();info[13]=0xC5;
        Check(!OriginalGpwProtocol.TryDeviceIdentity(info,out _),"Receiver ID in USB identity is not GPW");
        info=DeviceInfo();info[10]=0;info[4]=0x40;info[5]=0x79;
        Check(!OriginalGpwProtocol.TryDeviceIdentity(info,out _),"Matching bytes elsewhere are not an identity");
        info=DeviceInfo();info[10]=4;
        Check(OriginalGpwProtocol.TryDeviceIdentity(info,out identity) && identity=="HID++:WPID=4079","Wireless-only model identity");
        info=DeviceInfo();info[10]=8;info[11]=0xC0;info[12]=0x88;
        Check(OriginalGpwProtocol.TryDeviceIdentity(info,out identity) && identity=="HID++:USB=C088","USB-only transport ID at compact offset");

        var link=new Link(); link.Input.Enqueue(Voltage(3500,0));
        var read=OriginalGpwProtocol.Read(link,0xC539);
        Check(read.Level==93 && read.Model==OriginalGpwProtocol.Model && read.Identity==link.Name && link.VoltageReads==2,"Full name and two fresh voltage transactions");
        Check(read.Diagnostic.Contains("估算") && read.Diagnostic.Contains("mV=4096"),"Estimate and raw voltage provenance visible");
        Check(link.Requests.All(p=>p.Length==20 && p[0]==0x11 && p[1]==1 && (p[3]&15)!=0),"Only long messages, slot1 and nonzero software ID");
        Check(link.Requests.All(p=>p[2] is 0 or 2 or 3 && (p[3]>>4)<=1),"Only root, name and battery functions, no settings writes");
        for(int i=1;i<link.Requests.Count;i++)Check((link.Requests[i][3]&15)!=(link.Requests[i-1][3]&15),"Successive queries use distinct software IDs");

        foreach(string other in new[] { "PRO X SUPERLIGHT","G502 LIGHTSPEED","USB Receiver","PRO Wireless Gaming Mouse 2" })
        {
            var wrong=new Link { Name=other };
            read=OriginalGpwProtocol.Read(wrong,0xC539);
            Check(read.Level==-1 && read.Model==null && wrong.VoltageReads==0 && !wrong.Requests.Any(p=>p[2]==0 && p[4]==0x10),"Shared receiver name is not first-generation GPW: "+other);
        }
        var absent=new Link { NoName=true };
        Check(OriginalGpwProtocol.Read(absent,0xC539).Level==-1 && absent.VoltageReads==0,"No receiver identity means no battery curve");
        var ids=new Link { NoName=true,HasDeviceInfo=true };
        read=OriginalGpwProtocol.Read(ids,0xC539);
        Check(read.Level==93 && read.Identity=="HID++:WPID=4079,USB=C088" && ids.DeviceInfoReads==2,"Name service failure can use two live correct model IDs");
        var wrongInfo=new Link { NoName=true,HasDeviceInfo=true };
        wrongInfo.Transform=(request,reply)=>{if(request[2]==4)reply[12]=0x99;return new[] {reply};};
        Check(OriginalGpwProtocol.Read(wrongInfo,0xC539).Level==-1 && wrongInfo.VoltageReads==0,"Different wireless model never receives GPW battery queries");
        var partial=new Link { DropLastNameChunk=true };
        Check(OriginalGpwProtocol.Read(partial,0xC539).Model==null && partial.VoltageReads==0,"Partial name never confirms GPW");
        var malformed=new Link(); malformed.Transform=(request,reply)=>{if(request[2]==2 && request[3]>>4==1)reply[4]=0xFF;return new[] {reply};};
        Check(OriginalGpwProtocol.Read(malformed,0xC539).Model==null && malformed.VoltageReads==0,"Malformed UTF8 name rejected");

        foreach(string error in new[] { "wrong-slot","wrong-feature","wrong-function","wrong-client","zero-client","short","echo" })
        {
            var filtered=new Link(); filtered.Transform=(request,reply)=>
            {
                var bad=reply.ToArray();
                switch(error)
                {
                    case "wrong-slot":bad[1]=2;break;
                    case "wrong-feature":bad[2]=99;break;
                    case "wrong-function":bad[3]^=0x10;break;
                    case "wrong-client":bad[3]=(byte)((bad[3]&0xF0)|((bad[3]+1)%15+1));if(bad[3]==request[3])bad[3]^=1;break;
                    case "zero-client":bad[3]&=0xF0;break;
                    case "short":bad=bad[..7];break;
                    case "echo":bad=request.ToArray();break;
                }
                return new[] { bad,reply };
            };
            Check(OriginalGpwProtocol.Read(filtered,0xC539).Level==93,"Invalid traffic skipped before correct response: "+error);
        }
        var loose=new Link();loose.Transform=(_,reply)=>{reply[3]&=0xF0;return new[] {reply};};
        var looseResult=OriginalGpwProtocol.Read(loose,0xC539);
        Check(looseResult.Level==-1 && loose.VoltageReads==0 && looseResult.Diagnostic.Contains("LASTREJECT=11"),"No loose software-ID fallback; rejected traffic available in diagnostics");
        var one=new Link { DropSecondVoltage=true };
        read=OriginalGpwProtocol.Read(one,0xC539);
        Check(read.Level==-1 && read.Model==OriginalGpwProtocol.Model && read.Identity==one.Name,"One battery response cannot become fresh battery, name evidence survives");
        var plateau=new Link { Millivolts=4200,Flags=0x80 };
        Check(OriginalGpwProtocol.Read(plateau,0xC539).Level==-1,"No shape/percentage fallback on ambiguous charging plateau");
        var direct=new Link { NoName=true };
        Check(OriginalGpwProtocol.Read(direct,0xC088).Level==93,"Direct C088 USB identity permits GPW model without name service");
        var ff=new Link { AllowedSlot=0xFF,NoName=true };
        Check(OriginalGpwProtocol.Read(ff,0xC088).Level==93 && ff.Requests.Any(p=>p[1]==0xFF),"Direct C088 may use FF endpoint; receiver is never probed as FF");
        var unsupported=new Link();
        Check(OriginalGpwProtocol.Read(unsupported,0xC53F).Level==-1 && unsupported.Requests.Count==0,"Other PID sends no request");
        var flood=new Link();for(int i=0;i<32;i++)flood.Input.Enqueue(voltage);
        Check(OriginalGpwProtocol.Read(flood,0xC539).Level==-1 && flood.Requests.Count==0,"Undrained queue aborts before write");
        var shortWrite=new Link { WriteOk=false };
        Check(OriginalGpwProtocol.Read(shortWrite,0xC539).Level==-1,"Short write cannot yield a battery");
        var protocolError=new Link();protocolError.Transform=(request,_)=>
        {var b=new byte[20];b[0]=0x11;b[1]=request[1];b[2]=0xFF;b[3]=request[2];b[4]=request[3];b[5]=8;return new[] {b};};
        Check(OriginalGpwProtocol.Read(protocolError,0xC539).Diagnostic.Contains("HID++ 错误 0x08"),"Correlated protocol errors diagnosed");

        // Sanitized facts only from the old 2026-11-05 16:26 report; no real PnP IDs retained.
        InstanceScope.BeginScan();
        const string path="test#vid_046d&pid_c539&mi_02&col02#A";
        const string second="test#vid_046d&pid_c539&mi_02&col02#B";
        string a=InstanceScope.Register(path,new[] { (@"USB\VID_046D&PID_C539\SYNTHETIC_A","usbccgp") });
        string b=InstanceScope.Register(second,new[] { (@"USB\VID_046D&PID_C539\SYNTHETIC_B","usbccgp") });
        var ba=new HidDiscovery.Bucket { Vid=0x046D,Pid=0xC539,InstanceId=a,Product="USB Receiver",Kind=DeviceKind.Keyboard };
        var bb=new HidDiscovery.Bucket { Vid=0x046D,Pid=0xC539,InstanceId=b,Product="USB Receiver",Kind=DeviceKind.Mouse };
        var buckets=new Dictionary<string,HidDiscovery.Bucket> { [a]=ba,[b]=bb };
        HidDiscovery.VendorCollectionInfo Channel(string p)=>new() { Path=p,Vid=0x046D,Pid=0xC539,Product="USB Receiver",UsagePage=0xFF00,Usage=2,Input=20,Output=20 };
        var channels=new List<HidDiscovery.VendorCollectionInfo> { Channel(path),Channel(path),Channel(second),
            new() { Path=path+"short",Vid=0x046D,Pid=0xC539,UsagePage=0xFF00,Usage=1,Input=7,Output=7 },
            new() { Path=path+"dfu",Vid=0x046D,Pid=0xC539,UsagePage=0xFF00,Usage=4,Input=32,Output=32 } };
        int calls=0;
        ReferenceBatteryProtocol.Reading Query(string p,ushort vid,ushort pid)
        {
            calls++;
            Check(InstanceScope.Allows(p) && !InstanceScope.Allows(p==path?second:path),"Exact instance channel isolation");
            return p==path?new(93,false,true,ReferenceBatteryProtocol.State.Online,"synthetic",OriginalGpwProtocol.Model,"PRO Wireless Gaming Mouse"):
                ReferenceBatteryProtocol.Reading.Failed("Different device");
        }
        HidDiscovery.QueryOriginalGpwCollections(buckets,channels,false,null,Query);
        Check(calls==2 && ba.ToDevice().Level==93 && bb.ToDevice().Level==-1,"Same receiver PID never shares battery or model");
        Check(ba.ToDevice().Name==OriginalGpwProtocol.Model && ba.ToDevice().Kind==DeviceKind.Mouse && ba.ToDevice().RawName=="USB Receiver" && ba.ToDevice().PeripheralConfirmed,"Composite mouse and confirmed model with raw name retained");
        Check(bb.ToDevice().ConfirmedModel=="" && bb.ToDevice().ReceiverDiscovered && !bb.ToDevice().PeripheralConfirmed,"Receiver discovery is not peripheral/model confirmation");
        Check(ba.ToDevice().PhysicalKey!=bb.ToDevice().PhysicalKey,"Instances not merged by GPW model");
        calls=0;
        HidDiscovery.QueryOriginalGpwCollections(buckets,channels,true,null,Query);
        Check(calls==0 && ba.ToDevice().Level==-1 && ba.ToDevice().ConfirmedModel=="","Safe mode cannot promote previous model/battery");
        var prefs=new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds=new() { a,b } });
        HidDiscovery.QueryOriginalGpwCollections(buckets,channels,false,prefs,Query);
        Check(calls==0,"Disabled instance sends nothing");
        channels.RemoveAll(c=>c.Path==path);
        HidDiscovery.QueryOriginalGpwCollections(buckets,channels,false,null,Query);
        Check(calls==1 && ba.Battery==-1,"Another receiver cannot lend its missing long channel");
        Check(VendorProtocol.ProbeDevice(0x046D,0xC539,"GPW alias",false).Exchanges==0,"Export never reopens all receiver instances");
        Check(VendorProtocol.ReadOriginalGpw(path,0x046D,0xC539).Level==-1,"Unscoped native call sends nothing");
        return count;
    }

    private static byte[] Voltage(int mv,byte flags)
    {var r=new byte[20];r[0]=0x11;r[4]=(byte)(mv>>8);r[5]=(byte)mv;r[6]=flags;return r;}
    private static byte[] DeviceInfo()
    {var r=new byte[20];r[0]=0x11;r[10]=12;r[11]=0x40;r[12]=0x79;r[13]=0xC0;r[14]=0x88;return r;}
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now {get;private set;}
        public Queue<byte[]> Input=new();
        public List<byte[]> Requests=new();
        public string Name="PRO Wireless Gaming Mouse";
        public bool NoName,DropLastNameChunk,DropSecondVoltage,WriteOk=true,HasDeviceInfo;
        public int VoltageReads,DeviceInfoReads,Millivolts=4096;
        public byte Flags,AllowedSlot=1;
        public Func<byte[],byte[],byte[][]>? Transform;
        public byte[]? Read(int ms) {Now+=Math.Max(1,ms);return Input.Count>0?Input.Dequeue():null;}
        public bool Write(byte[] request,int ms)
        {
            Requests.Add(request.ToArray());
            if(!WriteOk)return false;
            if(request[1]!=AllowedSlot)return true;
            var reply=new byte[20];Array.Copy(request,reply,4);
            if(request[2]==0)
            {ushort id=(ushort)((request[4]<<8)|request[5]);reply[4]=id==5?(NoName?(byte)0:(byte)2):id==0x1001?(byte)3:id==3 && HasDeviceInfo?(byte)4:(byte)0;}
            else if(request[2]==2)
            {
                var name=Encoding.UTF8.GetBytes(Name);
                if(request[3]>>4==0)reply[4]=(byte)name.Length;
                else {int offset=request[4];if(DropLastNameChunk && offset>0)return true;name.AsSpan(offset,Math.Min(16,name.Length-offset)).CopyTo(reply.AsSpan(4));}
            }
            else if(request[2]==3)
            {VoltageReads++;if(DropSecondVoltage && VoltageReads>1)return true;reply[4]=(byte)(Millivolts>>8);reply[5]=(byte)Millivolts;reply[6]=Flags;}
            else if(request[2]==4)
            {DeviceInfoReads++;var ids=DeviceInfo();Array.Copy(ids,4,reply,4,16);}
            else throw new Exception("Unexpected feature command");
            foreach(var r in Transform?.Invoke(request,reply)??new[] {reply})Input.Enqueue(r);
            return true;
        }
        public bool SetFeature(byte[] r)=>throw new Exception("No feature transfer");
        public byte[] GetFeature(byte id,int length)=>throw new Exception("No feature transfer");
        public void Delay(int ms)=>throw new Exception("No configuration/wake delay");
    }
}
