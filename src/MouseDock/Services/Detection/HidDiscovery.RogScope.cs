using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    private static void QueryRogScope(Dictionary<string, Bucket> buckets, List<VendorCollectionInfo> collections,
        bool safeMode, DevicePreferences preferences)
    {
        foreach (var b in buckets.Values)
        {
            if (!VendorProtocol.IsRogOmni(b.Vid, b.Pid)) continue;
            var scoped = collections.Where(c => VendorProtocol.IsRogOmni(c.Vid, c.Pid) &&
                InstanceScope.ForPath(c.Path, "") == b.InstanceId).ToArray();
            string[] Paths(int reportId) => scoped.Where(c =>
                    VendorProtocol.IsRogOmniChannel(c.Path, c.Product, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature, reportId))
                .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();

            var pairing = Paths(1);
            var battery = Paths(2);
            string reason = safeMode ? "安全模式" :
                !(preferences?.CanRead(b.InstanceId, "hid-0B05-1ACE") ?? true) ? "识别已停用" :
                pairing.Length != 1 || battery.Length != 1 ? $"集合不唯一或缺失：配对={pairing.Length} 电量={battery.Length}" : null;
            var scan = BatteryReplay.Current;
            if (reason != null)
            {
                scan?.AddQuery(b.InstanceId, null, "RogScopeRxOmni", safeMode ? "safe-mode" :
                    pairing.Length != 1 || battery.Length != 1 ? "collection-missing-or-ambiguous" : "recognition-disabled", null);
                LastDiagnostics.Add($"{b.InstanceId} ROG Omni：{reason}");
                continue;
            }

            using var scope = InstanceScope.Enter(b.InstanceId);
            var trace = scan == null ? null : new BatteryReplay.Trace();
            var result = VendorProtocol.ReadRogScope(pairing[0], battery[0], trace);
            scan?.AddQuery(b.InstanceId, battery[0], "RogScopeRxOmni", result.Status.ToString(), result.Model, result.Level, trace);
            LastDiagnostics.Add($"{b.InstanceId} ROG Omni：{result.Diagnostic} 电量={result.Level}");
            if (result.Model == null) continue;
            b.RogScopePaired = true;
            b.Battery = result.Level;
            b.Charging = result.Charging;
            b.ReadStale = false;
            b.BatterySource = result.Diagnostic;
            if (result.Level < 0) b.BatteryFailures.Add(result.Diagnostic);
        }
    }
}
