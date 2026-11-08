using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// 248A:5B2F 的身份查询。必须在通用 33 字节旁路之前跑，否则会被错误协议吃掉。
    /// </summary>
    internal static void QueryM137Feature(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!M137FeatureProtocol.IsCandidate(b.Vid, b.Pid, b.Product)) continue;
            b.HandledBy = HandledBy.M137;
            b.M137Model = null;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";

            if (safeMode)
            {
                b.BatteryFailures.Add("安全模式已跳过 M137 身份查询");
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
                            M137FeatureProtocol.MatchesChannel(c.Vid, c.Pid, c.UsagePage, c.Usage,
                                c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                b.BatteryFailures.Add($"本实例 FF00/0001 Feature33 接口数={channels.Count}，需要唯一接口");
                continue;
            }

            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(channels[0].Path); }
                catch (Exception ex)
                {
                    result = ReferenceBatteryProtocol.Reading.Failed("M137 查询异常：" + ex.GetType().Name);
                }
            }
            if (result?.Model != null)
            {
                b.M137Model = result.Model;
                b.Identity = result.Identity ?? "";
                if (result.Level is >= 0 and <= 100)
                {
                    b.Battery = result.Level;
                    b.BatterySource = result.Diagnostic;
                }
            }
            if (b.Battery < 0)
                b.BatteryFailures.Add(result?.Diagnostic ?? "M137 读取器未返回结果");
            LastDiagnostics.Add(
                $"{b.InstanceId} M137：型号={b.M137Model ?? "未知"} 电量={b.Battery} {result?.Diagnostic}");
        }
    }
}
