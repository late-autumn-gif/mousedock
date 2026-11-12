using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;
namespace MouseDock.Services.Detection;
public static partial class HidDiscovery
{
    internal static void QueryScopedModernMouse(Dictionary<string,Bucket> buckets,List<VendorCollectionInfo> collections,bool safe,DevicePreferences preferences,
        Func<string,ushort,ushort,ReferenceBatteryProtocol.Reading> query)
    {
        foreach(var b in buckets.Values)
        {
            if(!VendorProtocol.IsScopedModernMouse(b.Vid,b.Pid,b.Product))continue;
            b.HandledBy=HandledBy.MchoseModern;b.ModernModel=null;b.Battery=-1;b.Charging=false;b.ReadStale=false;b.Identity="";b.BatterySource="";
            var channels=collections.Where(c=>c.Vid==b.Vid && c.Pid==b.Pid && string.Equals(c.Product?.Trim(),b.Product?.Trim(),StringComparison.OrdinalIgnoreCase) && InstanceScope.ForPath(c.Path,"")==b.InstanceId &&
                VendorProtocol.IsScopedModernChannel(c.Vid,c.Pid,c.Product,c.UsagePage,c.Usage,c.Input,c.Output,c.Feature)).GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            
            string why=safe?"安全模式未查询MHUB":!(preferences?.CanRead(b.InstanceId,null)??true)||!(preferences?.CanRead($"hid-{b.Vid:X4}-{b.Pid:X4}",VendorMap.PhysicalKeyFromName(b.Product))??true)?"此实例识别已停用":channels.Count!=1?"本实例MHUB精确集合不唯一/缺失":null;
            if(why!=null){b.BatteryFailures.Add(why);LastDiagnostics.Add($"{b.InstanceId} MHUB边界：{why}");continue;}
            ReferenceBatteryProtocol.Reading r;
            using(InstanceScope.Enter(b.InstanceId)){try{r=query(channels[0].Path,b.Vid,b.Pid);}catch(Exception ex){r=ReferenceBatteryProtocol.Reading.Failed(ex.GetType().Name);}}
            r??=ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
            if(r.Level is >=0 and <=100 && (r.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging) && string.Equals(r.Model,b.Product?.Trim(),StringComparison.OrdinalIgnoreCase)){b.Battery=r.Level;b.Charging=r.ChargingKnown&&r.Charging;b.BatterySource=r.Diagnostic;b.ModernModel=r.Model;}
            else b.BatteryFailures.Add(r.Diagnostic);
            if (r.Dpi is { } dpi)
            {
                b.ModernDpi = dpi;
                b.Dpi = dpi.Values[dpi.Index];
            }
            LastDiagnostics.Add($"{b.InstanceId} MHUB专用 型号={b.ModernModel} 电量={r.Level} {r.Diagnostic}");
        }
    }
}

