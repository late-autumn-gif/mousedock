using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// 248A:5B2F 的官方 GetInfo：两次事务、每次最多两次 GET，两次读数一致才采用。
    /// 只读，不发配置命令。
    /// </summary>
    internal static ReferenceBatteryProtocol.Reading ReadM137Feature(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例，未发送查询");
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
            return ReferenceBatteryProtocol.Reading.Failed("M137 接口打开失败 Win32=" + Marshal.GetLastWin32Error());
        try
        {
            var caps = Caps(h);
            if (!M137FeatureProtocol.MatchesChannel(0x248A, 0x5B2F, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                return ReferenceBatteryProtocol.Reading.Failed("M137 集合签名不符，未发送查询");
            var ids = FeatureReportIds(h, caps);
            if (!ids.Contains(M137FeatureProtocol.ReportId))
                return ReferenceBatteryProtocol.Reading.Failed(
                    "M137 未确认 report12（候选 " +
                    string.Join("/", ids.Select(x => x.ToString("X2"))) + "），未发送查询");

            byte sequence = 0;
            int level = -1;
            string identity = null, hex = "";
            for (int transaction = 0; transaction < 2; transaction++)
            {
                var set = M137FeatureProtocol.Request(sequence++);
                if (!HidD_SetFeature(h, set, set.Length))
                    return ReferenceBatteryProtocol.Reading.Failed("M137 SET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());
                int accepted = 0;
                for (int attempt = 0; attempt < 2 && accepted == 0; attempt++)
                {
                    Thread.Sleep(16);
                    var get = M137FeatureProtocol.Request(sequence++);
                    if (!HidD_GetFeature(h, get, get.Length))
                        return ReferenceBatteryProtocol.Reading.Failed("M137 GET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());
                    hex = Convert.ToHexString(get);
                    if (!M137FeatureProtocol.TryPercent(get, out int current, out string currentIdentity))
                        continue;
                    if (currentIdentity != "M137" && M137FeatureProtocol.ModelForIdentity(currentIdentity) == null)
                        return ReferenceBatteryProtocol.Reading.Failed(
                            "M137 身份 " + currentIdentity + " 未收录型号，未采用读数；最近RX=" + hex);
                    if (level >= 0 && (level != current || identity != currentIdentity))
                        return ReferenceBatteryProtocol.Reading.Failed(
                            "M137 两次事务读数不一致，未采用；最近RX=" + hex);
                    level = current;
                    identity = currentIdentity;
                    accepted = 1;
                }
                if (accepted == 0)
                    return ReferenceBatteryProtocol.Reading.Failed("M137 事务未取得有效身份帧；最近RX=" + hex);
            }
            string model = M137FeatureProtocol.ModelForIdentity(identity) ??
                           M137FeatureProtocol.DisplayForIdentity(identity);
            return new ReferenceBatteryProtocol.Reading(level, false, false,
                ReferenceBatteryProtocol.State.Online,
                "M137 GetInfo 两次事务一致；接收器报告电量，充电状态未确认；最近RX=" + hex,
                model, identity);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("M137 读取异常: " + ex.Message);
        }
        finally { CloseHandle(h); }
    }
}
