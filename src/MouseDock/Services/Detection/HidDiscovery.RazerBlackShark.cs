using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryRazerBlackShark(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, ushort, BatteryReplay.Trace, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!RazerBlackSharkProtocol.IsCandidate(b.Vid, b.Pid)) continue;
            b.HandledBy = HandledBy.RazerBlackShark;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.BatterySource = ""; b.Identity = "";
            var trace = new BatteryReplay.Trace();
            string path = null;
            void Record(string status) => BatteryReplay.Current?.AddQuery(b.InstanceId, path,
                RazerBlackSharkProtocol.Protocol, status, RazerBlackSharkProtocol.Model, b.Battery, trace);
            if (safeMode || !(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(b.Product)) ?? true))
            {
                b.BatteryFailures.Add(safeMode ? "安全模式已跳过黑鲨 V2 极速版查询" : "此实例识别已停用");
                Record(safeMode ? "SafeMode" : "RecognitionDisabled");
                continue;
            }
            var channels = collections.Where(c => InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                    c.Vid == b.Vid && c.Pid == b.Pid &&
                    RazerBlackSharkProtocol.MatchesChannel(c.Vid, c.Pid, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature))
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
            if (channels.Length != 1)
            {
                b.BatteryFailures.Add($"黑鲨 V2 极速版精确集合数={channels.Length}，需要唯一集合，未读取");
                Record("CollectionMismatch");
                continue;
            }
            path = channels[0].Path;
            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(path, b.Pid, trace); }
                catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed("黑鲨查询异常：" + ex.GetType().Name); }
            }
            result ??= ReferenceBatteryProtocol.Reading.Failed("黑鲨读取器未返回结果");
            if (result.Level is >= 0 and <= 100 && result.Model == RazerBlackSharkProtocol.Model &&
                result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
            {
                b.Battery = result.Level;
                b.Charging = result.ChargingKnown && result.Charging;
                b.BatterySource = result.Diagnostic;
            }
            else b.BatteryFailures.Add(result.Diagnostic);
            Record(result.Status.ToString());
            LastDiagnostics.Add($"{b.InstanceId} 黑鲨 V2 极速版：电量={b.Battery} {result.Diagnostic}");
        }
    }
}
