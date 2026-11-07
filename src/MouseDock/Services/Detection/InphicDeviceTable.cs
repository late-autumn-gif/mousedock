using System;

namespace MouseDock.Services.Detection;

/// <summary>
/// 英菲克官方设备表，来源：D:\英菲克\Inphic Mouse\config.xml（Inphic-IN9-20250909）。
///
/// 官方 IN9 驱动只认这些组合，并且要求设备出现在 MI_02 接口：
///   IN9 Gaming Mouse (3311)  dev_id=M909  2.4G 248A:5C2F / 249A:5C2F
///   A9                       dev_id=M900  2.4G 248A:5C2F / 249A:5C2F
///   IN9 Gaming Mouse (S205)  dev_id=IN90  2.4G 248A:5C2F / 249A:5C2F
///   IN9 Gaming Mouse (6827)  dev_id=M910  2.4G 248A:5C2F / 249A:5C2F
///   有线                     dev_id 同上  248A:5C2E / 5D2E / 5E2E
///
/// 关键事实：四个型号共用同一组 VID:PID，型号只能由设备返回的 dev_id 定；
/// 248A:5B2F 不在表内。英菲克官方 IN9 驱动不认它，INPHIC HUB 的二进制里也没有
/// 可读设备表条目（搜到的 5B2F 字节分别落在压缩数据和代码常量里，不是设备表）。
/// </summary>
internal static class InphicDeviceTable
{
    internal static readonly (ushort Vid, ushort Pid, string Transport)[] Entries =
    {
        (0x248A, 0x5C2E, "USB"),
        (0x248A, 0x5D2E, "USB"),
        (0x248A, 0x5E2E, "USB"),
        (0x248A, 0x5C2F, "2.4G"),
        (0x249A, 0x5C2F, "2.4G")
    };

    /// <summary>官方驱动给 IN9 系列留的接口：VID_248A&amp;PID_5C2F&amp;MI_02。</summary>
    internal const string RequiredInterface = "MI_02";

    internal static bool IsInphicVid(ushort vid) => vid is 0x248A or 0x249A;

    internal static bool IsInTable(ushort vid, ushort pid)
    {
        foreach (var (v, p, _) in Entries)
            if (v == vid && p == pid) return true;
        return false;
    }

    /// <summary>报告里给用户看的一句话。不猜型号——同一组 VID:PID 对应四个零售型号。</summary>
    internal static string Describe(ushort vid, ushort pid)
    {
        if (!IsInphicVid(vid)) return "非英菲克号段";
        if (!IsInTable(vid, pid))
            return $"官方表外（{vid:X4}:{pid:X4} 不在 IN9/A9 的 5C2E/5D2E/5E2E/5C2F 清单里，型号未确认）";
        string transport = pid == 0x5C2F ? "2.4G" : "USB";
        return $"官方表内（IN9/A9 系列，{transport}，要求 {RequiredInterface} 接口；具体型号由设备返回的 dev_id 决定）";
    }

    /// <summary>
    /// 官方 dev_id → 显示名。dev_id 来自设备返回，不能由 VID:PID 推出。
    /// </summary>
    internal static string ModelFromDevId(string devId) => devId switch
    {
        "M909" => "英菲克 IN9（3311）",
        "M910" => "英菲克 IN9（6827）",
        "IN90" => "英菲克 IN9（S205）",
        "M900" => "英菲克 A9",
        _ => null
    };
}
