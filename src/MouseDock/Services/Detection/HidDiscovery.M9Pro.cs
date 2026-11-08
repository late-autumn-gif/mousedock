using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryM9ProCollections(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var bucket in buckets.Values)
        {
            if (!M9ProProtocol.IsCandidate(bucket.Vid, bucket.Pid, bucket.Product)) continue;
            bucket.M9ProHandled = true; bucket.M9ProModel = null;
            bucket.Battery = -1; bucket.Charging = false; bucket.ReadStale = false;
            bucket.Identity = ""; bucket.BatterySource = "";
            var channels = collections.Where(c => InstanceScope.ForPath(c.Path, "") == bucket.InstanceId &&
                M9ProProtocol.MatchesChannel(c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            string reason = safeMode ? "安全模式已跳过 M9 Pro 电量读取" :
                !(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true) ? "此实例识别已停用" :
                string.IsNullOrWhiteSpace(bucket.InstanceId) ? "缺少物理实例，未读取" :
                channels.Count != 1 ? $"本实例 M9 Pro 精确集合数={channels.Count}，需要唯一集合，未读取" : null;
            if (reason != null)
            {
                bucket.BatteryFailures.Add(reason);
                LastDiagnostics.Add($"{bucket.InstanceId} M9 Pro：{reason}");
                continue;
            }
            ReferenceBatteryProtocol.Reading reading;
            using (InstanceScope.Enter(bucket.InstanceId))
            {
                try { reading = query(channels[0].Path); }
                catch (Exception ex) { reading = ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
            }
            reading ??= ReferenceBatteryProtocol.Reading.Failed("读取器未返回结果");
            if (reading.Level is >= 0 and <= 100 && reading.Model == M9ProProtocol.Model &&
                reading.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
            {
                bucket.M9ProModel = reading.Model;
                bucket.Battery = reading.Level;
                bucket.Charging = reading.ChargingKnown && reading.Charging;
                bucket.BatterySource = reading.Diagnostic;
            }
            else bucket.BatteryFailures.Add(reading.Diagnostic);
            LastDiagnostics.Add($"{bucket.InstanceId} M9 Pro：电量={bucket.Battery} {reading.Diagnostic}");
        }
    }
}
