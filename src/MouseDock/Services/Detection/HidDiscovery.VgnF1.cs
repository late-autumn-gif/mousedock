using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// VGN 蜻蜓 F1 MOBA（3554:F503）。整套协议只在真机验证过的构建里出现过，
    /// 见 <see cref="VgnF1Protocol"/>；这里负责找出唯一的 FF02/0002 17/17 接口并落库。
    /// </summary>
    internal static void QueryVgnF1(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!VgnF1Protocol.IsCandidate(b.Vid, b.Pid, b.Product)) continue;
            b.HandledBy = HandledBy.VgnF1;
            b.VgnF1Model = null;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";

            if (safeMode)
            {
                b.BatteryFailures.Add("安全模式已跳过 VGN F1 电量查询");
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
                            VgnF1Protocol.MatchesChannel(c.Vid, c.Pid, c.Product, c.UsagePage, c.Usage,
                                c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                b.BatteryFailures.Add($"本实例 VGN F1 精确集合数={channels.Count}，需要唯一集合");
                continue;
            }

            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(channels[0].Path); }
                catch (Exception ex)
                {
                    result = ReferenceBatteryProtocol.Reading.Failed("VGN F1 查询异常：" + ex.GetType().Name);
                }
            }
            if (result != null && result.Level is >= 0 and <= 100 &&
                result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
            {
                if (result.Model == VgnF1Protocol.Model && VgnF1Protocol.IsMobaIdentity(result.Identity))
                    b.VgnF1Model = result.Model;
                b.Battery = result.Level;
                b.Charging = result.ChargingKnown && result.Charging;
                b.BatterySource = result.Diagnostic;
            }
            else if (b.Battery < 0)
            {
                b.BatteryFailures.Add(result?.Diagnostic ?? "VGN F1 读取器未返回结果");
            }
            LastDiagnostics.Add(
                $"{b.InstanceId} VGN F1：型号={b.VgnF1Model ?? "未知"} CID/MID={b.Identity} 电量={b.Battery} {result?.Diagnostic}");
        }
    }
}
