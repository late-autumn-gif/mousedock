using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;
using MouseDock.Services;
namespace MouseDock.Services.Detection;
public static partial class HidDiscovery
{
    internal static void QueryZeroRainy(Dictionary<string,Bucket> buckets,List<VendorCollectionInfo> collections,bool safe,DevicePreferences preferences,
        Func<string,ushort,ushort,ReferenceBatteryProtocol.Reading> query)
    {
        foreach(var b in buckets.Values)
        {
            if(!ZeroRainyProtocol.Candidate(b.Vid,b.Pid,b.Product))continue;
            b.HandledBy=HandledBy.ZeroRainy;b.ZeroRainyModel=null;b.Battery=-1;b.Charging=false;b.ReadStale=false;b.Identity="";b.BatterySource="";b.RainyIdentified=false;
            var channels=collections.Where(c=>c.Vid==b.Vid && c.Pid==b.Pid && InstanceScope.ForPath(c.Path,"")==b.InstanceId &&
                ZeroRainyProtocol.Channel(c.Vid,c.Pid,c.Product,c.UsagePage,c.Usage,c.Input,c.Output,c.Feature)).GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            if(channels.Count==1)b.ZeroRainyModel=ZeroRainyProtocol.ReceiverModel(b.Product);
            string why=safe?"安全模式未查询ZERO/Rainy":!(preferences?.CanRead(b.InstanceId,null)??true)||!(preferences?.CanRead($"hid-{b.Vid:X4}-{b.Pid:X4}",null)??true)?"此实例识别已停用":channels.Count!=1?"本实例ZERO/Rainy精确集合不唯一/缺失":null;
            if(why!=null){b.BatteryFailures.Add(why);LastDiagnostics.Add($"{b.InstanceId} ZERO/Rainy边界：{why}");continue;}
            ReferenceBatteryProtocol.Reading r;
            using(InstanceScope.Enter(b.InstanceId)){try{r=query(channels[0].Path,b.Vid,b.Pid);}catch(Exception ex){r=ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name);}}
            r??=ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
            if(r.Model!=null)b.ZeroRainyModel=r.Model;
            if(r.Level is >=0 and <=100 && r.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging){b.Battery=r.Level;b.Charging=r.ChargingKnown&&r.Charging;b.BatterySource=r.Diagnostic;}
            else b.BatteryFailures.Add(r.Diagnostic);
            LastDiagnostics.Add($"{b.InstanceId} ZERO/Rainy专用 型号={b.ZeroRainyModel} 电量={r.Level} {r.Diagnostic}");
        }
    }
}

