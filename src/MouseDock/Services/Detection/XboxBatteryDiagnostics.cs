using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace MouseDock.Services.Detection;

// XInput slots do not establish HID physical identity. Diagnostics only; never merge by player index.
internal static class XboxBatteryDiagnostics
{
    [StructLayout(LayoutKind.Sequential,Pack=1)]
    internal struct Battery { public byte Type, Level; }
    [DllImport("xinput1_4.dll",CallingConvention=CallingConvention.Winapi)]
    private static extern uint XInputGetBatteryInformation(uint userIndex,byte deviceType,out Battery information);
    internal sealed record Evidence(uint Slot,byte Type,byte Level);
    private sealed record Recorded(uint Slot,string Status,Evidence Data,int? Error);
    private static readonly object Gate=new();
    private static Task<Recorded[]> pending;
    internal static string Band(byte type,byte level) => type is 2 or 3 ? level switch
    {0=>"耗尽",1=>"低",2=>"中",3=>"高",_=>null} : null;
    internal static void Capture(bool safeMode)
    {
        if (safeMode || BatteryReplay.Current is not {SelectedIds:null} scan) return;
        Task<Recorded[]> worker;
        lock(Gate)
        {
            if(pending is {IsCompleted:false}) { scan.AddQuery("xinput-diagnostic-scan",null,"XInputBatteryDiagnosticsScan","Busy",null);return; }
            pending=worker=Task.Run(ReadSlots);
        }
        try
        {
            if(!worker.Wait(1000)) {scan.AddQuery("xinput-diagnostic-scan",null,"XInputBatteryDiagnosticsScan","Timeout",null);return;}
            foreach(var r in worker.Result)
            {
                var trace=new BatteryReplay.Trace();
                trace.Add("xinput-battery",r.Status=="Reported",result:r.Data==null?null:JsonSerializer.Serialize(r.Data),nativeError:r.Error);
                scan.AddQuery("xinput-diagnostic-slot-"+r.Slot,null,"XInputBatteryDiagnostics",r.Status,null,trace:trace);
            }
        }
        catch(Exception ex){scan.AddQuery("xinput-diagnostic-scan",null,"XInputBatteryDiagnosticsScan",ex.GetType().Name,null);}
    }
    private static Recorded[] ReadSlots()
    {
        var records=new List<Recorded>();
        for(uint slot=0;slot<4;slot++)
        {
            try
            {
                uint error=XInputGetBatteryInformation(slot,0,out var battery);
                records.Add(new(slot,error==0?"Reported":"Unavailable",new Evidence(slot,battery.Type,battery.Level),unchecked((int)error)));
            }
            catch(Exception ex)when(ex is DllNotFoundException or EntryPointNotFoundException)
            {records.Add(new(slot,ex.GetType().Name,null,ex.HResult));}
        }
        return records.ToArray();
    }
}
