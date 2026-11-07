using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;
namespace MouseDock.Services.Detection;
public static partial class HidDiscovery
{
    private static void QueryScopedInphic(Dictionary<string,Bucket> buckets,List<VendorCollectionInfo> collections,bool safe,DevicePreferences preferences)
    {
        foreach(var b in buckets.Values)
        {
            if(!VendorProtocol.IsScopedInphic(b.Vid,b.Pid,b.Product))continue;
            b.InphicHandled=true;b.AulaHandled=true;b.AulaOnline=false;b.AulaModel=null;b.Identity="";b.Battery=-1;b.Charging=false;b.ReadStale=false;
            var channels=collections.Where(c=>c.Vid==b.Vid && c.Pid==b.Pid && InstanceScope.ForPath(c.Path,"")==b.InstanceId &&
                VendorProtocol.IsScopedInphicChannel(c.Vid,c.Pid,c.Product,c.Path,c.UsagePage,c.Usage,c.Input,c.Output)).GroupBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            string why=safe?"安全模式未查询英菲克":!(preferences?.CanRead(b.InstanceId,null)??true)||!(preferences?.CanRead($"hid-{b.Vid:X4}-{b.Pid:X4}",null)??true)?"识别已停用":channels.Count!=1?"本实例英菲克精确集合不唯一/缺失":null;
            if(why!=null){b.BatteryFailures.Add(why);continue;}
            using var scope=InstanceScope.Enter(b.InstanceId);
            string path = channels[0].Path;
            var wakePaths = collections.Where(c => InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                VendorProtocol.IsInphicWakeCollection(c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage, c.Feature) &&
                VendorProtocol.IsInphicWakeSibling(path, c.Path))
                .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
            var r = VendorProtocol.ReadScopedInphic(path, b.Vid, b.Pid, wakePaths.Count == 1 ? wakePaths[0] : null);
            if(r.Level>=0 && r.Model!=null){b.AulaModel=r.Model;b.AulaOnline=true;b.Battery=r.Level;b.Charging=r.Charging;b.BatterySource=r.Diagnostic;}
            else b.BatteryFailures.Add(r.Diagnostic);
            LastDiagnostics.Add($"{b.InstanceId} 英菲克专用 状态={r.Status} 型号={r.Model} 电量={r.Level} {r.Diagnostic}");
        }
    }
}
