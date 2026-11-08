using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    /// <summary>
    /// 迈从鼠标的 DPI 读取，独立一轮跑在电量之后。
    /// 只对已经读到电量的迈从设备发官方 0x12 0x67 只读请求；
    /// 设备不支持就留未知，不动电量、不发写入。
    /// </summary>
    internal static void QueryMchoseDpi(Dictionary<string, Bucket> buckets,
        List<VendorCollectionInfo> collections, bool safeMode, DevicePreferences preferences)
    {
        foreach (var b in buckets.Values)
        {
            if (safeMode || b.Battery < 0) continue;
            if (!VendorProtocol.IsMchoseA7V2(b.Vid, b.Pid) &&
                !VendorProtocol.IsMchoseG7(b.Vid, b.Pid, b.Product)) continue;
            if (!(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}") ?? true)) continue;

            // 官方 allInfo 走 64 字节 Feature；FF01/0001 是 A7 V2、G7 的厂商集合。
            var channels = collections
                .Where(c => c.Vid == b.Vid && c.Pid == b.Pid &&
                            InstanceScope.ForPath(c.Path, "") == b.InstanceId &&
                            c.UsagePage == 0xFF01 && c.Usage == 1 && c.Feature >= 64)
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (channels.Count != 1)
            {
                LastDiagnostics.Add($"{b.InstanceId} MCHOSE DPI：Feature 接口数={channels.Count}，跳过");
                continue;
            }

            using (InstanceScope.Enter(b.InstanceId))
            {
                var dpi = VendorProtocol.ReadMchoseDpi(channels[0].Path, false, out string diagnostic);
                if (dpi.Dpi > 0)
                {
                    b.Dpi = dpi.Dpi;
                    b.DpiSlots = dpi.Values;
                    b.DpiText = diagnostic;
                }
                LastDiagnostics.Add($"{b.InstanceId} {diagnostic}");
            }
        }
    }
}
