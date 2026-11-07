using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection;

public static partial class HidDiscovery
{
    // 公版 2.4G 接收器（248A/249A:5B2F/5C2F）没有可写的 OUTPUT 集合，发不出身份请求。
    // 这里只在前面所有读取器都失败后，做一次完全被动的旁听 + 只读 Feature，拿到两次同型号
    // 有效帧才认型号和电量；拿不到就维持原来的「未读到」，不猜。
    private static void QueryGenericReceiver33(Dictionary<string, Bucket> buckets, bool safeMode, DevicePreferences preferences)
    {
        foreach (var b in buckets.Values)
        {
            if (safeMode || b.Battery >= 0) continue;
            if (!VendorProtocol.IsGenericReceiver33(b.Vid, b.Pid, b.Product)) continue;
            if (!RecognitionPolicy.CanQuery(b.Vid, b.Pid, b.Product)) continue;
            if (!(preferences?.CanRead(b.InstanceId, $"hid-{b.Vid:X4}-{b.Pid:X4}") ?? true)) continue;

            using var scope = InstanceScope.Enter(b.InstanceId);
            var result = VendorProtocol.TryReadReceiver33(b.Vid, b.Pid);
            LastDiagnostics.Add($"{b.InstanceId} 公版33字节接收器：{result.Diagnostic}");
            if (result.Level < 0 || result.Model == null)
            {
                b.BatteryFailures.Add(result.Diagnostic);
                continue;
            }
            b.Battery = result.Level;
            b.Charging = result.Charging;
            b.AulaHandled = true;
            b.AulaOnline = true;
            b.AulaModel = result.Model;
            b.BatterySource = "公版 2.4G 接收器只读身份帧";
        }
    }
}
