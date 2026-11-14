using System.Runtime.InteropServices;
using System.Text.Json;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class XboxWirelessRegression
{
    internal static int Run()
    {
        int count=0; void Check(bool ok,string name){if(!ok)throw new Exception("Xbox wireless: "+name);count++;}
        ControllerExpansionSystem.Sample Sample(string id="raw",int? remaining=70,int? full=100,int? design=null,string status="Discharging") =>
            new(id,"Controller (Xbox One For Windows)",0x045E,0x0B00,true,status,remaining,full,design,null);
        const string path=@"\\?\hid#vid_045e&pid_0b00&ig_00#SYNTHETIC_FIRST";
        const string second=@"\\?\hid#vid_045e&pid_0b00&ig_00#SYNTHETIC_SECOND";
        var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[path]="instance-first",[second]="instance-second"};
        var bindings=new ControllerExpansionSystem.Bindings(paths,new HashSet<(ushort,ushort)>{(0x045E,0x0B00)},true);
        foreach(int value in new[]{0,10,40,70,100})
        {
            var d=ControllerExpansionSystem.Normalize(new[]{Sample(path,value)},bindings).Single();
            Check(d.Level==-1&&d.BatteryBand==$"{value}/100"&&d.Transport=="无线"&&!d.ChargeFull,"coarse report is preserved without fabricated percent or full flag");
        }
        foreach(var (remaining,full) in new (int?,int?)[]{(null,100),(70,null),(-1,100),(101,100),(1,0),(1,-1)})
        {
            var d=ControllerExpansionSystem.Normalize(new[]{Sample(path,remaining,full)},bindings).Single();
            Check(d.Level<0&&d.BatteryBand==""&&!d.Charging,"invalid system capacities cannot supply a reading");
        }
        foreach(string state in new[]{"Unknown","NotPresent","Unavailable"})
            Check(ControllerExpansionSystem.Normalize(new[]{Sample(path,status:state)},bindings).Single().BatteryBand=="","unknown status suppresses capacity bytes");
        var precise=ControllerExpansionSystem.Normalize(new[]{Sample(path,430,1000,1000)},bindings).Single();
        Check(precise.Level==43&&precise.BatteryBand=="","detailed report keeps its actual percentage path");
        var coarse=ControllerExpansionSystem.Normalize(new[]{Sample(path)},bindings).Single();
        Check(AutoDetectSource.MergeGroup(new(){new(){Id=coarse.Id,Kind=DeviceKind.Controller,Level=-1},coarse}).BatteryBand=="70/100","same-instance unknown channel cannot hide relative evidence");
        var exact=new List<DetectedDevice>{new(){Id=coarse.Id,Kind=DeviceKind.Controller,Level=43,Transport="有线"}};
        ExtraDeviceRoutes.MergeSystemControllers(exact,new[]{coarse});
        Check(exact[0].Level==43&&exact[0].BatteryBand==""&&exact[0].Transport=="无线","exact battery retained while bound wireless metadata updates");
        var noBattery=new List<DetectedDevice>{new(){Id=coarse.Id,Kind=DeviceKind.Controller,Level=-1,Transport="有线"}};
        ExtraDeviceRoutes.MergeSystemControllers(noBattery,ControllerExpansionSystem.Normalize(new[]{Sample(path,status:"Unknown")},bindings));
        Check(noBattery[0].Transport=="无线"&&noBattery[0].Level==-1&&noBattery[0].BatteryBand=="","bound wireless metadata does not require a battery report");
        var native=new DetectedDevice{Id="instance-first",Kind=DeviceKind.Controller,Name="Native Xbox",Transport="有线",Level=-1};
        var list=new List<DetectedDevice>{native};ExtraDeviceRoutes.MergeSystemControllers(list,new[]{coarse});
        Check(list.Count==1&&native.BatteryBand=="70/100"&&native.Level==-1&&native.Transport=="无线","exact-bound coarse data and wireless transport survive merge");
        var model=new AutoDetectSource().UpdateFromDetection(list).Single();
        Check(model.LevelText=="档位"&&model.MiniText=="档位"&&model.StatusText.Contains("70/100")&&!model.LevelText.Contains('%'),"normal model shows a relative reading without percent");
        Check(model.Kind==DeviceKind.Controller&&model.HandledByLabel=="SystemController","system origin retained");
        var changes=new List<string>();model.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName!);
        model.BatteryBand="40/100";
        Check(changes.Contains(nameof(DeviceInfo.LevelText))&&changes.Contains(nameof(DeviceInfo.StatusText)),"band changes update visible bindings");
        model.BatteryBand="";Check(model.LevelText=="—"&&model.StatusText.Contains("暂不可用"),"failed relative reading becomes unknown");
        model.Level=63;model.BatteryBand="70/100";Check(model.LevelText=="63%","precise percentage takes precedence");

        string independent=ControllerExpansionSystem.StableId("opaque-one");
        Check(bindings.Resolve("opaque-one",0x045E,0x0B00)==independent,"opaque controller keeps independent system identity");
        Check(bindings.Resolve("opaque-two",0x045E,0x0B00)!=independent,"same-model controllers never merge by PID");
        var separate=ControllerExpansionSystem.Normalize(new[]{Sample("opaque-one")},bindings).Single();
        var mixed=new List<DetectedDevice>{new(){Id="instance-first",Kind=DeviceKind.Controller,Level=-1}};
        ExtraDeviceRoutes.MergeSystemControllers(mixed,new[]{separate});
        Check(mixed.Count==2&&mixed[0].BatteryBand==""&&separate.Name.Contains("系统"),"unproven HID association cannot borrow the system battery");
        Check(!ControllerExpansionSystem.InScope("opaque-one",0x045E,0x0B00,bindings,new HashSet<string>{"instance-first"}),"single native scope excludes unbound system object before battery query");
        Check(ControllerExpansionSystem.InScope("opaque-one",0x045E,0x0B00,bindings,new HashSet<string>{independent}),"system selection scopes its own identity");
        Check(!ControllerExpansionSystem.CanQuery(path,0x045E,0x0B00,bindings,null,true),"safe mode blocks system battery query");
        var preferences=new DevicePreferences(new AppSettings{DisabledRecognitionDeviceIds=new(){"instance-second"}});
        Check(!ControllerExpansionSystem.CanQuery("opaque-one",0x045E,0x0B00,bindings,preferences,false),"independent fallback cannot bypass ambiguous native exclusion");
        Check(ControllerExpansionSystem.CanQuery(path,0x045E,0x0B00,bindings,preferences,false),"exact other instance remains independently readable");
        preferences=new(new(){DisabledRecognitionDeviceIds=new(){independent}});
        Check(!ControllerExpansionSystem.CanQuery("opaque-one",0x045E,0x0B00,bindings,preferences,false),"own system exclusion enforced");
        Check(bindings.Resolve(null!,0x045E,0x0B00)==null,"missing identity rejected");

        BatteryReplay.Query Query(ControllerExpansionSystem.Evidence e)=>new("receiver-one",null,"WindowsGameController","Reported",null,null,
            new[]{new BatteryReplay.Sample("system-battery-report",true,Result:JsonSerializer.Serialize(e))});
        var evidence=new ControllerExpansionSystem.Evidence(0x045E,0x0B00,true,"Discharging",70,100,null,null,"exact-path");
        var query=Query(evidence);var replay=BatteryReplay.Replay(query,Array.Empty<BatteryReplay.Collection>());
        Check(replay.Percent==null&&replay.BatteryBand=="70/100"&&replay.ValueKind=="system-relative-captured","non-HID battery report replay preserves precision");
        Check(BatteryReplay.Replay(Query(evidence with{Design=100}),[]).Percent==70,"detailed non-HID replay");
        Check(BatteryReplay.Replay(query with{Status="SafeMode"},[]).ValueKind=="unavailable","suppressed capture is not successful");
        Check(BatteryReplay.Replay(query with{Interface=1},[]).ValueKind=="unavailable","system data cannot claim a HID interface");
        Check(BatteryReplay.Replay(Query(evidence with{Remaining=101}),[]).ValueKind=="unavailable","out-of-range replay refused");
        Check(BatteryReplay.Replay(Query(evidence with{Status="Unknown"}),[]).ValueKind=="unavailable","unknown replay status cannot publish bytes");
        Check(!query.Samples[0].Result!.Contains("opaque")&&!query.Samples[0].Result!.Contains("SYNTHETIC"),"diagnostics omit raw controller and HID identities");
        Check(Marshal.SizeOf<XboxBatteryDiagnostics.Battery>()==2,"native XInput battery layout");
        foreach(byte type in new byte[]{2,3})for(byte level=0;level<4;level++)for(uint slot=0;slot<4;slot++)
        {
            var q=new BatteryReplay.Query("slot",null,"XInputBatteryDiagnostics","Reported",null,null,
                new[]{new BatteryReplay.Sample("xinput-battery",true,Result:JsonSerializer.Serialize(new XboxBatteryDiagnostics.Evidence(slot,type,level)),NativeError:0)});
            var r=BatteryReplay.Replay(q,[]);
            Check(r.Percent==null&&r.Charging==null&&r.BatteryBand==XboxBatteryDiagnostics.Band(type,level)&&r.ValueKind=="unbound-system-band","four XInput bands remain unassociated and not percentages");
        }
        foreach(byte type in new byte[]{0,1,255})Check(XboxBatteryDiagnostics.Band(type,3)==null,"disconnected/wired/unknown battery type has no level");
        Check(XboxBatteryDiagnostics.Band(2,4)==null,"invalid native band rejected");
        var previous=BatteryReplay.Current;
        try
        {
            var scan=new BatteryReplay.Scan(safeMode:true);BatteryReplay.Current=scan;XboxBatteryDiagnostics.Capture(true);
            Check(scan.Finish(null).Queries.Length==0,"safe mode diagnostics never call XInput");
            scan=new BatteryReplay.Scan(selectedIds:new[]{"instance-first"});BatteryReplay.Current=scan;XboxBatteryDiagnostics.Capture(false);
            Check(scan.Finish(null).Queries.Length==0,"single-device report never queries unbound XInput slots");
        }
        finally{BatteryReplay.Current=previous;}
        return count;
    }
}
