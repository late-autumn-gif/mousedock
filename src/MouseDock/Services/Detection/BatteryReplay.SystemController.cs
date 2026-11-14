using System;
using System.Linq;
using System.Text.Json;

namespace MouseDock.Services.Detection;

internal static partial class BatteryReplay
{
    private static Result ReplayXInputBattery(Query q)
    {
        Result Missing() => new(q.Receiver,null,q.Protocol,q.Status,null,null,null,"unavailable");
        if(q.Interface != null || q.Status!="Reported" || q.Samples is not {Length:1} || q.Samples[0] is not {Ok:true,Stage:"xinput-battery"} s || s.NativeError != 0 || s.Result?.Length is not (>0 and <=1024))return Missing();
        try
        {
            var e=JsonSerializer.Deserialize<XboxBatteryDiagnostics.Evidence>(s.Result);
            string band=e is {Slot:<4}?XboxBatteryDiagnostics.Band(e.Type,e.Level):null;
            return band==null?Missing():new(q.Receiver,null,q.Protocol,q.Status,null,null,null,"unbound-system-band",band);
        }
        catch(JsonException){return Missing();}
    }
    private static Result ReplaySystemController(Query query)
    {
        Result Missing() => new(query.Receiver,null,query.Protocol,query.Status,null,null,null,"unavailable");
        if (query.Interface != null || query.Status != "Reported" || query.Samples is not {Length:1}) return Missing();
        var sample = query.Samples[0];
        if (sample == null || !sample.Ok || sample.NativeError is not (null or 0) || sample.Stage != "system-battery-report" || sample.Result?.Length is not (>0 and <=4096)) return Missing();
        try
        {
            var e = JsonSerializer.Deserialize<ControllerExpansionSystem.Evidence>(sample.Result);
            if (e == null || e.Association is not ("exact-path" or "independent-system-id")) return Missing();
            var normalized = ControllerExpansionSystem.Normalize(new[]
            { new ControllerExpansionSystem.Sample("replay","",e.Vid,e.Pid,e.Wireless,e.Status,e.Remaining,e.Full,e.Design,e.ChargeRate) }).SingleOrDefault();
            if (normalized == null || normalized.Level < 0 && string.IsNullOrEmpty(normalized.BatteryBand)) return Missing();
            return new(query.Receiver,null,query.Protocol,query.Status,null,normalized.Level >= 0 ? normalized.Level : null,
                normalized.Charging,string.IsNullOrEmpty(normalized.BatteryBand) ? "system-report-captured" : "system-relative-captured", normalized.BatteryBand);
        }
        catch (JsonException) { return Missing(); }
    }
}
