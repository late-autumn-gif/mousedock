using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class DpiSafetyRegression
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string why) {checks++;if(!value)throw new Exception("DPI safety: "+why);}
        DpiSignature Signature(ushort page,ushort usage,int input,int output,int feature,byte[] ins,byte[] outs,byte[] feats,
            Dictionary<byte,int>? sizes=null)=>new(page,usage,input,output,feature,ins.ToHashSet(),outs.ToHashSet(),feats.ToHashSet(),sizes??new());
        var standard=Signature(0xFF02,2,17,17,0,new byte[]{8},new byte[]{8},Array.Empty<byte>());
        var main=new DpiChannel("instance-one","path-main",0x373B,0x11D8,1,standard);
        var other=main with{InstanceId="instance-two",Path="other-device"};
        Check(DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main,other}).Main==main,
            "the same model in another physical instance is never joined");
        Check(Fails(()=>DpiConnection.Resolve("instance-none",0x373B,0x11D8,new[]{main,other})),
            "unknown instance cannot borrow a compatible channel");
        Check(Fails(()=>DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main,main with{Path="duplicate"}})),
            "two matching control interfaces are ambiguous");
        Check(Fails(()=>DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main with{Signature=standard with{Input=64}}})),
            "a collection page does not override its wrong report length");
        Check(Fails(()=>DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main with{Signature=standard with{OutputIds=new HashSet<byte>{8,9}}}})),
            "extra report IDs cannot silently broaden the write channel");
        Check(DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main}).Key!=
            DpiConnection.Resolve("instance-one",0x373B,0x11D8,new[]{main with{Path="replacement"}}).Key,
            "interface replacement invalidates the bound connection fingerprint");
        var sora=DpiProfiles.Find(0x1915,0xAE8A)!;
        var soraSignature=Signature(0xFFA0,1,0,0,74,Array.Empty<byte>(),Array.Empty<byte>(),new byte[]{4,5},new(){{4,74},{5,32}});
        Check(!soraSignature.Main(sora,0),"value-cap totals do not prove Sora's per-ID report geometry");
        Check(DpiProfiles.Find(0x1915,0xAE8C)==null,
            "the signature check is subordinate to exact profile lookup");
        Check(!(soraSignature with{FeatureValueByteLowerBounds=new Dictionary<byte,int>{{4,32},{5,32}}}).Main(sora,0),
            "Sora's old battery-only 32 byte collection cannot be written");
        Check(!(soraSignature with{FeatureValueByteLowerBounds=new Dictionary<byte,int>()}).Main(sora,0),
            "unproven per-report Sora sizes fail closed");
        var pulsar=DpiProfiles.Find(0x3710,21510)!;
        Check(!standard.Main(pulsar,0)&&standard.Main(pulsar,1),"Pulsar requires the approved USB interface 1");
        var rapoo=DpiProfiles.Find(0x24AE,0x1453)!;
        var rmain=new DpiChannel("instance-r","r-main",rapoo.Vid,rapoo.Pid,2,
            Signature(0xFF00,2,19,0,0,new byte[]{7},Array.Empty<byte>(),Array.Empty<byte>()));
        var rout=rmain with{Path="r-out",Signature=Signature(0xFF00,14,0,33,0,Array.Empty<byte>(),new byte[]{6},Array.Empty<byte>())};
        var rfeature=rmain with{Path="r-feature",Signature=Signature(0xFF00,15,0,0,33,Array.Empty<byte>(),Array.Empty<byte>(),new byte[]{8})};
        var resolved=DpiConnection.Resolve("instance-r",rapoo.Vid,rapoo.Pid,new[]{rmain,rout,rfeature});
        Check(resolved.Output==rout&&resolved.Feature==rfeature,"Rapoo binds its three roles within one USB instance/interface");
        Check(DpiConnection.Resolve("instance-r",rapoo.Vid,rapoo.Pid,new[]{rmain,rout with{InstanceId="instance-other"},rfeature}).Output==null,
            "Rapoo never borrows a second device's output collection");
        Check(DpiConnection.Resolve("instance-r",rapoo.Vid,rapoo.Pid,new[]{rmain,rout with{Interface=3},rfeature}).Output==null,
            "Rapoo never joins different USB interfaces");

        var settings=new AppSettings();var prefs=new DevicePreferences(settings);bool safe=false;
        var backend=new FakeBackend();
        var service=new DpiService(backend,()=>safe,()=>prefs,2000);
        var device=Device("service");
        Check(service.Supports(device),"exact physical ATK mouse supports explicit DPI operations");
        Check(!service.Supports(new DeviceInfo{Id="hid-373B-11D8",Detail=device.Detail}),"legacy model IDs are insufficient for settings writes");
        Check(!service.Supports(new DeviceInfo{Id=device.Id,Detail="3837:100B"}),"MCHOSE has no inferred settings support");
        Check(!service.Supports(new DeviceInfo{Id=device.Id,Detail=device.Detail,Kind=DeviceKind.Keyboard}),"mouse settings are not exposed on another device kind");
        var mchose=new DeviceInfo{Id="instance-dpi-readonly",Name="A7 V2 Ultra+",Detail="3837:100B",Dpi=1200,DpiCapturedAt=DateTimeOffset.UtcNow};
        Check(service.Supports(mchose),"existing validated MCHOSE current DPI remains viewable");
        var current=service.ReadAsync(mchose).GetAwaiter().GetResult();
        Check(current.Family==DpiFamily.MchoseReadOnly&&!current.Writable&&current.Current.X==1200&&backend.Reads==0,
            "MCHOSE projects only the existing scan value and sends no new native command");
        Check(Fails(()=>service.ApplyAsync(current,0,1600).GetAwaiter().GetResult())&&backend.Applies==0,
            "MCHOSE read-only display never grants write permission");
        Check(!service.Supports(new DeviceInfo{Id=mchose.Id,Detail="3837:4021",Dpi=1200}),
            "a wired A7 PID has no inferred DPI route");
        Check(!service.Supports(new DeviceInfo{Id=mchose.Id,Detail="3837:100B",Dpi=-1}),
            "unknown MCHOSE current DPI is not presented as a valid reading");
        var snapshot=service.ReadAsync(device).GetAwaiter().GetResult();
        var after=service.ApplyAsync(snapshot,1,1200).GetAwaiter().GetResult();
        Check(after.Active==0&&after.Stages[1].X==1200&&backend.Applies==1,"service applies a selected inactive stage without switching");
        Check(Fails(()=>service.ApplyAsync(snapshot,1,1600).GetAwaiter().GetResult())&&backend.Applies==1,
            "an earlier snapshot cannot be reused");
        service.Invalidate(device.Id);
        Check(Fails(()=>service.ApplyAsync(after,0,null).GetAwaiter().GetResult()),"closing a control window revokes its snapshot");
        safe=true;int reads=backend.Reads;
        Check(Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&backend.Reads==reads,"safe mode blocks before backend I/O");
        safe=false;device.Connected=false;
        Check(Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&backend.Reads==reads,"disconnection blocks before backend I/O");
        device.Connected=true;prefs.Disable(device);
        Check(Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&backend.Reads==reads,"recognition exclusions block before backend I/O");
        prefs.Enable(device);device.RecognitionEnabled=true;
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();
            Check(Fails(()=>service.ReadAsync(device,canceled.Token).GetAwaiter().GetResult())&&backend.Reads==reads,
                "pre-canceled operations issue no backend request");
        }
        backend=new FakeBackend{Block=new ManualResetEventSlim(false)};service=new DpiService(backend,()=>false,()=>prefs,30);
        device=Device("blocked");var timed=service.ReadAsync(device);
        Check(backend.Entered.Wait(1000),"blocking fixture reached the worker");
        Check(Fails(()=>timed.GetAwaiter().GetResult()),"timeout returns without blocking the UI thread");
        Check(Fails(()=>service.ReadAsync(device).GetAwaiter().GetResult())&&backend.Reads==1,"timeout retains exclusive ownership until native work ends");
        backend.Block.Set();SpinWait.SpinUntil(()=>backend.Finished,1000);
        Check(Fails(()=>service.ApplyAsync(backend.State(device.Id),0,null).GetAwaiter().GetResult()),
            "a result arriving after timeout cannot become a writable snapshot");

        backend=new FakeBackend{Block=new ManualResetEventSlim(false)};service=new DpiService(backend,()=>false,()=>prefs,2000);
        device=Device("policy");var pending=service.ReadAsync(device);Check(backend.Entered.Wait(1000),"policy fixture started");
        prefs.Disable(device);backend.Block.Set();
        Check(Fails(()=>pending.GetAwaiter().GetResult()),"mid-flight preference generation changes discard the response");
        return checks;
    }
    internal static DeviceInfo Device(string suffix)=>new(){Id="instance-dpi-"+suffix,Name="ATK Zero",Detail="373B:11D8",Kind=DeviceKind.Mouse};
    private static bool Fails(Action action)
    {try{action();return false;}catch(Exception e) when(e is InvalidOperationException or TimeoutException or OperationCanceledException or System.IO.IOException){return true;}}
    internal sealed class FakeBackend : IDpiBackend
    {
        internal int Reads,Applies;
        internal ManualResetEventSlim? Block;
        internal ManualResetEventSlim Entered=new(false);
        internal volatile bool Finished;
        internal DpiSnapshot State(string id)=>new DpiSnapshot(DpiProfiles.Find(0x373B,0x11D8)!,0,
            new[]{new DpiStage(800,800),new DpiStage(1600,1600)},"1,26",10,42000,10).Bind(id,"fake-channel");
        public DpiSnapshot Read(string id,ushort vid,ushort pid,Action guard)
        {Interlocked.Increment(ref Reads);Entered.Set();try{Block?.Wait();guard();return State(id);}finally{Finished=true;}}
        public DpiSnapshot Apply(DpiSnapshot before,int stage,int? value,Action guard)
        {guard();Interlocked.Increment(ref Applies);return before.Target(stage,value).Bind(before.InstanceId,"fake-channel");}
    }
}
