using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// 狼蛛 AULA F75（3554:FA09）。官方驱动界面上不显示电量（ShowPower=0），
    /// 但接收器的 FF02/0002 20 字节 Report13 通道支持命令 5（型号）与 74（百分比），
    /// 见 <see cref="F75Protocol"/>。
    /// </summary>
    internal static void QueryF75(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!F75Protocol.IsCandidate(b.Vid, b.Pid, b.Product)) continue;
            b.HandledBy = HandledBy.AulaF75;
            b.F75Model = null;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";

            if (safeMode)
            {
                b.BatteryFailures.Add("安全模式已跳过 F75 查询");
                continue;
            }
            if (!(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}") ?? true))
            {
                b.BatteryFailures.Add("此实例识别已停用");
                continue;
            }
            var channels = collections
                .Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                            InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                            F75Protocol.MatchesChannel(c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage,
                                c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                b.BatteryFailures.Add($"本实例 Report13（20 字节）集合数={channels.Count}，需要唯一集合");
                continue;
            }

            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(channels[0].Path); }
                catch (Exception ex)
                {
                    result = ReferenceBatteryProtocol.Reading.Failed("F75 查询异常：" + ex.GetType().Name);
                }
            }
            if (result?.Model == F75Protocol.Model && result.Identity == F75Protocol.Identity &&
                result.Level is >= 0 and <= 100)
            {
                b.F75Model = result.Model;
                b.Battery = result.Level;
                b.Identity = result.Identity;
                b.BatterySource = result.Diagnostic;
            }
            else
            {
                b.BatteryFailures.Add(result?.Diagnostic ?? "F75 读取器未返回结果");
            }
            LastDiagnostics.Add(
                $"{b.InstanceId} F75：型号={b.F75Model ?? "未知"} 电量={b.Battery} {result?.Diagnostic}");
        }
    }
}
