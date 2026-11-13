using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class DpiReviewRegression
{
    internal static int Run()=>Pulsar()+Sora()+Mchose()+Revocation();
    internal static int Pulsar()
    {
        var profile=DpiProfiles.Find(0x3710,21510)!;
        var beforeHold=new StartFailureLink(false);
        Require(Fails(()=>DpiProtocol.Read(profile,beforeHold))&&beforeHold.Holds==0&&beforeHold.Releases==0,
            "Pulsar must send no hold release when the initial input drain fails before any hold attempt");
        var afterHold=new StartFailureLink(true);
        Require(Fails(()=>DpiProtocol.Read(profile,afterHold))&&afterHold.Holds==1&&afterHold.Releases==1,
            "Pulsar must attempt one release when the hold transfer itself fails");
        return 2;
    }
    internal static int Sora()
    {
        var profile=DpiProfiles.Find(0x1915,0xAE8A)!;
        var signature=new DpiSignature(0xFFA0,1,0,0,74,new HashSet<byte>(),new HashSet<byte>(),
            new HashSet<byte>{4,5},new Dictionary<byte,int>{{4,74},{5,32}});
        Require(!signature.Main(profile,0),
            "HID value-cap totals cannot prove exact Sora Feature5 layout; native selection must fail closed");
        var fake=new DpiSafetyRegression.FakeBackend();var prefs=new DevicePreferences(new AppSettings());
        var service=new DpiService(fake,()=>false,()=>prefs,1000);
        var device=new DeviceInfo{Id="instance-sora-unproven",Detail="1915:AE8A",Name="Sora V2"};
        Require(!service.Supports(device),"Sora native DPI must stay hidden until exact ID5 layout is proven");
        Require(Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&fake.Reads==0,
            "Sora native read must fail before opening a control channel");
        var values=new byte[72];values[2]=5;values[18]=8;values[20]=31;
        Require(DpiNativeLink.FeatureValueByteLowerBounds(values,1)[5]==32,
            "value-cap byte totals remain explicitly lower-bound evidence only");
        values[3]=1;
        Require(Fails(()=>DpiNativeLink.FeatureValueByteLowerBounds(values,1)),
            "aliased value caps must not be counted as independent report fields");
        return 5;
    }
    internal static int Mchose()
    {
        var property=typeof(DeviceInfo).GetProperty("DpiCapturedAt");
        Require(property!=null,"validated scan DPI needs its original observation timestamp");
        var fake=new DpiSafetyRegression.FakeBackend();var prefs=new DevicePreferences(new AppSettings());
        var service=new DpiService(fake,()=>false,()=>prefs,1000);
        var device=new DeviceInfo{Id="instance-review-mchose",Detail="3837:100B",Name="A7 V2",Dpi=1200};
        Require(!service.Supports(device),"MCHOSE current DPI with no valid timestamp must remain unknown");
        var stale=DateTimeOffset.UtcNow.AddSeconds(-91);
        property!.SetValue(device,stale);
        Require(!service.Supports(device)&&Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&fake.Reads==0,
            "MCHOSE projection must reject scan data older than 90 seconds");
        property.SetValue(device,DateTimeOffset.UtcNow.AddMinutes(1));
        Require(!service.Supports(device),"future scan timestamps cannot renew stale MCHOSE data");
        var captured=DateTimeOffset.UtcNow.AddSeconds(-10);property.SetValue(device,captured);
        Require(service.Supports(device),"fresh validated MCHOSE scan DPI remains visible");
        var state=service.ReadAsync(device).GetAwaiter().GetResult();
        Require(state.CapturedAt==captured&&!state.Writable&&state.Current.X==1200&&fake.Reads==0,
            "MCHOSE projection preserves scan timestamp rather than relabeling cached data as a fresh device read");
        return 6;
    }
    internal static int Revocation()
    {
        var fake=new DpiSafetyRegression.FakeBackend{Block=new ManualResetEventSlim(false)};
        var prefs=new DevicePreferences(new AppSettings());var service=new DpiService(fake,()=>false,()=>prefs,2000);
        var device=DpiSafetyRegression.Device("revocation");
        var pending=service.ReadAsync(device);
        Require(fake.Entered.Wait(1000),"revocation fixture started");
        service.Invalidate(device.Id);fake.Block.Set();
        Require(Fails(()=>pending.GetAwaiter().GetResult()),
            "invalidating during a native operation must reject its late response and prevent it restoring snapshot authority");
        return 2;
    }
    private static bool Fails(Action action)
    {try{action();return false;}catch(Exception e) when(e is InvalidOperationException or System.IO.IOException or TimeoutException){return true;}}
    private static void Require(bool value,string message){if(!value)throw new Exception("DPI review: "+message);}
    private sealed class StartFailureLink(bool afterHold) : IDpiLink
    {
        internal int Holds,Releases;
        public long Now=>0;
        public byte[] Read(int ms)
        {if(!afterHold)throw new System.IO.IOException("drain failed");return null!;}
        public bool Write(byte[] packet,int ms)
        {
            if(packet[1]!=3)throw new Exception("unexpected packet");
            if(packet[6]==0){Releases++;return true;}
            Holds++;throw new System.IO.IOException("hold transfer failed");
        }
        public bool SetFeature(byte[] b)=>throw new NotSupportedException();
        public byte[] GetFeature(byte id,int length)=>throw new NotSupportedException();
        public void Delay(int ms){}
    }
}
