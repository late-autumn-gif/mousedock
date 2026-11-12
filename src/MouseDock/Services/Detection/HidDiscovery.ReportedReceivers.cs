using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryReportedReceivers(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, VendorCollectionInfo, BatteryReplay.Trace, ReferenceBatteryProtocol.Reading> read)
    {
        foreach (var bucket in buckets.Values)
        {
            string route = VendorProtocol.ReportedReceiverRoute(bucket.Vid, bucket.Pid, bucket.Product);
            if (route == null || bucket.Battery >= 0) continue;
            bool mouse = route == VendorProtocol.A7UltraRoute;
            bucket.HandledBy = mouse ? HandledBy.MchoseA7Ultra : HandledBy.MonsGeekBatteryEvent;
            string policy = safeMode ? "安全模式已跳过专用电量读取" :
                string.IsNullOrWhiteSpace(bucket.InstanceId) ? "缺少物理实例" :
                !(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true) ? "此实例识别已停用" : null;

            var routes = mouse ? new[] { route } : new[] { route, VendorProtocol.MonsGeekStatusRoute };
            foreach (string reader in routes)
            {
                var scan = BatteryReplay.Current;
                var trace = scan == null ? null : new BatteryReplay.Trace();
                string reason = policy;
                VendorCollectionInfo channel = null;
                if (reason == null)
                {
                    var channels = collections.Where(c =>
                    {
                        if (InstanceScope.ForPath(c.Path, "") != bucket.InstanceId) return false;
                        HidInventory.ParsePath(c.Path, out _, out _, out string label);
                        return VendorProtocol.MatchesReportedReceiverChannel(reader, c.Vid, c.Pid, c.Product,
                            label, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature);
                    }).DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (channels.Length == 1) channel = channels[0];
                    else reason = $"本实例匹配的电量集合数={channels.Length}，需要唯一集合";
                }

                ReferenceBatteryProtocol.Reading reading;
                if (reason != null)
                {
                    trace?.Add("policy", false, result: reason);
                    reading = ReferenceBatteryProtocol.Reading.Failed(reason);
                }
                else
                {
                    using var scope = InstanceScope.Enter(bucket.InstanceId);
                    reading = read(reader, channel, trace) ?? ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
                }

                bool cached = reader == VendorProtocol.MonsGeekStatusRoute;
                bool valid = reading.Level is >= 0 and <= 100 &&
                    (cached ? reading.Status == ReferenceBatteryProtocol.State.Cached :
                        reading.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging) &&
                    (!mouse || reading.Model == VendorProtocol.A7UltraName);
                scan?.AddQuery(bucket.InstanceId, channel?.Path, reader,
                    valid ? reading.Status.ToString() : reason != null ? "Skipped" : "Failed",
                    valid ? reading.Model : null, valid ? reading.Level : -1, trace);
                LastDiagnostics.Add($"{bucket.InstanceId} {reader}：{reading.Diagnostic}");
                if (!valid)
                {
                    bucket.BatteryFailures.Add(reading.Diagnostic);
                    continue;
                }

                bucket.Battery = reading.Level;
                bucket.Charging = !cached && reading.ChargingKnown && reading.Charging;
                bucket.ReadStale = cached;
                bucket.BatterySource = reading.Diagnostic;
                if (cached) bucket.HandledBy = HandledBy.MonsGeekDongleStatus;
                break;
            }
        }
    }
}
