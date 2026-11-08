using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryK87Collections(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var bucket in buckets.Values)
        {
            if (!MchoseKeyboardProtocol.IsSupportedDevice(bucket.Vid, bucket.Pid, bucket.Product)) continue;
            bucket.Battery = -1; bucket.Charging = false; bucket.ReadStale = false;
            bucket.BatterySource = "";
            var channels = collections.Where(c => InstanceScope.ForPath(c.Path, "") == bucket.InstanceId &&
                MchoseKeyboardProtocol.IsSupportedDevice(c.Vid, c.Pid, c.Product) &&
                MchoseKeyboardProtocol.IsSupportedInterface(c.UsagePage, c.Usage, c.Input, c.Output))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            string reason = safeMode ? "安全模式已跳过 K87 查询" :
                !(preferences?.CanRead(bucket.InstanceId, $"hid-{bucket.Vid:X4}-{bucket.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(bucket.Product)) ?? true) ? "此实例识别已停用" :
                string.IsNullOrWhiteSpace(bucket.InstanceId) ? "缺少物理实例，未查询" :
                channels.Count != 1 ? $"本实例 QHW 集合数={channels.Count}，需要唯一集合" : null;
            ReferenceBatteryProtocol.Reading reading;
            if (reason != null) reading = ReferenceBatteryProtocol.Reading.Failed(reason);
            else
            {
                using var scope = InstanceScope.Enter(bucket.InstanceId);
                try { reading = query(channels[0].Path); }
                catch (Exception ex) { reading = ReferenceBatteryProtocol.Reading.Failed(ex.Message); }
            }
            reading ??= ReferenceBatteryProtocol.Reading.Failed("K87 读取器未返回结果");
            if (reading.Level is >= 0 and <= 100 &&
                reading.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
            {
                bucket.Battery = reading.Level;
                bucket.Charging = reading.ChargingKnown && reading.Charging;
                bucket.BatterySource = reading.Diagnostic;
            }
            else bucket.BatteryFailures.Add(reading.Diagnostic);
            LastDiagnostics.Add($"MCHOSE K87 V3 QHW: battery={bucket.Battery}, charging={bucket.Charging}, instance={bucket.InstanceId}, {reading.Diagnostic}");
        }
    }
}
