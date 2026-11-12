using System;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// VGN 蜻蜓 F1 MOBA（3554:F503）的电量与 DPI。集合必须是唯一的 FF02/0002 17/17，
    /// 否则不发送任何帧。
    /// </summary>
    internal static ReferenceBatteryProtocol.Reading ReadVgnF1(string path, bool readDpi = false, MouseDpiChange change = null)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例，未发送查询");
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
            return ReferenceBatteryProtocol.Reading.Failed("VGN F1 接口打开失败 Win32=" + Marshal.GetLastWin32Error());
        try
        {
            var caps = Caps(h);
            if (!VgnF1Protocol.MatchesSignature(caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                return ReferenceBatteryProtocol.Reading.Failed("VGN F1 集合签名不符，未发送查询");
            if (readDpi)
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                readDpi = HidD_GetAttributes(h, ref attributes) && VgnF1Protocol.IsCandidate(attributes.VendorID,
                    attributes.ProductID, HidDiscovery.ReadHidProduct(h, path));
                if (readDpi && HidD_GetPreparsedData(h, out var prep))
                {
                    try
                    {
                        readDpi = VgnF1ReportMatches(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps) &&
                            VgnF1ReportMatches(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps);
                    }
                    finally { HidD_FreePreparsedData(prep); }
                }
                else readDpi = false;
            }
            using var link = new HidStreamLink(h, caps.InputReportByteLength);
            return VgnF1Protocol.Read(link, readDpi, change);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("VGN F1 读取异常：" + ex.Message);
        }
        finally { CloseHandle(h); }
    }

    private static bool VgnF1ReportMatches(IntPtr prep, int type, ushort values, ushort buttons) =>
        values + buttons == 0 || TryGetReportId(prep, type, values, buttons, out byte id) && id == 8;

    /// <summary>
    /// 狼蛛 AULA F75（3554:FA09）的只读查询。集合必须是唯一的 FF02/0002 20/20。
    /// </summary>
    internal static ReferenceBatteryProtocol.Reading ReadF75(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例，未发送查询");
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
            return ReferenceBatteryProtocol.Reading.Failed("F75 接口打开失败 Win32=" + Marshal.GetLastWin32Error());
        try
        {
            var caps = Caps(h);
            if (!F75Protocol.MatchesSignature(caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                return ReferenceBatteryProtocol.Reading.Failed("F75 集合签名不符，未发送查询");
            using var link = new HidStreamLink(h, caps.InputReportByteLength);
            return F75Protocol.Read(link);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("F75 读取异常：" + ex.Message);
        }
        finally { CloseHandle(h); }
    }
}

