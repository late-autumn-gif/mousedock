using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryM137FeatureCollections(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var bucket in buckets.Values)
        {
            if (!M137FeatureProtocol.IsCandidate(bucket.Vid, bucket.Pid, bucket.Product)) continue;
            bucket.M137FeatureHandled = true; bucket.M137FeatureModel = null;
            bucket.Battery = -1; bucket.Charging = false; bucket.ReadStale = false;
            bucket.Identity = ""; bucket.BatterySource = "";
            var channels = collections.Where(c => M137FeatureProtocol.IsCandidate(c.Vid, c.Pid, c.Product) &&
                InstanceScope.ForPath(c.Path, "") == bucket.InstanceId &&
                ReceiverIdentityProtocol.MatchesChannel(c.Vid, c.Pid, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            string reason = safeMode ? "安全模式已跳过 M137 Feature 电量查询" :
                !(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true) ? "此实例识别已停用" :
                string.IsNullOrWhiteSpace(bucket.InstanceId) ? "缺少物理实例，未查询" :
                channels.Count != 1 ? $"本实例 Feature33 集合数={channels.Count}，需要唯一集合，未查询" : null;
            if (reason != null)
            {
                bucket.BatteryFailures.Add(reason);
                LastDiagnostics.Add($"{bucket.InstanceId} M137 Feature：{reason}");
                continue;
            }
            ReferenceBatteryProtocol.Reading reading;
            using (InstanceScope.Enter(bucket.InstanceId))
            {
                try { reading = query(channels[0].Path); }
                catch (Exception ex) { reading = ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
            }
            reading ??= ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
            if (reading.Identity == "M137" && reading.Model == M137FeatureProtocol.Model &&
                reading.Level is >= 0 and <= 100 && reading.Status == ReferenceBatteryProtocol.State.Online)
            {
                bucket.M137FeatureModel = reading.Model;
                bucket.Battery = reading.Level;
                bucket.BatterySource = reading.Diagnostic;
            }
            else bucket.BatteryFailures.Add(reading.Diagnostic);
            LastDiagnostics.Add($"{bucket.InstanceId} M137 Feature：型号={bucket.M137FeatureModel} 电量={bucket.Battery} {reading.Diagnostic}");
        }
    }
}
