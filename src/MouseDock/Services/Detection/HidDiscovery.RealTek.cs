using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// 凌豹 M9 Pro（093A:522C）的只读电量。只对用户报告里出现过的这一款发帧，
    /// 其余同形状设备没有证据，不主动查询。
    /// </summary>
    internal static void QueryRealTekBattery(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences)
    {
        foreach (var b in buckets.Values)
        {
            if (!VendorProtocol.IsRealTekShape(b.Vid, b.Pid)) continue;
            b.HandledBy = HandledBy.RealTek;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";

            if (safeMode)
            {
                b.BatteryFailures.Add("安全模式已跳过 RealTek 查询");
                continue;
            }
            if (!(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}") ?? true))
            {
                b.BatteryFailures.Add("此实例识别已停用");
                continue;
            }

            // 官方命令走 FF00/0001 的 Feature 报告（用户报告里是 0/0/64）。
            var channels = collections
                .Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                            InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                            c.UsagePage == 0xFF00 && c.Usage == 1 && c.Feature >= 64)
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                b.BatteryFailures.Add($"本实例 FF00/0001 Feature64 接口数={channels.Count}，需要唯一接口");
                continue;
            }

            using (InstanceScope.Enter(b.InstanceId))
            {
                int level = VendorProtocol.ReadRealTekBattery(b.Vid, b.Pid, channels[0].Path,
                    out bool charging, out string diagnostic);
                if (level >= 0)
                {
                    b.Battery = level;
                    b.Charging = charging;
                    b.BatterySource = diagnostic;
                }
                else b.BatteryFailures.Add(diagnostic);
                LastDiagnostics.Add($"{b.InstanceId} RealTek：{diagnostic}");
            }
        }
    }
}
