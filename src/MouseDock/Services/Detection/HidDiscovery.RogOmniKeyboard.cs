using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    internal static void QueryRogOmniKeyboards(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences,
        Func<string, string, ReferenceBatteryProtocol.Reading> query)
    {
        foreach (var b in buckets.Values)
        {
            if (!RogOmniKeyboardProtocol.IsCandidate(b.Vid, b.Pid, b.Product)) continue;
            b.RogOmniHandled = true; b.RogOmniModel = null;
            b.Battery = -1; b.Charging = false; b.ReadStale = false; b.Identity = ""; b.BatterySource = "";
            var channels = collections.Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                InstanceScope.ForPath(c.Path, "") == b.InstanceId)
                .Select(c => (Collection: c, Channel: RogOmniKeyboardProtocol.Match(c.Vid, c.Pid,
                    c.Product, c.Path, c.UsagePage, c.Usage, c.Input, c.Output, c.Feature)))
                .Where(c => c.Channel != null)
                .GroupBy(c => c.Collection.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var pairing = channels.Where(c => c.Channel == RogOmniKeyboardProtocol.Channel.Pairing).ToList();
            var keyboard = channels.Where(c => c.Channel == RogOmniKeyboardProtocol.Channel.Keyboard).ToList();
            string reason = safeMode ? "安全模式已跳过 OMNI 键盘查询" :
                !(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}",
                    VendorMap.PhysicalKeyFromName(b.Product)) ?? true) ? "此实例识别已停用" :
                string.IsNullOrWhiteSpace(b.InstanceId) ? "缺少接收器实例，未查询" :
                pairing.Count != 1 || keyboard.Count != 1 ? "本实例 OMNI 配对/键盘集合缺失或不唯一，未查询" : null;
            if (reason != null)
            {
                b.BatteryFailures.Add(reason);
                LastDiagnostics.Add($"{b.InstanceId} OMNI：{reason}");
                continue;
            }
            ReferenceBatteryProtocol.Reading result;
            using (InstanceScope.Enter(b.InstanceId))
            {
                try { result = query(pairing[0].Collection.Path, keyboard[0].Collection.Path); }
                catch (Exception ex) { result = ReferenceBatteryProtocol.Reading.Failed("OMNI 查询失败：" + ex.GetType().Name); }
            }
            result ??= ReferenceBatteryProtocol.Reading.Failed("OMNI 读取器未返回结果");
            if (result.Model == RogOmniKeyboardProtocol.Model && result.Identity == "1B7A")
            {
                b.RogOmniModel = result.Model;
                if (result.Level is >= 0 and <= 100 &&
                    result.Status is ReferenceBatteryProtocol.State.Online or ReferenceBatteryProtocol.State.Charging)
                {
                    b.Battery = result.Level; b.Charging = result.ChargingKnown && result.Charging;
                    b.BatterySource = result.Diagnostic;
                }
            }
            if (b.Battery < 0) b.BatteryFailures.Add(result.Diagnostic);
            LastDiagnostics.Add($"{b.InstanceId} OMNI 键盘：型号={b.RogOmniModel} 电量={b.Battery} {result.Diagnostic}");
        }
    }
}
