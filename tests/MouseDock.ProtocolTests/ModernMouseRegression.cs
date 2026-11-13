using MouseDock.Services.Detection;
using MouseDock.Services;
using MouseDock.Models;
internal static class ModernMouseRegression
{
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now{get;private set;}public int Writes;public Queue<byte[]> Input=new();public Func<int,byte[][]> Reply=_=>Array.Empty<byte[]>();
        public byte[]? Read(int timeout){Now+=Math.Max(1,timeout);return Input.Count>0?Input.Dequeue():null;}
        public Func<byte[],byte[][]>? ReplyTo;
        public bool Write(byte[] tx,int timeout){Writes++;foreach(var frame in ReplyTo?.Invoke(tx) ?? Reply(Writes))Input.Enqueue(frame);return true;}
        public bool SetFeature(byte[] b)=>throw new Exception();public byte[] GetFeature(byte id,int n)=>throw new Exception();public void Delay(int ms)=>throw new Exception();
    }
    private static byte[] Frame(ushort pid,int level,int ch)
    {
        var r=new byte[64];r[0]=0x4D;r[1]=1;r[2]=1;r[3]=14;r[5]=9;r[8]=0x37;r[9]=0x38;r[10]=(byte)pid;r[11]=(byte)(pid>>8);r[19]=(byte)ch;r[20]=(byte)level;CheckSum(r);return r;
    }
    private static void CheckSum(byte[] r){byte c=0;for(int i=2;i<8+r[3];i++)c^=r[i];r[8+r[3]]=c;}

    private static Link DpiLink(ushort pid, bool separateAxes = false, bool ignoreSelection = false)
    {
        byte[] data = [0, 0, 6, 2, separateAxes ? (byte)1 : (byte)0, 0x90, 1, 0x20, 3,
            0x40, 6, 0x80, 12, 0, 25, 0x10, 0xA4];
        return new Link
        {
            ReplyTo = request => (request[4] | request[5] << 8) switch
            {
                0x0900 => [Frame(pid, 76, 0)],
                0x0002 => [ModernMouseDpi.BuildRequest(2, new byte[9])],
                0x0003 => [ModernMouseDpi.BuildRequest(3, data)],
                0x0103 => Select(request),
                _ => throw new InvalidOperationException("Unexpected DPI request")
            }
        };

        byte[][] Select(byte[] request)
        {
            if (!ModernMouseDpi.TryPayload(request, 0x0103, 17, out var payload) ||
                payload[0] != 0 || payload[1] != 0 || payload[2] != 0 || payload[3] != 6 ||
                !payload[5..17].SequenceEqual(data.AsSpan(5)))
                throw new InvalidOperationException("Selecting a slot must preserve the DPI table");
            if (!ignoreSelection) data[3] = payload[4];
            return [ModernMouseDpi.BuildRequest(0x0103, [0])];
        }
    }
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
            var dpiLink = DpiLink(p.Wired);
            Check(VendorProtocol.ReadModernMouseExchange(dpiLink, name).Model == name,
                $"DPI fixture confirms exact model for {name}");
            var dpi = ModernMouseDpi.ReadSettings(dpiLink, null);
            Check(dpi is { Index: 2 } && dpi.Values[dpi.Index] == 1600, $"DPI read for {name}");
            var changed = ModernMouseDpi.ReadSettings(dpiLink, new(dpi!, 3));
            Check(changed is { Index: 3 } && changed.Values[3] == 3200, $"DPI write and readback for {name}");
        }
        var split = DpiLink(0x4031, separateAxes: true);
        var splitState = ModernMouseDpi.ReadSettings(split, null);
        Check(splitState is { SeparateAxes: true } &&
            ModernMouseDpi.ReadSettings(split, new(splitState, 3)) == null && split.Writes == 4,
            "Separate axes are readable but do not write");
        var unconfirmed = DpiLink(0x4031, ignoreSelection: true);
        var before = ModernMouseDpi.ReadSettings(unconfirmed, null);
        Check(ModernMouseDpi.ReadSettings(unconfirmed, new(before!, 3)) == null,
            "Acknowledgement without changed readback does not confirm selection");
        var changedProfile = before! with { Profile = 1 };
        var fresh = DpiLink(0x4031);
        Check(ModernMouseDpi.ReadSettings(fresh, new(changedProfile, 3)) == null && fresh.Writes == 2,
            "Profile change prevents write");
        var wrongTable = before with { Values = [800, 800, 1600, 3200, 6400, 42000] };
        fresh = DpiLink(0x4031);
        Check(ModernMouseDpi.ReadSettings(fresh, new(wrongTable, 3)) == null && fresh.Writes == 2,
            "DPI table change prevents write");
        var invalid = ModernMouseDpi.BuildRequest(3, [0, 0, 1, 0, 0, 0x20, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        invalid[13] ^= 1;
        Check(ModernMouseDpi.ParseSettings(invalid, 0) == null, "DPI bad XOR");
        var display = new AutoDetectSource().UpdateFromDetection([new()
        {
            Id = "dpi", Name = "Mouse", Kind = DeviceKind.Mouse, Dpi = 1600, DpiSettings = before
        }]).Single();
        Check(display.DpiLabel == "1600 DPI" && display.CanChangeDpi, "DPI reaches bound device model");
        Check(!VendorProtocol.IsScopedModernMouse(0x3837,0x1018,"USB Receiver"),
            "Generic receiver cannot enter the name-specific modern mouse reader");
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

