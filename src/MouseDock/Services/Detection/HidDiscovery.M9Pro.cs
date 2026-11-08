using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// 凌豹 M9 Pro（093A:522C，产品串 LB-M9PRO）的被动电量读取。
    /// 只放行唯一一个 FF05/0001 33/33 Feature9 集合，避免别的 RealTek 系设备误入。
    /// </summary>
    internal static void QueryM9Pro(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!M9ProProtocol.IsCandidate(b.Vid, b.Pid, b.Product)) continue;
            b.HandledBy = HandledBy.M9Pro;
            b.M9ProModel = null;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";

            if (safeMode)
            {
                b.BatteryFailures.Add("安全模式已跳过 M9 Pro 电量读取");
                continue;
            }
            if (!(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}") ?? true))
            {
                b.BatteryFailures.Add("此实例识别已停用");
                continue;
            }
            var channels = collections
                .Where(c => InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                            M9ProProtocol.MatchesChannel(c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage,
                                c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                var reason = $"本实例 M9 Pro 精确集合数={channels.Count}，需要唯一集合，未读取";
                b.BatteryFailures.Add(reason);
                LastDiagnostics.Add($"{b.InstanceId} M9 Pro：{reason}");
                continue;
            }

            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(channels[0].Path); }
                catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed("M9 Pro 查询异常：" + ex.GetType().Name); }
            }
            result ??= ReferenceBatteryProtocol.Reading.Failed("M9 Pro 读取器未返回结果");
            if (result.Level is >= 0 and <= 100 && result.Model == M9ProProtocol.Model &&
                result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
            {
                b.M9ProModel = result.Model;
                b.Battery = result.Level;
                b.Charging = result.ChargingKnown && result.Charging;
                b.BatterySource = result.Diagnostic;
            }
            else
            {
                b.BatteryFailures.Add(result.Diagnostic);
            }
            LastDiagnostics.Add($"{b.InstanceId} M9 Pro：型号={b.M9ProModel ?? "未知"} 电量={b.Battery} {result.Diagnostic}");
        }
    }
}
