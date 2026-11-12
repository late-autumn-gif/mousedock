using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryRogScope(Dictionary<string, Bucket> buckets, List<VendorCollectionInfo> collections,
        bool safeMode, DevicePreferences preferences,
        Func<string, string, string, BatteryReplay.Trace, ReferenceBatteryProtocol.Reading> query = null,
        bool readDpi = false, MouseDpiChange dpiChange = null)
    {
        foreach (var b in buckets.Values)
        {
            if (!VendorProtocol.IsRogOmni(b.Vid, b.Pid)) continue;
            b.Battery = -1;
            b.Charging = false;
            b.ChargeFull = false;
            b.ReadStale = false;
            b.BatterySource = "";
            b.RogScopeModel = null;
            if (b.HandledBy is HandledBy.RogScope or HandledBy.RogOmniMouse) b.HandledBy = HandledBy.None;
            var scoped = collections.Where(c => VendorProtocol.IsRogOmni(c.Vid, c.Pid) &&
                InstanceScope.ForPath(c.Path, "") == b.InstanceId).ToArray();
            string[] Paths(int reportId) => scoped.Where(c =>
                    VendorProtocol.IsRogOmniChannel(c.Path, c.Product, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature, reportId))
                .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();

            var pairing = Paths(1);
            var battery = Paths(2);
            var mouse = Paths(3);
            string reason = safeMode ? "安全模式" :
                !(preferences?.CanRead(b.InstanceId, "hid-0B05-1ACE") ?? true) ? "识别已停用" :
                pairing.Length != 1 ? $"配对集合不唯一或缺失：配对={pairing.Length}" : null;
            var scan = BatteryReplay.Current;
            if (reason != null)
            {
                scan?.AddQuery(b.InstanceId, null, "RogScopeRxOmni", safeMode ? "safe-mode" :
                    pairing.Length != 1 ? "collection-missing-or-ambiguous" : "recognition-disabled", null);
                LastDiagnostics.Add($"{b.InstanceId} ROG Omni：{reason}");
                continue;
            }

            using var scope = InstanceScope.Enter(b.InstanceId);
            var trace = scan == null ? null : new BatteryReplay.Trace();
            string batteryPath = battery.Length == 1 ? battery[0] : null;
            string mousePath = mouse.Length == 1 ? mouse[0] : null;
            var result = query != null ? query(pairing[0], batteryPath, mousePath, trace) :
                VendorProtocol.ReadRogScope(pairing[0], batteryPath, trace, mousePath, readDpi && scan == null, dpiChange);
            bool isMouse = result.Model == RogOmniMouseProtocol.Model;
            scan?.AddQuery(b.InstanceId, isMouse ? mousePath : batteryPath,
                isMouse ? RogOmniMouseProtocol.Protocol : "RogScopeRxOmni",
                result.Status.ToString(), result.Model, result.Level, trace);
            LastDiagnostics.Add($"{b.InstanceId} ROG Omni：{result.Diagnostic} 电量={result.Level}");
            if (result.Model == null) continue;
            b.HandledBy = isMouse ? HandledBy.RogOmniMouse : HandledBy.RogScope;
            if (!isMouse) b.RogScopeModel = result.Model;
            b.Battery = result.Level;
            b.Charging = result.Charging;
            b.ReadStale = false;
            b.BatterySource = result.Diagnostic;
            if (isMouse && result.Dpi is { } dpi)
            {
                b.DpiSettings = dpi;
                b.Dpi = dpi.Values[dpi.Index];
            }
            if (result.Level < 0) b.BatteryFailures.Add(result.Diagnostic);
        }
    }
}

