using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// 248A:5B2F 的官方 GetInfo：两次事务、每次最多两次 GET，SC580 跳零时增加一次确认。
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

            return M137FeatureProtocol.Read(
                packet => HidD_SetFeature(h, packet, packet.Length),
                packet => HidD_GetFeature(h, packet, packet.Length),
                Thread.Sleep, Marshal.GetLastWin32Error);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("M137 读取异常: " + ex.Message);
        }
        finally { CloseHandle(h); }
    }
}
