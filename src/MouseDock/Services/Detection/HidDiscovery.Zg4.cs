using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryZg4Collections(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<VendorCollectionInfo, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var bucket in buckets.Values)
        {
            if (bucket.Vid != 0x3654 || bucket.Pid != 0x5055 || !VendorMap.IsZhuAudioZg4(bucket.Product)) continue;
            bucket.Battery = -1;
            bucket.Charging = false;
            bucket.ReadStale = false;
            bucket.Identity = "";
            bucket.BatterySource = "";
            var channels = collections.Where(c =>
                VendorProtocol.IsZg4Channel(c.Vid, c.Pid, c.Product, c.UsagePage, c.Input, c.Output) &&
                string.Equals(InstanceScope.ForPath(c.Path, ""), bucket.InstanceId, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
            string boundary = safeMode ? "安全模式未查询 ZG4" :
                !(preferences?.CanRead(bucket.InstanceId, "hid-3654-5055") ?? true) ? "ZG4 此实例识别已停用" :
                channels.Count == 0 ? "ZG4 本实例未发现可读写的私有接口" : null;
            if (boundary != null)
            {
                bucket.BatteryFailures.Add(boundary);
                LastDiagnostics.Add($"{bucket.InstanceId} {boundary}");
                BatteryReplay.Current?.AddQuery(bucket.InstanceId, null, "Zg4", "Skipped", null);
                continue;
            }

            foreach (var channel in channels)
            {
                var scan = BatteryReplay.Current;
                var trace = scan == null ? null : new BatteryReplay.Trace();
                var previous = scan?.Active;
                ReferenceBatteryProtocol.Reading reading;
                try
                {
                    if (scan != null) scan.Active = trace;
                    using var scope = InstanceScope.Enter(bucket.InstanceId);
                    reading = query(channel) ?? ReferenceBatteryProtocol.Reading.Failed("ZG4 读取器未返回结果");
                }
                catch (Exception ex) { reading = ReferenceBatteryProtocol.Reading.Failed("ZG4 接口查询失败：" + ex.Message); }
                finally { if (scan != null) scan.Active = previous; }
                bool valid = reading.Level is >= 0 and <= 100 &&
                    reading.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging &&
                    VendorMap.IsZhuAudioZg4(reading.Model);
                scan?.AddQuery(bucket.InstanceId, channel.Path, "Zg4", valid ? reading.Status.ToString() : "Failed",
                    valid ? reading.Model : null, valid ? reading.Level : -1, trace);
                LastDiagnostics.Add($"{bucket.InstanceId} ZG4 {channel.UsagePage:X4}/{channel.Usage:X4} " +
                    $"{channel.Input}/{channel.Output}：{reading.Diagnostic}");
                if (!valid)
                {
                    bucket.BatteryFailures.Add(reading.Diagnostic);
                    continue;
                }
                bucket.Battery = reading.Level;
                bucket.Charging = reading.ChargingKnown && reading.Charging;
                bucket.Identity = bucket.Product.Trim();
                bucket.BatterySource = "竹林鸟 ZG4 接收器电量；" + reading.Diagnostic;
                break;
            }
        }
    }
}
